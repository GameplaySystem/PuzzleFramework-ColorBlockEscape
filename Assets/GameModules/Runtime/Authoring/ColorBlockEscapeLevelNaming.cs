using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ColorBlockEscape.Runtime.Authoring
{
    /// <summary>CBE-owned canonical Level N identity and authored JSON path policy.</summary>
    public static class ColorBlockEscapeLevelNaming
    {
        public const string DefaultExportDirectory =
            "Assets/Resources/ColorBlockEscape/Levels";

        public static bool TryParseLevelNumber(string rawValue, out int levelNumber)
        {
            return int.TryParse(rawValue?.Trim(), NumberStyles.None,
                       CultureInfo.InvariantCulture, out levelNumber) &&
                   levelNumber > 0;
        }

        public static string BuildCanonicalLabel(int levelNumber)
        {
            if (levelNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(levelNumber));
            return $"Level {levelNumber}";
        }

        public static string BuildSuggestedProjectRelativePath(int levelNumber)
        {
            if (levelNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(levelNumber));
            return $"{DefaultExportDirectory}/level_{levelNumber}.json";
        }

        public static bool TryParseCanonicalLabel(string label, out int levelNumber)
        {
            levelNumber = 0;
            if (string.IsNullOrWhiteSpace(label)) return false;
            const string prefix = "Level ";
            string trimmed = label.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (!TryParseLevelNumber(trimmed.Substring(prefix.Length), out levelNumber))
                return false;
            return string.Equals(trimmed, BuildCanonicalLabel(levelNumber),
                StringComparison.Ordinal);
        }

        public static string ResolveAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A level path is required.", nameof(path));
            string normalized = path.Trim();
            if (Path.IsPathRooted(normalized)) return Path.GetFullPath(normalized);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }
    }
}
