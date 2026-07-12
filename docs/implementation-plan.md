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
