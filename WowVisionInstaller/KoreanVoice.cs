using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace WowVisionInstaller
{
    /// <summary>
    /// Korean test builds only: puts the Korean voice pack (Yuna) into the installed
    /// WowVision, next to the release's Matthew and Joanna. The voice alerts (compass
    /// direction, target health, range, enemy count) play its recordings when the
    /// player picks Yuna as the alert's voice pack; the default stays Matthew.
    ///
    /// Same shape as KoreanLocale: the recordings ship inside the exe
    /// (payload\koVoice.zip) and are written after every run's install steps, since
    /// a release update replaces the whole WowVision folder. Two idempotent changes:
    /// audio\voice\Yuna is replaced with the payload's copy, and audio\voice.lua gets
    /// one registration line for it. Nothing happens when WowVision is not installed
    /// in that AddOns folder or is a developer symlink.
    /// </summary>
    public static class KoreanVoice
    {
        public const string PackName = "Yuna";
        public const string RegisterLine =
            "WowVision.audio:registerVoicePack({ key = \"Yuna\", label = \"Yuna\", filePath = \"Interface/AddOns/WowVision/audio/voice/Yuna\" }) -- Korean voice pack";
        private const string PayloadName = "koVoice.zip";

        /// <summary>The registration line on its own line at the end; unchanged if already there.</summary>
        public static string AddRegistration(string lua)
        {
            if (lua.Contains(RegisterLine)) return lua;
            string newline = lua.Contains("\r\n") ? "\r\n" : "\n";
            string head = lua.Length == 0 || lua.EndsWith("\n") ? lua : lua + newline;
            return head + RegisterLine + newline;
        }

        public static Stream OpenPayload()
        {
            var asm = Assembly.GetExecutingAssembly();
            string res = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase));
            if (res == null) throw new InvalidOperationException("Embedded Korean voice payload not found.");
            return asm.GetManifestResourceStream(res);
        }

        /// <summary>Applies the embedded payload. Returns whether anything was written.</summary>
        public static bool Apply(string addonsFolder)
        {
            using (var payload = OpenPayload())
                return ApplyFrom(payload, addonsFolder);
        }

        public static bool ApplyFrom(Stream payload, string addonsFolder)
        {
            string audio = Path.Combine(addonsFolder, Config.PrimaryFolder, "audio");
            string luaPath = Path.Combine(audio, "voice.lua");
            if (!File.Exists(luaPath) || AddonInstaller.IsSymlinked(addonsFolder, Config.PrimaryFolder))
                return false;

            // Extract next to the target first, then swap, so a failed extraction
            // leaves the previous pack in place.
            string voice = Path.Combine(audio, "voice");
            string target = Path.Combine(voice, PackName);
            string staging = Path.Combine(voice, PackName + ".new-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
                    zip.ExtractToDirectory(staging);
                string extracted = Path.Combine(staging, PackName);
                if (!File.Exists(Path.Combine(extracted, "directions", "east.mp3")))
                    throw new InvalidOperationException("The Korean voice payload has no Yuna\\directions\\east.mp3.");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(extracted, target);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }

            string lua = File.ReadAllText(luaPath);
            string updated = AddRegistration(lua);
            if (updated != lua) File.WriteAllText(luaPath, updated);
            Logger.Info($"Korean voice pack written into {target}");
            return true;
        }
    }
}
