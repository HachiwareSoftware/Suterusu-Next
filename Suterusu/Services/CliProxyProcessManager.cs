using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class CliProxyProcessManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly CliProxyConfigWriter _configWriter;
        private readonly CliProxyHttpClient _httpClient;
        private readonly object _sync = new object();

        private Process _serverProcess;
        private bool _serverOwned;

        public CliProxyProcessManager(ILogger logger)
            : this(logger, null, null)
        {
        }

        public CliProxyProcessManager(ILogger logger, CliProxyConfigWriter configWriter, CliProxyHttpClient httpClient)
        {
            _logger = logger;
            _configWriter = configWriter ?? new CliProxyConfigWriter(logger);
            _httpClient = httpClient ?? new CliProxyHttpClient(logger);
        }

        public Task<CliProxyResult> EnsureInstalledAsync(AppConfig config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (config?.CliProxy == null)
                return Task.FromResult(CliProxyResult.Fail("CLI proxy settings are not configured."));

            var settings = config.CliProxy;
            if (string.IsNullOrWhiteSpace(settings.RuntimeDirectory))
                settings.RuntimeDirectory = CliProxySettings.GetDefaultRuntimeDirectory();

            if (string.IsNullOrWhiteSpace(settings.ExecutablePath))
                settings.ExecutablePath = Path.Combine(settings.RuntimeDirectory, "bin", "cli-proxy-api.exe");

            string configuredPath = settings.ExecutablePath;
            if (File.Exists(configuredPath))
                return Task.FromResult(CliProxyResult.Ok());

            string bundledPath = FindBundledBinaryPath();
            if (string.IsNullOrWhiteSpace(bundledPath) || !File.Exists(bundledPath))
            {
                return Task.FromResult(CliProxyResult.Fail(
                    "Bundled CLIProxyAPI binary was not found. Expected tools/cliproxy/<arch>/cli-proxy-api.exe."));
            }

            try
            {
                string targetDirectory = Path.GetDirectoryName(configuredPath);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                    return Task.FromResult(CliProxyResult.Fail("CLI proxy executable path is invalid."));

                Directory.CreateDirectory(targetDirectory);
                File.Copy(bundledPath, configuredPath, true);

                CopyBundledMetadata(Path.GetDirectoryName(bundledPath), settings.RuntimeDirectory);

                _logger.Info($"CLI proxy binary installed: {configuredPath}");
                return Task.FromResult(CliProxyResult.Ok());
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to install bundled CLI proxy binary.", ex);
                return Task.FromResult(CliProxyResult.Fail(ex.Message));
            }
        }

        public async Task<CliProxyResult> LoginWithBrowserOAuthAsync(AppConfig config, CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            var installResult = await EnsureInstalledAsync(config, cancellationToken).ConfigureAwait(false);
            if (!installResult.Success)
                return installResult;

            var writeResult = _configWriter.Write(config);
            if (!writeResult.Success)
                return writeResult;

            var settings = config.CliProxy;
            var args = new List<string>
            {
                "--config", settings.ConfigPath,
                "--codex-login",
                "--oauth-callback-port", settings.OAuthCallbackPort.ToString()
            };

            var startInfo = CreateStartInfo(settings.ExecutablePath, args, true);

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    var output = new StringBuilder();
                    AttachOutputHandlers(process, output);

                    if (!process.Start())
                        return CliProxyResult.Fail("Failed to start CLI proxy login process.");

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    _logger.Info("Waiting for ChatGPT browser OAuth flow to complete...");

                    using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

                        try
                        {
                            int exitCode = await WaitForExitAsync(process, timeoutCts.Token).ConfigureAwait(false);
                            if (exitCode != 0)
                                return CliProxyResult.Fail(BuildProcessFailureMessage("CLI proxy login failed", output));
                        }
                        catch (OperationCanceledException)
                        {
                            if (!process.HasExited)
                            {
                                TryKillProcess(process);
                            }

                            if (cancellationToken.IsCancellationRequested)
                                return CliProxyResult.Fail("CLI proxy login canceled.");

                            return CliProxyResult.Fail("CLI proxy login timed out.");
                        }
                    }

                    return CliProxyResult.Ok();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("CLI proxy login process failed.", ex);
                return CliProxyResult.Fail(ex.Message);
            }
        }

        public async Task<CliProxyResult> StartAsync(AppConfig config, CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            if (await IsRunningAsync(config, cancellationToken).ConfigureAwait(false))
            {
                _logger.Info("CLI proxy is already running.");
                return CliProxyResult.Ok();
            }

            var installResult = await EnsureInstalledAsync(config, cancellationToken).ConfigureAwait(false);
            if (!installResult.Success)
                return installResult;

            var writeResult = _configWriter.Write(config);
            if (!writeResult.Success)
                return writeResult;

            var settings = config.CliProxy;
            var args = new List<string>
            {
                "--config", settings.ConfigPath
            };

            var startInfo = CreateStartInfo(settings.ExecutablePath, args, true);
            Process process;

            try
            {
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                var output = new StringBuilder();
                AttachOutputHandlers(process, output);

                if (!process.Start())
                    return CliProxyResult.Fail("Failed to start CLI proxy process.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                lock (_sync)
                {
                    _serverProcess = process;
                    _serverOwned = true;
                }

                process.Exited += (sender, _) =>
                {
                    _logger.Warn("CLI proxy process exited.");
                    lock (_sync)
                    {
                        if (ReferenceEquals(_serverProcess, sender))
                        {
                            _serverProcess = null;
                            _serverOwned = false;
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to start CLI proxy process.", ex);
                return CliProxyResult.Fail(ex.Message);
            }

            var readyResult = await _httpClient.WaitUntilReadyAsync(config, 15000, cancellationToken).ConfigureAwait(false);
            if (readyResult.Success)
                return readyResult;

            _logger.Warn("CLI proxy did not become ready. Stopping process.");
            Stop();
            return readyResult;
        }

        public async Task<bool> IsRunningAsync(AppConfig config, CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return false;

            lock (_sync)
            {
                if (_serverOwned && _serverProcess != null && !_serverProcess.HasExited)
                    return true;
            }

            var health = await _httpClient.GetModelsAsync(config, cancellationToken).ConfigureAwait(false);
            return health.Success;
        }

        public async Task<CliProxyHealthResult> GetModelsAsync(AppConfig config, CancellationToken cancellationToken)
        {
            return await _httpClient.GetModelsAsync(config, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CliProxyResult> TestModelAsync(AppConfig config, string model, CancellationToken cancellationToken)
        {
            return await _httpClient.TestChatCompletionAsync(config, model, cancellationToken).ConfigureAwait(false);
        }

        public CliProxyResult Stop()
        {
            lock (_sync)
            {
                if (!_serverOwned || _serverProcess == null)
                    return CliProxyResult.Ok();

                try
                {
                    if (!_serverProcess.HasExited)
                        TryKillProcess(_serverProcess);

                    _serverProcess.Dispose();
                    _serverProcess = null;
                    _serverOwned = false;
                    return CliProxyResult.Ok();
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to stop CLI proxy process.", ex);
                    return CliProxyResult.Fail(ex.Message);
                }
            }
        }

        private void CopyBundledMetadata(string bundledDirectory, string runtimeDirectory)
        {
            if (string.IsNullOrWhiteSpace(bundledDirectory) || string.IsNullOrWhiteSpace(runtimeDirectory))
                return;

            Directory.CreateDirectory(runtimeDirectory);

            string[] metadataFiles = { "LICENSE", "LICENSE.txt", "VERSION.txt", "checksums.txt" };
            foreach (string fileName in metadataFiles)
            {
                string source = Path.Combine(bundledDirectory, fileName);
                if (!File.Exists(source))
                    continue;

                string target = Path.Combine(runtimeDirectory, fileName);
                File.Copy(source, target, true);
            }
        }

        private string FindBundledBinaryPath()
        {
            string primaryArch = GetPreferredArchFolder();
            string primary = FindFileFromBase(Path.Combine("tools", "cliproxy", primaryArch, "cli-proxy-api.exe"), 6);
            if (!string.IsNullOrWhiteSpace(primary))
                return primary;

            return FindFileFromBase(Path.Combine("tools", "cliproxy", "windows-x64", "cli-proxy-api.exe"), 6);
        }

        private static string GetPreferredArchFolder()
        {
            string nativeArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
            if (string.IsNullOrWhiteSpace(nativeArch))
                nativeArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

            if (!string.IsNullOrWhiteSpace(nativeArch)
                && nativeArch.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "windows-arm64";
            }

            return "windows-x64";
        }

        private static string FindFileFromBase(string relativePath, int maxParentLevels)
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i <= maxParentLevels && !string.IsNullOrWhiteSpace(current); i++)
            {
                string candidate = Path.Combine(current, relativePath);
                if (File.Exists(candidate))
                    return candidate;

                var parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            return null;
        }

        private ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyList<string> args, bool redirect)
        {
            var arguments = BuildArguments(args);

            return new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect,
                WorkingDirectory = Path.GetDirectoryName(executablePath)
            };
        }

        private static string BuildArguments(IReadOnlyList<string> args)
        {
            if (args == null || args.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0)
                    sb.Append(' ');

                sb.Append(QuoteArgument(args[i]));
            }

            return sb.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (argument == null)
                return "\"\"";

            if (argument.Length == 0)
                return "\"\"";

            bool requiresQuotes = argument.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
            if (!requiresQuotes)
                return argument;

            var sb = new StringBuilder();
            sb.Append('"');

            int backslashCount = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashCount * 2 + 1);
                    sb.Append('"');
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    sb.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                sb.Append(c);
            }

            if (backslashCount > 0)
                sb.Append('\\', backslashCount * 2);

            sb.Append('"');
            return sb.ToString();
        }

        private void AttachOutputHandlers(Process process, StringBuilder output)
        {
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data == null)
                    return;

                lock (output)
                {
                    output.AppendLine(e.Data);
                }

                _logger.Info($"[CLIProxyAPI] {e.Data}");
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null)
                    return;

                lock (output)
                {
                    output.AppendLine(e.Data);
                }

                _logger.Warn($"[CLIProxyAPI] {e.Data}");
            };
        }

        private static string BuildProcessFailureMessage(string title, StringBuilder output)
        {
            string details;
            lock (output)
            {
                details = output.ToString();
            }

            if (string.IsNullOrWhiteSpace(details))
                return title + ".";

            string trimmed = details;
            if (trimmed.Length > 500)
                trimmed = trimmed.Substring(trimmed.Length - 500);

            return title + ": " + trimmed.Trim();
        }

        private static void TryKillProcess(Process process)
        {
            if (process == null || process.HasExited)
                return;

            process.Kill();
            process.WaitForExit(3000);
        }

        private static Task<int> WaitForExitAsync(Process process, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<int>();

            EventHandler handler = null;
            handler = (sender, args) =>
            {
                process.Exited -= handler;
                tcs.TrySetResult(process.ExitCode);
            };

            process.EnableRaisingEvents = true;
            process.Exited += handler;

            if (process.HasExited)
            {
                process.Exited -= handler;
                return Task.FromResult(process.ExitCode);
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    process.Exited -= handler;
                    tcs.TrySetCanceled();
                });
            }

            return tcs.Task;
        }

        public void Dispose()
        {
            Stop();
            _httpClient.Dispose();
        }
    }
}
