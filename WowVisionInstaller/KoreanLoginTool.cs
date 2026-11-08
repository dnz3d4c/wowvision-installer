using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace WowVisionInstaller
{
    /// <summary>
    /// Korean test builds only: puts the Korean WoW Login Tool files over the tool
    /// that <see cref="LoginToolInstaller"/> deployed from the Sku release. The
    /// release has no Korean strings, so without this the login tool speaks the
    /// language of the release only.
    ///
    /// Same shape as KoreanLocale and KoreanVoice: the files ship inside the exe
    /// (payload\koLoginTool.zip, built by wowwork tools.installer_loc payload from the
    /// Sku-WoW-Addon-TBC ko branch) and are written after the login tool step on every
    /// run, because the login tool step skips an already current tool and a release
    /// upgrade replaces the files. What the payload holds: v2\ (the driver), data\*.ini
    /// except settings.ini, data\localization\, and the rebuilt helper\SkuLoginSense.exe.
    /// The user's data\settings.ini is never written, even if a payload carried it.
    ///
    /// The payload is checked before anything is written: it must hold the Korean
    /// strings and every entry must stay inside the tool folder. Nothing happens when
    /// the login tool is not installed there.
    /// </summary>
    public static class KoreanLoginTool
    {
        private const string PayloadName = "koLoginTool.zip";
        private const string KoreanStrings = "data/localization/koKR.txt";
        private const string UserSettings = "data/settings.ini";

        public static Stream OpenPayload()
        {
            var asm = Assembly.GetExecutingAssembly();
            string res = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase));
            if (res == null) throw new InvalidOperationException("Embedded Korean login tool payload not found.");
            return asm.GetManifestResourceStream(res);
        }

        /// <summary>Applies the embedded payload to the login tool of this AddOns folder's WoW base.</summary>
        public static bool Apply(string addonsFolder)
        {
            using (var payload = OpenPayload())
                return ApplyFrom(payload, LoginToolInstaller.ResolveToolDir(addonsFolder));
        }

        /// <summary>Writes the payload into <paramref name="toolDir"/>. Returns whether anything was written.</summary>
        public static bool ApplyFrom(Stream payload, string toolDir)
        {
            if (!File.Exists(Path.Combine(toolDir, "v2", "START.ahk")))
                return false;

            string root = Path.GetFullPath(toolDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                var files = zip.Entries.Where(e => !e.FullName.EndsWith("/")).ToList();
                if (!files.Any(e => e.FullName == KoreanStrings))
                    throw new InvalidOperationException("The Korean login tool payload has no " + KoreanStrings + ".");
                var targets = new List<KeyValuePair<ZipArchiveEntry, string>>();
                foreach (var e in files)
                {
                    if (e.FullName.Equals(UserSettings, StringComparison.OrdinalIgnoreCase)) continue;
                    string dest = Path.GetFullPath(Path.Combine(toolDir, e.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The Korean login tool payload points outside the tool folder: " + e.FullName);
                    targets.Add(new KeyValuePair<ZipArchiveEntry, string>(e, dest));
                }

                foreach (var t in targets)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(t.Value));
                    // Next to the target first, then over it, so a failed write
                    // never leaves a half-written file the tool would load.
                    string tmp = t.Value + ".new-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        t.Key.ExtractToFile(tmp);
                        File.Copy(tmp, t.Value, true);
                    }
                    finally
                    {
                        try { File.Delete(tmp); } catch { }
                    }
                }
                Logger.Info($"Korean login tool files written into {toolDir} ({targets.Count} files)");
            }
            return true;
        }
    }
}
