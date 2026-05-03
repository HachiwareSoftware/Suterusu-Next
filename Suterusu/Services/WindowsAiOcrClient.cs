using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Suterusu.Models;

namespace Suterusu.Services
{
    /// <summary>
    /// IOcrClient implementation backed by the Windows App SDK TextRecognizer helper process.
    /// The main app targets net48, so Windows AI APIs run out-of-process in a modern helper.
    /// </summary>
    public class WindowsAiOcrClient : IOcrClient
    {
        private const string HelperDirectoryName = "WindowsAiOcr";
        private const string HelperExecutableName = "Suterusu.WindowsAiOcr.exe";

        private readonly ILogger _logger;
        private readonly string _helperPath;

        public WindowsAiOcrClient(ILogger logger)
            : this(logger, GetDefaultHelperPath())
        {
        }

        public WindowsAiOcrClient(ILogger logger, string helperPath)
        {
            _logger = logger;
            _helperPath = helperPath;
        }

        public async Task<AiSingleAttemptResult> RunOcrAsync(
            byte[] imageData,
            string prompt,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            if (imageData == null || imageData.Length == 0)
                return AiSingleAttemptResult.Fail("Windows AI OCR received an empty image.");

            if (string.IsNullOrWhiteSpace(_helperPath) || !File.Exists(_helperPath))
            {
                return AiSingleAttemptResult.Fail(
                    "Windows AI OCR helper not found (WindowsAiOcr/Suterusu.WindowsAiOcr.exe). " +
                    "This feature requires a Copilot+ PC with NPU. " +
                    "Download the full package that includes the helper, or build from source.");
            }

            string tempPath = Path.Combine(Path.GetTempPath(), "suterusu-windows-ai-ocr-" + Guid.NewGuid().ToString("N") + ".png");

            try
            {
                File.WriteAllBytes(tempPath, imageData);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs);
                    return await RunHelperAsync(tempPath, cts.Token, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AiSingleAttemptResult.Fail("Windows AI OCR timed out.");
            }
            catch (OperationCanceledException)
            {
                return AiSingleAttemptResult.Fail("Windows AI OCR was cancelled.");
            }
            catch (Exception ex)
            {
                _logger.Error("Windows AI OCR failed.", ex);
                return AiSingleAttemptResult.Fail("Windows AI OCR failed: " + ex.Message);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        public static string GetDefaultHelperPath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                HelperDirectoryName,
                HelperExecutableName);
        }

        private async Task<AiSingleAttemptResult> RunHelperAsync(
            string imagePath,
            CancellationToken timeoutToken,
            CancellationToken callerToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _helperPath,
                Arguments = QuoteArgument(imagePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(_helperPath)
            };

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                if (!process.Start())
                    return AiSingleAttemptResult.Fail("Failed to start Windows AI OCR helper.");

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                Task<int> exitTask = WaitForExitAsync(process, timeoutToken);

                int exitCode;
                try
                {
                    exitCode = await exitTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    throw;
                }

                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                callerToken.ThrowIfCancellationRequested();

                AiSingleAttemptResult parsed = ParseHelperOutput(stdout);
                if (parsed != null)
                {
                    if (!parsed.Success && exitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                        _logger.Warn("Windows AI OCR helper stderr: " + stderr.Trim());
                    return parsed;
                }

                string error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                if (string.IsNullOrWhiteSpace(error))
                    error = "Windows AI OCR helper exited with code " + exitCode + ".";

                return AiSingleAttemptResult.Fail("Windows AI OCR helper failed: " + error.Trim());
            }
        }

        private static AiSingleAttemptResult ParseHelperOutput(string stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout))
                return null;

            try
            {
                var json = JObject.Parse(stdout.Trim());
                bool success = json.Value<bool>("success");
                if (success)
                {
                    string text = json.Value<string>("text") ?? string.Empty;
                    return string.IsNullOrWhiteSpace(text)
                        ? AiSingleAttemptResult.Fail("No text recognized in the selected region.")
                        : AiSingleAttemptResult.Ok(text);
                }

                return AiSingleAttemptResult.Fail(json.Value<string>("error") ?? "Windows AI OCR helper failed.");
            }
            catch
            {
                return null;
            }
        }

        private static Task<int> WaitForExitAsync(Process process, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<int>();
            process.Exited += (sender, args) => tcs.TrySetResult(process.ExitCode);

            if (process.HasExited)
                tcs.TrySetResult(process.ExitCode);

            cancellationToken.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        private static string QuoteArgument(string value)
        {
            if (value == null)
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
                // Best-effort cleanup after timeout/cancel.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temp file cleanup is best effort.
            }
        }
    }
}
