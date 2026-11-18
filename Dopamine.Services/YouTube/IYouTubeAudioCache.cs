using System;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Working folder for downloaded audio and intermediate files.
    /// </summary>
    public interface IYouTubeAudioCache
    {
        string Folder { get; }

        string GetAudioPath(string videoId);

        bool TryGetCachedAudio(string videoId, out string path);

        string CreateTemporaryFilePath(string extension);

        void RemoveAudio(string videoId);

        void PurgeOlderThan(TimeSpan age);
    }
}
