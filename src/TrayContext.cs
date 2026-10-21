using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AspectSwitch
{
    internal sealed class TrayContext : ApplicationContext
    {
        private Settings settings;
        private SettingsWindowSession activeSettingsForm;
        private readonly EventWaitHandle stop;
        private readonly EventWaitHandle showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, Program.SettingsEventName);
        private readonly NotifyIcon icon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem devicesItem;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Policy policy = new Policy();
        private bool busy, quitting, editing, updatingMenus, settingsPending;
        private int requestedInput = -1;
        private string lastStatus = "";
        private DateTime nextPoll = DateTime.MinValue;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "AspectSwitch";

        internal TrayContext(Settings settings, EventWaitHandle stop, bool openSettings)
        {
            this.settings = settings; this.stop = stop; settingsPending = openSettings;
            menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("모니터 확인 중…") { Enabled = false }; menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("설정…", null, async delegate { await OpenSettings(); });
            enabledItem = new ToolStripMenuItem("자동 비율 전환") { Checked = settings.AutoEnabled, CheckOnClick = true };
            enabledItem.CheckedChanged += delegate { ToggleAutomatic(); }; menu.Items.Add(enabledItem);
            devicesItem = new ToolStripMenuItem("기기로 전환"); menu.Items.Add(devicesItem);
            menu.Items.Add(new ToolStripSeparator());
            startupItem = new ToolStripMenuItem("Windows 로그인 시 시작") { Checked = IsStartupEnabled() };
            startupItem.Click += delegate
            {
                try { SetStartup(!IsStartupEnabled()); }
                catch (Exception e) { MessageBox.Show(e.Message, "시작 설정 실패"); }
            };
            menu.Items.Add(startupItem);
            menu.Items.Add("로그 보기", null, delegate { try { Process.Start("notepad.exe", "\"" + Program.LogPath + "\""); } catch { } });
            menu.Items.Add("프로그램 폴더 열기", null, delegate { Process.Start("explorer.exe", "\"" + Program.Folder + "\""); });
            menu.Items.Add("종료", null, delegate { Quit(); });
            icon = new NotifyIcon { Icon = Theme.AppIcon, Text = "Aspect Switch", ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += async delegate { await OpenSettings(); };
            RefreshMenus();
            Program.Log("START", "version=" + Program.Version + "; settings=" + settings.ToJson());
            timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += async delegate { await Tick(); }; timer.Start();
        }

        private void RefreshMenus()
        {
            updatingMenus = true;
            try
            {
                enabledItem.Checked = settings.AutoEnabled;
                while (devicesItem.DropDownItems.Count > 0) { ToolStripItem item = devicesItem.DropDownItems[0]; devicesItem.DropDownItems.RemoveAt(0); item.Dispose(); }
                foreach (DeviceRule rule in settings.Devices)
                {
                    int input = rule.Input;
                    ToolStripMenuItem entry = new ToolStripMenuItem(rule.Name + " · " + Catalog.Label(settings.AvailableInputs, input));
                    entry.Click += delegate { requestedInput = input; nextPoll = DateTime.MinValue; };
                    devicesItem.DropDownItems.Add(entry);
                }
                devicesItem.Enabled = settings.Devices.Count > 0;
                startupItem.Checked = IsStartupEnabled();
            }
            finally { updatingMenus = false; }
        }
        private void ToggleAutomatic()
        {
            if (updatingMenus) return;
            try
            {
                Settings updated = settings.Copy(); updated.AutoEnabled = enabledItem.Checked;
                Program.SaveSettings(updated); settings = updated; policy.Reset();
                Program.Log("AUTO", settings.AutoEnabled ? "enabled" : "paused"); nextPoll = DateTime.MinValue;
            }
            catch (Exception e) { RefreshMenus(); MessageBox.Show(e.Message, "설정 저장 실패"); }
        }
        private async Task OpenSettings()
        {
            if (quitting) return;
            if (editing) { if (activeSettingsForm != null) activeSettingsForm.ActivateWindow(); return; }
            editing = true;
            enabledItem.Enabled = false; startupItem.Enabled = false; devicesItem.Enabled = false;
            try
            {
                // Drain any in-flight transaction before changing the mapping used by a worker.
                while (busy && !quitting) await Task.Delay(100);
                if (quitting) return;
                using (SettingsWindowSession form = new SettingsWindowSession())
                {
                    activeSettingsForm = form;
                    SettingsWindowSession.Response result = await form.Edit(settings, IsStartupEnabled());
                    if (result == null || quitting) return;
                    if (result.ResetAllData)
                    {
                        settings = new Settings(); requestedInput = -1; policy.Reset();
                        AppDataReset.Clear(Program.Folder, delegate { SetStartup(false); });
                        string errorFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AspectSwitch-ui-error.txt");
                        System.IO.File.Delete(errorFile);
                        RefreshMenus(); lastStatus = "설정에서 모니터를 선택하세요.";
                        statusItem.Text = lastStatus; icon.Text = "Aspect Switch: " + lastStatus;
                        settingsPending = true; return;
                    }
                    Settings updated = result.Settings;
                    Program.SaveSettings(updated); settings = updated;
                    requestedInput = -1; policy.Reset();
                    try { if (result.StartAtLogin != IsStartupEnabled()) SetStartup(result.StartAtLogin); }
                    catch (Exception e) { MessageBox.Show("입력 설정은 저장했습니다. 자동 시작 설정 실패: " + e.Message, "Aspect Switch"); }
                    RefreshMenus(); Program.Log("SETTINGS", settings.ToJson());
                    SetStatus("설정 저장됨 / 입력 확인 중…");
                }
            }
            catch (Exception e) { MessageBox.Show(e.Message, "설정 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { activeSettingsForm = null; editing = false; if (!quitting) { enabledItem.Enabled = true; startupItem.Enabled = true; RefreshMenus(); } nextPoll = DateTime.MinValue; }
        }
        private bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && String.Equals(key.GetValue(RunName) as string, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
        }
        private void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(RunName, false);
            }
            startupItem.Checked = IsStartupEnabled(); Program.Log("STARTUP", enabled ? "enabled" : "disabled");
        }
        private void SetStatus(string message)
        {
            if (quitting) return;
            statusItem.Text = message;
            string tip = "Aspect Switch: " + message; icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            if (message != lastStatus) { Program.Log("STATE", message); lastStatus = message; }
        }
        private async Task Tick()
        {
            if (quitting) return;
            if (stop.WaitOne(0)) { Quit(); return; }
            if (showSettings.WaitOne(0)) settingsPending = true;
            if (settingsPending) { settingsPending = false; await OpenSettings(); return; }
            if (editing || busy || DateTime.UtcNow < nextPoll) return;
            if (settings.MonitorName.Length == 0) { SetStatus("설정에서 모니터를 선택하세요."); return; }
            busy = true;
            try
            {
                if (requestedInput >= 0)
                {
                    int input = requestedInput; requestedInput = -1;
                    Reading switched = await Task.Run(delegate { return Program.CallWorker("input", -1, input, settings.Revision()); });
                    if (quitting) return;
                    Program.Log("INPUT", "target=" + input + "; " + Program.Json.Serialize(switched));
                    SetStatus(switched.Ok ? "입력 전환 확인 중…" : switched.Error);
                    policy.Missing(); return;
                }
                Reading reading = await Task.Run(delegate { return Program.CallWorker("probe", -1, -1); });
                if (quitting || editing) return;
                int desired = policy.Observe(reading, settings, DateTime.UtcNow);
                if (!reading.Ok) { SetStatus("연결 대기: " + reading.Error); return; }
                if (!settings.AutoEnabled) { SetStatus("일시정지 / " + Catalog.Label(settings.AvailableInputs, reading.Input)); return; }
                if (desired >= 0)
                {
                    policy.Attempted(DateTime.UtcNow);
                    Reading result = await Task.Run(delegate { return Program.CallWorker("aspect", reading.Input, desired, settings.Revision()); });
                    if (quitting) return;
                    Program.Log("ASPECT", "expectedInput=" + reading.Input + "; target=" + desired + "; " + Program.Json.Serialize(result));
                    SetStatus(result.Wrote ? "비율 적용 후 재연결 확인 중…" : result.Skipped ? "입력 또는 설정이 변경되어 다시 확인 중…" : "비율 적용 재시도 대기: " + result.Error);
                    policy.Missing();
                }
                else if (settings.DesiredAspect(reading.Input) < 0) SetStatus(Catalog.Label(settings.AvailableInputs, reading.Input) + " / 자동 전환 대상 아님");
                else if (reading.Aspect == settings.DesiredAspect(reading.Input)) SetStatus(Catalog.Label(settings.AvailableInputs, reading.Input) + " / " + Catalog.Label(settings.AspectModes, reading.Aspect) + " 확인됨");
                else if (policy.Attempts >= 3) SetStatus("변경 3회 실패 / 자동 전환을 껐다 켜서 재시도");
                else SetStatus(Catalog.Label(settings.AvailableInputs, reading.Input) + " / 비율 변경 대기");
            }
            catch (Exception e) { SetStatus("오류: " + e.Message); }
            finally { busy = false; nextPoll = DateTime.UtcNow.AddMilliseconds(settings.PollMilliseconds); }
        }
        private void Quit()
        {
            if (quitting) return;
            quitting = true; timer.Stop(); icon.Visible = false;
            if (activeSettingsForm != null) activeSettingsForm.Close();
            Program.Log("STOP", "stopped"); timer.Dispose(); icon.Dispose(); menu.Dispose(); showSettings.Dispose(); ExitThread();
        }
    }
}
