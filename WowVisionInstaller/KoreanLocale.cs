using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WowVisionInstaller
{
    /// <summary>
    /// Korean test builds only: puts the Korean strings (koKR) into the installed
    /// WowVision. The release this installer downloads does not contain koKR yet,
    /// so the strings ship inside the exe (payload\koKR.zip, like the SAPI2SR
    /// payload) and are written after every run's install steps: a release update
    /// replaces the whole WowVision folder and would otherwise drop them.
    ///
    /// Two changes, both idempotent: locale\koKR is replaced with the payload's
    /// copy, and locale\modules.xml gets one Include line for it. Nothing happens
    /// when WowVision is not installed in that AddOns folder or is a developer
    /// symlink. The game picks koKR only on a Korean client; others are unaffected.
    /// </summary>
    public static class KoreanLocale
    {
        public const string IncludeLine = "<Include file=\"koKR/modules.xml\" />";
        private const string PayloadName = "koKR.zip";
        // The packaged release has CRLF line endings, and .NET's multiline "$" only
        // matches before "\n", so the "\r" is allowed explicitly and kept outside
        // the match (the new line goes in before it).
        private static readonly Regex LastInclude =
            new Regex(@"^([ \t]*)<Include file=""[^""]+"" />[ \t]*(?=\r?$)", RegexOptions.Multiline);

        /// <summary>The Include line after the last existing one; unchanged if already there.</summary>
        public static string AddInclude(string xml)
        {
            if (xml.Contains(IncludeLine)) return xml;
            var last = LastInclude.Matches(xml).Cast<Match>().LastOrDefault();
            if (last == null) throw new InvalidOperationException("locale\\modules.xml has no Include line.");
            int end = last.Index + last.Length;
            string newline = xml.Contains("\r\n") ? "\r\n" : "\n";
            return xml.Substring(0, end) + newline + last.Groups[1].Value + IncludeLine + xml.Substring(end);
        }

        public static Stream OpenPayload()
        {
            var asm = Assembly.GetExecutingAssembly();
            string res = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase));
            if (res == null) throw new InvalidOperationException("Embedded koKR payload not found.");
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
            string locale = Path.Combine(addonsFolder, Config.PrimaryFolder, "locale");
            string xmlPath = Path.Combine(locale, "modules.xml");
            if (!File.Exists(xmlPath) || AddonInstaller.IsSymlinked(addonsFolder, Config.PrimaryFolder))
                return false;

            // Extract next to the target first, then swap, so a failed extraction
            // leaves the previous koKR in place.
            string target = Path.Combine(locale, "koKR");
            string staging = Path.Combine(locale, "koKR.new-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
                    zip.ExtractToDirectory(staging);
                string extracted = Path.Combine(staging, "koKR");
                if (!File.Exists(Path.Combine(extracted, "modules.xml")))
                    throw new InvalidOperationException("The koKR payload has no koKR\\modules.xml.");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(extracted, target);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }

            string xml = File.ReadAllText(xmlPath);
            string updated = AddInclude(xml);
            if (updated != xml) File.WriteAllText(xmlPath, updated);
            Logger.Info($"koKR written into {target}");
            return true;
        }
    }
}
