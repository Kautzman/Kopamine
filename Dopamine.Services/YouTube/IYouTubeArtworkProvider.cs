using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public interface IYouTubeArtworkProvider
    {
        /// <summary>
        /// Downloads the video thumbnail and center-crops it to a square JPEG suitable as album art.
        /// Returns null when no thumbnail is available or when cancelled.
        /// </summary>
        Task<byte[]> GetSquareCoverAsync(string videoId, CancellationToken cancellationToken);
    }
}
