# AI Tuber

Claude API を頭脳とする AITuber(AIバーチャル配信者)を動かすためのツール一式です。
配信でのリアルタイム視聴者対話・ゲーム実況・X(Twitter)への自律投稿を行います。
開発者が喋る必要はありません。実行環境は Windows を想定しています。

## できること

### 1. AIキャラクターとのおしゃべり(実装済み)

ペルソナパッケージ(後述)の `character.md` に書かれた人格を読み込み、LLM が視聴者コメントに応答します。
応答は VOICEVOX で音声合成され、VB-CABLE 経由で PuruPuruPNGTuber に渡ることで
アバターが口パクします。

```
コメント → Claude(人格) → VOICEVOX(音声合成) → VB-CABLE → PuruPuruPNGTuber(口パク) → OBS配信
```

- **LLM を選べる**: Claude / Gemini / OpenAI(GPT)を切り替えて使えます
- **会話の文脈を保つ**: 直近12ターンの会話履歴を渡すため、話の流れを踏まえた返答をします
- **コメントが途切れたら自動でフリートーク**: 45秒コメントが無ければ自分から話し始めます
- **禁止ワードフィルタ**: 不適切な応答は破棄して発話しません
- **配信メモの自動生成**: 配信終了時に内容を要約して `data/<ペルソナslug>/memory.json` に保存し、
  X への投稿ネタとして再利用できます
- **人格を差し替えられる**: 人格は「ペルソナパッケージ」として外部ディレクトリ/別リポジトリで管理し、
  環境変数 `PERSONA_DIR` の向け替えだけで別キャラとして起動できます(後述)
- **YouTube Live / Twitch のコメント取得に対応**: `--youtube <videoId|URL>` または
  `--twitch <channel|URL>` で実配信のコメントに応答できます(Twitch は API キー不要)

### 2. X(Twitter)への投稿(実装済み)

- **PostX**: コンソールから入力したテキストを X に投稿するツール
- **TwitterBot**: 配信メモをもとに Claude が自律的にツイート文を生成し、
  9〜24時の間に 180〜360 分のランダム間隔で投稿します。既定は dry-run(コンソール出力のみ)で、
  `TWEET_DRY_RUN=0` を設定したときだけ実投稿します

### 3. ゲーム実況(実装済み)

ゲームのウィンドウを 12 秒間隔でキャプチャし、Claude の Vision 機能で画面を認識して
実況コメントを生成・発話します。

```
dotnet run --project GameCommentary -- --window "<ウィンドウタイトルの一部>"
```

### 4. ブログ記事の自動投稿(実装済み)

