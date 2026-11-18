using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public sealed class ProcessRequest
    {
        public ProcessRequest(string fileName, IList<string> arguments)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentException("A file name is required.", nameof(fileName));
            }

            this.FileName = fileName;
            this.Arguments = arguments ?? new List<string>();
            this.AdditionalPathFolders = new List<string>();
        }

        public string FileName { get; }

        public IList<string> Arguments { get; }

        /// <summary>
        /// Folders prepended to the child process' PATH (e.g. so yt-dlp can find deno.exe and ffmpeg.exe).
        /// </summary>
        public IList<string> AdditionalPathFolders { get; }

        /// <summary>
        /// Invoked on a background thread for every line written to standard output.
        /// </summary>
        public Action<string> OnOutputLine { get; set; }
    }

    public sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string standardOutput, string standardError, bool isCancelled)
        {
            this.ExitCode = exitCode;
            this.StandardOutput = standardOutput ?? string.Empty;
            this.StandardError = standardError ?? string.Empty;
            this.IsCancelled = isCancelled;
        }

        public static ProcessResult Cancelled(string standardOutput, string standardError)
        {
            return new ProcessResult(-1, standardOutput, standardError, true);
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }

        /// <summary>
        /// True when the run was stopped through its CancellationToken. Output is then partial and should be ignored.
        /// </summary>
        public bool IsCancelled { get; }

        public bool IsSuccess => !this.IsCancelled && this.ExitCode == 0;
    }

    public interface IProcessRunner
    {
        /// <summary>
        /// Runs a console process without a window. Cancelling kills the process and its children and
        /// completes normally with <see cref="ProcessResult.IsCancelled"/> set; it never throws for cancellation.
        /// </summary>
        Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
    }
}
