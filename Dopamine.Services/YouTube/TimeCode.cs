using System;
using System.Globalization;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Formats and parses time codes such as "3:07.4" or "1:02:03.5".
    /// </summary>
    public static class TimeCode
    {
        public static string Format(double seconds, bool includeTenths)
        {
            if (double.IsNaN(seconds) || seconds < 0)
            {
                seconds = 0;
            }

            // Truncate (not round) so the displayed value never exceeds the real one.
            long tenths = (long)Math.Floor(seconds * 10.0);
            long totalSeconds = tenths / 10;
            long hours = totalSeconds / 3600;
            long minutes = (totalSeconds % 3600) / 60;
            long secs = totalSeconds % 60;

            string result = hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, secs)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, secs);

            return includeTenths ? result + "." + (tenths % 10).ToString(CultureInfo.InvariantCulture) : result;
        }

        public static string Format(TimeSpan? duration)
        {
            return duration.HasValue ? Format(duration.Value.TotalSeconds, false) : string.Empty;
        }

        /// <summary>
        /// Accepts "ss", "m:ss", "h:mm:ss", each with an optional fraction using '.' or ','.
        /// </summary>
        public static bool TryParse(string text, out double seconds)
        {
            seconds = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Trim().Replace(',', '.').Split(':');

            if (parts.Length > 3)
            {
                return false;
            }

            double total = 0;

            for (int index = 0; index < parts.Length; index++)
            {
                double part;
                bool isLast = index == parts.Length - 1;
                NumberStyles styles = isLast ? NumberStyles.AllowDecimalPoint : NumberStyles.None;

                if (!double.TryParse(parts[index], styles, CultureInfo.InvariantCulture, out part) || part < 0)
                {
                    return false;
                }

                // Minutes/seconds after the first component must stay below 60
                if (index > 0 && part >= 60)
                {
                    return false;
                }

                total = (total * 60) + part;
            }

            seconds = total;
            return true;
        }
    }
}
