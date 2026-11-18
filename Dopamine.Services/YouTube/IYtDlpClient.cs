using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    /// <summary>
    /// Thin wrapper around the yt-dlp executable. Adds the common arguments and makes
    /// ffmpeg and deno discoverable for the child process.
    /// </summary>
    public interface IYtDlpClient
    {
        Task<ProcessResult> RunAsync(IList<string> arguments, Action<string> onOutputLine, CancellationToken cancellationToken);

        /// <summary>
        /// Throws <see cref="ExternalToolFailedException"/> with yt-dlp's own error message when the run failed.
        /// A cancelled run is not a failure and does not throw; check <see cref="ProcessResult.IsCancelled"/> first.
        /// </summary>
        void EnsureSuccess(ProcessResult result);
    }
}