配信メモをもとに Claude が記事を生成し、別リポジトリ
[Atoyr/ai-tuber-blogs](https://github.com/Atoyr/ai-tuber-blogs)(GitHub Pages)へ公開します。
既定は dry-run で、`BLOG_DRY_RUN=0` のときだけ commit & push します。

## 実装状況

| 機能 | 状態 |
|---|---|
| LLM とのメッセージ送受信(Claude / Gemini / OpenAI) | 実装済み |
| VOICEVOX を使ったおしゃべり機能 | 実装済み |
| キャラクター人格・記憶・禁止ワードフィルタの共通基盤 | 実装済み |
| Live 配信本体(コンソール入力でのローカルテスト) | 実装済み |
| X への投稿機能(手動投稿) | 実装済み |
| YouTube Live のコメント自動取得 | 実装済み(実配信での動作テストは未実施) |
| Twitch チャットの自動取得(API キー不要) | 実装済み(実配信での動作テストは未実施) |
| X への自律投稿(TwitterBot) | 実装済み(dry-run 既定) |
| ゲーム実況(Vision) | 実装済み |
| ブログへの投稿機能(BlogBot) | 実装済み(dry-run 既定。公開先の GitHub Pages 設定が残) |
| ストリーミング発話・感情タグ・コメント優先度(品質向上) | 実装済み |

詳細な進捗は [docs/implementation-plan.md](docs/implementation-plan.md) を参照してください。

## 事前準備

1. **.NET 10 SDK** をインストールします
2. **VOICEVOX** をインストールして起動します(`http://127.0.0.1:50021` で待ち受けます)
3. 仮想オーディオデバイス(VB-CABLE / VoiceMeeter など)をインストールし、
   PuruPuruPNGTuber のマイク入力をその出力側(例: `CABLE Output`)に設定します
4. 使用する LLM の API キーを環境変数に設定します
   (既定は Claude の `ANTHROPIC_API_KEY`。Gemini / OpenAI も選べます。詳細は後述)
   環境変数の設定は下記の **Setup - 初期セットアップ UI** から GUI で行えます。

## 使い方

### Setup - 初期セットアップ UI

LLM API キー / VOICEVOX 設定 / X 認証情報を GUI から一括で設定できる WPF アプリです。
Windows でのみ動作します。

```
dotnet run --project Setup
```

- タブごとに「この項目を保存しない (スキップ)」チェックボックスがあり、
  未設定のまま保存対象から外せます
- 音声出力デバイスは現在利用可能なデバイス一覧から選択できます
- 保存すると **ユーザー環境変数** に書き込まれ、
  新しく起動した Live / Chat / PostX から有効になります
  (既に開いている PowerShell / CMD セッションには反映されません)
- X 認証情報を保存した場合は `PostX/.env` も同時に更新されます

### Live - 配信本体

コンソール入力を視聴者コメントに見立てて、応答から発話までの一連の流れを試せます。

```
dotnet run --project Live -- --console
```

コメントを入力すると、キャラクターが応答して喋ります。
45秒放置すると自分からフリートークを始めます。
`Ctrl+C` で終了すると、その日の配信内容を要約して `data/<ペルソナslug>/memory.json` に保存します。

実配信のコメントを取得する場合:

```
dotnet run --project Live -- --youtube <videoId|URL>   # YouTube Live (要 YOUTUBE_API_KEY)
dotnet run --project Live -- --twitch <channel|URL>    # Twitch (匿名接続 / APIキー不要)
```

#### 使用する LLM の切り替え

Claude / Gemini / OpenAI(GPT)のいずれかを選べます。既定は Claude です。
`--provider` オプション、または環境変数 `LLM_PROVIDER` で切り替えます。

```
dotnet run --project Live -- --console --provider gemini
dotnet run --project Live -- --console --provider openai
```

選んだプロバイダに対応する API キーの環境変数を設定してください。

#### 出力デバイスの選択

VOICEVOX の音声を流す出力デバイス(VB-CABLE / VoiceMeeter などの仮想オーディオデバイス)を選べます。
デバイス名は部分一致で判定されます(既定は `CABLE Input`)。

```
dotnet run --project Live -- --list-devices                    # 利用可能なデバイス一覧を表示して終了
dotnet run --project Live -- --console --device "VoiceMeeter"  # コマンドラインで指定
dotnet run --project Live -- --console --select-device         # 起動時に対話で選ぶ
```

環境変数 `VOICEVOX_OUTPUT_DEVICE` でも既定値を上書きできます。
指定したデバイスが見つからないときは対話ピッカーにフォールバックします。

| プロバイダ | API キー | モデル指定(任意) | 既定モデル |
|---|---|---|---|
| `claude` | `ANTHROPIC_API_KEY` | `CLAUDE_MODEL` | `claude-sonnet-4-6` |
| `gemini` | `GEMINI_API_KEY` | `GEMINI_MODEL` | `gemini-2.5-flash` |
| `openai` | `OPENAI_API_KEY` | `OPENAI_MODEL` | `gpt-4o` |

#### その他の環境変数

| 環境変数 | 既定値 | 説明 |
|---|---|---|
| `PERSONA_DIR` | `personas/default` | ペルソナパッケージのディレクトリ(下記「ペルソナ(人格)の差し替え」参照) |
| `LLM_PROVIDER` | `claude` | 使用する LLM(`claude` / `gemini` / `openai`) |
| `VOICEVOX_URL` | `http://127.0.0.1:50021` | VOICEVOX の API エンドポイント |
| `VOICEVOX_SPEAKER_ID` | (persona.json の値) | 話者 ID の上書き(`GET /speakers` で確認できます) |
| `VOICEVOX_OUTPUT_DEVICE` | `CABLE Input` | 音声の出力先デバイス名(部分一致 / VB-CABLE 以外の仮想デバイスも指定可) |
| `CHARACTER_NAME` | (persona.json の値) | コンソールに表示するキャラ名の上書き |

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

## ペルソナ(人格)の差し替え

このリポジトリは「AITuber エンジン」に徹し、キャラクターの人格は**ペルソナパッケージ**
(1人格 = 1ディレクトリ。通常は別リポジトリ)として外部管理します。
環境変数 `PERSONA_DIR` を向け替えるだけで、C# のコードを変更せずに別キャラとして起動できます。
フォーマットの正式契約は [docs/persona-architecture.md](docs/persona-architecture.md) を参照してください。

```
# 未設定なら同梱のサンプルペルソナ (personas/default) で起動する
dotnet run --project Live -- --console

# 外部のペルソナリポジトリを指定して起動する
$env:PERSONA_DIR = "..\ai-tuber-persona-potofu"
dotnet run --project Live -- --console
```

### ペルソナパッケージの構成

| ファイル | 必須 | 役割 |
|---|---|---|
| `persona.json` | ○ | マニフェスト(表示名・slug・VOICEVOX 話者ID・感情スタイル・追加禁止ワード) |
| `character.md` | ○ | 共通人格(名前・口調・性格・禁止事項)= キャラの魂 |
| `live_system.md` | 使うモードのみ | 配信モード専用の指示 |
| `tweet_system.md` | 〃 | ツイートモード専用の指示 |
| `game_system.md` | 〃 | ゲーム実況モード専用の指示 |
| `blog_system.md` | 〃 | ブログモード専用の指示 |

- 必須ファイルや起動するモードの md が無い場合は、不足ファイル名を示して起動時にエラーになります
- 設定の優先順位は「エンジンのデフォルト値 < persona.json < 環境変数」です。
  たとえば話者 ID は persona.json の `voice.speakerId` が使われ、環境変数 `VOICEVOX_SPEAKER_ID` を
  設定した場合のみそちらが優先されます
- 配信メモやツイート履歴は `data/<slug>/memory.json` にペルソナごとに保存されます
- 同梱の [personas/default/](personas/default/) はフォーマットの実例を兼ねたサンプルです。
  新しいペルソナを作るときはこれをコピーして書き換えてください

> **移行中の注意**: 従来の人格「ぽとふ」は外部リポジトリへの移行が完了するまで `prompts/` にあります。
> 従来どおりぽとふで起動するには `PERSONA_DIR=prompts` を設定してください(未設定だとサンプルが起動します)。

## プロジェクト構成

| プロジェクト | 説明 |
|---|---|
| `AiTuber.Core/` | 人格・記憶・禁止ワードフィルタ・設定の共通基盤 |
| `Live/` | 配信本体(コメント取得 → 応答 → 発話のメインループ。コンソール / YouTube / Twitch) |
| `GameCommentary/` | ゲーム実況(ウィンドウキャプチャ → Vision 実況 → 発話。Windows 専用) |
| `TwitterBot/` | X への自律投稿ボット(dry-run 既定) |
| `BlogBot/` | ブログ記事の自動生成・公開(dry-run 既定) |
| `Voicevox/` | VOICEVOX クライアントと NAudio による音声再生・TTS パイプライン・感情タグ |
| `MultiLLMClient/` | Claude / Gemini / OpenAI を抽象化した LLM クライアント |
| `X/` | X API v2 クライアント |
| `Setup/` | 初期セットアップ用 WPF アプリ (Windows 専用) |
| `Chat/`, `PostX/` | 動作確認用のコンソールアプリ |

## ビルドとテスト

リポジトリのルートから、全プロジェクトをまとめてビルド・テストできます。

```
dotnet build
dotnet test
```

テストプロジェクトは `AiTuber.Core.Tests` / `Live.Tests` / `MultiLLMClient.Tests` / `Voicevox.Tests` /
`TwitterBot.Tests` / `GameCommentary.Tests` / `BlogBot.Tests` の7つです。

## ドキュメント

- 利用者向け運用マニュアル: [docs/manual.md](docs/manual.md)
- 設計と動作仕様: [docs/architecture.md](docs/architecture.md)
- ペルソナ外部化(人格の別リポジトリ管理)の設計: [docs/persona-architecture.md](docs/persona-architecture.md)
- タスクと進捗: [docs/implementation-plan.md](docs/implementation-plan.md)
