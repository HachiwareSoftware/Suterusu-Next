using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly CliProxyReleaseService _releaseService;
        private readonly bool _ownsReleaseService;
        private readonly object _sync = new object();

        private Process _serverProcess;
        private bool _serverOwned;

        public CliProxyProcessManager(ILogger logger)
            : this(logger, null, null, null)
        {
        }

        public CliProxyProcessManager(
            ILogger logger,
            CliProxyConfigWriter configWriter,
            CliProxyHttpClient httpClient,
            CliProxyReleaseService releaseService)
        {
            _logger = logger;
            _configWriter = configWriter ?? new CliProxyConfigWriter(logger);
            _httpClient   = httpClient   ?? new CliProxyHttpClient(logger);

            if (releaseService != null)
            {
                _releaseService     = releaseService;
                _ownsReleaseService = false;
            }
            else
            {
                _releaseService     = new CliProxyReleaseService(logger);
                _ownsReleaseService = true;
            }
        }

        // ── Installation ──────────────────────────────────────────────────────

        public async Task<CliProxyResult> EnsureInstalledAsync(AppConfig config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            var settings = config.CliProxy;
            if (string.IsNullOrWhiteSpace(settings.RuntimeDirectory))
                settings.RuntimeDirectory = CliProxySettings.GetDefaultRuntimeDirectory();

            if (string.IsNullOrWhiteSpace(settings.ExecutablePath))
                settings.ExecutablePath = CliProxySettings.GetExecutablePath(settings.RuntimeDirectory);

            if (File.Exists(settings.ExecutablePath))
                return CliProxyResult.Ok();

            var migrationResult = TryMigrateLegacyInstall(settings);
            if (!migrationResult.Success)
                return migrationResult;

            if (File.Exists(settings.ExecutablePath))
                return CliProxyResult.Ok();

            _logger.Info("CLIProxyAPI not found — downloading from GitHub...");
            return await _releaseService.InstallLatestAsync(settings, null, cancellationToken).ConfigureAwait(false);
        }

        private CliProxyResult TryMigrateLegacyInstall(CliProxySettings settings)
        {
            string legacyBinDir = Path.Combine(CliProxySettings.GetLegacyRuntimeDirectory(), "bin");
            string legacyExe = CliProxySettings.GetExecutablePath(CliProxySettings.GetLegacyRuntimeDirectory());
            if (!File.Exists(legacyExe))
                return CliProxyResult.Ok();

            try
            {
                string targetBinDir = Path.GetDirectoryName(settings.ExecutablePath);
                Directory.CreateDirectory(targetBinDir);

                foreach (string file in Directory.GetFiles(legacyBinDir))
                {
                    string targetPath = Path.Combine(targetBinDir, Path.GetFileName(file));
                    File.Copy(file, targetPath, true);
                }

                string legacyVersionFile = Path.Combine(CliProxySettings.GetLegacyRuntimeDirectory(), CliProxyReleaseService.VersionFileName);
                if (File.Exists(legacyVersionFile))
                {
                    Directory.CreateDirectory(settings.RuntimeDirectory);
                    File.Copy(legacyVersionFile, Path.Combine(settings.RuntimeDirectory, CliProxyReleaseService.VersionFileName), true);
                }

                _logger.Info($"Migrated CLIProxyAPI from {legacyBinDir} to {targetBinDir}.");
                return CliProxyResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to migrate legacy CLIProxyAPI install: {ex.Message}");
                return CliProxyResult.Fail("Failed to migrate existing CLIProxyAPI install: " + ex.Message);
            }
        }

        // ── Version helpers (for Settings UI) ────────────────────────────────

        public string GetInstalledVersion(AppConfig config)
        {
            return _releaseService.GetInstalledVersion(config?.CliProxy);
        }

        public Task<string> GetLatestVersionAsync(CancellationToken cancellationToken)
        {
            return _releaseService.GetLatestVersionAsync(cancellationToken);
        }

        public Task<CliProxyResult> UpdateCliProxyAsync(
            AppConfig config,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return Task.FromResult(CliProxyResult.Fail("CLI proxy settings are not configured."));

            return _releaseService.InstallLatestAsync(config.CliProxy, progress, cancellationToken);
        }

        // ── Login ─────────────────────────────────────────────────────────────

        private const int OAuthUrlTimeoutMs = 15000;
        private const int LoginWaitTimeoutMs = 600000; // 10 min

        public async Task<CliProxyResult> LoginWithBrowserOAuthAsync(AppConfig config, CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            var settings = config.CliProxy;

            if (CliProxySettings.IsGeminiProvider(settings.Provider))
                return await LoginGeminiAsync(config, cancellationToken).ConfigureAwait(false);

            return await LoginCodexAsync(config, cancellationToken).ConfigureAwait(false);
        }

        private async Task<CliProxyResult> LoginGeminiAsync(
            AppConfig config, CancellationToken cancellationToken)
        {
            var installResult = await EnsureInstalledAsync(config, cancellationToken).ConfigureAwait(false);
            if (!installResult.Success)
                return installResult;

            var writeResult = _configWriter.Write(config);
            if (!writeResult.Success)
                return writeResult;

            var settings = config.CliProxy;
            var args = BuildLoginArguments(settings);
            var startInfo = CreateStartInfo(settings.ExecutablePath, args, false);

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                        return CliProxyResult.Fail("Failed to start CLI proxy login process.");

                    return await WaitForLoginProcessAsync(process, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("CLI proxy Gemini login process failed.", ex);
                return CliProxyResult.Fail(ex.Message);
            }
        }

        private async Task<CliProxyResult> LoginCodexAsync(AppConfig config, CancellationToken cancellationToken)
        {
            var installResult = await EnsureInstalledAsync(config, cancellationToken).ConfigureAwait(false);
            if (!installResult.Success)
                return installResult;

            var writeResult = _configWriter.Write(config);
            if (!writeResult.Success)
                return writeResult;

            var settings = config.CliProxy;
            var args = BuildLoginArguments(settings);

            var startInfo = CreateStartInfo(settings.ExecutablePath, args, true);
            var output = new StringBuilder();

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    var urlTcs = new TaskCompletionSource<string>();
                    AttachOAuthHandlers(process, output, urlTcs);

                    if (!process.Start())
                        return CliProxyResult.Fail("Failed to start CLI proxy login process.");

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    string oauthUrl = await WaitForOAuthUrlAsync(process, urlTcs, output, cancellationToken);
                    if (oauthUrl == null)
                        return CliProxyResult.Fail(
                            "CLIProxyAPI did not output an OAuth URL. Update CLIProxyAPI or complete login manually.");

                    LaunchBrowser(oauthUrl);

                    var exitResult = await WaitForLoginProcessAsync(process, cancellationToken);
                    if (!exitResult.Success)
                        return CliProxyResult.Fail(
                            BuildProcessFailureMessage(exitResult.Error, output));

                    return CliProxyResult.Ok();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("CLI proxy login process failed.", ex);
                return CliProxyResult.Fail(ex.Message);
            }
        }

        public async Task<CliProxyResult> ConnectAndUseAsync(
            AppConfig config,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            cancellationToken.ThrowIfCancellationRequested();
            config.CliProxy.Enabled = true;

            progress?.Report($"Starting {GetProviderDisplayName(config.CliProxy)} browser login...");
            var login = await LoginWithBrowserOAuthAsync(config, cancellationToken).ConfigureAwait(false);
            if (!login.Success)
                return CliProxyResult.Fail("CLI proxy login failed: " + login.Error);

            progress?.Report("Login complete. Starting local proxy...");
            var start = await StartAsync(config, cancellationToken).ConfigureAwait(false);
            if (!start.Success)
                return CliProxyResult.Fail("Failed to start CLI proxy: " + start.Error);

            progress?.Report("Detecting models...");
            var health = await GetModelsAsync(config, cancellationToken).ConfigureAwait(false);
            if (health.Success && health.Models.Count > 0
                && !health.Models.Contains(config.CliProxy.Model, StringComparer.OrdinalIgnoreCase))
            {
                config.CliProxy.Model = health.Models[0];
            }

            return CliProxyResult.Ok(config.CliProxy.Model);
        }

        public static IReadOnlyList<string> BuildLoginArguments(CliProxySettings settings)
        {
            var args = new List<string>
            {
                "--config", settings.ConfigPath,
                CliProxySettings.IsGeminiProvider(settings.Provider) ? "--login" : "--codex-login"
            };

            if (!CliProxySettings.IsGeminiProvider(settings.Provider))
                args.Add("--no-browser");

            args.Add("--oauth-callback-port");
            args.Add(settings.OAuthCallbackPort.ToString());

            if (CliProxySettings.IsGeminiProvider(settings.Provider)
                && !string.IsNullOrWhiteSpace(settings.GeminiProjectId))
            {
                args.Add("--project_id");
                args.Add(settings.GeminiProjectId.Trim());
            }

            return args;
        }

        private static string GetProviderDisplayName(CliProxySettings settings)
        {
            return CliProxySettings.IsGeminiProvider(settings?.Provider) ? "Gemini" : "ChatGPT";
        }

        private void AttachOAuthHandlers(Process process, StringBuilder output, TaskCompletionSource<string> urlTcs)
        {
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                lock (output) { output.AppendLine(e.Data); }
                _logger.Info($"[CLIProxyAPI] {e.Data}");
                if (TryExtractOAuthUrl(e.Data, out string found))
                    urlTcs.TrySetResult(found);
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                lock (output) { output.AppendLine(e.Data); }
                _logger.Warn($"[CLIProxyAPI] {e.Data}");
                if (TryExtractOAuthUrl(e.Data, out string found))
                    urlTcs.TrySetResult(found);
            };
            process.EnableRaisingEvents = true;
            process.Exited += (s, a) => urlTcs.TrySetCanceled();
        }

        private async Task<string> WaitForOAuthUrlAsync(
            Process process, TaskCompletionSource<string> urlTcs, StringBuilder output,
            CancellationToken cancellationToken)
        {
            _logger.Info("Waiting for CLIProxyAPI to output an OAuth URL...");

            using (var urlTimeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(OAuthUrlTimeoutMs)))
            using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, urlTimeoutCts.Token))
            {
                combinedCts.Token.Register(() => urlTcs.TrySetCanceled());

                try
                {
                    return await urlTcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                        TryKillProcess(process);

                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    if (process.HasExited)
                    {
                        _logger.Warn(BuildProcessFailureMessage(
                            "CLIProxyAPI exited without an OAuth URL", output));
                    }

                    return null;
                }
            }
        }

        private static void LaunchBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // Non-fatal: user can open the URL manually
                System.Diagnostics.Debug.WriteLine($"Could not open browser: {ex.Message}");
            }
        }

        private async Task<CliProxyResult> WaitForLoginProcessAsync(
            Process process, CancellationToken cancellationToken)
        {
            _logger.Info("Waiting for ChatGPT browser OAuth flow to complete...");

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(LoginWaitTimeoutMs));
                try
                {
                    int exitCode = await WaitForExitAsync(process, timeoutCts.Token).ConfigureAwait(false);
                    if (exitCode != 0)
                        return CliProxyResult.Fail("CLI proxy login exited with code " + exitCode);
                    return CliProxyResult.Ok();
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                        TryKillProcess(process);

                    return cancellationToken.IsCancellationRequested
                        ? CliProxyResult.Fail("CLI proxy login canceled.")
                        : CliProxyResult.Fail("CLI proxy login timed out.");
                }
            }
        }

        // ── Start / Stop / Status ─────────────────────────────────────────────

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
            var args     = new List<string> { "--config", settings.ConfigPath };
            var startInfo = CreateStartInfo(settings.ExecutablePath, args, true);
            Process process;

            try
            {
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                var out2 = new StringBuilder();
                AttachOutputHandlers(process, out2);

                if (!process.Start())
                    return CliProxyResult.Fail("Failed to start CLI proxy process.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                lock (_sync)
                {
                    _serverProcess = process;
                    _serverOwned   = true;
                }

                process.Exited += (sender, _) =>
                {
                    _logger.Warn("CLI proxy process exited.");
                    lock (_sync)
                    {
                        if (ReferenceEquals(_serverProcess, sender))
                        {
                            _serverProcess = null;
                            _serverOwned   = false;
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to start CLI proxy process.", ex);
                return CliProxyResult.Fail(ex.Message);
            }

            var readyResult = await _httpClient.WaitUntilReadyAsync(config, 15000, cancellationToken)
                .ConfigureAwait(false);
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

            var health = await _httpClient.GetModelsAsync(config, cancellationToken, false).ConfigureAwait(false);
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
                    _serverOwned   = false;
                    return CliProxyResult.Ok();
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to stop CLI proxy process.", ex);
                    return CliProxyResult.Fail(ex.Message);
                }
            }
        }

        // ── OAuth URL parser (public for testability) ─────────────────────────

        /// <summary>
        /// Scans a single output line and extracts an https:// URL if present.
        /// </summary>
        public static bool TryExtractOAuthUrl(string line, out string url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim();
            int idx = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            string candidate = trimmed.Substring(idx);
            int wsIdx = candidate.IndexOfAny(new[] { ' ', '\t' });
            if (wsIdx >= 0)
                candidate = candidate.Substring(0, wsIdx);

            if (candidate.Length > "https://".Length)
            {
                url = candidate;
                return true;
            }

            return false;
        }

        // ── Process helpers ───────────────────────────────────────────────────

        private ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyList<string> args, bool redirect)
        {
            return new ProcessStartInfo
            {
                FileName             = executablePath,
                Arguments            = BuildArguments(args),
                UseShellExecute      = false,
                CreateNoWindow       = redirect,
                RedirectStandardOutput = redirect,
                RedirectStandardError  = redirect,
                WorkingDirectory     = Path.GetDirectoryName(executablePath)
            };
        }

        private static string BuildArguments(IReadOnlyList<string> args)
        {
            if (args == null || args.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuoteArgument(args[i]));
            }
            return sb.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (argument == null)  return "\"\"";
            if (argument.Length == 0) return "\"\"";

            bool requiresQuotes = argument.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
            if (!requiresQuotes)
                return argument;

            var sb = new StringBuilder();
            sb.Append('"');

            int backslashCount = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { backslashCount++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashCount * 2 + 1);
                    sb.Append('"');
                    backslashCount = 0;
                    continue;
                }
                if (backslashCount > 0) { sb.Append('\\', backslashCount); backslashCount = 0; }
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
                if (e.Data == null) return;
                lock (output) { output.AppendLine(e.Data); }
                _logger.Info($"[CLIProxyAPI] {e.Data}");
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                lock (output) { output.AppendLine(e.Data); }
                _logger.Warn($"[CLIProxyAPI] {e.Data}");
            };
        }

        private static string BuildProcessFailureMessage(string title, StringBuilder output)
        {
            string details;
            lock (output) { details = output.ToString(); }

            if (string.IsNullOrWhiteSpace(details))
                return title + ".";

            string trimmed = details;
            if (trimmed.Length > 500)
                trimmed = trimmed.Substring(trimmed.Length - 500);

            return title + ": " + trimmed.Trim();
        }

        private static void TryKillProcess(Process process)
        {
            if (process == null || process.HasExited) return;
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
            if (_ownsReleaseService)
                _releaseService.Dispose();
        }
    }
}
