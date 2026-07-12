# 作業状態 (state.md)

目標: Phase E〜H を最終フェーズまで実装し、全ユニットテストが通ること。開発は Opus サブエージェントに指示して実施。完了後にマニュアル作成。

このファイルは作業再開用の記録。各ステップ開始時・完了時に更新する。

## タスク一覧

- [x] 0. ベースライン確認: `dotnet build` / `dotnet test` が現状で通ること(2026-07-11: ビルド成功・テスト54件全パス。実ターゲットは net10.0)
- [x] 1. Phase E: `YouTubeCommentSource` 実装完了(Data API v3 を HttpClient 直叩き、pollingIntervalMillis 準拠、`--youtube` オプション、テスト16件追加・全パス。実配信での動作テストのみ手動で要実施)
- [x] 2. Phase F: `TwitterBot/` + `TwitterBot.Tests/` 実装完了(dry-run デフォルト=TWEET_DRY_RUN、--once、9〜24時制限、180〜360分間隔、検証3回リトライ。テスト29件追加、全99件パス)
- [x] 3. Phase G: `GameCommentary/` + `GameCommentary.Tests/` 実装完了(WindowCapture=Win32 P/Invoke、12秒ループ、履歴4件、prompts/game_system.md 追加、--no-voice。テスト16件追加、全115件パス)
- [x] 4. Phase H: 品質向上完了(SentenceSplitter / TtsPipeline(Channels 2キュー) / EmotionTagParser+スタイルマップ / CommentSelector。Live は streaming 既定・`--no-stream` で旧経路。テスト全146件パス)
- [x] 5. 全体検証完了: ビルド0エラー、テスト146件全パス(MultiLLM 15 / Voicevox 28 / TwitterBot 29 / Live 28 / Core 30 / GameCommentary 16)。implementation-plan.md の未チェックは Phase E「動作テスト(実配信枠)」のみ=実配信が必要な手動作業
- [x] 6. マニュアル作成完了: `docs/manual.md`(471行、10章: セットアップ/環境変数/各モード実行/感情タグ/カスタマイズ/トラブルシューティング等)。README にリンク追加

## 進捗ログ

- 2026-07-11: 作業開始。タスク化と state.md 作成。
- 2026-07-11: ステップ0完了。ビルドOK・全テストパス(Voicevox 8 / MultiLLM 15 / Core 27 / Live 4)。注意: CLAUDE.md は .NET 8 表記だが実際は net10.0。新規プロジェクトは net10.0 に合わせる。
- 2026-07-11: ステップ1(Phase E)開始。Opus サブエージェントに YouTubeCommentSource 実装を指示。
- 2026-07-11: ステップ1完了。Live/YouTubeCommentSource.cs 追加、AppConfig に YOUTUBE_API_KEY、Program.cs に --youtube。テスト全パス(Live 20/Voicevox 8/MultiLLM 15/Core 27)。初回ページはスキップ(接続以降の新規のみ)。
- 2026-07-11: ステップ2(Phase F: TwitterBot)開始。Opus サブエージェントに指示。
- 2026-07-11: ステップ2完了。TwitterBot/TwitterBot.Tests を sln に追加。文字数はコードポイント数(EnumerateRunes)で判定。実投稿は Medoz.X.XClient(OAuth2, X_ACCESS_TOKEN)。全99テストパス。
- 2026-07-11: ステップ3(Phase G: GameCommentary)開始。Opus サブエージェントに指示。
- 2026-07-11: ステップ3完了。net10.0-windows、IWindowCapture/ISpeaker で分離しテスト可能に。ウィンドウ指定は WINDOW_TITLE_FRAGMENT env / --window。全115テストパス。
- 2026-07-11: ステップ4(Phase H: 品質向上)開始。Opus サブエージェントに指示。
- 2026-07-11: ステップ4完了。共通部品は Voicevox プロジェクトに集約(SentenceSplitter/EmotionTag/TtsPipeline)、CommentSelector は Live。感情タグ規約を prompts/character.md に追記。VOICEVOX_EMOTION_STYLES env で上書き可。全146テストパス。
- 2026-07-11: ステップ5(全体検証)開始。
- 2026-07-11: ステップ5完了。全146テストパスを自分でも再確認。
- 2026-07-11: ステップ6(マニュアル作成)開始。Opus サブエージェントに指示。
- 2026-07-12: ステップ6完了。ビルド0エラー・テスト146件全パス・マニュアル作成済み。
- 2026-07-12: ユーザー指示で追加タスク。全変更を d314052 でコミット・プッシュ済み。配信は YouTube より Twitch がメインとのことで Twitch 対応(ステップ8)開始。Opus サブエージェントに指示。
- 2026-07-12: ステップ8完了。Live/TwitchCommentSource.cs(IRC 行パースは static メソッドに分離しテスト可能、接続は Func<CancellationToken, Stream> 注入)+ Live.Tests/TwitchCommentSourceTests.cs(26件)。Live.Tests 54件、全体172件全パス。ステップ9でコミット・プッシュ。

## 手動テスト(Twitch)

- 実チャンネルでの接続確認: `dotnet run --project Live -- --twitch <チャンネル名>` を配信中のチャンネルで実行し、コメント取得→応答→発話を確認する(implementation-plan.md Phase E の未チェック項目)

## 追加タスク(2026-07-12 ユーザー指示)

- [x] 7. 全変更をコミット・プッシュ(d314052 として push 済み)
- [x] 8. Twitch ライブ配信対応完了: `TwitchCommentSource`(Twitch IRC 匿名接続=justinfan・APIキー不要、TLS 6697、PING/PONG、自動再接続、チャンネル名/URL 正規化)+ `--twitch <channel|URL>` オプション + テスト26件 + architecture.md / manual.md 更新。全172テストパス
- [x] 9. Twitch 対応をコミット・プッシュ済み

## 残作業(手動・任意)

- Phase E「動作テスト(実配信枠 or 限定公開)」— YouTube の実ライブ配信が必要なため自動化不可。手順は docs/manual.md の「未実施の手動テスト」参照
- マニュアル執筆中に見つかった軽微な不整合(コード未修正・要判断):
  1. X 認証の環境変数名が TwitterBot(X_API_KEY 系)と PostX(X_CONSUMER_KEY 系)で不一致(Setup は両方に書き込むため実害なし)
  2. TwitterBot の XTweetPoster は OAuth2 で X_ACCESS_SECRET を RefreshToken 相当として使用(変則的)
  3. CLAUDE.md の見出しが「.NET 8」のまま(実体は net10.0)

## 実施メモ(再開時に読む)

- 実装の正解仕様: `reference/python_v2/` と `docs/architecture.md` の動作仕様表
- 開発は Opus サブエージェント(Agent tool, model: opus)に指示して行う方針(ユーザー指定)
- 新規プロジェクトは `dotnet sln add` を忘れない
- dry-run デフォルト、人格は prompts/*.md のみ、ModerationFilter 共通利用、Medoz.* 名前空間
