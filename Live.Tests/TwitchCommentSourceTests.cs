using System.Text;
using Medoz.Live;

namespace Medoz.Live.Tests;

public class TwitchCommentSourceTests
{
    // ---- チャンネル名の正規化 ----
    [Theory]
    [InlineData("foo", "foo")]
    [InlineData("Foo", "foo")]
    [InlineData("#Foo", "foo")]
    [InlineData("  #Foo  ", "foo")]
    [InlineData("twitch.tv/Foo", "foo")]
    [InlineData("https://twitch.tv/Foo", "foo")]
    [InlineData("https://www.twitch.tv/Foo", "foo")]
    [InlineData("https://www.twitch.tv/Foo?referrer=raid", "foo")]
    [InlineData("https://m.twitch.tv/BarBaz", "barbaz")]
    public void NormalizeChannel_HandlesVariousForms(string input, string expected)
    {
        Assert.Equal(expected, TwitchCommentSource.NormalizeChannel(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    public void NormalizeChannel_Throws_WhenEmpty(string input)
    {
        Assert.Throws<ArgumentException>(() => TwitchCommentSource.NormalizeChannel(input));
    }

    // ---- PRIVMSG パース ----
    [Fact]
    public void TryParseComment_UsesDisplayNameTag()
    {
        string line = "@badge-info=;display-name=Foo;emotes= :foo!foo@foo.tmi.twitch.tv PRIVMSG #chan :こんにちは";

        Assert.True(TwitchCommentSource.TryParseComment(line, out var comment));
        Assert.Equal("Foo", comment.Author);
        Assert.Equal("こんにちは", comment.Message);
    }

    [Fact]
    public void TryParseComment_FallsBackToLogin_WhenNoTags()
    {
        // CAP tags が無い場合 (display-name タグ無し) は login 名を著者にする
        string line = ":bob!bob@bob.tmi.twitch.tv PRIVMSG #chan :やあ";

        Assert.True(TwitchCommentSource.TryParseComment(line, out var comment));
        Assert.Equal("bob", comment.Author);
        Assert.Equal("やあ", comment.Message);
    }

    [Fact]
    public void TryParseComment_FallsBackToLogin_WhenDisplayNameEmpty()
    {
        // display-name タグはあるが空 → login 名にフォールバック
        string line = "@display-name= :bob!bob@bob.tmi.twitch.tv PRIVMSG #chan :やあ";

        Assert.True(TwitchCommentSource.TryParseComment(line, out var comment));
        Assert.Equal("bob", comment.Author);
    }

    [Fact]
    public void TryParseComment_PreservesColonsAndEmojiInBody()
    {
        // 本文中の ":" や絵文字を壊さないこと (トレーリングは最初の " :" 以降すべて)
        string line = "@display-name=Foo :foo!foo@foo.tmi.twitch.tv PRIVMSG #chan :URL は https://example.com :) 🎉";

        Assert.True(TwitchCommentSource.TryParseComment(line, out var comment));
        Assert.Equal("URL は https://example.com :) 🎉", comment.Message);
    }

    [Theory]
    [InlineData(":tmi.twitch.tv 001 justinfan12345 :Welcome, GLHF!")]
    [InlineData(":justinfan12345!justinfan12345@justinfan12345.tmi.twitch.tv JOIN #chan")]
    [InlineData(":justinfan12345.tmi.twitch.tv 353 justinfan12345 = #chan :justinfan12345")]
    [InlineData(":tmi.twitch.tv NOTICE #chan :Login authentication failed")]
    [InlineData("PING :tmi.twitch.tv")]
    public void TryParseComment_IgnoresNonPrivmsg(string line)
    {
        Assert.False(TwitchCommentSource.TryParseComment(line, out _));
    }

    [Fact]
    public void TryParseComment_IgnoresEmptyBody()
    {
        string line = "@display-name=Foo :foo!foo@foo.tmi.twitch.tv PRIVMSG #chan :";
        Assert.False(TwitchCommentSource.TryParseComment(line, out _));
    }

    // ---- PING / PONG ----
    [Fact]
    public void IsPing_DetectsPing()
    {
        Assert.True(TwitchCommentSource.IsPing("PING :tmi.twitch.tv"));
        Assert.False(TwitchCommentSource.IsPing(":tmi.twitch.tv PRIVMSG #chan :hi"));
        Assert.False(TwitchCommentSource.IsPing(""));
    }

    [Fact]
    public void BuildPong_ReflectsPayload()
    {
        Assert.Equal("PONG :tmi.twitch.tv", TwitchCommentSource.BuildPong("PING :tmi.twitch.tv"));
    }

    // ---- タグパース ----
    [Fact]
    public void ParseLine_ParsesTagsAndPrefixAndCommand()
    {
        string line = "@badge-info=;display-name=Foo;mod=1;subscriber=0 :foo!foo@foo.tmi.twitch.tv PRIVMSG #chan :hi there";

        var msg = TwitchCommentSource.ParseLine(line);

        Assert.Equal("PRIVMSG", msg.Command);
        Assert.Equal("foo!foo@foo.tmi.twitch.tv", msg.Prefix);
        Assert.Equal("hi there", msg.Trailing);
        Assert.Equal("Foo", msg.Tags["display-name"]);
        Assert.Equal("1", msg.Tags["mod"]);
        Assert.Equal(string.Empty, msg.Tags["badge-info"]);
    }

    // ---- フェイクストリームによる接続→JOIN→PRIVMSG受信→コメント供給 ----
    [Fact]
    public async Task Start_SuppliesCommentsFromStream()
    {
        // サーバから届く一連の行 (welcome → JOIN 通知 → PRIVMSG 2件 → PING)
        string incoming = string.Join("\r\n",
            ":tmi.twitch.tv 001 justinfan12345 :Welcome, GLHF!",
            ":justinfan12345!justinfan12345@justinfan12345.tmi.twitch.tv JOIN #chan",
            "@display-name=Alice :alice!alice@alice.tmi.twitch.tv PRIVMSG #chan :こんばんは",
            "@display-name=Bob :bob!bob@bob.tmi.twitch.tv PRIVMSG #chan :初見です:D",
            "PING :tmi.twitch.tv") + "\r\n";

        var stream = new FakeDuplexStream(incoming);
        int connectCount = 0;
        Func<CancellationToken, Stream> factory = _ =>
        {
            connectCount++;
            // 2回目以降 (再接続) は即 EOF のストリームを返す
            return connectCount == 1 ? stream : new FakeDuplexStream("");
        };

        using var source = new TwitchCommentSource("#Chan", factory);
        using var cts = new CancellationTokenSource();
        source.Start(cts.Token);

        var collected = new List<Comment>();
        // コメントが2件そろうまでポーリング (最大3秒)
        for (int i = 0; i < 60 && collected.Count < 2; i++)
        {
            collected.AddRange(source.Drain(10));
            if (collected.Count < 2)
            {
                await Task.Delay(50, CancellationToken.None);
            }
        }
        cts.Cancel();

        Assert.Equal("chan", source.Channel);
        Assert.Equal(2, collected.Count);
        Assert.Equal("Alice", collected[0].Author);
        Assert.Equal("こんばんは", collected[0].Message);
        Assert.Equal("Bob", collected[1].Author);
        Assert.Equal("初見です:D", collected[1].Message);

        // ハンドシェイク (NICK justinfan... / JOIN #chan) が送信されていること
        string sent = stream.WrittenText();
        Assert.Contains("NICK justinfan", sent);
        Assert.Contains("JOIN #chan", sent);
        // PING に PONG を返していること
        Assert.Contains("PONG :tmi.twitch.tv", sent);
    }

    /// <summary>読み取りは事前データ、書き込みは別バッファに溜める簡易双方向ストリーム。</summary>
    private sealed class FakeDuplexStream : Stream
    {
        private readonly MemoryStream _incoming;
        private readonly MemoryStream _outgoing = new();
        private readonly object _lock = new();

        public FakeDuplexStream(string incoming)
        {
            _incoming = new MemoryStream(Encoding.UTF8.GetBytes(incoming));
        }

        public string WrittenText()
        {
            lock (_lock)
            {
                return Encoding.UTF8.GetString(_outgoing.ToArray());
            }
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => _incoming.Length;
        public override long Position { get => _incoming.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                return _incoming.Read(buffer, offset, count);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                _outgoing.Write(buffer, offset, count);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
