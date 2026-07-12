using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Medoz.Live;

/// <summary>
/// Twitch のライブ配信チャットを取得する <see cref="ICommentSource"/> 実装。
///
/// Twitch IRC (irc.chat.twitch.tv:6697) へ <b>匿名 (read-only) 接続</b>する。
/// <c>NICK justinfan&lt;数字&gt;</c> でログインすれば OAuth もトークンも不要で視聴者コメントを読める。
/// display-name タグを得るため <c>CAP REQ :twitch.tv/tags</c> を要求する。
///
/// サーバの <c>PING</c> には <c>PONG</c> を返す (返さないと切断される)。
/// 切断・例外時はログを出して数秒後に自動再接続し、ループを殺さない。キャンセルで綺麗に停止する。
///
/// IRC 行のパースは副作用の無い static メソッド (<see cref="ParseLine"/> /
/// <see cref="TryParseComment"/> / <see cref="IsPing"/> / <see cref="NormalizeChannel"/>) に
/// 分離してあり、ここがユニットテストの主対象。
/// </summary>
public class TwitchCommentSource : ICommentSource
{
    private const string Host = "irc.chat.twitch.tv";
    private const int Port = 6697;

    /// <summary>切断・例外時の再接続待機時間 (ミリ秒)</summary>
    private const int ReconnectDelayMs = 5000;

    private readonly string _channel;
    private readonly string _nick;
    private readonly Func<CancellationToken, Stream> _connect;

    private readonly ConcurrentQueue<Comment> _queue = new();
    private Task? _worker;

    /// <param name="channelOrUrl">チャンネル名 (素の名前 / <c>#name</c> / <c>twitch.tv/name</c> URL のいずれでも可)</param>
    /// <param name="connectionFactory">
    /// テスト用に注入する接続ファクトリ。未指定なら TLS で本番の Twitch IRC に接続する。
    /// 再接続のたびに呼ばれ、その都度新しいストリームを返す想定。
    /// </param>
    public TwitchCommentSource(string channelOrUrl, Func<CancellationToken, Stream>? connectionFactory = null)
    {
        _channel = NormalizeChannel(channelOrUrl);
        _nick = "justinfan" + Random.Shared.Next(10000, 99999);
        _connect = connectionFactory ?? DefaultConnect;
    }

    /// <summary>接続先チャンネル名 (正規化済み・小文字)。</summary>
    public string Channel => _channel;

    // ---- 接続 (本番) ----

