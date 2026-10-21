using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace AspectSwitch
{
    internal sealed class SettingsForm : Form
    {
        private Settings draft;
        private readonly Func<string, MonitorCandidate, Task<Reading>> hardware;
        private readonly Panel page = new Panel();
        private readonly Label heading = Theme.Label("", 23, true, Theme.Text);
        private readonly Label subtitle = Theme.Label("", 10, false, Theme.Muted);
        private readonly Label error = Theme.Label("", 9, false, Color.Firebrick);
        private readonly ModernButton save = new ModernButton { Text = "변경 사항 저장", Primary = true, Width = 146 };
        private readonly ModernButton cancel = new ModernButton { Text = "닫기", Width = 88, DialogResult = DialogResult.Cancel };
        private readonly List<ModernButton> nav = new List<ModernButton>();
        private readonly List<MonitorCandidate> candidates = new List<MonitorCandidate>();
        private bool startAtLogin, loading;

        internal Settings Result { get; private set; }
        internal bool StartAtLogin { get { return startAtLogin; } }
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        internal SettingsForm(Settings settings, bool startup, Func<string, MonitorCandidate, Task<Reading>> hardware = null)
        {
            draft = settings.Copy(); startAtLogin = startup; this.hardware = hardware;
            Text = "Aspect Switch"; Icon = Theme.AppIcon; Font = new Font("Segoe UI", 10F); ForeColor = Theme.Text; BackColor = Theme.Background;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(1040, 760); MinimumSize = new Size(960, 680);
            StartPosition = FormStartPosition.CenterScreen; MinimizeBox = false;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 216)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(root);
            Panel side = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Margin = new Padding(0), Padding = new Padding(18, 26, 18, 20) }; root.Controls.Add(side, 0, 0);
            Label brand = Theme.Label("Aspect\nSwitch", 18, true, Theme.Text); brand.SetBounds(24, 30, 172, 65); side.Controls.Add(brand);
            Label brandNote = Theme.Label("입력마다, 알맞은 화면으로", 9, false, Theme.Muted); brandNote.SetBounds(24, 104, 174, 30); side.Controls.Add(brandNote);
            string[] labels = { "기기와 화면", "모니터 호환성", "일반" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i; ModernButton button = new ModernButton { Text = labels[i], Width = 180, Height = 44, Left = 18, Top = 166 + i * 52, BackColor = Theme.Sidebar };
                button.Click += delegate { ShowPage(index); }; nav.Add(button); side.Controls.Add(button);
            }
            Label footerNote = Theme.Label("DDC/CI\nIndependent utility · v1.0.0", 9, false, Theme.Muted); footerNote.Dock = DockStyle.Bottom; footerNote.Height = 48; side.Controls.Add(footerNote);
            TableLayoutPanel main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(30, 24, 30, 12), Margin = new Padding(0) };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); root.Controls.Add(main, 1, 0);
            Panel title = new Panel { Dock = DockStyle.Fill }; heading.Dock = DockStyle.Top; heading.Height = 43; subtitle.Dock = DockStyle.Bottom; subtitle.Height = 37; title.Controls.Add(heading); title.Controls.Add(subtitle); main.Controls.Add(title, 0, 0);
            page.Dock = DockStyle.Fill; page.Margin = new Padding(0); main.Controls.Add(page, 0, 1);
            Panel bottom = new Panel { Dock = DockStyle.Fill }; main.Controls.Add(bottom, 0, 2);
            error.Dock = DockStyle.Top; error.Height = 42; bottom.Controls.Add(error);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
            actions.Controls.Add(save); actions.Controls.Add(cancel); bottom.Controls.Add(actions);
            save.Click += delegate { TrySave(); }; AcceptButton = save; CancelButton = cancel;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (loading) e.Cancel = true; };
            ShowPage(draft.MonitorName.Length == 0 ? 1 : 0);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e); NormalizeLayout();
            // A hidden tray launch can pass SW_HIDE to the first top-level form.
            if (hardware != null) ActivateWindow();
        }
        internal void ActivateWindow()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowWindow(Handle, 5); BringToFront(); Activate();
        }
        internal void NormalizeLayout()
        {
            if (page.Controls.Count == 0) return;
            FlowLayoutPanel stack = page.Controls[0] as FlowLayoutPanel;
            if (stack == null) return;
            foreach (Control card in stack.Controls) { card.Width = Math.Max(180, stack.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3); LayoutTree(card); }
        }
        private static void LayoutTree(Control control) { control.PerformLayout(); foreach (Control child in control.Controls) LayoutTree(child); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int corner = 2; DwmSetWindowAttribute(Handle, 33, ref corner, 4); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        private void ShowPage(int index)
        {
            foreach (ModernButton b in nav) { b.Selected = nav.IndexOf(b) == index; b.Invalidate(); }
            while (page.Controls.Count > 0) page.Controls[0].Dispose();
            if (index == 0) BuildDevices(); else if (index == 1) BuildCompatibility(); else BuildGeneral();
        }
        private FlowLayoutPanel Stack()
        {
            FlowLayoutPanel stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Background, Margin = new Padding(0) };
            page.Controls.Add(stack);
            stack.SizeChanged += delegate { foreach (Control c in stack.Controls) c.Width = Math.Max(180, stack.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3); };
            return stack;
        }
        private static void Add(FlowLayoutPanel stack, Control control, int height)
        { control.Width = Math.Max(180, stack.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3); control.Height = height; control.Margin = new Padding(0, 0, 0, 10); stack.Controls.Add(control); }
        private static Label TextLabel(string text, int height)
        { Label label = Theme.Label(text, 9.5F, false, Theme.Muted); label.Height = height; label.Dock = DockStyle.Fill; return label; }
        private Card Summary(string title, string description)
        {
            Card card = new Card(); Label a = Theme.Label(title, 11, true, Theme.Text); a.Dock = DockStyle.Top; a.Height = 26;
            Label b = Theme.Label(description, 9, false, Theme.Muted); b.Dock = DockStyle.Fill; card.Controls.Add(b); card.Controls.Add(a); return card;
        }
        private void BuildDevices()
        {
            heading.Text = "기기와 화면"; subtitle.Text = "기기별 입력과 화면 비율을 정하면, 전환할 때 자동으로 적용합니다.";
            FlowLayoutPanel stack = Stack();
            Add(stack, Summary(draft.MonitorName.Length == 0 ? "모니터를 먼저 연결하세요" : draft.MonitorName,
                draft.MonitorName.Length == 0 ? "왼쪽 ‘모니터 호환성’에서 검색하고 선택하세요." : "등록된 기기 " + draft.Devices.Count + "개  ·  " + (draft.AutoEnabled ? "자동 적용 켜짐" : "자동 적용 일시정지")), 88);
            Panel toolbar = new Panel(); Label label = Theme.Label("연결된 기기", 12, true, Theme.Text); label.Dock = DockStyle.Left; label.Width = 240; toolbar.Controls.Add(label);
            ModernButton add = new ModernButton { Text = "+  기기 추가", Dock = DockStyle.Right, Width = 124 };
            add.Click += delegate { AddDevice(); }; toolbar.Controls.Add(add); Add(stack, toolbar, 38);
            if (draft.Devices.Count == 0) Add(stack, Summary("첫 번째 기기를 등록하세요", "PC, Nintendo Switch, PlayStation, Xbox 등 원하는 이름을 사용할 수 있습니다."), 96);
            foreach (DeviceRule rule in draft.Devices) Add(stack, DeviceCard(rule), 142);
            Add(stack, TextLabel("등록하지 않은 입력은 변경하지 않습니다.\nHDMI 선택기 뒤의 기기들은 같은 입력으로 보이므로 자동 구분할 수 없습니다.", 52), 52);
        }
        private Card DeviceCard(DeviceRule rule)
        {
            Card card = new Card { Padding = new Padding(18, 14, 18, 12) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.Controls.Add(layout);
            TableLayoutPanel top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64)); layout.Controls.Add(top, 0, 0);
            TextBox name = new TextBox { Text = rule.Name, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, MaxLength = 48, Margin = new Padding(0, 6, 12, 0), AccessibleName = "기기 이름" };
            name.TextChanged += delegate { rule.Name = name.Text; }; top.Controls.Add(name, 0, 0);
            Label on = TextLabel("사용", 28); on.Dock = DockStyle.Fill; on.Padding = new Padding(0, 5, 0, 0); top.Controls.Add(on, 1, 0);
            Toggle enabled = new Toggle { Checked = rule.Enabled, AccessibleName = rule.Name + " 자동 적용" }; enabled.CheckedChanged += delegate { rule.Enabled = enabled.Checked; }; top.Controls.Add(enabled, 2, 0);
            ModernButton remove = new ModernButton { Text = "삭제", Dock = DockStyle.Top, Height = 30 }; remove.Click += delegate { draft.Devices.Remove(rule); ShowPage(0); }; top.Controls.Add(remove, 3, 0);
            TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.Controls.Add(fields, 0, 1);
            fields.Controls.Add(TextLabel("입력 포트", 20), 0, 0); fields.Controls.Add(TextLabel("적용할 화면 비율", 20), 1, 0);
            ComboBox input = Theme.Combo(draft.AvailableInputs, rule.Input); input.AccessibleName = rule.Name + " 입력 포트";
            ComboBox aspect = Theme.Combo(draft.AspectModes, rule.Aspect); aspect.AccessibleName = rule.Name + " 화면 비율";
            input.SelectedIndexChanged += delegate { if (input.SelectedItem != null) rule.Input = ((Choice)input.SelectedItem).Value; };
            aspect.SelectedIndexChanged += delegate { if (aspect.SelectedItem != null) rule.Aspect = ((Choice)aspect.SelectedItem).Value; };
            fields.Controls.Add(input, 0, 1); fields.Controls.Add(aspect, 1, 1); return card;
        }
        internal void AddDevice()
        {
            if (draft.MonitorName.Length == 0 || draft.AspectModes.Length == 0) { error.Text = "‘모니터 호환성’에서 모니터와 화면 비율을 먼저 등록하세요."; return; }
            int free = -1; foreach (Choice input in draft.AvailableInputs) if (!draft.HasInput(input.Value)) { free = input.Value; break; }
            if (free < 0) { error.Text = "등록 가능한 입력을 모두 사용하고 있습니다. 새 포트는 모니터 검색 또는 현재 비율 등록으로 확인하세요."; return; }
            draft.Devices.Add(new DeviceRule { Name = "새 기기 " + (draft.Devices.Count + 1), Input = free, Aspect = draft.AspectModes[0].Value }); error.Text = ""; ShowPage(0);
        }
        private void BuildCompatibility()
        {
            heading.Text = "모니터 호환성"; subtitle.Text = "모니터를 찾고, 모델에 맞는 화면 비율을 등록합니다.";
            FlowLayoutPanel stack = Stack();
            Card monitor = new Card(); Add(stack, monitor, 190);
            TableLayoutPanel monitorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            monitorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            monitorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); monitorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); monitorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); monitorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); monitor.Controls.Add(monitorLayout);
            Label title = Theme.Label("대상 모니터", 12, true, Theme.Text); title.Dock = DockStyle.Fill; monitorLayout.Controls.Add(title, 0, 0);
            ComboBox selected = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Height = 32 };
            foreach (MonitorCandidate candidate in candidates) selected.Items.Add(candidate);
            selected.Format += delegate(object sender, ListControlConvertEventArgs e) { MonitorCandidate c = e.ListItem as MonitorCandidate; if (c != null) e.Value = c.Name + " · 장치 " + (candidates.IndexOf(c) + 1); };
            if (selected.Items.Count > 0) { selected.SelectedIndex = 0; foreach (MonitorCandidate c in candidates) if (c.Id == draft.MonitorId && c.Name == draft.MonitorName) selected.SelectedItem = c; }
            monitorLayout.Controls.Add(selected, 0, 1);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            ModernButton scan = new ModernButton { Text = "모니터 검색", Width = 120, Enabled = hardware != null };
            ModernButton use = new ModernButton { Text = "선택한 모니터 사용", Width = 170, Enabled = hardware != null && candidates.Count > 0 };
            buttons.Controls.Add(scan); buttons.Controls.Add(use); monitorLayout.Controls.Add(buttons, 0, 2);
            monitorLayout.Controls.Add(TextLabel("다른 모니터를 선택하면 기기 목록을 새로 설정합니다.", 25), 0, 3);
            scan.Click += async delegate { await Scan(); };
            use.Click += async delegate { if (selected.SelectedItem != null) await SelectMonitor((MonitorCandidate)selected.SelectedItem); };
            string state = draft.MonitorName.Length == 0 ? "아직 선택된 모니터가 없습니다." : Catalog.IsKnown(draft.MonitorName) ? "검증된 모델 프리셋 · AGP346UCSD" : "사용자 등록 프로필 · 실제 화면 확인 필요";
            Add(stack, Summary(state, draft.MonitorName.Length == 0 ? "검색 후 사용할 모니터를 선택하세요." : draft.MonitorName + "  ·  입력 " + draft.AvailableInputs.Length + "개 / 비율 " + draft.AspectModes.Length + "개"), 88);
            Card learn = new Card(); Add(stack, learn, 188);
            Label learnTitle = Theme.Label("OSD에서 선택한 비율 등록", 12, true, Theme.Text); learnTitle.Dock = DockStyle.Top; learnTitle.Height = 30; learn.Controls.Add(learnTitle);
            Label instructions = TextLabel("모니터 OSD에서 원하는 화면 비율을 선택한 뒤 이름을 입력하세요.\n이 창이 열려 있는 동안 자동 적용은 잠시 멈춥니다.", 52); instructions.Dock = DockStyle.Top; learn.Controls.Add(instructions); instructions.BringToFront();
            TableLayoutPanel row = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 38, ColumnCount = 2 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158)); learn.Controls.Add(row);
            TextBox modeName = new TextBox { Text = "원본 화면 비율", MaxLength = 80, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 16, 0), AccessibleName = "등록할 화면 비율 이름" }; row.Controls.Add(modeName, 0, 0);
            ModernButton capture = new ModernButton { Text = "현재 비율 등록", Dock = DockStyle.Fill, Enabled = hardware != null && draft.MonitorName.Length > 0 };
            capture.Click += async delegate { await Learn(modeName.Text); }; row.Controls.Add(capture, 1, 0);
            if (draft.AvailableInputs.Length > 0)
            {
                Card ports = new Card(); Add(stack, ports, 116);
                Label portsTitle = Theme.Label("포트 이름", 11, true, Theme.Text); portsTitle.Dock = DockStyle.Top; portsTitle.Height = 30; ports.Controls.Add(portsTitle);
                TableLayoutPanel portRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 3 };
                portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); ports.Controls.Add(portRow);
                ComboBox port = Theme.Combo(draft.AvailableInputs, draft.AvailableInputs[0].Value); portRow.Controls.Add(port, 0, 0);
                TextBox portName = new TextBox { Text = ((Choice)port.SelectedItem).Label, MaxLength = 80, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), AccessibleName = "포트 이름" }; portRow.Controls.Add(portName, 1, 0);
                port.SelectedIndexChanged += delegate { portName.Text = ((Choice)port.SelectedItem).Label; };
                ModernButton rename = new ModernButton { Text = "이름 적용", Dock = DockStyle.Fill }; portRow.Controls.Add(rename, 2, 0);
                rename.Click += delegate { if (String.IsNullOrWhiteSpace(portName.Text)) { error.Text = "포트 이름을 입력하세요."; return; } ((Choice)port.SelectedItem).Label = portName.Text.Trim(); ShowPage(1); error.Text = "포트 이름을 바꿨습니다. 변경 사항 저장을 누르면 적용됩니다."; };
            }
            Add(stack, TextLabel("모델별 기능 차이로 G-Menu 지원 여부만으로 호환성을 보장할 수 없습니다.\n입력·비율을 DDC/CI로 읽을 수 없는 모델은 자동 제어가 지원되지 않습니다.", 56), 56);
        }
        private void BuildGeneral()
        {
            heading.Text = "일반"; subtitle.Text = "자동 적용과 Windows 시작 동작을 설정합니다.";
            FlowLayoutPanel stack = Stack();
            Add(stack, ToggleCard("자동 화면 비율 적용", "등록한 기기의 입력이 감지되면 지정한 비율로 바꿉니다.", draft.AutoEnabled, delegate(bool value) { draft.AutoEnabled = value; }), 94);
            Add(stack, ToggleCard("Windows 로그인 시 시작", "로그인할 때 트레이에서 조용히 실행합니다.", startAtLogin, delegate(bool value) { startAtLogin = value; }), 94);
            Card intervalCard = new Card(); Add(stack, intervalCard, 110);
            TableLayoutPanel intervalLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            intervalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); intervalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            intervalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); intervalLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); intervalCard.Controls.Add(intervalLayout);
            Label intervalTitle = Theme.Label("입력 확인 간격 (초)", 11, true, Theme.Text); intervalTitle.Dock = DockStyle.Fill; intervalLayout.Controls.Add(intervalTitle, 0, 0);
            NumericUpDown interval = new NumericUpDown { Minimum = 1, Maximum = 10, DecimalPlaces = 1, Increment = .5M, Value = draft.PollMilliseconds / 1000M, Dock = DockStyle.Fill, AccessibleName = "입력 확인 간격 (초)" };
            interval.ValueChanged += delegate { draft.PollMilliseconds = (int)(interval.Value * 1000M); }; intervalLayout.Controls.Add(interval, 1, 0);
            Label intervalNote = TextLabel("기본 2초 · 전환 직후에는 모니터 응답을 기다릴 수 있습니다.", 26); intervalLayout.Controls.Add(intervalNote, 0, 1); intervalLayout.SetColumnSpan(intervalNote, 2);
            Add(stack, Summary("G-Menu 없이 실행", "설정과 프로필은 프로그램 폴더의 settings.json에 저장됩니다.\n모니터 제조사와 무관한 독립 유틸리티입니다."), 112);
        }
        private Card ToggleCard(string title, string text, bool initial, Action<bool> changed)
        {
            Card card = new Card(); Toggle toggle = new Toggle { Checked = initial, Dock = DockStyle.Right, Width = 48, AccessibleName = title };
            Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 16, 0) };
            Label a = Theme.Label(title, 11, true, Theme.Text); a.Dock = DockStyle.Top; a.Height = 27; Label b = TextLabel(text, 24); b.Dock = DockStyle.Fill;
            content.Controls.Add(b); content.Controls.Add(a); card.Controls.Add(content); card.Controls.Add(toggle);
            // Keep the switch compact rather than stretching its painted track with the card.
            toggle.Dock = DockStyle.None; toggle.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            card.Resize += delegate { toggle.Location = new Point(card.Width - 70, 28); content.Width = card.Width - 110; };
            content.Dock = DockStyle.None; content.SetBounds(20, 20, 480, 54);
            toggle.CheckedChanged += delegate { changed(toggle.Checked); }; return card;
        }
        private async Task<Reading> ReadHardware(string operation, MonitorCandidate candidate)
        {
            loading = true; page.Enabled = false; save.Enabled = false; cancel.Enabled = false; foreach (ModernButton b in nav) b.Enabled = false;
            error.ForeColor = Theme.Muted; error.Text = "모니터와 통신 중… 잠시 기다려 주세요.";
            try { return await hardware(operation, candidate); }
            finally { loading = false; page.Enabled = true; save.Enabled = true; cancel.Enabled = true; foreach (ModernButton b in nav) b.Enabled = true; error.ForeColor = Color.Firebrick; }
        }
        private async Task Scan()
        {
            try
            {
                Reading result = await ReadHardware("list", null);
                if (!result.Ok || result.Monitors == null) throw new Exception(result.Error ?? "모니터 검색 실패");
                candidates.Clear(); candidates.AddRange(result.Monitors); ShowPage(1);
                error.Text = candidates.Count == 0 ? "모니터를 찾지 못했습니다. 영상 케이블과 DDC/CI 설정을 확인하세요." : "검색 완료. 모니터를 선택한 뒤 ‘선택한 모니터 사용’을 누르세요.";
            }
            catch (Exception e) { error.Text = e.Message; }
        }
        private async Task SelectMonitor(MonitorCandidate candidate)
        {
            try
            {
                Reading result = await ReadHardware("inspect", candidate);
                if (result.Monitors == null || result.Monitors.Length != 1) throw new Exception(result.Error ?? "모니터 확인 실패");
                draft = Capabilities.Configure(result.Monitors[0], draft); ShowPage(1); error.Text = "모니터를 선택했습니다. 기기와 화면에서 입력 규칙을 추가하세요. 저장하면 적용됩니다.";
            }
            catch (Exception e) { error.Text = e.Message; }
        }
        private async Task Learn(string label)
        {
            try
            {
                Reading result = await ReadHardware("inspect", new MonitorCandidate { Name = draft.MonitorName, Id = draft.MonitorId });
                if (result.Monitors == null || result.Monitors.Length != 1) throw new Exception(result.Error ?? "현재 비율 읽기 실패");
                Capabilities.Learn(draft, result.Monitors[0], label); ShowPage(1); error.Text = "‘" + label.Trim() + "’ 등록 완료. 기기와 화면에서 선택하고 저장하세요.";
            }
            catch (Exception e) { error.Text = e.Message; }
        }
        internal void TrySave()
        {
            try { draft.Validate(); Result = draft.Copy(); DialogResult = DialogResult.OK; }
            catch (Exception e) { error.ForeColor = Color.Firebrick; error.Text = e.Message; }
        }
        internal void PreviewPage(int index) { ShowPage(index); }
    }
}
