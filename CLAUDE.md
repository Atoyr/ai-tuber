# AITuber (C# / .NET 8)

Claude API を頭脳とする AITuber(AIバーチャル配信者)。
配信でのリアルタイム視聴者対話・ゲーム実況・X(Twitter)への自律投稿を行う。
開発者は喋らない前提。実行環境は Windows。

## パイプライン全体像

```
コメント/ゲーム画面 → Claude(人格) → VOICEVOX(音声合成) → VB-CABLE → PuruPuruPNGTuber(口パク) → OBS配信
```

## ソリューション構成

既存(触ってよいが破壊的変更は避ける):
- `MultiLLMClient/` — `ILLMClient` で Claude/Gemini/OpenAI を抽象化。名前空間 `Medoz.MultiLLMClient`
- `X/` — X API v2 クライアント自作ライブラリ。名前空間 `Medoz.X`
- `Chat/`, `PostX/` — 動作確認用コンソールアプリ

新規追加予定(詳細は @docs/architecture.md):
- `Voicevox/` — VOICEVOX クライアント + NAudio 再生
- `AiTuber.Core/` — Persona / Memory / ModerationFilter
- `Live/`, `GameCommentary/`, `TwitterBot/`, `BlogBot/` — 各モードの実行アプリ
  (ブログの設計と公開先リポジトリは @docs/blog-architecture.md)
- `personas/default/` — 同梱サンプルペルソナ(人格・モード別プロンプト Markdown + persona.json)。
  キャラ人格は外部の**ペルソナリポジトリ**で管理し `PERSONA_DIR` で差し替える(詳細は @docs/persona-architecture.md)。
  現在の人格「ぽとふ」は private リポジトリ [Atoyr/ai-tuber-potofu](https://github.com/Atoyr/ai-tuber-potofu) にある

## ビルド・実行

- `dotnet build` / `dotnet test` (ルートの `AiTuber.sln` が全プロジェクトを束ねている)
- `dotnet run --project Chat` (LLM疎通確認)
- `dotnet run --project Live -- --console` (配信ループのローカルテスト)
- テストは xUnit。新規ロジックには最低限のユニットテストを付ける
- 新規プロジェクトを追加したら `dotnet sln add <path>.csproj` を忘れずに

## 必ず守るルール

- **キャラ人格は `PERSONA_DIR` の `character.md` が唯一の真実**(ペルソナリポジトリで管理)。人格・口調をC#コードにハードコードしない。モード別指示は同ディレクトリの `live_system.md` / `tweet_system.md` に分離
- **外部投稿・発話を伴うモードは dry-run がデフォルト**。環境変数で明示的に本番化する設計にする
- **禁止ワードフィルタ(ModerationFilter)は Live / Twitter / GameCommentary で共通のクラスを使う**
- プラットフォーム依存部(コメント取得元、投稿先)はインターフェースで抽象化する(例: `ICommentSource`)
- APIキー・トークンは環境変数から取得。コード・リポジトリに含めない
- 名前空間は既存に合わせ `Medoz.*`。コードスタイルは `.editorconfig` に従う
- Claudeのデフォルトモデルは `claude-sonnet-4-6` に統一(既存コードの `claude-3-5-sonnet-20241022` は更新する)
- 変更は小さく。関係ないコードのリファクタリングを勝手にしない

## リファレンス実装(重要)

`reference/python_v2/` に **動作する Python 実装一式** がある。
これは移植元ではなく**動作仕様書**として扱う: 挙動(履歴ターン数、フリートーク条件、
投稿間隔、フィルタの再生成ロジックなど)はこれに合わせ、コードの書き方はC#らしく書く。
数値パラメータの一覧は @docs/architecture.md の「動作仕様」表を参照。

## ドキュメント

- 設計と動作仕様: @docs/architecture.md
- ペルソナ外部化(人格の別リポジトリ管理)の設計: @docs/persona-architecture.md
- 配信 E2E テストと画面表示の AI 検証(StageCheck)の設計: @docs/e2e-test-architecture.md
- タスクと進捗: @docs/implementation-plan.md (完了したら [x] を付けて更新する)
