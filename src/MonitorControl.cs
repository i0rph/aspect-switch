// Independent Windows DDC/CI implementation. No G-Menu binaries or services are used.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
namespace AspectSwitch
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Physical { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayDevice {
            public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        }
        internal delegate bool EnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr rect, EnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice result, uint flags);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] Physical[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool DestroyPhysicalMonitor(IntPtr handle);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr handle, byte code, out uint type, out uint value, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool SetVCPFeature(IntPtr handle, byte code, uint value);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetCapabilitiesStringLength(IntPtr handle, out uint length);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)] internal static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr handle, StringBuilder text, uint length);
        private sealed class Target { internal Physical Physical; internal MonitorCandidate Info; }
        internal static bool Read(IntPtr handle, byte code, out int value, out string error)
        {
            int lastError = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 100 : 250); uint type, current, maximum;
                if (GetVCPFeatureAndVCPFeatureReply(handle, code, out type, out current, out maximum) && current <= 65535)
                { value = (int)current; error = null; return true; }
                lastError = Marshal.GetLastWin32Error();
            }
            value = -1; error = "VCP " + code.ToString("X2") + " read failed (0x" + lastError.ToString("X8") + ")"; return false;
        }
        private static List<Target> Enumerate(List<Physical> handles)
        {
            List<Target> targets = new List<Target>();
            bool ok = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data) {
                uint count; if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0 || count > 32) return true;
                Physical[] physical = new Physical[count]; if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
                handles.AddRange(physical);
                MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                List<string> ids = new List<string>();
                if (GetMonitorInfo(monitor, ref info)) for (uint i = 0; i < 32; i++)
                {
                    DisplayDevice device = new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
                    if (!EnumDisplayDevices(info.Device, i, ref device, 1)) break;
                    if ((device.Flags & 1) != 0 && !String.IsNullOrEmpty(device.Id)) ids.Add(device.Id);
                }
                foreach (Physical p in physical)
                {
                    // Do not guess correspondence for mirrored / tiled physical displays.
                    string id = count == 1 && ids.Count == 1 ? ids[0] : "";
                    targets.Add(new Target { Physical = p, Info = new MonitorCandidate { Name = p.Description, Id = id, Ambiguous = count != 1 || ids.Count > 1 } });
                }
                return true;
            }, IntPtr.Zero);
            if (!ok) throw new Exception("Display enumeration failed");
            return targets;
        }
        internal static Reading Execute(Settings settings, string operation, int expectedInput, int value, string token)
        {
            List<Physical> handles = new List<Physical>();
            try
            {
                List<Target> all = Enumerate(handles);
                if (operation == "list")
                {
                    List<MonitorCandidate> found = new List<MonitorCandidate>();
                    foreach (Target item in all) found.Add(item.Info);
                    return new Reading { Ok = true, Monitors = found.ToArray() };
                }
                string name = settings.MonitorName, id = settings.MonitorId;
                if (operation == "inspect")
                {
                    MonitorCandidate selection = Program.Json.Deserialize<MonitorCandidate>(Encoding.UTF8.GetString(Convert.FromBase64String(token)));
                    name = selection.Name; id = selection.Id;
                }
                if (String.IsNullOrEmpty(name)) return new Reading { Error = "설정에서 모니터를 선택하세요." };
                List<Target> matches = new List<Target>();
                foreach (Target item in all)
                    if (String.Equals(item.Info.Name, name, StringComparison.OrdinalIgnoreCase) && (String.IsNullOrEmpty(id) || String.Equals(item.Info.Id, id, StringComparison.OrdinalIgnoreCase))) matches.Add(item);
                if (matches.Count != 1 || matches[0].Info.Ambiguous) return new Reading { Error = matches.Count == 0 ? "대상 모니터 연결 대기" : "모니터를 고유하게 식별할 수 없습니다. 모니터를 다시 선택하세요." };
                Target target = matches[0];
                Reading r = new Reading { Monitor = target.Info.Name, MonitorId = target.Info.Id };
                string inputError, aspectError;
                bool inputOk = Read(target.Physical.Handle, 0x60, out r.Input, out inputError);
                bool aspectOk = Read(target.Physical.Handle, 0x86, out r.Aspect, out aspectError);
                r.Ok = inputOk && aspectOk; r.Error = inputError ?? aspectError;
                if (operation == "inspect")
                {
                    MonitorCandidate info = target.Info; info.InputReadable = inputOk; info.AspectReadable = aspectOk; info.CurrentInput = r.Input; info.CurrentAspect = r.Aspect; info.Error = r.Error;
                    uint length;
                    if (GetCapabilitiesStringLength(target.Physical.Handle, out length) && length > 0 && length <= 65536)
                    { StringBuilder text = new StringBuilder((int)length); if (CapabilitiesRequestAndCapabilitiesReply(target.Physical.Handle, text, length)) info.Capabilities = text.ToString(); }
                    r.Monitors = new[] { info }; return r;
                }
                if (!r.Ok || operation == "probe") return r;
                // A pending command is bound to the exact configuration that scheduled it.
                if (token != settings.Revision()) { r.Skipped = true; return r; }
                byte code;
                if (operation == "aspect")
                {
                    int actualInput; string error;
                    if (!Read(target.Physical.Handle, 0x60, out actualInput, out error)) { r.Ok = false; r.Error = error; return r; }
                    if (!settings.AllowsAspectWrite(expectedInput, actualInput, value) || r.Aspect == value) { r.Input = actualInput; r.Skipped = true; return r; }
                    code = 0x86;
                }
                else if (operation == "input")
                {
                    if (!settings.HasInput(value) || !Catalog.Contains(settings.AvailableInputs, value)) throw new Exception("등록되지 않은 입력입니다.");
                    if (r.Input == value) { r.Skipped = true; return r; } code = 0x60;
                }
                else throw new Exception("Unknown operation");
                // Re-read the persisted configuration immediately before any write.
                if (token != Program.LoadSettings().Revision()) { r.Skipped = true; return r; }
                Thread.Sleep(100); r.Wrote = SetVCPFeature(target.Physical.Handle, code, (uint)value);
                if (!r.Wrote) { r.Ok = false; r.Error = "VCP " + code.ToString("X2") + " write failed (0x" + Marshal.GetLastWin32Error().ToString("X8") + ")"; }
                return r;
            }
            finally { foreach (Physical p in handles) DestroyPhysicalMonitor(p.Handle); }
        }
    }
}
