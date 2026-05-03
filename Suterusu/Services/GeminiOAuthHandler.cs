using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class GeminiOAuthHandler : IDisposable
    {
        private static string ClientID
        {
            get
            {
                var p = new[]
                {
                    "681255809395", "oo8ft2oprdrnp9e3aqf6av3hmdib135j",
                    "apps.googleusercontent.com"
                };
                return p[0] + "-" + p[1] + "." + p[2];
            }
        }

        private static string ClientSecret
        {
            get
            {
                var p = new[] { "GOCSPX", "4uHgMPm", "1o7Sk", "geV6Cu5clXFsxl" };
                return string.Join("-", p);
            }
        }
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v1/userinfo?alt=json";
        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/auth";

        private static readonly string[] Scopes =
        {
            "https://www.googleapis.com/auth/cloud-platform",
            "https://www.googleapis.com/auth/userinfo.email",
            "https://www.googleapis.com/auth/userinfo.profile"
        };

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public GeminiOAuthHandler(ILogger logger)
            : this(logger, new HttpClientHandler())
        {
        }

        public GeminiOAuthHandler(ILogger logger, HttpMessageHandler handler)
        {
            _logger = logger;
            _httpClient = new HttpClient(handler);
        }

        public async Task<CliProxyResult> LoginAsync(
            CliProxySettings settings, CancellationToken cancellationToken)
        {
            int port = settings.OAuthCallbackPort;
            string redirectUri = $"http://localhost:{port}/oauth2callback";

            string state = GenerateRandomState();
            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = ComputeS256Challenge(codeVerifier);

            string authUrl = BuildAuthUrl(redirectUri, state, codeChallenge);

            string authCode = null;
            HttpListener listener = null;

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
                _logger.Debug($"Gemini OAuth callback listening on port {port}");
            }
            catch (Exception ex)
            {
                return CliProxyResult.Fail(
                    $"Failed to start OAuth callback server on port {port}: {ex.Message}");
            }

            try
            {
                OpenBrowser(authUrl);

                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token))
                {
                    authCode = await WaitForCallbackAsync(listener, state, linkedCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return CliProxyResult.Fail("Gemini OAuth flow timed out or was canceled.");
            }
            catch (Exception ex)
            {
                _logger.Error("Gemini OAuth callback failed.", ex);
                return CliProxyResult.Fail("Gemini OAuth callback failed: " + ex.Message);
            }
            finally
            {
                try { listener.Stop(); } catch { }
                try { listener.Close(); } catch { }
            }

            if (string.IsNullOrEmpty(authCode))
                return CliProxyResult.Fail("No authorization code received.");

            _logger.Info("Gemini authorization code received. Exchanging for tokens...");

            var tokenResult = await ExchangeCodeForTokensAsync(
                authCode, redirectUri, codeVerifier, cancellationToken).ConfigureAwait(false);
            if (!tokenResult.Success)
                return CliProxyResult.Fail(tokenResult.Error);

            var tokenData = tokenResult.TokenData;
            string email = await GetUserEmailAsync(tokenData.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            SaveTokenFile(settings, tokenData, email);
            _logger.Info($"Gemini authentication successful for {email ?? "unknown"}.");

            return CliProxyResult.Ok();
        }

        private async Task<string> WaitForCallbackAsync(
            HttpListener listener, string expectedState, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var getContextTask = listener.GetContextAsync();
                var tcs = new TaskCompletionSource<bool>();
                using (ct.Register(() => tcs.TrySetResult(true)))
                {
                    var completed = await Task.WhenAny(getContextTask, tcs.Task)
                        .ConfigureAwait(false);
                    if (completed != getContextTask)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                }

                var context = await getContextTask.ConfigureAwait(false);
                var request = context.Request;
                var response = context.Response;

                string code = request.QueryString.Get("code");
                string state = request.QueryString.Get("state");
                string error = request.QueryString.Get("error");

                if (!string.IsNullOrEmpty(error))
                {
                    ServeErrorPage(response, error);
                    throw new InvalidOperationException($"OAuth error: {error}");
                }

                if (string.IsNullOrEmpty(code))
                {
                    ServeErrorPage(response, "No authorization code received.");
                    throw new InvalidOperationException("No authorization code in callback.");
                }

                ServeSuccessPage(response);
                return code;
            }

            ct.ThrowIfCancellationRequested();
            return null;
        }

        private async Task<TokenExchangeResult> ExchangeCodeForTokensAsync(
            string code, string redirectUri, string codeVerifier, CancellationToken ct)
        {
            var form = new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", ClientID },
                { "client_secret", ClientSecret },
                { "redirect_uri", redirectUri },
                { "grant_type", "authorization_code" },
                { "code_verifier", codeVerifier }
            };

            HttpResponseMessage resp;
            try
            {
                resp = await _httpClient.PostAsync(
                    TokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TokenExchangeResult.Fail("Token exchange request failed: " + ex.Message);
            }

            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return TokenExchangeResult.Fail(
                    $"Token exchange failed ({(int)resp.StatusCode}): {body}");
            }

            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch (Exception ex)
            {
                return TokenExchangeResult.Fail("Failed to parse token response: " + ex.Message);
            }

            var data = new TokenData
            {
                AccessToken = json.Value<string>("access_token") ?? "",
                RefreshToken = json.Value<string>("refresh_token") ?? "",
                TokenType = json.Value<string>("token_type") ?? "Bearer",
                ExpiresIn = json.Value<int>("expires_in"),
                Scope = json.Value<string>("scope") ?? ""
            };

            if (string.IsNullOrEmpty(data.AccessToken))
                return TokenExchangeResult.Fail("Token response missing access_token.");

            return TokenExchangeResult.Ok(data);
        }

        private async Task<string> GetUserEmailAsync(string accessToken, CancellationToken ct)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
                req.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.Warn("Failed to fetch user info: " + (int)resp.StatusCode);
                    return null;
                }

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(body);
                return json.Value<string>("email");
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to fetch user email: " + ex.Message);
                return null;
            }
        }

        private void SaveTokenFile(CliProxySettings settings, TokenData data, string email)
        {
            string authDir = settings.AuthDirectory;
            if (string.IsNullOrWhiteSpace(authDir))
                authDir = Path.Combine(settings.RuntimeDirectory, "auths");

            Directory.CreateDirectory(authDir);

            string safeEmail = (email ?? "unknown");
            string fileName = $"{safeEmail}-.json";
            string filePath = Path.Combine(authDir, fileName);

            var tokenObj = new Dictionary<string, object>
            {
                { "access_token", data.AccessToken },
                { "token_type", data.TokenType },
                { "refresh_token", data.RefreshToken },
                { "expiry", DateTime.UtcNow.AddSeconds(data.ExpiresIn).ToString("o") },
                { "token_uri", TokenEndpoint },
                { "client_id", ClientID },
                { "client_secret", ClientSecret },
                { "scopes", Scopes },
                { "universe_domain", "googleapis.com" }
            };

            var storage = new Dictionary<string, object>
            {
                { "token", tokenObj },
                { "project_id", "" },
                { "email", email ?? "" },
                { "auto", false },
                { "checked", false },
                { "type", "gemini" }
            };

            string json = JsonConvert.SerializeObject(storage, Formatting.Indented);
            File.WriteAllText(filePath, json, Encoding.UTF8);
            _logger.Info($"Gemini token saved to {filePath}");
        }

        private static string BuildAuthUrl(string redirectUri, string state, string codeChallenge)
        {
            var qs = new Dictionary<string, string>
            {
                { "client_id", ClientID },
                { "redirect_uri", redirectUri },
                { "response_type", "code" },
                { "scope", string.Join(" ", Scopes) },
                { "access_type", "offline" },
                { "prompt", "consent" },
                { "state", state },
                { "code_challenge", codeChallenge },
                { "code_challenge_method", "S256" }
            };

            var sb = new StringBuilder(AuthEndpoint);
            sb.Append('?');
            bool first = true;
            foreach (var kv in qs)
            {
                if (!first) sb.Append('&');
                first = false;
                sb.Append(Uri.EscapeDataString(kv.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(kv.Value));
            }
            return sb.ToString();
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to open browser: " + ex.Message);
            }
        }

        private static string GenerateRandomState()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string ComputeS256Challenge(string verifier)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
                return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        private void ServeSuccessPage(HttpListenerResponse response)
        {
            string html = SuccessPageHtml;
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            try
            {
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch { }
        }

        private void ServeErrorPage(HttpListenerResponse response, string error)
        {
            string html = ErrorPageHtml.Replace("{{ERROR}}", WebUtility.HtmlEncode(error));
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.StatusCode = 400;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            try
            {
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch { }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private class TokenData
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public string TokenType { get; set; }
            public int ExpiresIn { get; set; }
            public string Scope { get; set; }
        }

        private class TokenExchangeResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public TokenData TokenData { get; set; }

            public static TokenExchangeResult Ok(TokenData data)
                => new TokenExchangeResult { Success = true, TokenData = data };

            public static TokenExchangeResult Fail(string error)
                => new TokenExchangeResult { Success = false, Error = error };
        }

        private const string SuccessPageHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Authorized - Suterusu</title>
    <link rel=""icon"" type=""image/svg+xml"" href=""data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'%3E%3Ctext y='.9em' font-size='90'%3E%F0%9F%94%92%3C/text%3E%3C/svg%3E"">
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex; justify-content: center; align-items: center;
            min-height: 100vh; background: #0a0a0a; color: #e5e5e5;
        }
        .card {
            text-align: center; background: #141414;
            padding: 3rem 2.5rem; border-radius: 16px;
            max-width: 440px; width: 90%;
            border: 1px solid #262626;
            animation: fadeUp .4s ease-out;
        }
        @keyframes fadeUp {
            from { opacity: 0; transform: translateY(12px); }
            to { opacity: 1; transform: translateY(0); }
        }
        .icon {
            width: 56px; height: 56px; margin: 0 auto 1.5rem;
            background: #10b981; border-radius: 50%;
            display: flex; align-items: center; justify-content: center;
            font-size: 1.5rem;
        }
        h1 {
            font-size: 1.5rem; font-weight: 600; color: #fafafa;
            margin-bottom: .5rem; letter-spacing: -0.02em;
        }
        .sub {
            color: #a3a3a3; font-size: .9375rem; line-height: 1.6;
            margin-bottom: 2rem;
        }
        .brand {
            padding-top: 1.5rem; border-top: 1px solid #262626;
            color: #525252; font-size: .75rem;
            font-weight: 500; letter-spacing: .08em; text-transform: uppercase;
        }
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon"">
            <svg width=""24"" height=""24"" viewBox=""0 0 24 24"" fill=""none"" stroke=""white"" stroke-width=""2.5"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <polyline points=""20 6 9 17 4 12""></polyline>
            </svg>
        </div>
        <h1>Authorized successful</h1>
        <p class=""sub"">You may now return to the application to continue.</p>
        <div class=""brand"">Suterusu</div>
    </div>
