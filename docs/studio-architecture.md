# Studio(配信コントロールパネル)アーキテクチャ設計

配信中に使うローカル Web UI。ブラウザから **会話ログの閲覧・設定変更・配信の開始/停止・
VOICEVOX / PuruPuruPNGTuber の起動** を行えるようにする。
配信者(開発者)は喋らない前提なので、配信中の操作はすべてこの画面に集約するのがゴール。

## 全体像

```
ブラウザ (http://127.0.0.1:5100)
   │  静的UI + REST + SSE(イベントストリーム)
┌──▼───────────────────────────────────────────┐
│ Studio (ASP.NET Core / Kestrel, localhost限定)│
│ ├── AppLauncher     ── Process.Start ──► VOICEVOX (engine)      │
│ │                   ── Process.Start ──► PuruPuruPNGTuber (run_local_server.bat) │
│ ├── LiveSession(Live から抽出したループを in-process 実行)      │
│ │     ICommentSource(console/twitch/youtube) + UI注入コメント   │
│ │     → LLM → フィルタ → TtsPipeline → AudioPlayer(CABLE Input) │
│ └── StudioSettings  ── data/studio.json(UI で編集した設定)     │
└──────────────────────────────────────────────┘
```

- **Studio は Live の置き換えではない**。配信ループ本体を `LiveSession` クラスに抽出し、
  従来の `dotnet run --project Live -- --console` (CLI) と Studio (Web) の両方から同じループを使う
- **Setup(WPF)との棲み分け**: Setup = APIキー等シークレットの初期設定(ユーザー環境変数への保存)。
  Studio = 配信中の運用操作。**Studio では APIキーを表示・編集しない**(設定済み/未設定の表示のみ)。
  配信中に画面共有してもシークレットが映らないようにするため
- **外部投稿はしない**(発話・キャプチャ・プロセス起動のみ)。dry-run 原則に抵触する操作は持たない

## プロジェクト構成

- プロジェクト: `Studio/`(`Microsoft.NET.Sdk.Web`、`net10.0-windows`、名前空間 `Medoz.Studio`)
- 参照: `Live`(LiveSession / ICommentSource 実装)、`GameCommentary`(WindowCapture / CommentaryLoop)、
  `AiTuber.Core`、`Voicevox`、`MultiLLMClient`
- フロントエンドは `Studio/wwwroot/` の **素の HTML + CSS + JS(ビルド不要・外部ライブラリ不要)**。
  リアルタイム更新は Server-Sent Events(`EventSource`)を使い、SignalR 等のクライアントJSを持ち込まない。
  このリポジトリに node ツールチェーンを増やさない(Nuxt は ai-tuber-blogs 側だけ)
- 待ち受けは **`http://127.0.0.1:5100` 固定(localhost のみ)**。認証なし。LAN には公開しない。
  ポートは環境変数 `STUDIO_PORT` で変更可

起動:

```
dotnet run --project Studio            # ブラウザで http://127.0.0.1:5100 を開く
dotnet run --project Studio -- --open  # 既定ブラウザを自動で開く
```

## Live のリファクタリング(LiveSession 抽出)

現在 `Live/Program.cs` に in-line で書かれている配信ループを `Live/LiveSession.cs` に抽出する。
**挙動は変えない**(4秒バッチ・45秒フリートーク・12ターン履歴・フィルタ違反時の履歴破棄・
終了時の配信メモ要約はそのまま)。

