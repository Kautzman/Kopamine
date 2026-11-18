using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Base;
using Dopamine.Core.IO;
using System;
using System.IO;

namespace Dopamine.Services.YouTube
{
    public class YouTubeAudioCache : IYouTubeAudioCache
    {
        private const string CacheSubFolder = "YouTube";
        private static readonly TimeSpan StartupPurgeAge = TimeSpan.FromDays(1);

        public YouTubeAudioCache()
        {
            this.Folder = Path.Combine(SettingsClient.ApplicationFolder(), ApplicationPaths.CacheFolder, CacheSubFolder);
            Directory.CreateDirectory(this.Folder);

            // Leftovers from cancelled or abandoned downloads
            this.PurgeOlderThan(StartupPurgeAge);
        }

        public string Folder { get; }

        public string GetAudioPath(string videoId)
        {
            return Path.Combine(this.Folder, videoId + FileFormats.MP3);
        }

        public bool TryGetCachedAudio(string videoId, out string path)
        {
            path = this.GetAudioPath(videoId);
            FileInfo info = new FileInfo(path);

            return info.Exists && info.Length > 0;
        }

        public string CreateTemporaryFilePath(string extension)
        {
            return Path.Combine(this.Folder, Guid.NewGuid().ToString("N") + extension);
        }

        public void RemoveAudio(string videoId)
        {
            TryDelete(this.GetAudioPath(videoId));
        }

        public void PurgeOlderThan(TimeSpan age)
        {
            try
            {
                DateTime threshold = DateTime.UtcNow - age;

                foreach (string file in Directory.GetFiles(this.Folder))
                {
                    if (System.IO.File.GetLastWriteTimeUtc(file) < threshold)
                    {
                        TryDelete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                LogClient.Warning("Could not purge the YouTube cache. Exception: {0}", ex.Message);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                // Typically still opened by the preview player; the startup purge will get it later.
                LogClient.Warning("Could not delete '{0}'. Exception: {1}", path, ex.Message);
            }
        }
    }
}
