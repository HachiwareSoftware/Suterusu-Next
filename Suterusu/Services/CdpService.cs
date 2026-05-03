using System;
using System.Threading;
using Suterusu.Configuration;

namespace Suterusu.Services
{
    public sealed class CdpService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly CdpClient _client;
        private readonly CdpScriptInjector _injector;
        private readonly object _configLock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _thread;
        private AppConfig _config;
        private bool _started;
        private string _lastFailureMessage;
        private string _lastScriptsSignature;
        private int _failureCount;

        public CdpService(AppConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
            _client = new CdpClient(logger);
            _injector = new CdpScriptInjector(logger);
        }

        public void Start()
        {
            if (_started)
                return;

            _started = true;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Suterusu-CDP"
            };
            _thread.Start();
        }

        public void RefreshConfiguration(AppConfig config)
        {
            lock (_configLock)
                _config = config;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                if (_thread != null && _thread.IsAlive)
                    _thread.Join(1000);
            }
            catch
            {
            }

            _client.Dispose();
            _cts.Dispose();
        }

        private void Run()
        {
            _logger.Info("CDP worker started.");

            while (!_cts.IsCancellationRequested)
            {
                CdpSettings settings = GetSettingsSnapshot();
                if (settings == null || !settings.Enabled)
                {
                    SleepInterruptibly(5000);
                    continue;
                }

                try
                {
                    if (!_client.IsConnected)
                    {
                        bool connected = _client.ConnectAsync(settings, _cts.Token).GetAwaiter().GetResult();
                        if (connected)
                        {
                            ResetConnectionFailureState();
                            _lastScriptsSignature = null;
                        }
                        else
                        {
                            RecordConnectionFailure(settings, "No matching CDP page target found on 127.0.0.1:" + settings.Port + ".");
                        }
                    }

                    if (_client.IsConnected)
                        InjectScriptsIfChanged(settings);
                }
                catch (OperationCanceledException)
                {
                    if (_cts.IsCancellationRequested)
                        break;

                    RecordConnectionFailure(settings, "CDP probe timed out after " + settings.ConnectTimeoutMs + " ms.");
                    _client.Disconnect();
                    _lastScriptsSignature = null;
                }
                catch (Exception ex)
                {
                    RecordConnectionFailure(settings, BuildConnectionFailureMessage(settings, ex));
                    _client.Disconnect();
                    _lastScriptsSignature = null;
                }

                SleepInterruptibly(settings.RetryIntervalMs);
            }

            _logger.Info("CDP worker stopped.");
        }

        private CdpSettings GetSettingsSnapshot()
        {
            lock (_configLock)
            {
                if (_config?.Cdp == null)
                    return null;

                return new CdpSettings
                {
                    Enabled = _config.Cdp.Enabled,
                    Port = _config.Cdp.Port,
                    UrlPattern = _config.Cdp.UrlPattern,
                    StartupScriptsDirectory = _config.Cdp.StartupScriptsDirectory,
                    RetryIntervalMs = _config.Cdp.RetryIntervalMs,
                    ConnectTimeoutMs = _config.Cdp.ConnectTimeoutMs,
                    InjectOnStartup = _config.Cdp.InjectOnStartup
                };
            }
        }

        private void SleepInterruptibly(int milliseconds)
        {
            if (milliseconds < 1)
                milliseconds = 1;

            _cts.Token.WaitHandle.WaitOne(milliseconds);
        }

        private void RecordConnectionFailure(CdpSettings settings, string message)
        {
            _failureCount++;

            if (!string.Equals(_lastFailureMessage, message, StringComparison.Ordinal))
            {
                _lastFailureMessage = message;
                _logger.Warn(message + " Retrying every " + settings.RetryIntervalMs + " ms.");
                return;
            }

            if (_failureCount % 10 == 0)
                _logger.Debug("CDP still unavailable after " + _failureCount + " attempts: " + message);
        }

        private void ResetConnectionFailureState()
        {
            if (_failureCount > 0)
                _logger.Info("CDP connection restored.");

            _lastFailureMessage = null;
            _failureCount = 0;
        }

        private void InjectScriptsIfChanged(CdpSettings settings)
        {
            string signature = _injector.GetStartupScriptsSignature(settings);
            if (string.Equals(_lastScriptsSignature, signature, StringComparison.Ordinal))
                return;

            _injector.InjectStartupScriptsAsync(_client, settings, _cts.Token).GetAwaiter().GetResult();
            _lastScriptsSignature = signature;
        }

        private static string BuildConnectionFailureMessage(CdpSettings settings, Exception ex)
        {
            Exception root = ex;
            while (root.InnerException != null)
                root = root.InnerException;

            string detail = string.IsNullOrWhiteSpace(root.Message) ? ex.Message : root.Message;
            return "CDP unavailable at 127.0.0.1:" + settings.Port + ": " + detail;
        }
    }
}
