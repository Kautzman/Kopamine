using Digimezzo.Foundation.Core.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public class YtDlpSearchService : IYouTubeSearchService
    {
        private const int MaximumResults = 50;
        private const int UploadDateWorkerCount = 4;
        private const string UploadDateSeparator = "|";
        private const string YtDlpDateFormat = "yyyyMMdd";

        private readonly IYtDlpClient ytDlpClient;

        public YtDlpSearchService(IYtDlpClient ytDlpClient)
        {
            this.ytDlpClient = ytDlpClient;
        }

        public async Task<IList<YouTubeVideo>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<YouTubeVideo>();
            }

            int count = Math.Max(1, Math.Min(maxResults, MaximumResults));

            List<string> arguments = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "ytsearch{0}:{1}", count, query.Trim()),
                "--flat-playlist",
                "--dump-single-json",
                "--no-warnings",
                // Lets flat results carry an approximate date ("3 years ago") without a request per video.
                "--extractor-args", "youtubetab:approximate_date"
            };

            ProcessResult result = await this.ytDlpClient.RunAsync(arguments, null, cancellationToken).ConfigureAwait(false);

            if (result.IsCancelled)
            {
                return new List<YouTubeVideo>();
            }

            this.ytDlpClient.EnsureSuccess(result);

            return ParseSearchResults(result.StandardOutput);
        }

        public Task ResolveUploadDatesAsync(IList<string> videoIds, IProgress<UploadDateResolution> progress, CancellationToken cancellationToken)
        {
            if (videoIds == null || videoIds.Count == 0)
            {
                return Task.CompletedTask;
            }

            // yt-dlp handles URLs sequentially, so split the work over a few processes.
            List<Task> workers = new List<Task>();

            for (int worker = 0; worker < UploadDateWorkerCount; worker++)
            {
                List<string> chunk = new List<string>();

                for (int index = worker; index < videoIds.Count; index += UploadDateWorkerCount)
                {
                    chunk.Add(videoIds[index]);
                }

                if (chunk.Count > 0)
                {
                    workers.Add(this.ResolveChunkAsync(chunk, progress, cancellationToken));
                }
            }

            return Task.WhenAll(workers);
        }

        private async Task ResolveChunkAsync(IList<string> videoIds, IProgress<UploadDateResolution> progress, CancellationToken cancellationToken)
        {
            List<string> arguments = new List<string>
            {
                "--skip-download",
                "--no-playlist",
                "--no-warnings",
                "--ignore-errors",
                "--print", "%(id)s" + UploadDateSeparator + "%(upload_date)s"
            };

            arguments.AddRange(videoIds.Select(id => "https://www.youtube.com/watch?v=" + id));

            Action<string> onOutputLine = line =>
            {
                UploadDateResolution resolution;

                if (TryParseUploadDateLine(line, out resolution))
                {
                    progress?.Report(resolution);
                }
            };

            try
            {
                // Exit code is ignored on purpose: with --ignore-errors a single failing video should not hide the others.
                await this.ytDlpClient.RunAsync(arguments, onOutputLine, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Exact dates are a refinement; the approximate ones are already shown.
                LogClient.Warning("Could not resolve upload dates. Exception: {0}", ex.Message);
            }
        }

        private static bool TryParseUploadDateLine(string line, out UploadDateResolution resolution)
        {
            resolution = null;

            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            int separatorIndex = line.LastIndexOf(UploadDateSeparator, StringComparison.Ordinal);

            if (separatorIndex <= 0)
            {
                return false;
            }

            string id = line.Substring(0, separatorIndex).Trim();
            DateTime uploadDate;

            if (!TryParseYtDlpDate(line.Substring(separatorIndex + 1).Trim(), out uploadDate))
            {
                return false;
            }

            resolution = new UploadDateResolution(id, uploadDate);
            return true;
        }

        private static IList<YouTubeVideo> ParseSearchResults(string json)
        {
            List<YouTubeVideo> videos = new List<YouTubeVideo>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return videos;
            }

            JObject root = JObject.Parse(json);
            JArray entries = root["entries"] as JArray;

            if (entries == null)
            {
                return videos;
            }

            foreach (JToken entry in entries)
            {
                YouTubeVideo video = TryParseEntry(entry);

                if (video != null)
                {
                    videos.Add(video);
                }
            }

            return videos;
        }

        private static YouTubeVideo TryParseEntry(JToken entry)
        {
            string id = (string)entry["id"];
            string liveStatus = (string)entry["live_status"];
            double? durationSeconds = (double?)entry["duration"];

            // Skip channels/playlists, live streams and anything without a duration (cannot be downloaded as a song).
            if (string.IsNullOrEmpty(id) || id.Length != 11 || !durationSeconds.HasValue || durationSeconds.Value <= 0)
            {
                return null;
            }

            if (string.Equals(liveStatus, "is_live", StringComparison.OrdinalIgnoreCase) || string.Equals(liveStatus, "is_upcoming", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string title = (string)entry["title"];
            string uploader = (string)entry["channel"] ?? (string)entry["uploader"];

            DateTime? uploadDate = null;
            DateTime parsedDate;

            if (TryParseYtDlpDate((string)entry["upload_date"], out parsedDate))
            {
                uploadDate = parsedDate;
            }
            else
            {
                long? timestamp = (long?)entry["timestamp"];

                if (timestamp.HasValue)
                {
                    uploadDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp.Value).ToLocalTime().Date;
                }
            }

            return new YouTubeVideo(id, title, uploader, TimeSpan.FromSeconds(durationSeconds.Value), uploadDate, true);
        }

        private static bool TryParseYtDlpDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value, YtDlpDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}