```csharp
public sealed class LiveSession : IAsyncDisposable
{
    public LiveSession(LiveSessionOptions options, ICommentSource source,
                       Persona persona, ModerationFilter filter, SharedMemory memory,
                       VoicevoxClient voicevox, AudioPlayer player);

    public Task RunAsync(CancellationToken ct);          // ループ本体 (Ctrl+C / Stop で抜けて配信メモ保存)
    public event Action<LiveEvent>? EventRaised;         // UI・コンソールへの通知 (下記)
    public void UpdateOptions(Action<LiveSessionOptions> mutate); // 実行中パラメータの変更
}

// UI・ログに流すイベント。UI 非依存の record にして Live 側に置く
public abstract record LiveEvent(DateTimeOffset At);
public record CommentsPicked(DateTimeOffset At, IReadOnlyList<Comment> Comments) : LiveEvent(At);
public record FreeTalkTriggered(DateTimeOffset At) : LiveEvent(At);
public record ReplySpoken(DateTimeOffset At, string Text) : LiveEvent(At);
public record ReplySkipped(DateTimeOffset At, string Reason) : LiveEvent(At);   // フィルタ違反等
public record SessionStateChanged(DateTimeOffset At, SessionState State) : LiveEvent(At); // Starting/Running/Stopping/Stopped/Faulted
public record StreamNoteSaved(DateTimeOffset At, string Summary) : LiveEvent(At);
```

- `Live/Program.cs` は「引数パース → LiveSession 構築 → EventRaised を Console.WriteLine に接続」だけの
  薄い CLI になる(従来の表示・体験は維持)
- `LiveSessionOptions` は実行中に変更可能なパラメータを持つ:
  `CommentBatchSec` / `FreetalkAfterSec` / `FreetalkEnabled` / `SpeakerId` / `EmotionStyleIds` /
  `Muted`(合成はするが再生しない、ではなく**応答生成自体を一時停止**する Pause として実装)
- UI からの任意コメント注入のため `ManualCommentSource`(スレッドセーフな Enqueue を公開)を追加し、
  プラットフォームソースと合成する `CompositeCommentSource` を用意する。
  Studio では常に「選択したソース + ManualCommentSource」の合成で起動する
  (配信中にテストコメントを差し込める。console モードは Manual のみ)

## AppLauncher(外部アプリの起動・監視)

VOICEVOX / PuruPuruPNGTuber を UI のボタンで起動・停止し、状態を表示する。

PuruPuruPNGTuber は exe アプリではなく**ブラウザで動く Web アプリ**
(静的な index.html + app.js を同梱の `run_local_server.bat` = Python ローカルサーバで配信し、
既定 `http://127.0.0.1:8223/` をブラウザや OBS で開いて表示する)。
そのため Studio が起動するのはローカルサーバで、死活判定も VOICEVOX と同じ HTTP 方式になる。

| アプリ | 起動 | 死活判定 | 停止 |
|---|---|---|---|
| VOICEVOX | `VOICEVOX_EXE_PATH` を `Process.Start`(エンジン単体 `vv-engine/run.exe` 推奨。エディタ exe でも可) | `GET {VOICEVOX_URL}/version` をポーリング(2秒間隔、起動待ちタイムアウト60秒) | Studio が起動したプロセスのみ Kill |
| PuruPuruPNGTuber | `PURUPURU_PATH`(`run_local_server.bat` のパス)を `Process.Start`。画面は UI の「画面を開く」リンクからブラウザで開く | `GET {PURUPURU_URL}/`(既定 `http://127.0.0.1:8223`)をポーリング(2秒間隔、タイムアウト30秒)。応答は HTML なのでバージョンとしては表示しない | 同上(プロセスツリーごと Kill = bat 配下の python も止まる) |

設計原則:

- **すでに起動していたら起動しない**: VOICEVOX は `/version`、PuruPuru はローカルサーバの
  HTTP 応答があれば「起動済み(外部)」と検出。この場合 UI の停止ボタンは無効化する
  (**ユーザーが手で起動したアプリを Studio が殺さない**)
- 起動パスは環境変数(`VOICEVOX_EXE_PATH` / `PURUPURU_PATH`)または studio.json。未設定なら
  UI に「パス未設定」と表示してボタンを無効化する(起動失敗時はエラーメッセージをそのまま表示)
- 状態機械は `NotConfigured / Stopped / Starting / Running / RunningExternal / Faulted` の6状態。
  判定ロジックはプロセス操作を `IProcessRunner` インターフェースで注入してユニットテスト対象にする
  (`TwitchCommentSource.ParseLine` と同じ「純粋部分を切り出す」方針)
