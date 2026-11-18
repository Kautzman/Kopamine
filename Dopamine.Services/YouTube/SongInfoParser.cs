using System;
using System.Text.RegularExpressions;

namespace Dopamine.Services.YouTube
{
    public class SongInfoParser : ISongInfoParser
    {
        private const string TopicSuffix = " - Topic";
        private const string VevoSuffix = "VEVO";

        // Bracketed noise such as "(Official Music Video)", "[Lyrics]", "(HD Remaster)", "\u3010MV\u3011"
        private static readonly Regex NoiseRegex = new Regex(
            @"\s*[\(\[\u3010][^\)\]\u3011]*\b(official|lyrics?|audio|video|visuali[sz]er|hd|hq|4k|m/?v|explicit|clip|remaster(ed)?)\b[^\)\]\u3011]*[\)\]\u3011]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // "Artist - Title", also with en/em dashes or a colon-free pipe
        private static readonly Regex ArtistTitleRegex = new Regex(
            @"^\s*(?<artist>.+?)\s+[-\u2013\u2014|]\s+(?<title>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WhitespaceRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        public SongInfoGuess Parse(YouTubeVideo video)
        {
            if (video == null)
            {
                return new SongInfoGuess(string.Empty, string.Empty);
            }

            string cleanedTitle = Clean(NoiseRegex.Replace(video.Title, string.Empty));
            string uploaderArtist = CleanUploader(video.Uploader);

            Match match = ArtistTitleRegex.Match(cleanedTitle);

            if (match.Success)
            {
                return new SongInfoGuess(Unquote(match.Groups["title"].Value), Clean(match.Groups["artist"].Value));
            }

            return new SongInfoGuess(Unquote(cleanedTitle), uploaderArtist);
        }

        private static string CleanUploader(string uploader)
        {
            string result = (uploader ?? string.Empty).Trim();

            if (result.EndsWith(TopicSuffix, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - TopicSuffix.Length);
            }
            else if (result.EndsWith(VevoSuffix, StringComparison.Ordinal) && result.Length > VevoSuffix.Length)
            {
                result = result.Substring(0, result.Length - VevoSuffix.Length);
            }

            return Clean(result);
        }

        private static string Unquote(string value)
        {
            string result = Clean(value);

            if (result.Length > 1 && ((result[0] == '"' && result[result.Length - 1] == '"') || (result[0] == '\'' && result[result.Length - 1] == '\'')))
            {
                result = result.Substring(1, result.Length - 2);
            }

            return Clean(result);
        }

        private static string Clean(string value)
        {
            return WhitespaceRegex.Replace(value ?? string.Empty, " ").Trim();
        }
    }
}
