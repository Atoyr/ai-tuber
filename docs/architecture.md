# AITuber C#版 アーキテクチャ設計

## 背景

Python版(`reference/python_v2/`)で全機能のプロトタイプが動作確認済み。
これを C#/.NET 8 のソリューションとして再構築する。既存の `MultiLLMClient` と `X` ライブラリを活かす。

## 全体アーキテクチャ

```
                    prompts/character.md(共通人格 = キャラの魂)
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
                 │      data/memory.json    │
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
| mss + pygetwindow | `GameCommentary/WindowCapture.cs` | Win32 API (EnumWindows + PrintWindow) |

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

## VOICEVOX / 音声出力

- VOICEVOX はローカル API (`http://127.0.0.1:50021`)。speaker ID は設定値(デフォルト3)
- 合成した wav を NAudio で "CABLE Input"(部分一致)デバイスに再生 → PuruPuruPNGTuber がマイク入力として拾い口パク
- デバイスが見つからない場合は利用可能デバイス一覧を例外メッセージに含める(Python版と同じUX)

## YouTube Live コメント取得の選択肢

1. **YouTube Data API v3** (`Google.Apis.YouTube.v3`) — 公式だがクォータ制(1日10,000ユニット)。
   liveChatMessages のポーリング間隔は API が返す pollingIntervalMillis に従う
2. innertube 直叩き(pytchat方式) — クォータ不要だが非公式

まず `ICommentSource` を切って `ConsoleCommentSource` で全体を動かし、YouTube実装は後から差し替える。

## 将来ロードマップ(v3計画から引き継ぎ)

- **Phase 3(最優先・効果最大)**: streaming + 文単位分割で発話遅延削減、
  `System.Threading.Channels` による合成キュー+再生キューの2キュー式TTSパイプライン、
  感情タグ(発話に `[joy]` 等を付けさせ VOICEVOX スタイル切替・アバター表情同期)、
  コメント選択の優先度ロジック
- Phase 4: 視聴者ごとのプロフィール、配信サマリの階層化
- Phase 5: エラー復旧・ログ・ローカル監視ダッシュボード
- Phase 6: 切り抜き生成、Twitch対応(ICommentSource の実装追加)
