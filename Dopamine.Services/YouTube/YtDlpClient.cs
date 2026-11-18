using Digimezzo.Foundation.Core.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public class YtDlpClient : IYtDlpClient
    {
        private const string ErrorPrefix = "ERROR:";

        private readonly IExternalToolLocator toolLocator;
        private readonly IProcessRunner processRunner;
        private bool hasWarnedAboutMissingDeno;

        public YtDlpClient(IExternalToolLocator toolLocator, IProcessRunner processRunner)
        {
            this.toolLocator = toolLocator;
            this.processRunner = processRunner;
        }

        public Task<ProcessResult> RunAsync(IList<string> arguments, Action<string> onOutputLine, CancellationToken cancellationToken)
        {
            string ytDlpPath = this.toolLocator.GetRequiredPath(ExternalTool.YtDlp);

            List<string> allArguments = new List<string>
            {
                "--ignore-config",
                "--encoding", "utf-8"
            };

            allArguments.AddRange(arguments);

            ProcessRequest request = new ProcessRequest(ytDlpPath, allArguments)
            {
                OnOutputLine = onOutputLine
            };

            this.AddToolFolderToPath(request, ExternalTool.Ffmpeg);

            if (!this.AddToolFolderToPath(request, ExternalTool.Deno) && !this.hasWarnedAboutMissingDeno)
            {
                this.hasWarnedAboutMissingDeno = true;
                LogClient.Warning("deno.exe was not found. yt-dlp needs a JavaScript runtime for full YouTube support; downloads may fail.");
            }

            return this.processRunner.RunAsync(request, cancellationToken);
        }

        public void EnsureSuccess(ProcessResult result)
        {
            if (result.IsSuccess || result.IsCancelled)
            {
                return;
            }

            string message = ExtractErrorMessage(result.StandardError);
            LogClient.Error("yt-dlp failed with exit code {0}. {1}", result.ExitCode, result.StandardError);

            throw new ExternalToolFailedException(message, result.ExitCode);
        }

        private bool AddToolFolderToPath(ProcessRequest request, ExternalTool tool)
        {
            string toolPath;

            if (!this.toolLocator.TryLocate(tool, out toolPath))
            {
                return false;
            }

            string folder = Path.GetDirectoryName(toolPath);

            if (!request.AdditionalPathFolders.Contains(folder))
            {
                request.AdditionalPathFolders.Add(folder);
            }

            return true;
        }

        private static string ExtractErrorMessage(string standardError)
        {
            string lastError = null;

            using (StringReader reader = new StringReader(standardError ?? string.Empty))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith(ErrorPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        lastError = line.Substring(ErrorPrefix.Length).Trim();
                    }
                }
            }

            return string.IsNullOrEmpty(lastError) ? "yt-dlp failed. See the log file for details." : lastError;
        }
    }
}
