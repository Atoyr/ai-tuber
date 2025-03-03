using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Medoz.X;

/// <summary>
/// Main class for interacting with X API
/// </summary>
public class XClient
{
    private readonly HttpClient _httpClient;
    private readonly string _consumerKey;
    private readonly string _consumerSecret;
    private readonly string _accessToken;
    private readonly string _accessTokenSecret;
    private readonly string _baseUrl = "https://api.twitter.com/2";

    /// <summary>
    /// Initializes a new instance of the XClient class
    /// </summary>
    /// <param name="consumerKey">API Key/Consumer Key</param>
    /// <param name="consumerSecret">API Secret Key/Consumer Secret</param>
    /// <param name="accessToken">Access Token</param>
    /// <param name="accessTokenSecret">Access Token Secret</param>
    public XClient(string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret)
    {
        _consumerKey = consumerKey;
        _consumerSecret = consumerSecret;
        _accessToken = accessToken;
        _accessTokenSecret = accessTokenSecret;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Posts a new tweet to X
    /// </summary>
    /// <param name="text">The text content of the tweet</param>
    /// <returns>The response from the X API</returns>
    public async Task<XResponse> PostTweetAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Tweet text cannot be empty", nameof(text));

        if (text.Length > 280)
            throw new ArgumentException("Tweet text cannot exceed 280 characters", nameof(text));

        var endpoint = $"{_baseUrl}/tweets";
        var payload = new { text = text };
        var jsonPayload = JsonSerializer.Serialize(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        // Add OAuth 1.0a authentication headers
        AddOAuthHeaders(request, HttpMethod.Post, endpoint, payload);

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
    /// Posts a tweet with media attachment
    /// </summary>
    /// <param name="text">The text content of the tweet</param>
    /// <param name="mediaPath">Path to the media file</param>
    /// <returns>The response from the X API</returns>
    public Task<XResponse> PostTweetWithMediaAsync(string text, string mediaPath)
    {
        // This would involve first uploading the media, getting a media ID
        // and then attaching it to a tweet. Simplified for this example.
        throw new NotImplementedException("Media upload is not implemented in this version");
    }

    /// <summary>
    /// Deletes a tweet by ID
    /// </summary>
    /// <param name="tweetId">The ID of the tweet to delete</param>
    /// <returns>The response from the X API</returns>
    public async Task<XResponse> DeleteTweetAsync(string tweetId)
    {
        if (string.IsNullOrEmpty(tweetId))
            throw new ArgumentException("Tweet ID cannot be empty", nameof(tweetId));

        var endpoint = $"{_baseUrl}/tweets/{tweetId}";

        var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);

        // Add OAuth 1.0a authentication headers
        AddOAuthHeaders(request, HttpMethod.Delete, endpoint, null);

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
    /// Gets a user's timeline
    /// </summary>
    /// <param name="userId">The user ID whose timeline to fetch</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <returns>The response from the X API</returns>
    public async Task<XResponse> GetUserTimelineAsync(string userId, int maxResults = 10)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("User ID cannot be empty", nameof(userId));

        var endpoint = $"{_baseUrl}/users/{userId}/tweets";
        var queryParams = new Dictionary<string, string>
            {
                { "max_results", maxResults.ToString() }
            };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={p.Value}"));
        var fullUrl = $"{endpoint}?{queryString}";

        var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);

        // Add OAuth 1.0a authentication headers
        AddOAuthHeaders(request, HttpMethod.Get, endpoint, null, queryParams);

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
    /// Adds OAuth 1.0a authentication headers to the request
    /// </summary>
    private void AddOAuthHeaders(HttpRequestMessage request, HttpMethod method, string url,
        object? payload = null, Dictionary<string, string>? queryParams = null)
    {
        // Implementation of OAuth 1.0a signature generation
        // This is a placeholder - actual implementation would be quite complex

        var oauthTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var oauthNonce = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "").Replace("/", "").Replace("=", "");

        // Generate OAuth signature (this is simplified)
        // Actual implementation would need to follow OAuth 1.0a spec

        var authHeader = $"OAuth " +
                         $"oauth_consumer_key=\"{_consumerKey}\", " +
                         $"oauth_nonce=\"{oauthNonce}\", " +
                         $"oauth_signature=\"GENERATED_SIGNATURE\", " + // Placeholder
                         $"oauth_signature_method=\"HMAC-SHA1\", " +
                         $"oauth_timestamp=\"{oauthTimestamp}\", " +
                         $"oauth_token=\"{_accessToken}\", " +
                         $"oauth_version=\"1.0\"";

        request.Headers.Add("Authorization", authHeader);
    }
}
