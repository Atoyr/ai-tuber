using Medoz.AiTuber.Core;
using Medoz.X;

namespace Medoz.TwitterBot;

/// <summary>投稿先の抽象化。dry-run と実投稿を差し替えられるようにする。</summary>
public interface ITweetPoster
{
    Task PostAsync(string text, CancellationToken ct = default);
}

/// <summary>実投稿せずコンソール表示のみ (Python版 TWEET_DRY_RUN 相当のデフォルト挙動)</summary>
public sealed class DryRunTweetPoster : ITweetPoster
{
    public Task PostAsync(string text, CancellationToken ct = default)
    {
        Console.WriteLine($"[DRY RUN] 投稿内容: {text}");
        return Task.CompletedTask;
    }
}

/// <summary>既存 <see cref="XClient"/> を使った実投稿 (TWEET_DRY_RUN=0 のときのみ使用)</summary>
public sealed class XTweetPoster : ITweetPoster
{
    private readonly XClient _client;

    public XTweetPoster(XClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>環境変数由来の設定から XClient を組み立てる</summary>
    public static XTweetPoster FromConfig(AppConfig config)
    {
        // 事前に取得済みのアクセストークンを使う (対話認可フローはここでは行わない)
        var token = new OAuth2Token(
            TokenType: "bearer",
            ExpiresIn: 0,
            AccessToken: config.XAccessToken,
            Scope: Scopes.tweet_read | Scopes.tweet_write | Scopes.users_read,
            RefreshToken: string.IsNullOrEmpty(config.XAccessSecret) ? null : config.XAccessSecret);
        var client = new XClient(config.XApiKey, config.XApiSecret, token);
        return new XTweetPoster(client);
    }

    public async Task PostAsync(string text, CancellationToken ct = default)
    {
        var response = await _client.PostTweetAsync(text);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"ツイート投稿に失敗しました: {response.ErrorTitle} - {response.ErrorMessage}");
        }
        Console.WriteLine($"[投稿完了] {text}");
    }
}
