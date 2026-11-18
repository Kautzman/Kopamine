using Digimezzo.Foundation.Core.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.YouTube
{
    public class ProcessRunner : IProcessRunner
    {
        private static readonly char[] CharactersRequiringQuotes = new char[] { ' ', '\t', '\n', '\v', '"' };

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // Process.WaitForExit is blocking. Running it on the thread pool keeps the UI responsive
            // and guarantees that all redirected output has been read before we return.
            // The token is deliberately not passed to Task.Run: a token that is already cancelled would
            // otherwise produce a cancelled Task, which throws when awaited.
            return Task.Run(() => this.Run(request, cancellationToken));
        }

        private ProcessResult Run(ProcessRequest request, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                Arguments = BuildCommandLine(request.Arguments),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // yt-dlp is a Python application: force UTF-8 so titles with non-Latin characters survive.
            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

            if (request.AdditionalPathFolders.Count > 0)
            {
                string existingPath = startInfo.EnvironmentVariables["PATH"] ?? string.Empty;
                startInfo.EnvironmentVariables["PATH"] = string.Join(";", request.AdditionalPathFolders) + ";" + existingPath;
            }

            StringBuilder standardOutput = new StringBuilder();
            StringBuilder standardError = new StringBuilder();
            Action<string> onOutputLine = request.OnOutputLine;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }

                    standardOutput.AppendLine(e.Data);

                    if (onOutputLine != null)
                    {
                        try
                        {
                            onOutputLine(e.Data);
                        }
                        catch (Exception ex)
                        {
                            LogClient.Error("Output line handler failed. Exception: {0}", ex.Message);
                        }
                    }
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        standardError.AppendLine(e.Data);
                    }
                };

                if (cancellationToken.IsCancellationRequested)
                {
                    return ProcessResult.Cancelled(string.Empty, string.Empty);
                }

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (cancellationToken.Register(() => KillProcessTree(process)))
                {
                    // Parameterless WaitForExit also waits for the asynchronous output readers to reach EOF.
                    process.WaitForExit();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    LogClient.Info("Cancelled '{0}'.", System.IO.Path.GetFileName(request.FileName));
                    return ProcessResult.Cancelled(standardOutput.ToString(), standardError.ToString());
                }

                return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString(), false);
            }
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }

                // yt-dlp spawns ffmpeg; taskkill /T takes the whole tree down.
                using (Process killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    killer?.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                LogClient.Warning("taskkill failed, falling back to Process.Kill. Exception: {0}", ex.Message);

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Exception)
                {
                    // The process exited in the meantime
                }
            }
        }

        /// <summary>
        /// Quotes arguments following the Windows CommandLineToArgvW rules.
        /// </summary>
        internal static string BuildCommandLine(IList<string> arguments)
        {
            StringBuilder builder = new StringBuilder();

            foreach (string argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                AppendQuoted(builder, argument ?? string.Empty);
            }

            return builder.ToString();
        }

        private static void AppendQuoted(StringBuilder builder, string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(CharactersRequiringQuotes) < 0)
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');
            int backslashes = 0;

            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', (backslashes * 2) + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(character);
                }

                backslashes = 0;
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
        }
    }
}
