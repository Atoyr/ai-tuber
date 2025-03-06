﻿using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Medoz.X;

/// <summary>
/// Main class for interacting with X API
/// </summary>
public class XClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://api.twitter.com/2";

    // OAuth 1.0a認証情報（レガシーサポート用）
    private readonly string _consumerKey = string.Empty;
    private readonly string _consumerSecret = string.Empty;
    private readonly string _accessToken = string.Empty;
    private readonly string _accessTokenSecret = string.Empty;

    // OAuth 2.0 User Context認証情報
    private readonly string _clientId = string.Empty;
    private readonly string _clientSecret = string.Empty;
    private string _oauth2AccessToken = string.Empty;
    private string _oauth2RefreshToken = string.Empty;
    private DateTime _oauth2TokenExpiry = DateTime.MinValue;

    // Bearer Token認証用（一部のエンドポイントで使用）
    private readonly string? _bearerToken;

    // 認証タイプを示す列挙型
    private enum AuthType
    {
        OAuth1,
        OAuth2,
        BearerToken
    }

    private readonly AuthType _authType;

    /// <summary>
    /// Initializes a new instance of the XClient class with OAuth 1.0a credentials (レガシーサポート)
    /// </summary>
    /// <param name="consumerKey">Consumer Key (API Key)</param>
    /// <param name="consumerSecret">Consumer Secret (API Secret)</param>
    /// <param name="accessToken">Access Token</param>
    /// <param name="accessTokenSecret">Access Token Secret</param>
    public XClient(string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret)
    {
        _consumerKey = consumerKey;
        _consumerSecret = consumerSecret;
        _accessToken = accessToken;
        _accessTokenSecret = accessTokenSecret;
        _httpClient = new HttpClient();
        _authType = AuthType.OAuth1;
    }

    /// <summary>
    /// Initializes a new instance of the XClient class with OAuth 2.0 User Context credentials
    /// </summary>
    /// <param name="clientId">OAuth 2.0 Client ID</param>
    /// <param name="clientSecret">OAuth 2.0 Client Secret</param>
    /// <param name="accessToken">OAuth 2.0 Access Token</param>
    /// <param name="refreshToken">OAuth 2.0 Refresh Token</param>
    /// <param name="tokenExpiry">Access Token有効期限（UTC）</param>
    public XClient(string clientId, string clientSecret, string accessToken, string refreshToken, DateTime tokenExpiry)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _oauth2AccessToken = accessToken;
        _oauth2RefreshToken = refreshToken;
        _oauth2TokenExpiry = tokenExpiry;
        _httpClient = new HttpClient();
        _authType = AuthType.OAuth2;
    }

    /// <summary>
    /// Initializes a new instance of the XClient class with Bearer Token
    /// </summary>
    /// <param name="bearerToken">Bearer token for API v2 authentication</param>
    public XClient(string bearerToken)
    {
        _bearerToken = bearerToken;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        _authType = AuthType.BearerToken;
    }

    /// <summary>
    /// OAuth 2.0アクセストークンが有効期限切れかどうかを確認し、必要に応じてリフレッシュする
    /// </summary>
    /// <returns>リフレッシュが必要だった場合はtrue、それ以外はfalse</returns>
    private async Task<bool> RefreshTokenIfNeededAsync()
    {
        // アクセストークンの有効期限が切れているか、または切れる10分前の場合はリフレッシュ
        if (_authType == AuthType.OAuth2 &&
            (_oauth2TokenExpiry == DateTime.MinValue ||
             _oauth2TokenExpiry.AddMinutes(-10) < DateTime.UtcNow))
        {
            // トークンリフレッシュのエンドポイント
            var tokenUrl = "https://api.twitter.com/2/oauth2/token";

            // リフレッシュトークンリクエストの作成
            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", _oauth2RefreshToken },
                { "client_id", _clientId }
            });

            // Basic認証ヘッダーの作成（クライアントIDとシークレットを使用）
            if (!string.IsNullOrEmpty(_clientSecret))
            {
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            }

            // リフレッシュトークンリクエストの送信
            var response = await _httpClient.PostAsync(tokenUrl, requestContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // レスポンスからトークン情報を取得
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // 新しいアクセストークンを設定
                _oauth2AccessToken = tokenResponse.GetProperty("access_token").GetString() ?? string.Empty;

                // リフレッシュトークンが含まれている場合は更新
                if (tokenResponse.TryGetProperty("refresh_token", out var refreshToken))
                {
                    _oauth2RefreshToken = refreshToken.GetString() ?? _oauth2RefreshToken;
                }

                // 有効期限を設定
                if (tokenResponse.TryGetProperty("expires_in", out var expiresIn))
                {
                    _oauth2TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn.GetInt32());
                }
                else
                {
                    // デフォルトの有効期限（1時間）
                    _oauth2TokenExpiry = DateTime.UtcNow.AddHours(1);
                }

                return true;
            }
            else
            {
                throw new InvalidOperationException($"Failed to refresh OAuth 2.0 token: {responseContent}");
            }
        }

        return false;
    }

    /// <summary>
    /// Posts a new tweet to X using the configured authentication method
    /// </summary>
    /// <param name="text">The text content of the tweet</param>
    /// <param name="replyTo">Optional tweet ID to reply to</param>
    /// <param name="quoteTweetId">Optional tweet ID to quote</param>
    /// <param name="pollOptions">Optional poll options</param>
    /// <param name="pollDurationMinutes">Optional poll duration in minutes</param>
    /// <param name="mediaIds">Optional media IDs to attach</param>
    /// <returns>The response from the X API</returns>
    public async Task<XResponse> PostTweetAsync(
        string text,
        string? replyTo = null,
        string? quoteTweetId = null,
        IEnumerable<string>? pollOptions = null,
        int? pollDurationMinutes = null,
        IEnumerable<string>? mediaIds = null)
    {
        if (string.IsNullOrEmpty(text) && mediaIds == null)
            throw new ArgumentException("Tweet must contain either text or media", nameof(text));

        if (!string.IsNullOrEmpty(text) && text.Length > 280)
            throw new ArgumentException("Tweet text cannot exceed 280 characters", nameof(text));

        // 認証タイプに応じた検証
        switch (_authType)
        {
            case AuthType.OAuth1:
                if (string.IsNullOrEmpty(_consumerKey) || string.IsNullOrEmpty(_consumerSecret) ||
                    string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_accessTokenSecret))
                {
                    throw new InvalidOperationException("OAuth 1.0a credentials are required for posting tweets with OAuth 1.0a authentication.");
                }
                break;

            case AuthType.OAuth2:
                if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_oauth2AccessToken))
                {
                    throw new InvalidOperationException("OAuth 2.0 credentials are required for posting tweets with OAuth 2.0 authentication.");
                }

                // OAuth 2.0トークンのリフレッシュが必要な場合は実行
                await RefreshTokenIfNeededAsync();
                break;

            case AuthType.BearerToken:
                throw new InvalidOperationException("Bearer Token authentication cannot be used for posting tweets. Use OAuth 1.0a or OAuth 2.0 authentication instead.");
        }

        var endpoint = $"{_baseUrl}/tweets";

        // Build the tweet payload
        var payload = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(text))
            payload.Add("text", text);

        // Add reply settings if specified
        if (!string.IsNullOrEmpty(replyTo))
        {
            payload.Add("reply", new Dictionary<string, object>
                {
                    { "in_reply_to_tweet_id", replyTo }
                });
        }

        // Add quote tweet if specified
        if (!string.IsNullOrEmpty(quoteTweetId))
        {
            payload.Add("quote_tweet_id", quoteTweetId);
        }

        // Add poll if specified
        if (pollOptions != null && pollOptions.Count() >= 2 && pollDurationMinutes.HasValue)
        {
            payload.Add("poll", new Dictionary<string, object>
                {
                    { "options", pollOptions },
                    { "duration_minutes", pollDurationMinutes.Value }
                });
        }

        // Add media if specified
        if (mediaIds != null && mediaIds.Count() > 0)
        {
            payload.Add("media", new Dictionary<string, object>
                {
                    { "media_ids", mediaIds }
                });
        }

        var jsonPayload = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        // 認証タイプに応じた認証ヘッダーを設定
        switch (_authType)
        {
            case AuthType.OAuth1:
                // OAuth 1.0a認証ヘッダーを生成
                var authHeader = GenerateOAuthHeader(HttpMethod.Post, endpoint);
                request.Headers.Add("Authorization", authHeader);
                break;

            case AuthType.OAuth2:
                // OAuth 2.0 Bearer認証ヘッダーを設定
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _oauth2AccessToken);
                break;
        }

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        return new XResponse
        {
            IsSuccess = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            Content = responseContent
        };
    }

    /// <summary>
    /// OAuth 1.0a認証ヘッダーを生成する（レガシーサポート用）
    /// </summary>
    /// <param name="method">HTTPメソッド</param>
    /// <param name="url">リクエストURL</param>
    /// <param name="parameters">追加のパラメータ（オプション）</param>
    /// <returns>OAuth認証ヘッダー文字列</returns>
    private string GenerateOAuthHeader(HttpMethod method, string url, Dictionary<string, string>? parameters = null)
    {
        // OAuth 1.0aに必要なパラメータ
        var oauthParams = new Dictionary<string, string>
        {
            { "oauth_consumer_key", _consumerKey },
            { "oauth_nonce", GenerateNonce() },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
            { "oauth_token", _accessToken },
            { "oauth_version", "1.0" }
        };

        // 追加のパラメータがあれば結合
        var allParams = new Dictionary<string, string>(oauthParams);
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                allParams.Add(param.Key, param.Value);
            }
        }

        // 署名ベース文字列の作成
        var signatureBaseString = GenerateSignatureBaseString(method.ToString(), url, allParams);

        // 署名キーの作成
        var signatureKey = $"{Uri.EscapeDataString(_consumerSecret)}&{Uri.EscapeDataString(_accessTokenSecret)}";

        // 署名の生成
        var signature = GenerateSignature(signatureBaseString, signatureKey);

        // 署名をOAuthパラメータに追加
        oauthParams.Add("oauth_signature", signature);

        // OAuth認証ヘッダーの作成
        var headerBuilder = new StringBuilder("OAuth ");
        var first = true;
        foreach (var param in oauthParams)
        {
            if (!first)
                headerBuilder.Append(", ");
            headerBuilder.Append($"{Uri.EscapeDataString(param.Key)}=\"{Uri.EscapeDataString(param.Value)}\"");
            first = false;
        }

        return headerBuilder.ToString();
    }

    /// <summary>
    /// OAuth 1.0a用のランダムなnonceを生成する（レガシーサポート用）
    /// </summary>
    /// <returns>ランダムなnonce文字列</returns>
    private string GenerateNonce()
    {
        var random = new Random();
        var nonce = new byte[32];
        random.NextBytes(nonce);
        return Convert.ToBase64String(nonce).Replace("+", "").Replace("/", "").Replace("=", "");
    }

    /// <summary>
    /// OAuth 1.0a用の署名ベース文字列を生成する（レガシーサポート用）
    /// </summary>
    /// <param name="method">HTTPメソッド</param>
    /// <param name="url">リクエストURL</param>
    /// <param name="parameters">パラメータ</param>
    /// <returns>署名ベース文字列</returns>
    private string GenerateSignatureBaseString(string method, string url, Dictionary<string, string> parameters)
    {
        // パラメータをソートしてエンコード
        var encodedParams = parameters
            .OrderBy(p => p.Key)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")
            .ToList();

        var paramString = string.Join("&", encodedParams);

        // 署名ベース文字列の作成
        return $"{method.ToUpper()}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(paramString)}";
    }

    /// <summary>
    /// HMAC-SHA1署名を生成する（レガシーサポート用）
    /// </summary>
    /// <param name="input">署名ベース文字列</param>
    /// <param name="key">署名キー</param>
    /// <returns>HMAC-SHA1署名</returns>
    private string GenerateSignature(string input, string key)
    {
        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.ASCII.GetBytes(input));
        return Uri.EscapeDataString(Convert.ToBase64String(hash));
    }
}
