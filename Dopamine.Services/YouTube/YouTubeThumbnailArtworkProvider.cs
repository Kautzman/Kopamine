using Digimezzo.Foundation.Core.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Dopamine.Services.YouTube
{
    public class YouTubeThumbnailArtworkProvider : IYouTubeArtworkProvider
    {
        private const int JpegQuality = 92;

        // Highest quality first. maxresdefault is 16:9 without bars; hqdefault is 4:3 with letterbox bars.
        private static readonly string[] ThumbnailUrlFormats = new string[]
        {
            "https://i.ytimg.com/vi/{0}/maxresdefault.jpg",
            "https://i.ytimg.com/vi/{0}/sddefault.jpg",
            "https://i.ytimg.com/vi/{0}/hqdefault.jpg"
        };

        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public async Task<byte[]> GetSquareCoverAsync(string videoId, CancellationToken cancellationToken)
        {
            foreach (string format in ThumbnailUrlFormats)
            {
                string url = string.Format(format, videoId);

                try
                {
                    using (HttpResponseMessage response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        byte[] imageBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return CropToSquareJpeg(imageBytes);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cancelled: no cover, no exception
                    return null;
                }
                catch (Exception ex)
                {
                    LogClient.Warning("Could not get thumbnail '{0}'. Exception: {1}", url, ex.Message);
                }
            }

            return null;
        }

        private static byte[] CropToSquareJpeg(byte[] imageBytes)
        {
            using (MemoryStream input = new MemoryStream(imageBytes))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];

                int width = frame.PixelWidth;
                int height = frame.PixelHeight;

                // 4:3 thumbnails contain a 16:9 picture with black bars above and below it.
                bool isLetterboxed = height > width * 0.7;
                int contentHeight = isLetterboxed ? (int)Math.Round(width * 9.0 / 16.0) : height;
                int side = Math.Min(width, contentHeight);

                Int32Rect cropRect = new Int32Rect((width - side) / 2, (height - side) / 2, side, side);
                CroppedBitmap cropped = new CroppedBitmap(frame, cropRect);

                JpegBitmapEncoder encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
                encoder.Frames.Add(BitmapFrame.Create(cropped));

                using (MemoryStream output = new MemoryStream())
                {
                    encoder.Save(output);
                    return output.ToArray();
                }
            }
        }
    }
}
