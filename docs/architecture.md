# AITuber C#版 アーキテクチャ設計

## 背景

Python版(`reference/python_v2/`)で全機能のプロトタイプが動作確認済み。
これを C#/.NET 8 のソリューションとして再構築する。既存の `MultiLLMClient` と `X` ライブラリを活かす。

## 全体アーキテクチャ

```
              $PERSONA_DIR/character.md(共通人格 = キャラの魂)
                          │
          ┌───────────────┼────────────────┐
   live_system.md         │          tweet_system.md
          │               │                │
┌─────────▼─────┐  ┌──────▼──────┐  ┌──────▼───────┐
│    Live       │  │GameCommentary│  │  TwitterBot   │
│ コメント→応答  │  │ 画面→実況    │  │ 自律投稿      │
└─────┬─────────┘  └──────┬──────┘  └──────▲───────┘
      │    Voicevox + NAudio│               │
      └──────────┬──────────┘               │
                 │   data/&lt;slug&gt;/memory.json│
                 └──── 配信メモを書く/読む ───┘
```

## プロジェクト対応表(Python → C#)

| Python v2 | C# | 実現方法 |
|---|---|---|
| `core/persona.py` | `AiTuber.Core/Persona.cs` | character.md + モード別mdを結合してsystemプロンプト化 |
| `core/memory.py` | `AiTuber.Core/SharedMemory.cs` | `System.Text.Json`。`data/memory.json` |
| `config.py` の BANNED_WORDS | `AiTuber.Core/ModerationFilter.cs` | 全モード共通 |
| anthropic SDK | 既存 `MultiLLMClient.ClaudeClient` を拡張 | 履歴・Vision・streaming 対応 |
| VOICEVOX requests | `Voicevox/VoicevoxClient.cs` | `/audio_query` → `/synthesis` を HttpClient で |
| sounddevice | `Voicevox/AudioPlayer.cs` | **NAudio**。デバイス名部分一致("CABLE Input")で出力先選択 |
| pytchat | `Live/ICommentSource.cs` + 実装 | まず `ConsoleCommentSource`、次に YouTube |
| tweepy | 既存 `Medoz.X.XClient` | そのまま利用 |
| mss + pygetwindow | `GameCommentary/WgcWindowCapture.cs` | Windows.Graphics.Capture (既定)。ウィンドウ探索は Win32 EnumWindows (`WindowFinder`) |

## MultiLLMClient の拡張方針

現状の `ILLMClient.GenerateTextAsync(systemPrompt, userPrompt)` は単発生成のみ。
後方互換を保ちつつ拡張する:

```csharp
public record ChatMessage(string Role, string Content);          // "user" | "assistant"
public record ImageContent(string MediaType, string Base64Data); // Vision用

public interface IChatClient : ILLMClient
{
    Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                               int maxTokens = 300, CancellationToken ct = default);
    IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                               int maxTokens = 300, CancellationToken ct = default); // SSE
    Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                               int maxTokens = 150, CancellationToken ct = default);
}
```

streaming は Anthropic の SSE (`"stream": true`) を `IAsyncEnumerable<string>` で返す。
実装時は https://docs.claude.com/en/api/messages-streaming を確認すること。

## 動作仕様(Python版と同一にする)

