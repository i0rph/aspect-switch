using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
namespace AspectSwitch
{
    public sealed class MonitorCandidate
    {
        public string Name;
        public string Id;
        public bool InputReadable;
        public bool AspectReadable;
        public int CurrentInput = -1;
        public int CurrentAspect = -1;
        public string Capabilities;
        public string Error;
        public bool Ambiguous;
        public override string ToString() { return Name + (Ambiguous ? " · 식별 불가" : ""); }
        public bool CanConfigure { get { return !Ambiguous && !String.IsNullOrWhiteSpace(Name) && InputReadable && AspectReadable && CurrentInput >= 0 && CurrentInput <= 65535 && CurrentAspect >= 0 && CurrentAspect <= 65535; } }
    }
    public static class Capabilities
    {
        // Parse balanced VCP groups; nested values must never be mistaken for feature codes.
        public static int[] Values(string text, int code)
        {
            if (String.IsNullOrEmpty(text) || text.Length > 65536) return new int[0];
            Match start = Regex.Match(text, @"\bvcp\s*\(", RegexOptions.IgnoreCase);
            if (!start.Success) return new int[0];
            int p = start.Index + start.Length;
            while (p < text.Length)
            {
                while (p < text.Length && Char.IsWhiteSpace(text[p])) p++;
                if (p == text.Length || text[p] == ')') break;
                int begin = p; while (p < text.Length && Uri.IsHexDigit(text[p])) p++;
                int feature;
                if (begin == p || !Int32.TryParse(text.Substring(begin, p - begin), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out feature)) return new int[0];
                while (p < text.Length && Char.IsWhiteSpace(text[p])) p++;
                if (p < text.Length && text[p] == '(')
                {
                    int inner = ++p, depth = 1;
                    while (p < text.Length && depth > 0) { if (text[p] == '(') depth++; if (text[p] == ')') depth--; p++; }
                    if (depth != 0) return new int[0];
                    if (feature == code)
                    {
                        List<int> values = new List<int>(); string group = text.Substring(inner, p - inner - 1);
                        foreach (string token in group.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int value; if (!Int32.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) || value < 0 || value > 65535) return new int[0];
                            if (!values.Contains(value)) values.Add(value);
                        }
                        return values.ToArray();
                    }
                }
            }
            return new int[0];
        }
        public static Settings Configure(MonitorCandidate monitor, Settings previous)
        {
            if (!monitor.CanConfigure) throw new Exception("현재 입력(0x60)과 화면 비율(0x86)을 모두 읽을 수 있고 고유하게 식별되는 모니터가 필요합니다. DDC/CI와 연결 상태를 확인하세요.");
            bool same = previous.MonitorId.Length > 0 ? String.Equals(previous.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase) : String.Equals(previous.MonitorName, monitor.Name, StringComparison.OrdinalIgnoreCase);
            Settings s = same ? previous.Copy() : new Settings { PollMilliseconds = previous.PollMilliseconds, AutoEnabled = previous.AutoEnabled };
            s.MonitorName = monitor.Name; s.MonitorId = monitor.Id;
            List<Choice> inputs = new List<Choice>();
            foreach (int value in Values(monitor.Capabilities, 0x60)) inputs.Add(new Choice(value, (value == 21 && !Catalog.IsKnown(monitor.Name) ? "입력 0x15" : Catalog.Label(Catalog.Inputs, value))));
            if (!Catalog.Contains(inputs.ToArray(), monitor.CurrentInput)) inputs.Add(new Choice(monitor.CurrentInput, (monitor.CurrentInput == 21 && !Catalog.IsKnown(monitor.Name) ? "입력 0x15" : Catalog.Label(Catalog.Inputs, monitor.CurrentInput))));
            if (same) foreach (Choice input in previous.AvailableInputs)
            {
                if (!Catalog.Contains(inputs.ToArray(), input.Value)) inputs.Add(input);
                else foreach (Choice current in inputs) if (current.Value == input.Value) current.Label = input.Label;
            }
            if (Catalog.IsKnown(monitor.Name)) foreach (Choice input in Settings.KnownMonitor().AvailableInputs) if (!Catalog.Contains(inputs.ToArray(), input.Value)) inputs.Add(input);
            s.AvailableInputs = inputs.ToArray();
            if (Catalog.IsKnown(monitor.Name) && !same) s.AspectModes = Catalog.Clone(Catalog.Aspects);
            if (!Catalog.Contains(s.AspectModes, monitor.CurrentAspect))
            { List<Choice> modes = new List<Choice>(s.AspectModes); modes.Add(new Choice(monitor.CurrentAspect, "현재 화면 비율")); s.AspectModes = modes.ToArray(); }
            s.Validate(); return s;
        }
        public static void Learn(Settings settings, MonitorCandidate monitor, string label)
        {
            if (!monitor.CanConfigure || !String.Equals(settings.MonitorName, monitor.Name, StringComparison.OrdinalIgnoreCase) || (settings.MonitorId.Length > 0 && !String.Equals(settings.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase)))
                throw new Exception("선택한 모니터의 현재 값을 읽지 못했습니다.");
            if (String.IsNullOrWhiteSpace(label) || label.Trim().Length > 80) throw new Exception("화면 비율 이름을 입력하세요. (최대 80자)");
            List<Choice> modes = new List<Choice>();
            foreach (Choice mode in settings.AspectModes) if (mode.Value != monitor.CurrentAspect) modes.Add(mode);
            modes.Add(new Choice(monitor.CurrentAspect, label.Trim())); settings.AspectModes = modes.ToArray();
            if (!Catalog.Contains(settings.AvailableInputs, monitor.CurrentInput))
            { List<Choice> inputs = new List<Choice>(settings.AvailableInputs); inputs.Add(new Choice(monitor.CurrentInput, (monitor.CurrentInput == 21 && !Catalog.IsKnown(monitor.Name) ? "입력 0x15" : Catalog.Label(Catalog.Inputs, monitor.CurrentInput)))); settings.AvailableInputs = inputs.ToArray(); }
        }
    }
}
