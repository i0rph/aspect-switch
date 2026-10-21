using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AspectSwitch
{
    // The tray owns persistence; WinUI edits an isolated draft and returns it on Save.
    internal sealed class SettingsWindowSession : IDisposable
    {
        public sealed class Request
        {
            public Settings Settings { get; set; }
            public bool StartAtLogin { get; set; }
            public string ControllerPath { get; set; }
            public string SessionId { get; set; }
        }
        public sealed class Response
        {
            public bool ResetAllData { get; set; }
            public Settings Settings { get; set; }
            public bool StartAtLogin { get; set; }
            public string SessionId { get; set; }
        }
        private readonly string id = Guid.NewGuid().ToString("N");
        private readonly string folder;
        private Process process;
        internal static string Executable { get { return Path.Combine(Program.Folder, "ui-runtime", "AspectSwitch.Settings.exe"); } }
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        internal SettingsWindowSession() { folder = Path.Combine(Path.GetTempPath(), "AspectSwitch-" + id); }
        internal async Task<Response> Edit(Settings settings, bool startup)
        {
            if (!File.Exists(Executable)) throw new FileNotFoundException("WinUI 설정 파일이 없습니다. 배포 ZIP의 ui-runtime 폴더를 함께 복사하세요.", Executable);
            Directory.CreateDirectory(folder);
            string request = Path.Combine(folder, "request.json"), response = Path.Combine(folder, "response.json");
            File.WriteAllText(request, Program.Json.Serialize(new Request { Settings = settings.Copy(), StartAtLogin = startup, ControllerPath = System.Windows.Forms.Application.ExecutablePath, SessionId = id }), new UTF8Encoding(false));
            process = Process.Start(new ProcessStartInfo(Executable, "--request \"" + request + "\" --response \"" + response + "\"") { UseShellExecute = false, CreateNoWindow = true });
            await Task.Run(delegate { process.WaitForExit(); });
            if (process.ExitCode != 0) throw new Exception("WinUI 설정 창을 실행하지 못했습니다. 임시 폴더의 AspectSwitch-ui-error.txt를 확인하세요.");
            if (!File.Exists(response)) return null;
            Response result = Program.Json.Deserialize<Response>(File.ReadAllText(response, Encoding.UTF8));
            if (result == null || result.Settings == null || result.SessionId != id) throw new Exception("설정 응답이 올바르지 않습니다.");
            result.Settings.Validate(); return result;
        }
        internal void ActivateWindow()
        {
            if (process == null || process.HasExited) return;
            process.Refresh(); IntPtr window = process.MainWindowHandle;
            if (window == IntPtr.Zero) return;
            if (IsIconic(window)) ShowWindow(window, 9);
            SetForegroundWindow(window);
        }
        internal void Close() { if (process != null && !process.HasExited) process.CloseMainWindow(); }
        public void Dispose()
        {
            if (process != null) process.Dispose();
            // Delete only this session's own IPC files, never user settings.
            try
            {
                foreach (string name in new[] { "request.json", "response.json", "response.json.tmp" }) File.Delete(Path.Combine(folder, name));
                if (Directory.Exists(folder)) Directory.Delete(folder);
            }
            catch (IOException) { }
        }
    }
}
