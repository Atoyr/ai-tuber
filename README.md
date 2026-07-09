# AI Tuber

Claude API を頭脳とする AITuber(AIバーチャル配信者)を動かすためのツール一式です。
配信でのリアルタイム視聴者対話・ゲーム実況・X(Twitter)への自律投稿を行います。
開発者が喋る必要はありません。実行環境は Windows を想定しています。

## できること

### 1. AIキャラクターとのおしゃべり(実装済み)

`prompts/character.md` に書かれた人格を読み込み、LLM が視聴者コメントに応答します。
応答は VOICEVOX で音声合成され、VB-CABLE 経由で PuruPuruPNGTuber に渡ることで
アバターが口パクします。

```
コメント → Claude(人格) → VOICEVOX(音声合成) → VB-CABLE → PuruPuruPNGTuber(口パク) → OBS配信
```

- **LLM を選べる**: Claude / Gemini / OpenAI(GPT)を切り替えて使えます
- **会話の文脈を保つ**: 直近12ターンの会話履歴を渡すため、話の流れを踏まえた返答をします
- **コメントが途切れたら自動でフリートーク**: 45秒コメントが無ければ自分から話し始めます
- **禁止ワードフィルタ**: 不適切な応答は破棄して発話しません
- **配信メモの自動生成**: 配信終了時に内容を要約して `data/memory.json` に保存し、
  X への投稿ネタとして再利用できます

### 2. X(Twitter)への投稿(一部実装済み)

- **PostX**: コンソールから入力したテキストを X に投稿するツール(実装済み)
- **TwitterBot**: 配信メモをもとに Claude が自律的にツイート文を生成し、
  ランダムな間隔で投稿する機能(未実装 / Phase F)

### 3. ゲーム実況(未実装 / Phase G)

ゲームのウィンドウを一定間隔でキャプチャし、Claude の Vision 機能で画面を認識して
実況コメントを生成・発話します。

## 実装状況

| 機能 | 状態 |
|---|---|
| LLM とのメッセージ送受信(Claude / Gemini / OpenAI) | 実装済み |
| VOICEVOX を使ったおしゃべり機能 | 実装済み |
| キャラクター人格・記憶・禁止ワードフィルタの共通基盤 | 実装済み |
| Live 配信本体(コンソール入力でのローカルテスト) | 実装済み |
| X への投稿機能(手動投稿) | 実装済み |
| YouTube Live のコメント自動取得 | 未実装 |
| X への自律投稿(TwitterBot) | 未実装 |
| ゲーム実況(Vision) | 未実装 |
| ブログへの投稿機能 | 未実装 |

詳細な進捗は [docs/implementation-plan.md](docs/implementation-plan.md) を参照してください。

## 事前準備

1. **.NET 8 SDK** をインストールします
2. **VOICEVOX** をインストールして起動します(`http://127.0.0.1:50021` で待ち受けます)
3. **VB-CABLE** をインストールし、PuruPuruPNGTuber のマイク入力を `CABLE Output` に設定します
4. 使用する LLM の API キーを環境変数に設定します
   (既定は Claude の `ANTHROPIC_API_KEY`。Gemini / OpenAI も選べます。詳細は後述)

## 使い方

### Live - 配信本体

コンソール入力を視聴者コメントに見立てて、応答から発話までの一連の流れを試せます。

```
dotnet run --project Live -- --console
```

コメントを入力すると、キャラクターが応答して喋ります。
45秒放置すると自分からフリートークを始めます。
`Ctrl+C` で終了すると、その日の配信内容を要約して `data/memory.json` に保存します。

#### 使用する LLM の切り替え

Claude / Gemini / OpenAI(GPT)のいずれかを選べます。既定は Claude です。
`--provider` オプション、または環境変数 `LLM_PROVIDER` で切り替えます。

```
dotnet run --project Live -- --console --provider gemini
dotnet run --project Live -- --console --provider openai
```

選んだプロバイダに対応する API キーの環境変数を設定してください。

| プロバイダ | API キー | モデル指定(任意) | 既定モデル |
|---|---|---|---|
| `claude` | `ANTHROPIC_API_KEY` | `CLAUDE_MODEL` | `claude-sonnet-4-6` |
| `gemini` | `GEMINI_API_KEY` | `GEMINI_MODEL` | `gemini-2.5-flash` |
| `openai` | `OPENAI_API_KEY` | `OPENAI_MODEL` | `gpt-4o` |

#### その他の環境変数

| 環境変数 | 既定値 | 説明 |
|---|---|---|
| `LLM_PROVIDER` | `claude` | 使用する LLM(`claude` / `gemini` / `openai`) |
| `VOICEVOX_URL` | `http://127.0.0.1:50021` | VOICEVOX の API エンドポイント |
| `VOICEVOX_SPEAKER_ID` | `3` | 話者 ID(`GET /speakers` で確認できます) |
| `VOICEVOX_OUTPUT_DEVICE` | `CABLE Input` | 音声の出力先デバイス名(部分一致) |
| `CHARACTER_NAME` | `ぷる乃` | コンソールに表示するキャラ名 |

### Chat - LLM の疎通確認

Claude / Gemini / OpenAI のいずれかと対話し、任意で VOICEVOX 発話も試せます。

```
dotnet run --project Chat
```

### PostX - X への投稿

コンソールから入力したテキストを X に投稿するコマンドラインツールです。

1. `.env.sample` を `.env` にコピーします。

   ```
   cp PostX/.env.sample PostX/.env
   ```

2. `.env` を編集して X API 認証情報を設定します。

   ```
   X_CONSUMER_KEY=your_consumer_key
   X_CONSUMER_SECRET=your_consumer_secret
   X_ACCESS_TOKEN=your_access_token
   X_ACCESS_TOKEN_SECRET=your_access_token_secret
   ```

   認証情報は [X Developer Portal](https://developer.twitter.com/en/portal/dashboard) から取得できます。

3. 実行します。

   ```
   dotnet run --project PostX
   ```

4. プロンプトが表示されたら、投稿するメッセージ(280文字以内)を入力します。

## キャラクターのカスタマイズ

キャラクターの人格は `prompts/` 配下の Markdown ファイルが唯一の設定元です。
C# のコードを変更する必要はありません。

| ファイル | 役割 |
|---|---|
| `prompts/character.md` | 共通人格(名前・口調・性格・禁止事項) |
| `prompts/live_system.md` | 配信モード専用の指示 |
| `prompts/tweet_system.md` | ツイートモード専用の指示 |

## プロジェクト構成

| プロジェクト | 説明 |
|---|---|
| `AiTuber.Core/` | 人格・記憶・禁止ワードフィルタ・設定の共通基盤 |
| `Live/` | 配信本体(コメント取得 → 応答 → 発話のメインループ) |
| `Voicevox/` | VOICEVOX クライアントと NAudio による音声再生 |
| `MultiLLMClient/` | Claude / Gemini / OpenAI を抽象化した LLM クライアント |
| `X/` | X API v2 クライアント |
| `Chat/`, `PostX/` | 動作確認用のコンソールアプリ |

## ビルドとテスト

リポジトリのルートから、全プロジェクトをまとめてビルド・テストできます。

```
dotnet build
dotnet test
```

テストプロジェクトは `AiTuber.Core.Tests` / `Live.Tests` / `MultiLLMClient.Tests` / `Voicevox.Tests` の4つです。

## ドキュメント

- 設計と動作仕様: [docs/architecture.md](docs/architecture.md)
- タスクと進捗: [docs/implementation-plan.md](docs/implementation-plan.md)
