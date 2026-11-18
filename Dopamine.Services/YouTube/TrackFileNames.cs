using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Turns a song title (or a name typed by the user) into a safe, unique file name.
    /// </summary>
    public static class TrackFileNames
    {
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private const int MaximumBaseNameLength = 120;
        private const string FallbackBaseName = "Untitled";

        private static readonly char[] InvalidCharacters = Path.GetInvalidFileNameChars();

        private static readonly string[] ReservedNames = new string[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// The suggested base name (no extension) for a title: all whitespace removed, unsafe characters replaced.
        /// "Back In Black" becomes "BackInBlack".
        /// </summary>
        public static string SuggestBaseName(string title)
        {
            return Sanitize(WhitespaceRegex.Replace(title ?? string.Empty, string.Empty));
        }

        /// <summary>
        /// Final file name for a user-supplied base name. The user's own spacing is kept (only the ends are trimmed),
        /// and a typed extension matching <paramref name="extension"/> is not doubled.
        /// </summary>
        public static string FromBaseName(string baseName, string extension)
        {
            string name = (baseName ?? string.Empty).Trim();

            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - extension.Length);
            }

            return Sanitize(name) + extension;
        }

        private static string Sanitize(string name)
        {
            StringBuilder builder = new StringBuilder();

            foreach (char character in (name ?? string.Empty).Trim())
            {
                builder.Append(InvalidCharacters.Contains(character) ? '_' : character);
            }

            // Windows silently drops trailing dots and spaces
            string baseName = builder.ToString().Trim().TrimEnd('.', ' ');

            if (baseName.Length > MaximumBaseNameLength)
            {
                baseName = baseName.Substring(0, MaximumBaseNameLength).TrimEnd('.', ' ');
            }

            if (baseName.Length == 0)
            {
                baseName = FallbackBaseName;
            }

            if (ReservedNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            {
                baseName = "_" + baseName;
            }

            return baseName;
        }

        /// <summary>
        /// Returns folder\name.ext, or folder\name (2).ext etc. when that file already exists.
        /// </summary>
        public static string GetUniquePath(string folder, string fileName)
        {
            string candidate = Path.Combine(folder, fileName);

            if (!System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            for (int counter = 2; counter < 10000; counter++)
            {
                candidate = Path.Combine(folder, string.Format("{0} ({1}){2}", baseName, counter, extension));

                if (!System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("Could not find a free file name for '" + fileName + "'.");
        }
    }
}
