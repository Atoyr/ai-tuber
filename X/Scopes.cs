namespace Medoz.X;

/// <summary>
/// https://docs.x.com/resources/fundamentals/authentication/oauth-2-0/authorization-code#confidential-clients
/// </summary>
[Flags]
public enum Scopes
{
    None = 0,                       // 権限なし
    tweet_read = 1,                 // All the Tweets you can view, including Tweets from protected accounts.
    tweet_write = 1 << 1,           // Tweet and Retweet for you.
    tweet_moderate_write = 1 << 2,  // Hide and unhide replies to your Tweets.
    users_read = 1 << 3,            // Any account you can view, including protected accounts.
    follows_read = 1 << 4,          // People who follow you and people who you follow.
    follows_write = 1 << 5,         // Follow and unfollow people for you.
    offline_access = 1 << 6,        // Stay connected to your account until you revoke access.
    space_read = 1 << 7,            // All the Spaces you can view.
    mute_read = 1 << 8,             // Accounts you've muted.
    mute_write = 1 << 9,            // Mute and unmute accounts for you.
    like_read = 1 << 10,            // Tweets you've liked and likes you can view.
    like_write = 1 << 11,           // Like and un-like Tweets for you.
    list_read = 1 << 12,            // Lists, list members, and list followers of lists you've created or are a member of, including private lists.
    list_write = 1 << 13,           // Create and manage Lists for you.
    block_read = 1 << 14,           // Accounts you've blocked.
    block_write = 1 << 15,          // Block and unblock accounts for you.
    bookmark_read = 1 << 16,        // Get Bookmarked Tweets from an authenticated user.
    bookmark_write = 1 << 17,       // Bookmark and remove Bookmarks from Tweets.
    media_write = 1 << 18           // Upload media.
}

public static class ScopesExtensions
{
    public static string ToScopeString(this Scopes scopes)
    {
        if (scopes == Scopes.None)
        {
            return string.Empty;
        }

        // 複数のスコープが含まれている場合、それらを個別に処理
        var scopeValues = Enum.GetValues(typeof(Scopes))
            .Cast<Scopes>()
            .Where(s => s != Scopes.None && scopes.HasFlag(s))
            .Select(s => s.ToString().Replace('_', '.'));

        return string.Join(" ", scopeValues);
    }

    /// <summary>
    /// スコープ文字列からScopesに変換します。
    /// </summary>
    /// <param name="scopeString">スペース区切りのスコープ文字列（例："tweet.write users.read tweet.read"）</param>
    /// <returns>変換されたScopes値</returns>
    /// <exception cref="ArgumentException">無効なスコープが含まれている場合にスローされます</exception>
    public static Scopes FromScopeString(string scopeString)
    {
        if (string.IsNullOrWhiteSpace(scopeString))
        {
            return Scopes.None;
        }

        var result = Scopes.None;
        var scopeParts = scopeString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var scopePart in scopeParts)
        {
            // ドット(.)をアンダースコア(_)に置き換え
            var enumName = scopePart.Replace('.', '_');

            // 文字列をEnum値に変換
            if (Enum.TryParse<Scopes>(enumName, out var scope))
            {
                result |= scope;
            }
            else
            {
                throw new ArgumentException($"無効なスコープが指定されました: {scopePart}", nameof(scopeString));
            }
        }

        return result;
    }
}