- 「まとめて起動」ボタン: VOICEVOX 起動 → `/version` 応答待ち → PuruPuru 起動 →
  (任意)Live セッション開始、を1クリックで行う

OBS の起動・操作は v1 ではやらない(将来 obs-websocket 対応とまとめて。e2e-test-architecture.md 参照)。

## 設定変更(UI での設定の扱い)

設定は3種類に分けて扱いを変える:

| 種類 | 例 | UI での扱い |
|---|---|---|
| 実行中に反映 | FreetalkAfterSec, CommentBatchSec, フリートークON/OFF, SpeakerId, 感情スタイル, 一時停止 | 変更即時反映(`LiveSession.UpdateOptions`)+ studio.json に保存 |
| セッション再起動で反映 | コメントソース(console/twitch/youtube と接続先), 出力デバイス, LLMプロバイダ/モデル, PERSONA_DIR | 編集可。ただし「次回セッション開始時に反映」と明示。studio.json に保存 |
| シークレット | ANTHROPIC_API_KEY 等の各種APIキー | **編集・表示しない**。設定済み/未設定のバッジのみ(編集は Setup ツールへ誘導) |

- 保存先は `data/studio.json`(実行時に変化するファイルなのでリポジトリ管理外。`data/` の扱いは memory.json と同じ)
- 優先順位: **エンジンのデフォルト < persona.json < 環境変数 < studio.json(UI で明示的に変えた項目のみ)**。
  Studio から起動したセッションだけに適用し、CLI(`Live -- --console`)は従来どおり studio.json を読まない。
  studio.json には「UI で触った項目」だけを保存する(全設定のスナップショットにしない。
  env や persona を変えたのに古い値が勝ち続ける事故を防ぐ)
- 出力デバイスは `GET /api/devices` で一覧を返し、ドロップダウンで選択(Setup と同じ部分一致解決)

## HTTP API(契約)

```
GET  /                       → wwwroot/index.html
GET  /api/status             → { voicevox: {state, version?}, purupuru: {state},
                                 live: {state, source, persona, startedAt?},
                                 commentary: {state, window?, startedAt?} }
POST /api/apps/{voicevox|purupuru}/start
POST /api/apps/{voicevox|purupuru}/stop
POST /api/apps/start-all     → まとめて起動
POST /api/live/start         → body: { source: "manual"|"twitch"|"youtube", target?: "<channel|videoId>" }
POST /api/live/stop          → 配信メモ保存まで行って停止 (Ctrl+C 相当)
POST /api/live/comment       → body: { author: "テスト", text: "こんにちは" } (ManualCommentSource へ注入)
GET  /api/windows            → 可視ウィンドウのタイトル一覧 (ゲーム実況の対象選択用)
POST /api/commentary/start   → body: { window: "<タイトル片>" } (ウィンドウキャプチャ→Vision実況→発話)
POST /api/commentary/stop    → ゲーム実況の停止
GET  /api/settings           → 現在の実効設定 (シークレットは "set"/"unset" のみ)
PUT  /api/settings           → 変更項目のみ受け取り studio.json 更新 + 反映可能なものは即時反映
GET  /api/devices            → 出力デバイス名の一覧
GET  /api/events             → SSE。LiveEvent と AppLauncher の状態変化を JSON で流す
```

SSE のイベント形式(AI や他ツールからも読める素直な JSON):

```
event: reply
data: {"at":"2026-07-15T20:31:05+09:00","text":"[joy]こんばんは!"}

event: comments
data: {"at":"...","comments":[{"author":"viewer1","text":"初見です"}]}

event: commentary
data: {"at":"...","text":"[fun]敵が出てきたよ!"}

event: state
data: {"live":"Running","voicevox":"Running","purupuru":"RunningExternal","commentary":"Stopped"}
```

接続直後に直近のログ(メモリ上のリングバッファ、最大200件)をリプレイしてから追記配信する
(ページをリロードしてもログが消えない)。ログの永続化(ファイル書き出し)は v1 ではやらない。

