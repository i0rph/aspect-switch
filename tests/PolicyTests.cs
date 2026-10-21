using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using AspectSwitch;
internal static class PolicyTests
{
    private static int passed;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, name); }
    private static Reading Read(int input, int aspect) { return new Reading { Ok = true, Input = input, Aspect = aspect }; }
    private static MonitorCandidate Candidate(string name, string id, int current)
    { return new MonitorCandidate { Name = name, Id = id, InputReadable = true, AspectReadable = true, CurrentInput = 15, CurrentAspect = current, Capabilities = "(prot(monitor)model(example)vcp(10 12 60(0F 10 11 12 15) 86(01 02 03) D6(01 04)))" }; }
    private static bool Fits(Control control)
    {
        foreach (Control child in control.Controls)
        {
            if (control is TableLayoutPanel && child.Right > control.ClientSize.Width + 1) return false;
            if (!Fits(child)) return false;
        }
        return true;
    }
    [STAThread] private static int Main()
    {
        try
        {
            Settings s = Settings.KnownMonitor(); s.Validate(); new Settings().Validate();
            Check(new Settings().Devices.Count == 0, "fresh installs do not assume any model or input");
            Settings legacy = Settings.FromJson("{\"MonitorName\":\"AOC AGP346UCSD\",\"SwitchInput\":17,\"PollMilliseconds\":3000}");
            Check(legacy.SchemaVersion == 3 && legacy.Devices[1].Input == 17 && legacy.PollMilliseconds == 3000, "v1 migration preserves inputs and polling");
            Settings old = Settings.FromJson("{\"SchemaVersion\":2,\"SwitchInput\":17,\"ConsoleInput\":18,\"PcInput\":21,\"PcAspect\":40,\"AutoEnabled\":false}");
            Check(old.Devices[0].Input == 21 && old.Devices[0].Aspect == 40 && old.Devices[1].Input == 18 && !old.AutoEnabled, "v2 migration precedence, modes and pause state");
            s.Devices.Add(new DeviceRule { Name = "PlayStation 5", Input = 17, Aspect = 35 });
            s.Devices.Add(new DeviceRule { Name = "노트북", Input = 21, Aspect = 40 }); s.Validate();
            Check(s.DesiredAspect(17) == 35 && s.DesiredAspect(21) == 40 && s.Devices.Count == 4, "four independent device rules");
            Settings copy = s.Copy(); copy.Devices[0].Name = "edited"; copy.AspectModes[0].Label = "edited";
            Check(s.Devices[0].Name == "PC" && s.AspectModes[0].Label != "edited", "cancel and edits are deep isolated");
            Settings round = Settings.FromJson(s.ToJson()); Check(round.Devices.Count == 4 && round.Devices[2].Name == "PlayStation 5", "multi-device unicode settings roundtrip");
            string rev = s.Revision(); s.Devices[3].Enabled = false;
            Check(s.DesiredAspect(21) == -1 && rev != s.Revision(), "disabled rule is excluded and changes revision");
            Check(s.AllowsAspectWrite(17, 17, 35), "third device write allowed");
            Check(!s.AllowsAspectWrite(18, 15, 35), "input race rejected");
            Check(!s.AllowsAspectWrite(18, 18, 39), "stale ratio rejected");
            Check(!s.AllowsAspectWrite(21, 21, 40), "disabled rule cannot write");
            s.AutoEnabled = false; Check(!s.AllowsAspectWrite(17, 17, 35), "pause prevents writes"); s.AutoEnabled = true;
            Settings invalid = s.Copy(); invalid.Devices[2].Input = 18; Reject(invalid.Validate, "duplicate port rejected across all devices");
            invalid = s.Copy(); invalid.Devices[0].Name = " "; Reject(invalid.Validate, "blank name rejected");
            invalid = s.Copy(); invalid.Devices[0].Aspect = 65536; Reject(invalid.Validate, "unregistered ratio rejected");
            invalid = s.Copy(); invalid.Devices[0].Input = 4; Reject(invalid.Validate, "unregistered input rejected");
            invalid = s.Copy(); invalid.AspectModes = null; Reject(invalid.Validate, "null mode table rejected");
            invalid = s.Copy(); invalid.Devices = null; Reject(invalid.Validate, "null rules rejected");
            Settings otherBrand = s.Copy(); otherBrand.MonitorName = "Other vendor"; otherBrand.Validate();
            Check(Settings.FromJson(otherBrand.ToJson()).MonitorName == "Other vendor", "non-AOC settings survive validation and roundtrip");
            invalid = s.Copy(); invalid.PollMilliseconds = 999; Reject(invalid.Validate, "poll minimum");
            invalid = s.Copy(); invalid.PollMilliseconds = 10001; Reject(invalid.Validate, "poll maximum");
            Reject(delegate { Settings.FromJson("null"); }, "empty JSON rejected");
            Reject(delegate { Settings.FromJson("{broken"); }, "corrupt JSON rejected");
            Reject(delegate { Settings.FromJson("{\"SchemaVersion\":99}"); }, "future schema rejected");
            int[] inputs = Capabilities.Values("(model(x)vcp(10 60(0f 10 11 12 15) 86(23 27)))", 0x60);
            Check(inputs.Length == 5 && inputs[1] == 16 && inputs[4] == 21, "capabilities variable input list parsed");
            Check(Capabilities.Values("(vcp(10 60(0F 0F 11) 86(23 27)))", 0x60).Length == 2, "duplicate advertised values deduplicated");
            Check(Capabilities.Values("(vcp(60(0F (10)) 86(23)))", 0x60).Length == 0, "nested malformed values rejected");
            Check(Capabilities.Values("(vcp(60(0F 11", 0x60).Length == 0, "truncated values rejected");
            Check(Capabilities.Values("(vcp(60(ZZ)))", 0x60).Length == 0, "non-hex values rejected");
            Check(Capabilities.Values("(vcp(60(10000)))", 0x60).Length == 0, "out-of-range capabilities rejected");
            Check(Capabilities.Values("(vcp(10 12))", 0x60).Length == 0, "missing capability has no guessed values");
            // Synthetic fixtures verify brand-independent logic, not physical model support.
            foreach (string brand in new[] { "LG fixture", "Samsung fixture", "Dell fixture", "ASUS fixture", "MSI fixture", "BenQ fixture", "Generic PnP Monitor" })
            {
                MonitorCandidate fixture = Candidate(brand, "fixture-device", 2);
                Settings configured = Capabilities.Configure(fixture, new Settings());
                configured.Devices.Add(new DeviceRule { Name = "PC", Input = 15, Aspect = 2 });
                Check(Settings.FromJson(configured.ToJson()).AllowsAspectWrite(15, 15, 2) && configured.AspectModes.Length == 1, "capability-based registration and policy: " + brand);
            }
            MonitorCandidate unsupported = Candidate("Generic display", "unsupported", 2);
            unsupported.InputReadable = false;
            Reject(delegate { Capabilities.Configure(unsupported, s); }, "advertised features without readable input rejected");
            unsupported.AspectReadable = false;
            Reject(delegate { Capabilities.Configure(unsupported, s); }, "brightness-only or non-DDC display rejected");
            unsupported.InputReadable = true; unsupported.AspectReadable = true; unsupported.CurrentInput = -1;
            Reject(delegate { Capabilities.Configure(unsupported, s); }, "invalid observed input rejected");
            unsupported.CurrentInput = 15; unsupported.CurrentAspect = 65536;
            Reject(delegate { Capabilities.Configure(unsupported, s); }, "invalid observed scaling rejected");
            unsupported.CurrentAspect = 2; unsupported.Name = " ";
            Reject(delegate { Capabilities.Configure(unsupported, s); }, "unidentifiable monitor rejected");
            MonitorCandidate generic = Candidate("Generic ultrawide fixture", "device-a", 2);
            Settings genericSettings = Capabilities.Configure(generic, new Settings());
            Check(genericSettings.AvailableInputs.Length == 5 && genericSettings.AspectModes.Length == 1 && genericSettings.AspectModes[0].Value == 2, "generic model uses actual inputs and observed ratio only");
            Check(!Catalog.Contains(genericSettings.AspectModes, 39), "AGP-specific modes never leak to other models");
            genericSettings.AvailableInputs[0].Label = "PC cable";
            genericSettings = Capabilities.Configure(generic, genericSettings);
            Check(genericSettings.AvailableInputs[0].Label == "PC cable", "rescan preserves custom port names");
            generic.CurrentAspect = 3; Capabilities.Learn(genericSettings, generic, "원본 비율");
            Check(genericSettings.AspectModes.Length == 2 && Catalog.Label(genericSettings.AspectModes, 3) == "원본 비율", "OSD ratio learning");
            Capabilities.Learn(genericSettings, generic, "16:9 유지"); Check(genericSettings.AspectModes.Length == 2, "rename learned value without duplicates");
            genericSettings.Devices.Add(new DeviceRule { Name = "Xbox", Input = 17, Aspect = 3 }); genericSettings.Validate();
            Check(genericSettings.AllowsAspectWrite(17, 17, 3), "generic learned mode usable by policy");
            MonitorCandidate other = Candidate("Generic ultrawide fixture", "device-b", 1);
            Reject(delegate { Capabilities.Learn(genericSettings, other, "wrong monitor"); }, "same model different identity cannot teach profile");
            Settings replaced = Capabilities.Configure(other, genericSettings);
            Check(replaced.Devices.Count == 0 && replaced.MonitorId == "device-b" && replaced.AspectModes.Length == 1, "different physical monitor resets incompatible mappings");
            other.AspectReadable = false; Reject(delegate { Capabilities.Configure(other, s); }, "unreadable scaling is unsupported");
            other.AspectReadable = true; other.Ambiguous = true; Reject(delegate { Capabilities.Configure(other, s); }, "ambiguous display mapping rejected");
            generic.Capabilities = null; Settings noCaps = Capabilities.Configure(generic, new Settings());
            Check(noCaps.AvailableInputs.Length == 1 && noCaps.AspectModes.Length == 1, "missing capabilities uses read evidence only");
            MonitorCandidate known = Candidate("AOC AGP346UCSD", "known-id", 39);
            Settings bound = Capabilities.Configure(known, Settings.KnownMonitor());
            Check(bound.Devices.Count == 2 && bound.MonitorId == "known-id" && bound.AspectModes.Length == 7, "legacy monitor binding preserves rules and known modes");
            Policy policy = new Policy(); DateTime now = DateTime.UtcNow;
            Check(policy.Observe(Read(17, 39), s, now) == -1, "debounce first read");
            Check(policy.Observe(Read(17, 39), s, now) == 35, "third device selected after stable read");
            policy.Attempted(now); policy.Missing();
            Check(policy.Observe(new Reading(), s, now) == -1, "disconnect suppresses writes");
            Check(policy.Observe(Read(17, 39), s, now.AddSeconds(11)) == -1, "reconnect requires stability");
            Check(policy.Observe(Read(17, 39), s, now.AddSeconds(12)) == 35, "retry after cooldown");
            policy.Attempted(now.AddSeconds(12)); Check(policy.Observe(Read(17, 39), s, now.AddSeconds(15)) == -1, "cooldown prevents repeated writes");
            policy.Attempted(now.AddSeconds(24)); Check(policy.Observe(Read(17, 39), s, now.AddMinutes(1)) == -1, "three attempt cap");
            policy.Observe(Read(15, 35), s, now); Check(policy.Observe(Read(15, 35), s, now) == 39, "source change resets retry cap");
            Check(policy.Observe(Read(15, 39), s, now) == -1, "confirmed ratio no redundant write");
            policy.Observe(Read(21, 35), s, now); Check(policy.Observe(Read(21, 35), s, now) == -1, "disabled fourth rule ignored");
            policy.Observe(Read(16, 35), s, now); Check(policy.Observe(Read(16, 35), s, now) == -1, "unregistered input ignored");
            string file = Path.Combine(Path.GetTempPath(), "aspect-test-" + Guid.NewGuid().ToString("N") + ".json");
            try {
                s.Save(file); old.Save(file); Check(Settings.Load(file).Devices[0].Input == 21, "atomic saved replacement");
                invalid = s.Copy(); invalid.Devices[1].Input = 15; Reject(delegate { invalid.Save(file); }, "invalid save rejected");
                Check(Settings.Load(file).Devices[0].Input == 21, "invalid save preserves original");
            } finally { if (File.Exists(file)) File.Delete(file); }
            using (SettingsForm form = new SettingsForm(Settings.KnownMonitor(), true))
            {
                form.AddDevice(); form.AddDevice(); form.AddDevice(); form.TrySave();
                Check(form.Result.Devices.Count == 4, "UI adds devices up to available inputs");
                Check(form.StartAtLogin && form.DialogResult == DialogResult.OK, "UI returns startup choice and saved result");
                Check(Settings.KnownMonitor().Devices.Count == 2, "UI does not mutate original settings");
            }
            using (SettingsForm form = new SettingsForm(s, false))
            {
                Settings draft = (Settings)typeof(SettingsForm).GetField("draft", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                draft.Devices[2].Input = draft.Devices[1].Input; form.TrySave();
                Check(form.Result == null, "UI displays duplicate port failure without closing");
                draft.Devices.RemoveAt(2); form.PreviewPage(1); form.PreviewPage(2); form.PreviewPage(0); form.TrySave();
                Check(form.Result.Devices.Count == 3, "UI removal and page navigation preserve edits");
            }
            using (SettingsForm form = new SettingsForm(Settings.KnownMonitor(), false))
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-32000, -32000); form.ShowInTaskbar = false;
                form.Scale(new System.Drawing.SizeF(1.5F, 1.5F)); form.Show(); Application.DoEvents(); form.NormalizeLayout();
                Check(Fits(form), "scaled UI tables fit their parent at 150 percent"); form.Close();
            }
            using (SettingsForm form = new SettingsForm(Settings.KnownMonitor(), false))
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-32000, -32000); form.ShowInTaskbar = false;
                form.Size = form.MinimumSize; form.Show(); Application.DoEvents(); form.NormalizeLayout();
                Check(Fits(form), "minimum window width keeps controls inside cards"); form.Close();
            }
            using (SettingsForm form = new SettingsForm(new Settings(), false, delegate(string op, MonitorCandidate target) {
                MonitorCandidate found = Candidate("Generic ultrawide fixture", "fake-monitor", 2);
                return Task.FromResult(new Reading { Ok = true, Monitors = new[] { found } });
            }))
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                ((Task)typeof(SettingsForm).GetMethod("Scan", flags).Invoke(form, null)).GetAwaiter().GetResult();
                ((Task)typeof(SettingsForm).GetMethod("SelectMonitor", flags).Invoke(form, new object[] { Candidate("Generic ultrawide fixture", "fake-monitor", 2) })).GetAwaiter().GetResult();
                ((Task)typeof(SettingsForm).GetMethod("Learn", flags).Invoke(form, new object[] { "Learned original" })).GetAwaiter().GetResult();
                form.AddDevice(); form.TrySave();
                Check(form.Result.MonitorId == "fake-monitor" && form.Result.Devices.Count == 1 && form.Result.AspectModes[0].Label == "Learned original", "UI discovery, selection, learning and save integration");
            }
            string resetRoot = Path.Combine(Path.GetTempPath(), "AspectResetTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(resetRoot, "data"));
            try
            {
                Settings.KnownMonitor().Save(Path.Combine(resetRoot, "settings.json"));
                foreach (string fixtureFile in new[] { "settings.json.bak", "status.json", "discovery.json", "keep.txt" }) File.WriteAllText(Path.Combine(resetRoot, fixtureFile), "fixture");
                foreach (string fixtureFile in new[] { "events.log", "events.log.1", "unrelated.txt" }) File.WriteAllText(Path.Combine(resetRoot, "data", fixtureFile), "fixture");
                bool unregistered = false;
                AppDataReset.Clear(resetRoot, delegate { unregistered = true; });
                Check(unregistered && !File.Exists(Path.Combine(resetRoot, "settings.json")), "reset unregisters startup and removes settings");
                Check(!File.Exists(Path.Combine(resetRoot, "settings.json.bak")) && !File.Exists(Path.Combine(resetRoot, "status.json")) && !File.Exists(Path.Combine(resetRoot, "discovery.json")), "reset removes settings backups and diagnostics");
                Check(!File.Exists(Path.Combine(resetRoot, "data", "events.log")) && !File.Exists(Path.Combine(resetRoot, "data", "events.log.1")), "reset removes current and rotated logs");
                Check(File.Exists(Path.Combine(resetRoot, "keep.txt")) && File.Exists(Path.Combine(resetRoot, "data", "unrelated.txt")), "reset preserves unrelated files");
                Settings.KnownMonitor().Save(Path.Combine(resetRoot, "settings.json"));
                Reject(delegate { AppDataReset.Clear(resetRoot, delegate { throw new IOException("fixture failure"); }); }, "reset surfaces cleanup failure");
                Check(Settings.Load(Path.Combine(resetRoot, "settings.json")).MonitorName.Length == 0, "failed cleanup cannot reload old monitor mapping");
            }
            finally { Directory.Delete(resetRoot, true); }
            Console.WriteLine("PASS: " + passed + " checks. No monitor commands issued."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
