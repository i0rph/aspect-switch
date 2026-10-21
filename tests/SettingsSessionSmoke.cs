// Interactive protocol check. Contains no monitor API or registry access.
using System;
using System.IO;
using System.Web.Script.Serialization;
namespace AspectSwitch
{
    internal static class Program
    {
        internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        [STAThread] private static int Main(string[] args)
        {
            if (args.Length != 1) { Console.Error.WriteLine("This isolated UI test does not access hardware. Open AspectSwitch.exe --settings for normal use."); return 1; } // In particular, reject hardware worker requests.
            try
            {
                using (var session = new SettingsWindowSession())
                {
                    var result = session.Edit(Settings.KnownMonitor(), false).GetAwaiter().GetResult();
                    if (result == null) throw new Exception("Save was cancelled.");
                    if (result.Settings.Devices.Count != 2 || result.Settings.Devices[1].Name != "Nintendo Switch") throw new Exception("IPC settings roundtrip failed.");
                    result.Settings.Validate();
                    File.WriteAllText(args[0], "PASS .NET Framework -> WinUI -> .NET Framework saved draft; no hardware or registry access.");
                    return 0;
                }
            }
            catch (Exception e) { File.WriteAllText(args[0], e.ToString()); return 1; }
        }
    }
}
