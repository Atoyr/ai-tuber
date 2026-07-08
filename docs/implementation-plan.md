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

- [ ] `Persona` — `prompts/character.md` + モード別mdを結合、`IChatClient` をラップ
- [ ] `SharedMemory` — `data/memory.json` の load/save/AddStreamNote/AddTweet
- [ ] `ModerationFilter` — 禁止ワード判定(設定から読み込み)
- [ ] `AppConfig` — 環境変数と定数の集約(Python版 config.py 相当)
- [ ] ユニットテスト

## Phase D: Live(配信本体)

- [ ] `ICommentSource` + `ConsoleCommentSource`(コンソール入力をコメント化)
- [ ] メインループ: 4秒バッチでコメント取得 → 12ターン履歴で応答生成 → フィルタ → 発話
- [ ] 45秒無コメントで自動フリートーク
- [ ] Ctrl+C 終了時に配信メモを要約して SharedMemory に保存
- 完了条件: `dotnet run --project Live -- --console` で Python版 `live_aituber.py --console` と同じ体験

## Phase E: YouTube Live 対応

- [ ] `YouTubeCommentSource` 実装(方式は architecture.md の選択肢から判断。公式APIならクォータ配慮)
- [ ] 動作テスト(実配信枠 or 限定公開)

## Phase F: TwitterBot(自律投稿)

- [ ] コンテキスト組立(現在日時・直近配信メモ・直近ツイート10件)
- [ ] JSON出力のパース → 140字・フィルタ・重複検証 → 最大3回再生成
- [ ] dry-run デフォルト、`--once` オプション、9〜24時制限、180〜360分ランダム間隔
- [ ] 既存 `Medoz.X.XClient` で投稿
- 完了条件: dry-run で `python twitter_bot.py --once` 相当の出力

## Phase G: GameCommentary(ゲーム実況)

- [ ] `WindowCapture` — ウィンドウタイトル部分一致で対象特定、キャプチャ、幅800pxリサイズ
- [ ] 12秒間隔ループ: キャプチャ → Vision実況(直近4件の文脈付き) → 発話
- [ ] エラー時はスキップして次のキャプチャで再試行

## Phase H: 品質向上(v3ロードマップ Phase 3)

- [ ] streaming応答を文単位("。!?"区切り)で分割し、確定した文から順次合成
- [ ] `System.Threading.Channels` で合成キュー+再生キューの2キュー化(合成と再生の並行化)
- [ ] 感情タグ規約を character.md に追加、パースして VOICEVOX スタイル切替
- [ ] コメント選択の優先度ロジック(初見優先・質問優先など)