| パラメータ | 値 | 意味 |
|---|---|---|
| HISTORY_TURNS | 12 | Claudeに渡す直近会話ターン数 |
| COMMENT_BATCH_SEC | 4 | コメントをまとめて拾う間隔(秒) |
| FREETALK_AFTER_SEC | 45 | この秒数コメントが無ければ自動フリートーク |
| コメント選択 | 最大5件取り出しClaudeに渡す | live_system.md 側で1つ選んで返答させる |
| 配信終了時 | 発話ログ末尾50件を要約 | memory.json の stream_notes に保存(最大10件) |
| TWEET_MIN/MAX_INTERVAL_MIN | 180〜360 | ランダム投稿間隔(分) |
| TWEET_ACTIVE_HOURS | 9〜24時 | 投稿してよい時間帯 |
| ツイート形式 | `{"tweet": "...", "kind": "daily\|announce\|thanks\|question"}` | JSONのみ出力させパース |
| ツイート検証 | 140字以内・フィルタ・完全重複チェック | 失敗したら再生成、最大3回でスキップ |
| RECENT_TWEETS_KEEP | 20 | 重複回避のため記憶する直近ツイート数 |
| フィルタ違反時(Live) | 応答を破棄し、そのターンの履歴も破棄 | ログに出して次へ |
| CAPTURE_INTERVAL_SEC | 12 | ゲーム実況のキャプチャ間隔 |
| 画像リサイズ | 幅800px, JPEG q80 | トークン節約 |
| 実況履歴 | 直近4件を文脈として渡す | 繰り返し防止 |
| COMMENTARY_MAX_TOKENS | 500 | 実況1回の生成 maxTokens。**Python版の150から意図的に変更**(日本語実況が単語レベルで途切れるため。プロンプトも1〜2文→3〜5文に変更済み) |
| GAME_KNOWLEDGE | (空) | 実況に使うゲーム知識名。ペルソナの `knowledge/<name>.md` をシステムプロンプトに結合(persona-architecture.md 参照) |

## ゲーム実況のキャプチャ方式

`IWindowCapture` の実装を差し替えて選ぶ(`WindowCaptureFactory`)。方式は環境変数 `CAPTURE_METHOD`
または `--capture-method`(既定 `wgc`)。OBS のウィンドウキャプチャにある「キャプチャ方法」と同じ考え方。

| 方式 | 実装 | 撮れるもの・制約 |
|---|---|---|
| `wgc` (既定) | `WgcWindowCapture` | **Windows.Graphics.Capture**。OBS の「Windows 10 (1903以降)」と同じ方式。管理者権限で動くウィンドウ・他ウィンドウに隠れたウィンドウ・GPU(DirectX/Unity)描画のいずれも撮れる。要 Windows 10 1903+ |
| `printwindow` | `WindowCapture` | 従来の Win32 `PrintWindow` (PW_RENDERFULLCONTENT)。Windows 10 1903 未満へのフォールバック |

`printwindow` の制約(WGC を既定にした理由):

- **管理者権限のウィンドウを撮れない**。PrintWindow は対象へ `WM_PRINT` を送る方式のため、
  自プロセスより高い整合性レベルのウィンドウは UIPI に阻まれ `ERROR_ACCESS_DENIED(5)` で失敗する
  (実測: 管理者権限で動くゲームで失敗、通常権限のアプリはすべて成功)。
  WGC はメッセージを送らずコンポジタから合成済みの絵をもらうためこの制約が無い
- 画面からの BitBlt を代替にすると手前のウィンドウが写り込む(WGC は隠れていても対象自身が撮れる)

どちらの方式も対象ウィンドウの探索は `WindowFinder` を共有し、見つからない場合は
可視ウィンドウ一覧付きの例外にする(AudioPlayer のデバイス未発見時と同じ UX)。
キャプチャ画像は方式によらず幅800px / JPEG q80(動作仕様表のとおり)。

WGC は既定でキャプチャ対象に黄色い枠を描く。消すには `GraphicsCaptureSession.IsBorderRequired = false`
(Windows 11 + 10.0.26100 の projection が必要)。現状の TFM は 10.0.19041.0 のため未対応。

## VOICEVOX / 音声出力

- VOICEVOX はローカル API (`http://127.0.0.1:50021`)。speaker ID は設定値(デフォルト3)
- 合成した wav を NAudio で "CABLE Input"(部分一致)デバイスに再生 → PuruPuruPNGTuber がマイク入力として拾い口パク
- デバイスが見つからない場合は利用可能デバイス一覧を例外メッセージに含める(Python版と同じUX)

## YouTube Live コメント取得の選択肢

1. **YouTube Data API v3** (`Google.Apis.YouTube.v3`) — 公式だがクォータ制(1日10,000ユニット)。
   liveChatMessages のポーリング間隔は API が返す pollingIntervalMillis に従う