## 画面構成(1ページ)

```
┌──────────────────────────────────────────────────────┐
│ ヘッダ: ペルソナ名 | 状態バッジ [VOICEVOX●] [PuruPuru●] [Live●] │
├──────────────────────────────┬───────────────────────┤
│ 会話ログ (タイムライン)        │ ▼ 起動パネル            │
│  20:31 viewer1: 初見です       │   [まとめて起動]         │
│  20:31 ぽとふ: [joy]こんばんは!│   VOICEVOX  [起動][停止] │
│  20:32 [skip] フィルタ違反     │   PuruPuru  [起動][停止] │
│  20:33 (フリートーク) …        │ ▼ 配信パネル            │
│                              │   ソース [manual|twitch|…]│
│  ────────────────            │   [配信開始] [配信停止]   │
│  コメント注入:                 │ ▼ 設定パネル             │
│  [author] [text    ] [送信]   │   フリートーク秒数 [45]   │
│                              │   話者ID [3] / 一時停止 □ │
└──────────────────────────────┴───────────────────────┘
```

- 会話ログは種別で色分け(コメント / 発話 / skip / フリートーク / 実況 / システム)。自動スクロール+一時停止
- 設定パネルの各項目に「即時反映」か「次回セッションから」かを明示する
- ダーク基調(配信中に画面の一部として映っても眩しくない)

## ゲーム実況パネル(CommentarySessionHost)

GameCommentary の `CommentaryLoop`(キャプチャ→Vision実況→フィルタ→発話)を Studio 内で
バックグラウンド実行する。CLI(`dotnet run --project GameCommentary`)と同じループ・同じ挙動
(CAPTURE_INTERVAL_SEC 間隔、game_system.md、実況履歴 4 件)。

- 対象ウィンドウは `GET /api/windows`(`WindowCapture.GetVisibleWindowTitles`)の一覧から
  ドロップダウンで選択。start 時に未発見なら可視ウィンドウ一覧付きの 400 を返す(CLI と同じ UX)
- 設定の解決は Live と同じ「デフォルト < persona.json < 環境変数 < studio.json」
- **Live セッションとの同時実行は 409 で相互排他**(両方が別々に発話してかぶるため)
- 実況文は SSE `commentary` イベント、[error] / [skip] は `log` イベントで会話ログに流す
  (`CommentaryLoop` に診断ログの出力先 `Action<string>` を注入できるようにした)
- この参照のため Studio の TFM は `net10.0-windows`(WindowCapture が Win32 API 依存)

## テスト方針

- `LiveSession` 抽出はリファクタリングなので、既存の Live.Tests を壊さないこと +
  イベント発火(コメント→ReplySpoken、フィルタ違反→ReplySkipped と履歴破棄)をフェイクの
  IChatClient / ICommentSource / 合成シンクで検証するテストを追加
- `AppLauncher` の状態遷移は `IProcessRunner` フェイクでユニットテスト
- studio.json のマージ(「UI で触った項目だけが env より優先」)をユニットテスト
- Web ハンドラは薄く保ち、E2E は StageCheck / Stage 2(e2e-test-architecture.md)に委ねる。
  Claude Code が `curl http://127.0.0.1:5100/api/status` と SSE を読めば配信状態を確認できるので、
  StageCheck の検証ワークフローからも利用できる

## 将来拡張(v1 ではやらない)

- obs-websocket 連携(シーン切替・配信開始/停止・`GetSourceScreenshot`)
- TwitterBot / BlogBot の状態表示と dry-run 実行ボタン(全モードの統合コンソール化。
  ~~GameCommentary~~ → ゲーム実況パネルとして実装済み)
- 配信ログのファイル永続化とセッション履歴の閲覧
- 会話履歴(LLM に渡す 12 ターン)の中身の可視化・手動編集
- 視聴者プロフィール(ロードマップ Phase 4)の閲覧 UI
