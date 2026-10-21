// Brand-independent input-dependent aspect control. Uses only Windows APIs and .NET Framework.
// No G-Menu executable, service, assembly, or source code is required at runtime.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AspectSwitch
{
    internal static class Program
    {
        internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string SettingsPath = Path.Combine(Folder, "settings.json");
        internal static readonly string DataFolder = Path.Combine(Folder, "data");
        internal static readonly string LogPath = Path.Combine(DataFolder, "events.log");
        internal const string InstanceName = @"Local\AspectSwitch.Instance.v1";
        internal const string SettingsEventName = @"Local\AspectSwitch.Settings.v1";
        internal const string StopName = @"Local\AspectSwitch.Stop.v1";
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object LogLock = new object();

        internal static Settings LoadSettings()
        {
            return Settings.Load(SettingsPath);
        }
        internal static void SaveSettings(Settings settings) { settings.Save(SettingsPath); }
        internal static string Version { get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }
        internal static void Log(string kind, string message)
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(DataFolder);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                {
                    string old = LogPath + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogPath, old);
                }
                File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("o") + "\t" + kind + "\t" + message + Environment.NewLine, Encoding.UTF8);
            }
        }
        internal static Reading CallWorker(string operation, int expectedInput, int value, string token = "-")
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath,
                    "--worker " + operation + " " + expectedInput + " " + value + " " + token);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;
                using (Process p = Process.Start(info))
                {
                    Task<string> output = p.StandardOutput.ReadToEndAsync();
                    Task<string> errors = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(operation == "inspect" ? 20000 : 8000))
                    {
                        try { p.Kill(); p.WaitForExit(1000); } catch { }
                        return new Reading { Error = "모니터 응답 시간 초과; 다음 주기에 재시도" };
                    }
                    if (p.ExitCode != 0) return new Reading { Error = "Worker failed: " + errors.Result.Trim() };
                    Reading result = new JavaScriptSerializer().Deserialize<Reading>(output.Result.Trim());
                    return result ?? new Reading { Error = "Worker returned no result" };
                }
            }
            catch (Exception e) { return new Reading { Error = e.Message }; }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--worker")
                {
                    if (args.Length != 5) throw new Exception("Invalid worker arguments");
                    // Serialize DDC access across compatible clients.
                    using (Mutex bus = new Mutex(false, @"Local\AGPAspectSwitch.DDC.v1"))
                    {
                        bool locked = false;
                        try
                        {
                            try { locked = bus.WaitOne(4000); } catch (AbandonedMutexException) { locked = true; }
                            Reading reading = locked ? Native.Execute(LoadSettings(), args[1], Int32.Parse(args[2]), Int32.Parse(args[3]), args[4]) : new Reading { Error = "DDC busy" };
                            using (StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))) output.WriteLine(Json.Serialize(reading));
                        }
                        finally { if (locked) bus.ReleaseMutex(); }
                    }
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--stop")
                {
                    EventWaitHandle stop;
                    if (EventWaitHandle.TryOpenExisting(StopName, out stop)) using (stop) stop.Set();
                    return 0;
                }
                if (args.Length > 0 && (args[0] == "--status" || args[0] == "--discover"))
                {
                    if (args.Length != 3 || args[1] != "--out") throw new Exception("Use --status --out <file>");
                    string result = Json.Serialize(CallWorker(args[0] == "--discover" ? "list" : "probe", -1, -1));
                    File.WriteAllText(args[2], result, new UTF8Encoding(false));
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--preview-settings")
                {
                    string extra = args.Length >= 3 && args[1] == "--out" ? " --render \"" + Path.GetFullPath(args[2]) + "\"" : "";
                    Process.Start(new ProcessStartInfo(SettingsWindowSession.Executable, "--preview" + extra) { UseShellExecute = false });
                    return 0;
                }
                if (args.Length > 0 && args[0] != "--settings") throw new Exception("Unknown argument");
                bool created;
                using (Mutex mutex = new Mutex(true, InstanceName, out created))
                {
                    if (!created)
                    {
                        if (args.Length > 0 && args[0] == "--settings")
                        { EventWaitHandle show; if (EventWaitHandle.TryOpenExisting(SettingsEventName, out show)) using (show) show.Set(); }
                        return 0;
                    }
                    try
                    {
                        Settings settings = LoadSettings();
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName))
                        { stop.Reset(); Application.Run(new TrayContext(settings, stop, args.Length > 0 || settings.MonitorName.Length == 0)); }
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception e)
            {
                if (args.Length > 0) { try { Console.Error.WriteLine(e.ToString()); } catch { } }
                else MessageBox.Show(e.Message, "Aspect Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

}
