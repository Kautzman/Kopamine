using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Base;
using Dopamine.Services.Indexing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public class YouTubeImportService : IYouTubeImportService
    {
        private readonly IAudioTrimService trimService;
        private readonly ITrackTagWriter tagWriter;
        private readonly IYouTubeArtworkProvider artworkProvider;
        private readonly IYouTubeAudioCache cache;
        private readonly IIndexingService indexingService;

        public YouTubeImportService(IAudioTrimService trimService, ITrackTagWriter tagWriter, IYouTubeArtworkProvider artworkProvider,
            IYouTubeAudioCache cache, IIndexingService indexingService)
        {
            this.trimService = trimService;
            this.tagWriter = tagWriter;
            this.artworkProvider = artworkProvider;
            this.cache = cache;
            this.indexingService = indexingService;
        }

        public string LastUsedFolder { get; private set; }

        public string SuggestFileBaseName(string title)
        {
            return TrackFileNames.SuggestBaseName(title);
        }

        public string BuildFileName(string fileBaseName)
        {
            return TrackFileNames.FromBaseName(fileBaseName, FileFormats.MP3);
        }

        public async Task<string> ImportAsync(YouTubeImportRequest request, CancellationToken cancellationToken)
        {
            Validate(request);

            string temporaryPath = this.cache.CreateTemporaryFilePath(FileFormats.MP3);

            try
            {
                bool isTrimmed = await this.trimService.TrimMp3Async(request.SourceAudioPath, temporaryPath, request.TrimStart, request.TrimEnd, cancellationToken);

                if (!isTrimmed || cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (request.UseThumbnailAsCover && request.Tags.Artwork == null)
                {
                    request.Tags.Artwork = await this.artworkProvider.GetSquareCoverAsync(request.VideoId, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                // No token on Task.Run: it would throw for an already-cancelled token instead of returning.
                await Task.Run(() => this.tagWriter.Write(temporaryPath, request.Tags));

                // Tag before moving, so a folder watcher never sees a half-written file.
                string baseName = string.IsNullOrWhiteSpace(request.FileBaseName) ? this.SuggestFileBaseName(request.Tags.Title) : request.FileBaseName;
                string destinationPath = TrackFileNames.GetUniquePath(request.DestinationFolder, this.BuildFileName(baseName));
                System.IO.File.Move(temporaryPath, destinationPath);

                LogClient.Info("Added YouTube video {0} to the collection as '{1}'", request.VideoId, destinationPath);

                this.LastUsedFolder = request.DestinationFolder;
                this.cache.RemoveAudio(request.VideoId);
                this.RefreshCollection();

                return destinationPath;
            }
            finally
            {
                if (System.IO.File.Exists(temporaryPath))
                {
                    try
                    {
                        System.IO.File.Delete(temporaryPath);
                    }
                    catch (Exception ex)
                    {
                        LogClient.Warning("Could not delete temporary file '{0}'. Exception: {1}", temporaryPath, ex.Message);
                    }
                }
            }
        }

        private async void RefreshCollection()
        {
            try
            {
                await this.indexingService.RefreshCollectionImmediatelyAsync();
            }
            catch (Exception ex)
            {
                LogClient.Error("Could not refresh the collection after a YouTube import. Exception: {0}", ex.Message);
            }
        }

        private static void Validate(YouTubeImportRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Tags == null || string.IsNullOrWhiteSpace(request.Tags.Title))
            {
                throw new ArgumentException("A title is required.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.DestinationFolder) || !Directory.Exists(request.DestinationFolder))
            {
                throw new DirectoryNotFoundException("The destination folder does not exist: " + request.DestinationFolder);
            }

            if (request.TrimEnd <= request.TrimStart)
            {
                throw new ArgumentException("The end of the selection must be after its start.", nameof(request));
            }
        }
    }
}