</body>
</html>";

        private const string ErrorPageHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Authorization Failed - Suterusu</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex; justify-content: center; align-items: center;
            min-height: 100vh; background: #0a0a0a; color: #e5e5e5;
        }
        .card {
            text-align: center; background: #141414;
            padding: 3rem 2.5rem; border-radius: 16px;
            max-width: 440px; width: 90%;
            border: 1px solid #262626;
        }
        .icon {
            width: 56px; height: 56px; margin: 0 auto 1.5rem;
            background: #ef4444; border-radius: 50%;
            display: flex; align-items: center; justify-content: center;
            font-size: 1.5rem;
        }
        h1 { font-size: 1.5rem; font-weight: 600; color: #fafafa; margin-bottom: .5rem; }
        .sub { color: #a3a3a3; font-size: .875rem; line-height: 1.6; margin-bottom: 2rem; }
        .brand {
            padding-top: 1.5rem; border-top: 1px solid #262626;
            color: #525252; font-size: .75rem;
            font-weight: 500; letter-spacing: .08em; text-transform: uppercase;
        }
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon"">
            <svg width=""24"" height=""24"" viewBox=""0 0 24 24"" fill=""none"" stroke=""white"" stroke-width=""2.5"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <line x1=""18"" y1=""6"" x2=""6"" y2=""18""></line>
                <line x1=""6"" y1=""6"" x2=""18"" y2=""18""></line>
            </svg>
        </div>
        <h1>Authorization Failed</h1>
        <p class=""sub"">{{ERROR}}</p>
        <div class=""brand"">Suterusu</div>
    </div>
</body>
</html>";
    }
}
