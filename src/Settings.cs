using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace AspectSwitch
{
    public sealed class Choice
    {
        public int Value;
        public string Label;
        public Choice() { }
        public Choice(int value, string label) { Value = value; Label = label; }
        public override string ToString() { return Label; }
    }
    public static class Catalog
    {
        public static readonly Choice[] Inputs = {
            new Choice(1, "VGA"), new Choice(3, "DVI 1"), new Choice(4, "DVI 2"),
            new Choice(15, "DisplayPort 1"), new Choice(16, "DisplayPort 2"),
            new Choice(17, "HDMI 1"), new Choice(18, "HDMI 2"), new Choice(21, "USB-C")
        };
        // This exact model has a physically verified vendor-specific mapping.
        public static readonly Choice[] Aspects = {
            new Choice(39, "전체 화면 (21:9)"), new Choice(40, "1:1 (21:9)"),
            new Choice(35, "원본 화면 비율"), new Choice(41, "전체 화면 (4:3)"),
            new Choice(42, "1:1 (4:3)"), new Choice(44, "27인치"), new Choice(43, "24.5인치")
        };
        public static bool Contains(Choice[] list, int value)
        { if (list != null) foreach (Choice c in list) if (c != null && c.Value == value) return true; return false; }
        public static string Label(Choice[] list, int value)
        { if (list != null) foreach (Choice c in list) if (c != null && c.Value == value) return c.Label; return "값 0x" + value.ToString("X2"); }
        public static Choice[] Clone(Choice[] list)
        { List<Choice> result = new List<Choice>(); foreach (Choice c in list) result.Add(new Choice(c.Value, c.Label)); return result.ToArray(); }
        public static bool IsKnown(string name)
        { return String.Equals(name, "AOC AGP346UCSD", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "AGP346UCSD", StringComparison.OrdinalIgnoreCase); }

    }
    public sealed class DeviceRule
    {
        public string Name = "새 기기";
        public int Input;
        public int Aspect;
        public bool Enabled = true;
        public DeviceRule Copy() { return (DeviceRule)MemberwiseClone(); }
    }
    public sealed class Settings
    {
        public int SchemaVersion = 3;
        public string MonitorName = "";
        public string MonitorId = "";
        public Choice[] AvailableInputs = new Choice[0];
        public Choice[] AspectModes = new Choice[0];
        public List<DeviceRule> Devices = new List<DeviceRule>();
        public int PollMilliseconds = 2000;
        public bool AutoEnabled = true;
        public static Settings KnownMonitor()
        {
            return new Settings { MonitorName = "AOC AGP346UCSD", AvailableInputs = new[] { new Choice(15, "DisplayPort"), new Choice(17, "HDMI 1"), new Choice(18, "HDMI 2"), new Choice(21, "USB-C") },
                AspectModes = Catalog.Clone(Catalog.Aspects), Devices = new List<DeviceRule> {
                    new DeviceRule { Name = "PC", Input = 15, Aspect = 39 }, new DeviceRule { Name = "Nintendo Switch", Input = 18, Aspect = 35 } } };
        }
        private static void ValidateChoices(Choice[] list, string label)
        {
            if (list == null || list.Length > 256) throw new Exception(label + " 목록이 올바르지 않습니다.");
            HashSet<int> seen = new HashSet<int>();
            foreach (Choice c in list)
                if (c == null || c.Value < 0 || c.Value > 65535 || String.IsNullOrWhiteSpace(c.Label) || c.Label.Length > 80 || !seen.Add(c.Value))
                    throw new Exception(label + " 목록에 잘못되거나 중복된 값이 있습니다.");
        }
        public void Validate()
        {
            if (SchemaVersion != 3) throw new Exception("지원하지 않는 설정 파일 버전입니다.");
            if (MonitorName == null || MonitorId == null || MonitorName.Length > 128 || MonitorId.Length > 512) throw new Exception("모니터 정보가 올바르지 않습니다.");
            ValidateChoices(AvailableInputs, "입력"); ValidateChoices(AspectModes, "화면 비율");
            if (Devices == null || Devices.Count > 64) throw new Exception("기기는 최대 64개까지 등록할 수 있습니다.");
            if (Devices.Count > 0 && MonitorName.Length == 0) throw new Exception("먼저 모니터를 선택하세요.");
            HashSet<int> inputs = new HashSet<int>();
            foreach (DeviceRule rule in Devices)
            {
                if (rule == null || String.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 48) throw new Exception("기기 이름은 1~48자로 입력하세요.");
                if (!Catalog.Contains(AvailableInputs, rule.Input)) throw new Exception(rule.Name + ": 지원 입력을 선택하세요.");
                if (!Catalog.Contains(AspectModes, rule.Aspect)) throw new Exception(rule.Name + ": 등록된 화면 비율을 선택하세요.");
                if (!inputs.Add(rule.Input)) throw new Exception("한 입력에는 기기를 하나만 등록할 수 있습니다. (" + Catalog.Label(AvailableInputs, rule.Input) + ")");
            }
            if (PollMilliseconds < 1000 || PollMilliseconds > 10000) throw new Exception("확인 간격은 1~10초로 설정하세요.");
        }
        public int DesiredAspect(int input)
        { foreach (DeviceRule rule in Devices) if (rule.Enabled && rule.Input == input) return rule.Aspect; return -1; }
        public bool HasInput(int input)
        { foreach (DeviceRule rule in Devices) if (rule.Input == input) return true; return false; }
        public bool AllowsAspectWrite(int expectedInput, int actualInput, int value)
        { return AutoEnabled && expectedInput == actualInput && DesiredAspect(actualInput) == value && Catalog.Contains(AspectModes, value); }
        public string Revision()
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(ToJson()))).Replace("-", "");
        }
        public Settings Copy()
        {
            Settings copy = (Settings)MemberwiseClone(); copy.AvailableInputs = Catalog.Clone(AvailableInputs); copy.AspectModes = Catalog.Clone(AspectModes);
            copy.Devices = new List<DeviceRule>(); foreach (DeviceRule rule in Devices) copy.Devices.Add(rule.Copy()); return copy;
        }
        public static Settings FromJson(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> fields = serializer.Deserialize<Dictionary<string, object>>(json);
            if (fields == null) throw new Exception("설정 파일이 비어 있습니다.");
            int version = fields.ContainsKey("SchemaVersion") ? Convert.ToInt32(fields["SchemaVersion"]) : 1;
            Settings settings;
            if (version == 1 || version == 2)
            {
                settings = KnownMonitor();
                if (fields.ContainsKey("MonitorName")) settings.MonitorName = Convert.ToString(fields["MonitorName"]);
                if (!Catalog.IsKnown(settings.MonitorName)) throw new Exception("이전 버전 설정의 모니터를 확인할 수 없습니다.");
                if (fields.ContainsKey("PcInput")) settings.Devices[0].Input = Convert.ToInt32(fields["PcInput"]);
                string consoleKey = fields.ContainsKey("ConsoleInput") ? "ConsoleInput" : "SwitchInput";
                if (fields.ContainsKey(consoleKey)) settings.Devices[1].Input = Convert.ToInt32(fields[consoleKey]);
                if (fields.ContainsKey("PcAspect")) settings.Devices[0].Aspect = Convert.ToInt32(fields["PcAspect"]);
                if (fields.ContainsKey("ConsoleAspect")) settings.Devices[1].Aspect = Convert.ToInt32(fields["ConsoleAspect"]);
                if (fields.ContainsKey("PollMilliseconds")) settings.PollMilliseconds = Convert.ToInt32(fields["PollMilliseconds"]);
                if (fields.ContainsKey("AutoEnabled")) settings.AutoEnabled = Convert.ToBoolean(fields["AutoEnabled"]);
            }
            else if (version == 3) settings = serializer.Deserialize<Settings>(json);
            else throw new Exception("지원하지 않는 설정 파일 버전입니다.");
            settings.Validate(); return settings;
        }
        public string ToJson() { Validate(); return new JavaScriptSerializer().Serialize(this); }
        public static Settings Load(string path)
        { return File.Exists(path) ? FromJson(File.ReadAllText(path, Encoding.UTF8)) : new Settings(); }
        public void Save(string path)
        {
            string json = ToJson(); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, json + Environment.NewLine, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