2. innertube 直叩き(pytchat方式) — クォータ不要だが非公式

まず `ICommentSource` を切って `ConsoleCommentSource` で全体を動かし、YouTube実装は後から差し替える。

## Twitch Live チャット取得(実装済み)

配信のメインが Twitch のため `TwitchCommentSource` を実装済み。方式は **Twitch IRC への匿名(read-only)接続**:

- `irc.chat.twitch.tv:6697`(TLS)へ `TcpClient` + `SslStream` で接続
- `NICK justinfan<ランダム数字>` で匿名ログイン(**OAuth・PASS 不要 = APIキー不要**)
- `CAP REQ :twitch.tv/tags` で `display-name` タグを要求、`JOIN #<channel>` で参加
- サーバの `PING :xxx` には `PONG :xxx` を返す(返さないと切断される)
- コメント行 `@...;display-name=Foo;... :login!login@... PRIVMSG #channel :本文` を、
  IRC 行パース(`[@tags] [:prefix] COMMAND [params] [:trailing]`)で著者・本文に分解。
  著者は `display-name`(空なら login 名)、本文はトレーリング(最初の `" :"` 以降=本文中の `:` や絵文字を保持)
- パース処理は副作用の無い static メソッド(`ParseLine` / `TryParseComment` / `IsPing` / `NormalizeChannel`)に
  分離してユニットテスト対象にする。接続部は `Func<CancellationToken, Stream>` で注入可能にしフェイクストリームでテストする
- 切断・例外時はログを出して数秒後に自動再接続し、ループを殺さない。キャンセルで綺麗に停止
- チャンネル指定は素の名前 / `#name` / `twitch.tv/name` URL のいずれも受け付けて小文字に正規化

`dotnet run --project Live -- --twitch <channel|URL>` で起動する。

## 将来ロードマップ(v3計画から引き継ぎ)

- **Phase 3(最優先・効果最大)**: streaming + 文単位分割で発話遅延削減、
  `System.Threading.Channels` による合成キュー+再生キューの2キュー式TTSパイプライン、
  感情タグ(発話に `[joy]` 等を付けさせ VOICEVOX スタイル切替・アバター表情同期)、
  コメント選択の優先度ロジック
- Phase 4: 視聴者ごとのプロフィール、配信サマリの階層化
- Phase 5: エラー復旧・ログ・ローカル監視ダッシュボード
- Phase 6: 切り抜き生成、~~Twitch対応(ICommentSource の実装追加)~~ → Twitch対応は `TwitchCommentSource`(IRC匿名接続)で実装済み
- **Phase 7: Twitch チャットへのコメント投稿(書き込み)**。現状の読み取りは匿名接続で認証不要だが、
  書き込みには OAuth 2.0 が必須になる:
  - ボット用アカウント + Twitch 開発者コンソールでのアプリ登録(Client ID)
  - 認可フローは Device Code Grant か Authorization Code Grant。スコープは IRC 経由なら `chat:edit`(+`chat:read`)、
    Helix API(`POST /helix/chat/messages`)なら `user:write:chat`。リフレッシュトークンの更新処理も必要
  - 送信方法: IRC で `PASS oauth:<token>` + 本名 NICK でログインし `PRIVMSG`、もしくは Helix API
  - 設計方針: `ICommentPoster` のような抽象を切り、トークンは環境変数から注入、**dry-run デフォルト**の原則に従う

## TwitterBot の Linux 常時運用

TwitterBot は配信PCとは別のラズパイ / 軽量 Linux VM で常時稼働させる。
systemd timer(`OnUnitInactiveSec=180min` + `RandomizedDelaySec=180min` = 前回実行から180〜360分後)で
ランダム間隔を実現し、アプリは `--scheduled`(時間帯チェック付き1回実行)で起動する。
発行は `linux-arm64` self-contained 単一ファイル、ホストの TZ は Asia/Tokyo 必須。
memory.json の配信PCとの同期を含む詳細は @docs/twitterbot-linux-deployment.md。
