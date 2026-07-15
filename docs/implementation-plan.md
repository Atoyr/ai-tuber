# 実装計画

各フェーズは独立して動作確認できる単位。完了したらチェックを付けること。
挙動の正解は `reference/python_v2/` と `docs/architecture.md` の動作仕様表。

## Phase A: VOICEVOX 発話 (READMEのTODO最優先)

- [x] `Voicevox/` プロジェクト作成 (`Medoz.Voicevox`, classlib, net8.0)
- [x] `VoicevoxClient` — `/audio_query` → `/synthesis` で wav バイト列を返す
- [x] `AudioPlayer` (NAudio) — デバイス名部分一致で出力先選択、wav再生。見つからない場合は候補一覧付きの例外
- [x] `Chat` に発話オプションを追加し、Claude応答 → VOICEVOX → VB-CABLE の最小パイプラインを確認
- 完了条件: Windows で `dotnet run --project Chat` から PuruPuruPNGTuber が口パクする

## Phase B: MultiLLMClient 拡張

- [x] `ChatMessage` / `IChatClient` 追加(architecture.md のシグネチャ参照。既存 `ILLMClient` は壊さない)
- [x] `ClaudeClient` に履歴対応 `GenerateAsync` 実装
- [x] `ClaudeClient` に Vision (`GenerateWithImageAsync`) 実装
- [x] `ClaudeClient` に SSE streaming (`GenerateStreamAsync`) 実装
- [x] デフォルトモデルを `claude-sonnet-4-6` に更新
- [x] ユニットテスト(リクエストJSON組み立てとレスポンスパース)

## Phase C: AiTuber.Core

- [x] `Persona` — `prompts/character.md` + モード別mdを結合、`IChatClient` をラップ
- [x] `SharedMemory` — `data/memory.json` の load/save/AddStreamNote/AddTweet
- [x] `ModerationFilter` — 禁止ワード判定(設定から読み込み)
- [x] `AppConfig` — 環境変数と定数の集約(Python版 config.py 相当)
- [x] ユニットテスト

## Phase D: Live(配信本体)

- [x] `ICommentSource` + `ConsoleCommentSource`(コンソール入力をコメント化)
- [x] メインループ: 4秒バッチでコメント取得 → 12ターン履歴で応答生成 → フィルタ → 発話
- [x] 45秒無コメントで自動フリートーク
- [x] Ctrl+C 終了時に配信メモを要約して SharedMemory に保存
- 完了条件: `dotnet run --project Live -- --console` で Python版 `live_aituber.py --console` と同じ体験

## Phase E: YouTube Live 対応

- [x] `YouTubeCommentSource` 実装(方式は architecture.md の選択肢から判断。公式APIならクォータ配慮)
- [ ] 動作テスト(実配信枠 or 限定公開)
- [x] `TwitchCommentSource` 実装(Twitch IRC 匿名接続 / APIキー不要。`--twitch <channel|URL>`)
- [ ] Twitch 実配信での動作テスト(実チャンネルで接続・コメント取得を確認)

## Phase F: TwitterBot(自律投稿)

- [x] コンテキスト組立(現在日時・直近配信メモ・直近ツイート10件)
- [x] JSON出力のパース → 140字・フィルタ・重複検証 → 最大3回再生成
- [x] dry-run デフォルト、`--once` オプション、9〜24時制限、180〜360分ランダム間隔
- [x] 既存 `Medoz.X.XClient` で投稿
- 完了条件: dry-run で `python twitter_bot.py --once` 相当の出力

## Phase G: GameCommentary(ゲーム実況)

- [x] `WindowCapture` — ウィンドウタイトル部分一致で対象特定、キャプチャ、幅800pxリサイズ
- [x] 12秒間隔ループ: キャプチャ → Vision実況(直近4件の文脈付き) → 発話
- [x] エラー時はスキップして次のキャプチャで再試行

## Phase H: 品質向上(v3ロードマップ Phase 3)

- [x] streaming応答を文単位("。!?"区切り)で分割し、確定した文から順次合成
- [x] `System.Threading.Channels` で合成キュー+再生キューの2キュー化(合成と再生の並行化)
- [x] 感情タグ規約を character.md に追加、パースして VOICEVOX スタイル切替
- [x] コメント選択の優先度ロジック(初見優先・質問優先など)

## Phase I: ブログ(GitHub Pages)