    private static Stream DefaultConnect(CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            client.Connect(Host, Port);
            var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            ssl.AuthenticateAsClient(Host);
            // SslStream と TcpClient の両方を確実に破棄できるよう合成ストリームで包む
            return new OwningStream(ssl, client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Start(CancellationToken ct)
    {
        Console.WriteLine($"Twitch チャットに接続します (#{_channel}, 匿名接続 / APIキー不要)");
        _worker = Task.Run(() => RunLoopAsync(ct), ct);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using Stream stream = _connect(ct);
                using var reader = new StreamReader(stream, new UTF8Encoding(false));
                using var writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = "\r\n",
                };

                // 匿名ログイン + タグ要求 + チャンネル参加 (PASS は不要)
                await writer.WriteLineAsync("CAP REQ :twitch.tv/tags");
                await writer.WriteLineAsync($"NICK {_nick}");
                await writer.WriteLineAsync($"JOIN #{_channel}");
                Console.WriteLine($"[twitch] #{_channel} に参加しました");

                string? line;
                while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) is not null)
                {
                    if (IsPing(line))
                    {
                        // PONG を返さないとサーバに切断される
                        await writer.WriteLineAsync(BuildPong(line));
                        continue;
                    }

                    if (TryParseComment(line, out var comment))
                    {
                        _queue.Enqueue(comment);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[twitch] 接続エラー: {ex.Message} ({ReconnectDelayMs / 1000}秒後に再接続)");
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            // 切断・EOF・例外のいずれでもここに来る。少し待ってから再接続する。
            try
            {
                await Task.Delay(ReconnectDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public IReadOnlyList<Comment> Drain(int max)
    {
        var picked = new List<Comment>();
        while (picked.Count < max && _queue.TryDequeue(out var comment))
        {
            picked.Add(comment);
        }
        return picked;
    }

    public void Dispose()
    {
        // ReadLineAsync でブロック中のワーカーはキャンセルで抜ける (プロセス終了時にも回収される)
        GC.SuppressFinalize(this);
    }

    // ================= 以下、純粋 (副作用なし) なパース処理 =================

    /// <summary>パース済みの IRC メッセージ (テスト用に公開)。</summary>
    /// <param name="Tags"><c>@key=value;...</c> の展開結果。無ければ空。</param>
    /// <param name="Prefix"><c>:</c> の直後の送信元 (例 <c>login!login@login.tmi.twitch.tv</c>)。無ければ null。</param>
    /// <param name="Command">コマンド (<c>PRIVMSG</c> / <c>PING</c> / <c>JOIN</c> / 数値応答など)。</param>
    /// <param name="Parameters">トレーリング (<c>:</c> 以降) を除いたパラメータ列。</param>
    /// <param name="Trailing"><c>:</c> 以降の本文。無ければ null。</param>
    public sealed record IrcMessage(
        IReadOnlyDictionary<string, string> Tags,
        string? Prefix,
        string Command,
        IReadOnlyList<string> Parameters,
        string? Trailing);

    /// <summary>
    /// IRC 行を <c>[@tags] [:prefix] COMMAND [params] [:trailing]</c> に分解する。
    /// トレーリング開始は最初の <c>" :"</c> なので、本文中の <c>:</c> や絵文字はそのまま保持される。
    /// </summary>
    public static IrcMessage ParseLine(string line)
    {
        string rest = line ?? string.Empty;

        // 1) タグ (@...)
        IReadOnlyDictionary<string, string> tags;
        if (rest.StartsWith('@'))
        {
            int sp = rest.IndexOf(' ');
            string tagSection = sp < 0 ? rest[1..] : rest[1..sp];
            tags = ParseTags(tagSection);
            rest = sp < 0 ? string.Empty : rest[(sp + 1)..];
        }
        else
        {
            tags = new Dictionary<string, string>();
        }

        // 2) プレフィックス (:...)
        string? prefix = null;
        if (rest.StartsWith(':'))
        {
            int sp = rest.IndexOf(' ');
            if (sp < 0)
            {
                prefix = rest[1..];
                rest = string.Empty;
            }
            else
            {
                prefix = rest[1..sp];
                rest = rest[(sp + 1)..];
            }
        }

        // 3) トレーリング (最初の " :" 以降)
        string? trailing = null;
        int trailingAt = rest.IndexOf(" :", StringComparison.Ordinal);
        if (trailingAt >= 0)
        {
            trailing = rest[(trailingAt + 2)..];
            rest = rest[..trailingAt];
        }
        else if (rest.StartsWith(':'))
        {
            // パラメータが無く先頭がトレーリングのみ (例: "PING" は該当しないが保険)
            trailing = rest[1..];
            rest = string.Empty;
        }

        // 4) コマンド + パラメータ
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string command = tokens.Length > 0 ? tokens[0] : string.Empty;
        var parameters = tokens.Length > 1 ? tokens[1..] : Array.Empty<string>();

        return new IrcMessage(tags, prefix, command, parameters, trailing);
    }

    /// <summary><c>key=value;key2=value2;...</c> 形式のタグ列を辞書に展開する。</summary>
    public static IReadOnlyDictionary<string, string> ParseTags(string tagSection)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(tagSection))
        {
            return dict;
        }
        foreach (var pair in tagSection.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
            {
                dict[pair] = string.Empty;
            }
            else
            {
                dict[pair[..eq]] = pair[(eq + 1)..];
            }
        }
        return dict;
    }

    /// <summary>
    /// PRIVMSG 行なら著者・本文を取り出して true を返す。それ以外 (JOIN / PING / システム通知等) は false。
    /// 著者名はタグ <c>display-name</c>、空なら login 名 (プレフィックスの <c>!</c> より前) にフォールバックする。
    /// </summary>
    public static bool TryParseComment(string line, out Comment comment)
    {
        comment = default!;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var msg = ParseLine(line);
        if (!string.Equals(msg.Command, "PRIVMSG", StringComparison.Ordinal))
        {
            return false;
        }

        string? body = msg.Trailing;
        if (string.IsNullOrEmpty(body))
        {
            return false;
        }

        string author = ResolveAuthor(msg);
        comment = new Comment(author, body);
        return true;
    }

    /// <summary>display-name タグ (空でない) を優先し、無ければプレフィックスの login 名を使う。</summary>
    private static string ResolveAuthor(IrcMessage msg)
    {
        if (msg.Tags.TryGetValue("display-name", out var displayName) && !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        string? login = ExtractLogin(msg.Prefix);
        return string.IsNullOrEmpty(login) ? "名無し" : login;
    }

    /// <summary><c>login!login@login.tmi.twitch.tv</c> から login 部分を取り出す。</summary>
    private static string? ExtractLogin(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return null;
        }
        int bang = prefix.IndexOf('!');
        if (bang > 0)
        {
            return prefix[..bang];
        }
        int at = prefix.IndexOf('@');
        return at > 0 ? prefix[..at] : prefix;
    }

    /// <summary>サーバの生存確認 (<c>PING :xxx</c>) かどうか。</summary>
    public static bool IsPing(string line)
        => !string.IsNullOrEmpty(line)
           && string.Equals(ParseLine(line).Command, "PING", StringComparison.Ordinal);

    /// <summary>
    /// PING 行に対して返すべき <c>PONG :xxx</c> 応答文字列を組み立てる。
    /// ペイロードは PING のトレーリングをそのまま反射する。
    /// </summary>
    public static string BuildPong(string line)
    {
        string? payload = ParseLine(line).Trailing;
        return string.IsNullOrEmpty(payload) ? "PONG" : $"PONG :{payload}";
    }

    /// <summary>
    /// チャンネル指定を正規化する。<c>twitch.tv/Name</c> URL / <c>#Name</c> / 素の <c>Name</c> の
    /// いずれも受け付け、小文字のチャンネル名を返す。
    /// </summary>
    public static string NormalizeChannel(string channelOrUrl)
    {
        if (string.IsNullOrWhiteSpace(channelOrUrl))
        {
            throw new ArgumentException("チャンネル名または twitch.tv の URL を指定してください。", nameof(channelOrUrl));
        }

        string value = channelOrUrl.Trim();

        if (value.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            string withScheme = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
            if (Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
                && uri.Host.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                {
                    value = segments[0];
                }
            }
        }

        value = value.TrimStart('#').Trim();
        if (value.Length == 0)
        {
            throw new ArgumentException($"チャンネル名を抽出できませんでした: {channelOrUrl}", nameof(channelOrUrl));
        }

        return value.ToLowerInvariant();
    }

    /// <summary>SslStream と、その土台の TcpClient をまとめて破棄するためのラッパー。</summary>
    private sealed class OwningStream : Stream
    {
        private readonly Stream _inner;
        private readonly IDisposable _owned;

        public OwningStream(Stream inner, IDisposable owned)
        {
            _inner = inner;
            _owned = owned;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => _inner.ReadAsync(buffer, ct);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            => _inner.WriteAsync(buffer, ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _owned.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
