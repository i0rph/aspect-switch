using System;
using System.IO;
namespace AspectSwitch
{
    internal static class AppDataReset
    {
        // Only files owned by this app are removed. Never recurse through the install folder.
        internal static void Clear(string folder, Action unregisterStartup)
        {
            string root = Path.GetFullPath(folder);
            string data = Path.Combine(root, "data");
            if (Directory.Exists(data) && (File.GetAttributes(data) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("데이터 폴더가 다른 위치로 연결되어 있어 초기화를 중단했습니다.");
            // If cleanup fails later, a restart must not restore the old monitor mapping.
            new Settings().Save(Path.Combine(root, "settings.json"));
            unregisterStartup();
            foreach (string file in Directory.GetFiles(root, "settings.json.*", SearchOption.TopDirectoryOnly)) File.Delete(file);
            foreach (string name in new[] { "settings.json", "status.json", "discovery.json" }) File.Delete(Path.Combine(root, name));
            if (Directory.Exists(data))
            {
                foreach (string name in new[] { "events.log", "events.log.1" }) File.Delete(Path.Combine(data, name));
                if (Directory.GetFileSystemEntries(data).Length == 0) Directory.Delete(data);
            }
        }
    }
}