設計: @docs/blog-architecture.md。サイト本体は別リポジトリ [Atoyr/ai-tuber-blogs](https://github.com/Atoyr/ai-tuber-blogs)。

- [x] ai-tuber-blogs: Nuxt 3 + @nuxt/content サイト一式(記事一覧・詳細・About・deploy.yml・CLAUDE.md)
- [x] `prompts/blog_system.md` 追加
- [x] `SharedMemory` に `RecentPosts`(直近記事、最大10件)追加
- [x] `BlogBot/` — 生成 → JSONパース → 検証(タイトル40字・本文200〜2000字・フィルタ・重複)→ 最大3回再生成
- [x] `IBlogPublisher` — `DryRunBlogPublisher`(デフォルト)/ `GitBlogPublisher`(BLOG_DRY_RUN=0 で commit & push)
- [x] ユニットテスト
- [ ] ai-tuber-blogs を main に push し、Settings → Pages → Source を「GitHub Actions」に設定(手動)
- [ ] 実機テスト: `BLOG_DRY_RUN=0` で記事が公開されることを確認
- 完了条件: `dotnet run --project BlogBot` (dry-run) で記事 markdown が出力され、本番化すると
  https://atoyr.github.io/ai-tuber-blogs/ に記事が載る

## Phase J: TwitterBot の Linux (ラズパイ/VM) 常時運用

設計: @docs/twitterbot-linux-deployment.md。systemd timer でランダム間隔 (180〜360分) を実現する。

- [x] `TwitterBot` に `--scheduled` オプション追加 (時間帯チェック付き1回実行。timer から呼ばれる)
- [x] CI (GitHub Actions): Windows でビルド+全テスト、Ubuntu で linux-arm64 publish 検証 (.github/workflows/ci.yml)
- [ ] `dotnet publish -r linux-arm64 --self-contained -p:PublishSingleFile=true` で発行し、実機に配置
- [ ] `/etc/ai-tuber/twitterbot.env` (TZ=Asia/Tokyo, APIキー, TWEET_DRY_RUN=1) と
      `twitterbot.service` / `twitterbot.timer` を設置
- [ ] dry-run で `--once` 手動実行 → timer 発火を数回確認 (時間外スキップ含む)
- [ ] `TWEET_DRY_RUN=0` で本番化
- [ ] (任意) Syncthing で `data/` を配信PCと同期し、配信メモをツイートに反映
- 完了条件: 配信PCを落としていてもラズパイ単独で 9〜24時にランダム投稿され続ける

## Phase K: ペルソナ外部化

設計: @docs/persona-architecture.md。人格を外部ディレクトリ/別リポジトリに切り出し、
このリポジトリはエンジンに徹する。

- [x] `AiTuber.Core/PersonaPackage.cs` — persona.json + character.md のロードと検証(fail fast)+ ユニットテスト
- [x] `personas/default/` 同梱サンプルペルソナ(中立キャラ + persona.json)
- [x] `AppConfig`: `PromptDir` → `PersonaDir`(環境変数 `PERSONA_DIR`)、SpeakerId/EmotionStyleIds/BannedWords の優先順位を「デフォルト < persona.json < 環境変数」に
- [x] 各アプリ(Live / TwitterBot / GameCommentary / BlogBot)を PersonaPackage 経由に切替。表示名も persona.json から
      (Chat はプロンプト手入力の疎通確認アプリのためペルソナを使わない)
- [x] `SharedMemory` の保存先を `data/<slug>/memory.json` に(既存 `data/memory.json` は `data/potofu/` へ移動済み)
- [x] 移行措置: `prompts/persona.json`(ぽとふのマニフェスト)を追加し、`PERSONA_DIR=prompts` で従来人格のまま動くことを確認。
      **`PERSONA_DIR` 未設定時は同梱サンプルが起動する**(従来のぽとふではない)点に注意
- [x] `Atoyr/ai-tuber-potofu`(private)を作成し現 `prompts/` 一式(persona.json 含む)を移動、
      sibling clone + `PERSONA_DIR=../ai-tuber-potofu` で従来と同一動作を確認(TwitterBot でロード確認済み)
- [x] `prompts/` を削除し、CLAUDE.md / architecture.md の「prompts/character.md が唯一の真実」関連記述を更新
- 完了条件: `PERSONA_DIR` の向け替えだけで別人格として `dotnet run --project Live -- --console` が動く

## Phase L: StageCheck(実機画面・音声の AI 検証ツール)

設計: @docs/e2e-test-architecture.md。VB-CABLE → PuruPuruPNGTuber → OBS の最終段を
人の目・耳の代わりに Claude Code が確認できるようにするローカル専用ツール。外部投稿・配信開始はしない。

- [ ] `StageCheck/` プロジェクト作成(console, `net10.0-windows`, `Medoz.StageCheck`)。`dotnet sln add`。
      `GameCommentary`(WindowCapture 再利用)と `Voicevox` を参照
- [ ] `--list-windows` / `--snapshot "<タイトル片>"...` / `--screen` — キャプチャを
      `artifacts/stagecheck/<timestamp>/` に JPEG 保存。`artifacts/` を .gitignore に追加
- [ ] `MotionMetrics` — グレースケール縮小フレームの差分計算と合否判定(pure static)+ ユニットテスト
- [ ] `--lipsync "<タイトル片>" [--text ...]` — 無音Nフレーム → VOICEVOX 発話中Nフレーム →
      差分比較で口パク判定。report.json 出力、exit code で合否
- [ ] `--audio [--device ...]` — NAudio `WasapiLoopbackCapture` で再生デバイスの RMS を測定し、
      音が実際に CABLE Input へ流れたことを検証。report.json 出力
- 完了条件: Claude Code が「画面表示をテストして」の指示だけで StageCheck を実行し、
  report.json と JPEG を読んで口パク・レイアウトを合否判定できる

## Phase M: 配信リハーサルと本番初配信(実機 E2E)

設計: @docs/e2e-test-architecture.md の「最終テスト計画」。各 Stage は前段の合格が前提。
Stage 4 は Phase K 完了後(PERSONA_DIR がぽとふリポジトリを指す状態)で行う。

- [ ] Stage 0: セルフチェック(build / test / `--list-devices` に CABLE Input / VOICEVOX 応答 / ペルソナロード)
- [ ] Stage 1: StageCheck `--audio` と `--lipsync` が pass(音声経路と口パクの全自動検証)
- [ ] Stage 2: `Live -- --console` フルループ(応答→発話→口パク、Ctrl+C で配信メモ保存まで)+ 並行 `--snapshot` で画面確認
- [ ] Stage 3a: Twitch 実チャンネルでチャット取得リハーサル(= Phase E の Twitch 残項目。消化したら E 側にも [x])
- [ ] Stage 3b: YouTube 限定公開枠でコメント取得リハーサル(= Phase E の YouTube 残項目)
- [ ] Stage 3c: OBS から帯域テストモード(Twitch)or 限定公開(YouTube)で実際に配信し、視聴側で映像・音声・口パクを確認
- [ ] Stage 4: 本番初配信(短時間)→ 配信メモ保存 → TwitterBot dry-run への反映確認
- 完了条件: 事故なく 1 枠完走し、manual.md「未実施の手動テスト」の Live 関連項目がすべて消える

## Phase N: Studio(配信コントロールパネル / ローカル Web UI)

設計: @docs/studio-architecture.md。会話ログ閲覧・設定変更・VOICEVOX / PuruPuruPNGTuber の
起動をブラウザ(localhost 限定)から行う。外部投稿はしない。

- [ ] Live リファクタリング: `Live/Program.cs` のループを `LiveSession` に抽出
      (`LiveEvent` イベント発火 + `LiveSessionOptions` の実行中変更)。CLI の挙動は不変。
      `ManualCommentSource` / `CompositeCommentSource` 追加 + ユニットテスト
- [ ] `Studio/` プロジェクト作成(`Microsoft.NET.Sdk.Web`, `net10.0-windows`, `Medoz.Studio`)。
      `dotnet sln add`。`http://127.0.0.1:5100`(`STUDIO_PORT` で変更可)で待ち受け
- [ ] `AppLauncher` — VOICEVOX(`VOICEVOX_EXE_PATH`, `/version` で起動待ち)と
      PuruPuruPNGTuber(`PURUPURU_EXE_PATH`)の起動・停止・状態表示。
      外部起動済みなら殺さない。`IProcessRunner` 注入でユニットテスト
- [ ] REST + SSE API(`/api/status`, `/api/apps/*`, `/api/live/*`, `/api/settings`, `/api/events`)。
      SSE は直近200件リプレイ + 追記配信
- [ ] `data/studio.json` — UI で変更した設定の永続化
      (優先順位: デフォルト < persona.json < 環境変数 < studio.json)+ マージのユニットテスト
- [ ] `wwwroot/` フロントエンド(素の HTML/CSS/JS、ビルド不要): 会話ログタイムライン・
      コメント注入・起動/配信/設定パネル
- 完了条件: `dotnet run --project Studio` → ブラウザから「まとめて起動」→ 配信開始 →
  コメント注入で応答・発話し、会話ログに流れる。設定変更(フリートーク秒数等)が即時反映される
