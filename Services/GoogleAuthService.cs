using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace XenChat.Services
{
    public class GoogleUserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public string Name { get; set; } = string.Empty;
        public string GivenName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string Picture { get; set; } = string.Empty;
    }

    public class GoogleAuthService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GoogleAuthService> _logger;

        public GoogleAuthService(
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            ILogger<GoogleAuthService> logger)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private string GetSetting(params string[] keys)
        {
            foreach (var key in keys)
            {
                var envVal = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(envVal)) return envVal.Trim();

                var configVal = _config[key];
                if (!string.IsNullOrWhiteSpace(configVal)) return configVal.Trim();
            }
            return string.Empty;
        }

        public string ClientId => GetSetting("GOOGLE_CLIENT_ID", "Google:ClientId", "Authentication:Google:ClientId", "Authentication__Google__ClientId");

        public string ClientSecret => GetSetting("GOOGLE_CLIENT_SECRET", "Google:ClientSecret", "Authentication:Google:ClientSecret", "Authentication__Google__ClientSecret");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

        /// <summary>
        /// Derives the callback redirect URI matching the user's registered redirect URI in Google Cloud Console.
        /// Handles reverse proxy HTTPS termination (e.g. Render / Cloudflare) dynamically.
        /// </summary>
        public string GetRedirectUri(HttpRequest request)
        {
            // 1. Check explicit override in config or environment
            var explicitRedirect = GetSetting("GOOGLE_REDIRECT_URI", "Google:RedirectUri", "Authentication:Google:RedirectUri");

            if (!string.IsNullOrWhiteSpace(explicitRedirect))
            {
                return explicitRedirect.Trim();
            }

            // 2. Derive scheme (detect reverse proxy / HTTPS on Render)
            string scheme = "http";
            if (request.Headers.TryGetValue("X-Forwarded-Proto", out var protoVal) && !string.IsNullOrWhiteSpace(protoVal))
            {
                scheme = protoVal.ToString().Split(',')[0].Trim().ToLowerInvariant();
            }
            else if (request.IsHttps || request.Host.Host.EndsWith("onrender.com", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https";
            }
            else
            {
                scheme = request.Scheme;
            }

            // 3. Derive host
            string host = request.Host.Value;
            if (request.Headers.TryGetValue("X-Forwarded-Host", out var hostVal) && !string.IsNullOrWhiteSpace(hostVal))
            {
                host = hostVal.ToString().Split(',')[0].Trim();
            }

            // If running on Render, ensure https is forced
            if (host.EndsWith("onrender.com", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https";
            }

            return $"{scheme}://{host}/auth/google/callback";
        }

        /// <summary>
        /// Generates the Google OAuth 2.0 authorization URL.
        /// </summary>
        public string GetAuthorizationUrl(string redirectUri, string state)
        {
            var query = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["access_type"] = "online",
                ["prompt"] = "select_account"
            };

            var queryString = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            return $"https://accounts.google.com/o/oauth2/v2/auth?{queryString}";
        }

        /// <summary>
        /// Exchanges authorization code for an access token and fetches user info from Google.
        /// </summary>
        public async Task<GoogleUserInfo?> ExchangeCodeForUserInfoAsync(string code, string redirectUri)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("[GoogleAuth] Cannot exchange code: Google OAuth credentials are not configured.");
                return null;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();

                // 1. Exchange authorization code for tokens
                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["code"] = code,
                        ["client_id"] = ClientId,
                        ["client_secret"] = ClientSecret,
                        ["redirect_uri"] = redirectUri,
                        ["grant_type"] = "authorization_code"
                    })
                };

                var tokenResponse = await client.SendAsync(tokenRequest);
                var tokenContent = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("[GoogleAuth] Token exchange failed with status {StatusCode}: {Response}", tokenResponse.StatusCode, tokenContent);
                    return null;
                }

                using var tokenDoc = JsonDocument.Parse(tokenContent);
                var root = tokenDoc.RootElement;
                if (!root.TryGetProperty("access_token", out var accessTokenElem))
                {
                    _logger.LogError("[GoogleAuth] No access_token found in response.");
                    return null;
                }

                var accessToken = accessTokenElem.GetString();
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    _logger.LogError("[GoogleAuth] access_token is empty.");
                    return null;
                }

                // 2. Fetch userinfo using access_token
                var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
                userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var userInfoResponse = await client.SendAsync(userInfoRequest);
                var userInfoContent = await userInfoResponse.Content.ReadAsStringAsync();

                if (!userInfoResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("[GoogleAuth] Userinfo request failed with status {StatusCode}: {Response}", userInfoResponse.StatusCode, userInfoContent);
                    return null;
                }

                using var userDoc = JsonDocument.Parse(userInfoContent);
                var userRoot = userDoc.RootElement;

                var userInfo = new GoogleUserInfo
                {
                    Id = userRoot.TryGetProperty("sub", out var subElem) ? subElem.GetString() ?? "" : "",
                    Email = userRoot.TryGetProperty("email", out var emailElem) ? emailElem.GetString() ?? "" : "",
                    EmailVerified = userRoot.TryGetProperty("email_verified", out var evElem) && (evElem.ValueKind == JsonValueKind.True || evElem.GetString() == "true"),
                    Name = userRoot.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "",
                    GivenName = userRoot.TryGetProperty("given_name", out var gnElem) ? gnElem.GetString() ?? "" : "",
                    FamilyName = userRoot.TryGetProperty("family_name", out var fnElem) ? fnElem.GetString() ?? "" : "",
                    Picture = userRoot.TryGetProperty("picture", out var picElem) ? picElem.GetString() ?? "" : ""
                };

                return userInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GoogleAuth] Error during Google OAuth code exchange.");
                return null;
            }
        }

        /// <summary>
        /// Validates Google Identity Services (One Tap) credential (ID token).
        /// </summary>
        public async Task<GoogleUserInfo?> ValidateCredentialAsync(string credential)
        {
            if (string.IsNullOrWhiteSpace(credential))
                return null;

            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(credential)}");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                return new GoogleUserInfo
                {
                    Id = root.TryGetProperty("sub", out var subElem) ? subElem.GetString() ?? "" : "",
                    Email = root.TryGetProperty("email", out var emailElem) ? emailElem.GetString() ?? "" : "",
                    EmailVerified = root.TryGetProperty("email_verified", out var evElem) && (evElem.ValueKind == JsonValueKind.True || evElem.GetString() == "true"),
                    Name = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "",
                    GivenName = root.TryGetProperty("given_name", out var gnElem) ? gnElem.GetString() ?? "" : "",
                    FamilyName = root.TryGetProperty("family_name", out var fnElem) ? fnElem.GetString() ?? "" : "",
                    Picture = root.TryGetProperty("picture", out var picElem) ? picElem.GetString() ?? "" : ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GoogleAuth] Error validating Google ID token credential.");
                return null;
            }
        }
    }
}
