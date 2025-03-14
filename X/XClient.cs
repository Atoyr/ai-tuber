using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;

namespace Medoz.X;

/// <summary>
/// Main class for interacting with X API
/// </summary>
public class XClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// APIエンドポイントのベースURL
    /// </summary>
    private readonly string _baseUrl = "https://api.twitter.com/2";

    /// <summary>
    /// 認可エンドポイント
    /// </summary>
    private readonly string _authzEndpoint = "https://twitter.com/i/oauth2/authorize";

    private string _tokenEndpoint => $"{_baseUrl}/oauth2/token";
    private string _postTweetEndpoint => $"{_baseUrl}/tweets";

    private string _getMeEndpoint => $"{_baseUrl}/users/me";

    private readonly uint _tweetLength = 280;

    /// <summary>
    /// ローカルで立ち上げるサーバ
    /// </summary>
    private readonly string _domain = "http://localhost";

    /// <summary>
    /// ローカルで立ち上げるサーバのポート
    /// </summary>
    private readonly int _port = 18080;

    private string _redirectUrl => $"{_domain}:{_port}";

    private readonly object _oAuth2AccessTokenLock = new();
    private OAuth2Token? _oAuth2AccessToken;
    internal OAuth2Token? OAuth2AccessToken
    {
        get
        {
            lock (_oAuth2AccessTokenLock)
            {
                return _oAuth2AccessToken;
            }
        }
        // setは所定の箇所のみなので、lockは不要
        private set => _oAuth2AccessToken = value;
    }

    /// <summary>
    /// OAuth 2.0 認証情報
    /// </summary>
    private readonly Session _session;

    // Bearer Token認証用（一部のエンドポイントで使用）
    private readonly string? _bearerToken;

    /// <summary>
    /// スコープを設定する
    /// </summary>
    private Scopes _scopse { get; init; }

    // 認証タイプを示す列挙型
    private enum AuthType
    {
        OAuth1,
        OAuth2,
        BearerToken
    }

    private readonly AuthType _authType;

    /// <summary>
    /// Initializes a new instance of the XClient class with OAuth 2.0 User Context credentials
    /// </summary>
    /// <param name="clientId">OAuth 2.0 Client ID</param>
    /// <param name="clientSecret">OAuth 2.0 Client Secret</param>
    /// <param name="redirectUrl">OAuth 2.0 Redirect URL</param>
    public XClient(
        string clientId,
        string clientSecret,
        int port = 18080,
        string domain = "http://localhost",
        Scopes scopes = Scopes.tweet_read | Scopes.tweet_write | Scopes.users_read)
    {
        _domain = domain;
        _port = port;
        _session = new Session(_redirectUrl, clientId, clientSecret);
        _scopse = scopes;
        _httpClient = new HttpClient();
    }

    public XClient(
        string clientId,
        string clientSecret,
        OAuth2Token accessToken,
        int port = 18080,
        string domain = "http://localhost",
        Scopes scopes = Scopes.tweet_read | Scopes.tweet_write | Scopes.users_read)
        : this(clientId, clientSecret, port, domain)
    {
        _httpClient = new HttpClient();
        OAuth2AccessToken = accessToken;
    }

    public async Task<OAuth2Token> AuthzAsync(Action<string> authzRequestHandler)
    {
        return await Task.Run(() => {
            // 長期間ロックが発生するので、OAuth2AccessTokenの実体にアクセスする
            lock(_oAuth2AccessTokenLock)
            {
                if(_oAuth2AccessToken is null)
                {
                    var url = buildAuthzURL(_session, _scopse);
                    authzRequestHandler(url);
                    TaskAwaiter<string> awaiter = WaitForAuthorizationCodeAsync(url).GetAwaiter();
                    awaiter.OnCompleted(async () =>
                    {
                        var code = awaiter.GetResult();
                        Console.WriteLine(code);
                        var token = await GetTokenAsync(code, _session);
                        _oAuth2AccessToken = token;
                    });
                }
                return _oAuth2AccessToken!;
            }
        });
    }

    /// <summary>
    /// 認可エンドポイントのURLを構築する
    /// </summary>
    private string buildAuthzURL(Session session, Scopes scopes)
    {
        Dictionary<string, string> queryParams = new()
        {
            { "response_type", "code" },
            { "client_id", session.ClientId },
            { "redirect_uri", session.RedirectUri },
            { "scope", scopes.ToScopeString()},
            { "state", _session.State },
            { "code_challenge", _session.CodeChallenge },
            { "code_challenge_method", _session.CodeChallengeMethod }
        };
        var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var endpoint = $"{_authzEndpoint}?{queryString}";

        return endpoint;
    }


    /// <summary>
    /// ローカルHTTPサーバーを起動して認可コードを待ち受ける
    /// </summary>
    /// <returns>認可コード</returns>
    private async Task<string> WaitForAuthorizationCodeAsync(string authzUrl)
    {
        TaskCompletionSource<string> authorizationCodeTaskSource = new TaskCompletionSource<string>();
        string? authorizationCode = null;

        // HTTPサーバーの設定
        HttpListener listener = new();
        listener.Prefixes.Add(_redirectUrl + "/");

        try
        {
            listener.Start();

            // 非同期でリクエストを待ち受け
            ThreadPool.QueueUserWorkItem(async (state) =>
            {
                try
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    HttpListenerRequest request = context.Request;
                    HttpListenerResponse response = context.Response;

                    // クエリパラメータから認可コードを取得
                    if (request.QueryString.AllKeys.Contains("code"))
                    {
                        authorizationCode = request.QueryString.Get("code");
                        Console.WriteLine($"Authorization Code: {authorizationCode}");

                        // ブラウザに成功メッセージを返す
                        string responseString = "<html><head><title>認証成功</title></head><body><h1>認証が成功しました</h1><p>このウィンドウを閉じて、アプリケーションに戻ってください。</p></body></html>";
                        byte[] buffer = Encoding.UTF8.GetBytes(responseString);

                        response.ContentType = "text/html";
                        response.ContentLength64 = buffer.Length;
                        response.StatusCode = 200;

                        Stream output = response.OutputStream;
                        await output.WriteAsync(buffer, 0, buffer.Length);
                        output.Close();

                        // タスクを完了としてマーク
                        authorizationCodeTaskSource.SetResult(authorizationCode ?? "");
                    }
                    else if (request.QueryString.AllKeys.Contains("error"))
                    {
                        string error = request.QueryString.Get("error")!;
                        string errorDescription = request.QueryString.Get("error_description") ?? "詳細情報なし";

                        // ブラウザにエラーメッセージを返す
                        string responseString = $"<html><head><title>認証エラー</title></head><body><h1>認証エラーが発生しました</h1><p>エラー: {error}</p><p>説明: {errorDescription}</p></body></html>";
                        byte[] buffer = Encoding.UTF8.GetBytes(responseString);

                        response.ContentType = "text/html";
                        response.ContentLength64 = buffer.Length;
                        response.StatusCode = 400;

                        Stream output = response.OutputStream;
                        await output.WriteAsync(buffer, 0, buffer.Length);
                        output.Close();

                        // タスクをエラーとしてマーク
                        authorizationCodeTaskSource.SetException(new Exception($"認証エラー: {error} - {errorDescription}"));
                    }
                }
                catch (Exception ex)
                {
                    authorizationCodeTaskSource.SetException(ex);
                }
                finally
                {
                    listener.Stop();
                }
            });

            // 認可コードを待機
            return await authorizationCodeTaskSource.Task;
        }
        catch (Exception)
        {
            listener.Stop();
            throw;
        }
    }

    /// <summary>
    /// 認可コードを使用してアクセストークンを取得する
    /// </summary>
    /// <param name="code"></param>
    /// <param name="session"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<OAuth2Token> GetTokenAsync(
        string code,
        Session session)
    {
        var body = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", session.RedirectUri },
            { "code_verifier", session.CodeVerifier },
        };
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{session.ClientId}:{session.ClientSecret}"));

        var content = new FormUrlEncodedContent(body);
        var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", basic
        );
        request.Content = content;
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get access token: {responseContent}");
        }

        var token = JsonSerializer.Deserialize<OAuth2Token>(responseContent);
        if (token is null)
        {
            throw new Exception("Failed to deserialize access token");
        }
        return token;
    }

    private HttpRequestMessage GenerateHttpRequest(string endpoint)
    {
        if (OAuth2AccessToken is null)
        {
            throw new Exception("Access token is not set");
        }

        var header = new AuthenticationHeaderValue("Bearer", OAuth2AccessToken.AccessToken);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = header;

        return request;
    }

    private XResponse<T> CreateError<T>(ErrorResponse? error)
    {
        if (error is null)
        {
            return new XResponse<T>(500, default, "Unknown error", "An unknown error occurred");
        }
        var status = error.Status is null ? 500 : int.Parse(error.Status);
        return new XResponse<T>(status, default, error!.Title, error!.Detail);
    }

    private XResponse<T> CreateResponse<T>(int statusCode, T? content)
    {
        if (content is null)
        {
            return new XResponse<T>(500, default, "Unknown error", "An unknown error occurred");
        }
        return new XResponse<T>(statusCode, content);
    }

    /// <summary>
    /// Posts a tweet to the authenticated user's timeline
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public async Task<XResponse<PostTweetResponse>> PostTweetAsync(string text)
    {
        if (!string.IsNullOrEmpty(text) && text.Length > _tweetLength)
        {
            throw new ArgumentException($"Tweet text cannot exceed {_tweetLength} characters", nameof(text));
        }

        var request = GenerateHttpRequest(_postTweetEndpoint);
        var payload = new Dictionary<string, string>
        {
            { "text", text }
        };
        var jsonContent = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        request.Content = content;

        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return CreateError<PostTweetResponse>(JsonSerializer.Deserialize<ErrorResponse>(responseContent));
        }

        return CreateResponse(
            (int)response.StatusCode,
            JsonSerializer.Deserialize<PostTweetResponse>(responseContent));
    }

    /// <summary>
    /// Gets the authenticated user's profile information
    /// </summary>
    /// <returns></returns>
    public async Task<XResponse<GetMeResponse>> GetMeAsync()
    {
        var request = GenerateHttpRequest(_postTweetEndpoint);

        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync();

        // TODO: Deserialize response content
        if (!response.IsSuccessStatusCode)
        {
            return CreateError<GetMeResponse>(JsonSerializer.Deserialize<ErrorResponse>(responseContent));
        }

        return CreateResponse(
            (int)response.StatusCode,
            JsonSerializer.Deserialize<GetMeResponse>(responseContent));
    }
}
