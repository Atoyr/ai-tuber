# AITuber 運用マニュアル

Claude(または Gemini / OpenAI)を頭脳とする AITuber を動かすための、利用者向け運用マニュアルです。
配信でのリアルタイム視聴者対話・ゲーム実況・X(Twitter)への自律投稿を行います。開発者が喋る必要はありません。実行環境は Windows を想定しています。

> このマニュアルはコードの実装(2026年7月時点)に合わせて記述しています。設計思想や動作仕様の背景は [architecture.md](architecture.md)、進捗は [implementation-plan.md](implementation-plan.md) を参照してください。

---

## 1. 概要

### パイプライン全体像

```
コメント / ゲーム画面
        │
        ▼
   Claude(人格 = prompts/character.md)
        │  ← 応答テキスト(+ 感情タグ [fun] など)
        ▼
   VOICEVOX(音声合成 / http://127.0.0.1:50021)
        │  ← wav
        ▼
   VB-CABLE(仮想オーディオデバイス "CABLE Input")
        │
        ▼
   PuruPuruPNGTuber(マイク入力を拾って口パク)
        │
        ▼
   OBS(配信)
```

- 入力側は 2 系統: 視聴者コメント(コンソール入力 / YouTube Live)と、ゲーム画面のスクリーンショット(Vision)。
- LLM は Claude / Gemini / OpenAI を切り替え可能。既定は Claude。
- 合成した音声は仮想オーディオデバイスへ再生し、PuruPuruPNGTuber がそれをマイク入力として拾って口パクします。

### モードと実装状況

| モード | プロジェクト | 内容 | 状態 |
|---|---|---|---|
| おしゃべり疎通確認 | `Chat` | LLM と対話し任意で発話 | 実装済み |
| 配信本体(Live) | `Live` | コメント → 応答 → 発話。コンソール入力 / YouTube Live | 実装済み(YouTube 実配信テストは未実施) |
| 自律ツイート | `TwitterBot` | 配信メモから自動ツイート生成・投稿 | 実装済み |
| ゲーム実況 | `GameCommentary` | ウィンドウキャプチャ → Vision 実況 | 実装済み |
| 手動投稿 | `PostX` | コンソール入力を X に投稿 | 実装済み |
| 初期セットアップ | `Setup` | 環境変数を GUI で設定(WPF) | 実装済み |

---

## 2. 前提セットアップ

### 2.1 .NET 10 SDK

本ソリューションは全プロジェクトが `net10.0`(GUI / Windows 依存プロジェクトは `net10.0-windows`)を対象にしています。**.NET 10 SDK** をインストールしてください。

```powershell
dotnet --version   # 10.x が出ることを確認
```

### 2.2 VOICEVOX

音声合成に [VOICEVOX](https://voicevox.hiroshiba.jp/) を使います。

1. VOICEVOX をインストールして起動します。
2. 既定ではローカル API が `http://127.0.0.1:50021` で待ち受けます(`VOICEVOX_URL` で変更可)。
3. 話者 ID は既定で `3`(ずんだもん ノーマル)。他の話者を使う場合は `GET http://127.0.0.1:50021/speakers` で ID を調べて `VOICEVOX_SPEAKER_ID` に設定します。

> VOICEVOX を起動していないと発話系のモード(Live / GameCommentary / Chat の発話あり)は合成時にエラーになります。

### 2.3 VB-CABLE(仮想オーディオデバイス)

合成音声を PuruPuruPNGTuber に渡すため、仮想オーディオデバイスを使います。

1. [VB-CABLE](https://vb-audio.com/Cable/)(または VoiceMeeter など)をインストールします。
2. アプリ側は出力先デバイス名を**部分一致**で選びます。既定は `CABLE Input`。
3. VoiceMeeter など別の仮想デバイスを使う場合は、`VOICEVOX_OUTPUT_DEVICE` またはコマンドライン `--device` でデバイス名の一部を指定します。

### 2.4 PuruPuruPNGTuber

1. PuruPuruPNGTuber を起動します。
2. マイク入力を仮想オーディオデバイスの**出力側**(VB-CABLE なら `CABLE Output`)に設定します。
3. AITuber が VOICEVOX 音声を `CABLE Input` に流すと、`CABLE Output` 経由で PuruPuruPNGTuber がそれを拾い、口パクします。

### 2.5 OBS

PuruPuruPNGTuber のウィンドウ(アバター)を OBS のソースとして取り込み、配信します。この連携は OBS 側の設定であり、本ツールは関与しません。

### 2.6 Setup(初期セットアップ UI / 任意)

LLM API キー / VOICEVOX 設定 / X 認証情報を GUI から一括設定できる WPF アプリです(Windows 専用)。

```powershell
dotnet run --project Setup
```

- LLM / VOICEVOX / X の 3 タブがあり、タブごとに「保存しない(スキップ)」を選べます。
- 音声出力デバイスは利用可能なデバイス一覧から選択できます。
- 保存すると **ユーザー環境変数**(`EnvironmentVariableTarget.User`)に書き込まれます。すでに開いている PowerShell / CMD セッションには反映されないため、**新しいターミナルを開き直してから**各モードを起動してください。
- X 認証情報を保存すると、環境変数(`X_API_*` / `X_CONSUMER_*` 両系統)に加えて `PostX/.env` も更新されます。

---

## 3. 環境変数一覧

値は `AiTuber.Core/AppConfig.cs` の `LoadFromEnvironment()` から転記しています。**未設定の項目は既定値のまま**動作します。

### 3.1 LLM

| 環境変数 | 既定値 | 必須/任意 | 意味 |
|---|---|---|---|
| `LLM_PROVIDER` | `claude` | 任意 | 使用する LLM。`claude` / `gemini` / `openai` |
| `ANTHROPIC_API_KEY` | (空) | Claude 使用時必須 | Claude の API キー |
| `CLAUDE_MODEL` | `claude-sonnet-4-6` | 任意 | Claude のモデル名 |
| `GEMINI_API_KEY` | (空) | Gemini 使用時必須 | Gemini の API キー |
| `GEMINI_MODEL` | `gemini-2.5-flash` | 任意 | Gemini のモデル名 |
| `OPENAI_API_KEY` | (空) | OpenAI 使用時必須 | OpenAI の API キー |
| `OPENAI_MODEL` | `gpt-4o` | 任意 | OpenAI のモデル名 |

> 必須なのは「選択中プロバイダのキー」だけです。`LLM_PROVIDER=claude` なら `ANTHROPIC_API_KEY` のみが必須。キー未設定で起動すると `環境変数 XXX が未設定です` と表示して終了します。

### 3.2 VOICEVOX / 音声

| 環境変数 | 既定値 | 必須/任意 | 意味 |
|---|---|---|---|
| `VOICEVOX_URL` | `http://127.0.0.1:50021` | 任意 | VOICEVOX API のエンドポイント |
| `VOICEVOX_SPEAKER_ID` | `3` | 任意 | 話者 ID(`GET /speakers` で確認) |
| `VOICEVOX_OUTPUT_DEVICE` | `CABLE Input` | 任意 | 音声の出力先デバイス名(部分一致) |
| `VOICEVOX_EMOTION_STYLES` | (空 → 既定マップ) | 任意 | 感情タグ → スタイル ID の上書き。書式は「5. 感情タグ」参照 |

### 3.3 YouTube Live

| 環境変数 | 既定値 | 必須/任意 | 意味 |
|---|---|---|---|
| `YT_VIDEO_ID` | (空) | 任意 | YouTube の videoId(`--youtube` 未指定時のフォールバック) |
| `YOUTUBE_API_KEY` | (空) | YouTube モード時必須 | YouTube Data API v3 の API キー |

### 3.4 Twitter(X)

| 環境変数 | 既定値 | 必須/任意 | 意味 |
|---|---|---|---|
| `TWEET_DRY_RUN` | `1`(dry-run) | 任意 | `1` または未設定なら dry-run(投稿しない)。`0` で実投稿 |
| `X_API_KEY` | (空) | 実投稿時必須 | X API キー(Consumer Key) |
| `X_API_SECRET` | (空) | 実投稿時必須 | X API シークレット(Consumer Secret) |
| `X_ACCESS_TOKEN` | (空) | 実投稿時必須 | X アクセストークン |
| `X_ACCESS_SECRET` | (空) | 任意 | X アクセスシークレット(TwitterBot の X クライアントでは RefreshToken 相当として渡される) |

> **注意(名前の違い)**: `TwitterBot` は `X_API_KEY` / `X_API_SECRET` / `X_ACCESS_TOKEN` / `X_ACCESS_SECRET` を使います。一方 `PostX` は `.env` の `X_CONSUMER_KEY` / `X_CONSUMER_SECRET` / `X_ACCESS_TOKEN` / `X_ACCESS_TOKEN_SECRET` を使います。名前が異なる点に注意してください。Setup を使うと両系統に書き込まれるので混乱を避けられます。

### 3.5 その他

| 環境変数 | 既定値 | 必須/任意 | 意味 |
|---|---|---|---|
| `WINDOW_TITLE_FRAGMENT` | (空) | 任意 | ゲーム実況の対象ウィンドウタイトル(部分一致) |
| `CHARACTER_NAME` | `ぽとふ` | 任意 | コンソールログに表示するキャラ名(人格そのものではない。人格は `prompts/character.md`) |

### 3.6 設定例(PowerShell)

現在のセッションだけに設定する場合(`$env:` 形式):

```powershell
$env:LLM_PROVIDER = "claude"
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:VOICEVOX_OUTPUT_DEVICE = "CABLE Input"
$env:TWEET_DRY_RUN = "1"          # dry-run(既定)
$env:YOUTUBE_API_KEY = "AIza..."  # YouTube Live を使うとき
$env:WINDOW_TITLE_FRAGMENT = "VALORANT"
```

恒久的にユーザー環境変数へ設定する場合(次回起動時から有効):

```powershell
[Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...", "User")
```

または Setup(3.7 節)を使ってください。

---

## 4. 各モードの実行方法

すべてリポジトリのルートから `dotnet run --project <プロジェクト>` で起動します。`--` 以降がアプリへの引数です。

### 4.1 Chat — LLM 疎通確認・発話テスト

```powershell
dotnet run --project Chat
```

対話式で以下を順に尋ねられます。

1. 使用する LLM(1: Gemini / 2: OpenAI / 3: Claude)
2. API キー(直接入力。環境変数ではなくこの場で入力)
3. システムプロンプト(空でも可)
4. VOICEVOX で発話するか(`y` / `N`)。`y` の場合は出力デバイスを一覧から選択

以降、質問を入力するたびに LLM が回答し、発話有効時は VOICEVOX で読み上げます。`exit` で終了。
発話を有効にしていて出力デバイスの初期化に失敗した場合は、その場で終了します。

### 4.2 Live — 配信本体

```powershell
# コンソール入力を視聴者コメントに見立ててローカルテスト
dotnet run --project Live -- --console

# LLM を切り替える
dotnet run --project Live -- --console --provider gemini
dotnet run --project Live -- --console --provider openai

# YouTube Live のコメントを取得(videoId または URL)
dotnet run --project Live -- --youtube dQw4w9WgXcQ
dotnet run --project Live -- --youtube "https://www.youtube.com/watch?v=dQw4w9WgXcQ"

# 出力デバイス関連
dotnet run --project Live -- --list-devices                    # デバイス一覧を表示して終了
dotnet run --project Live -- --console --device "VoiceMeeter"  # デバイス名(部分一致)を指定
dotnet run --project Live -- --console --select-device         # 起動時に対話で選ぶ

# 旧経路(ストリーミングせず一括生成してから発話)
dotnet run --project Live -- --console --no-stream
```

**オプション一覧**

| オプション | 意味 |
|---|---|
| `--console` | コンソール入力をコメント化するローカルテストモード |
| `--youtube <videoId\|URL>` | YouTube Live のコメントを取得。URL は watch / youtu.be / live / embed / shorts 形式に対応 |
| `--provider <claude\|gemini\|openai>` | LLM プロバイダを上書き(`LLM_PROVIDER` より優先) |
| `--device <名前>` | 出力デバイスを部分一致で指定(`VOICEVOX_OUTPUT_DEVICE` より優先) |
| `--select-device` | 起動時にデバイスを対話選択 |
| `--list-devices` | 利用可能な出力デバイス一覧を表示して終了 |
| `--no-stream` | 文単位ストリーミングを使わず、一括生成 → 合成 → 再生の旧経路にする |

**videoId の解決順**: `--youtube` の値 → 位置引数 → 環境変数 `YT_VIDEO_ID`。`--console` も `--youtube` も無く videoId も無い場合は、使い方を表示して終了します。YouTube モードでは `YOUTUBE_API_KEY` が必須です。

**動作**(architecture.md の動作仕様に準拠):

- 4 秒ごと(`CommentBatchSec`)にコメントをまとめて拾い、優先度(初見・質問優先)で最大 5 件を選んで LLM に渡します。
- 直近 12 ターン(`HistoryTurns`)の会話履歴を渡します。
- 45 秒(`FreetalkAfterSec`)コメントが無ければ自動でフリートークします。
- 既定ではストリーミング(トークンを文単位に分割し、確定した文から順に合成・再生する 2 キュー TTS)で発話します。文ごとに感情タグを解釈しスタイルを切り替えます。
- 禁止ワードに触れた応答は破棄し、そのターンの会話履歴も破棄して次へ進みます(`[skip]` とログ表示)。
- **終了**: `Ctrl+C` を押すと即座には止まらず、ループを抜けてからその配信の発話ログ末尾 50 件を要約し、`data/memory.json` の配信メモに保存します(`配信メモ保存: ...` と表示)。

コンソールモードでは、入力したテキストは著者名「テスト太郎」のコメントとして扱われます。

### 4.3 TwitterBot — 自律ツイート投稿

```powershell
# 常駐してスケジュール投稿(既定は dry-run)
dotnet run --project TwitterBot

# 1回だけ生成・投稿して終了(動作確認向け)
dotnet run --project TwitterBot -- --once

# LLM を切り替える
dotnet run --project TwitterBot -- --provider gemini
```

**オプション一覧**

| オプション | 意味 |
|---|---|
| `--once` | 1 回だけ生成・投稿して終了 |
| `--provider <claude\|gemini\|openai>` | LLM プロバイダを上書き |

**dry-run と本番化**

- 既定は **dry-run**(`TWEET_DRY_RUN` が未設定または `1`)。実際には投稿せず `[DRY RUN] 投稿内容: ...` とコンソール表示するだけです。
- 本番投稿するには `TWEET_DRY_RUN=0` を設定し、かつ `X_API_KEY` / `X_API_SECRET` / `X_ACCESS_TOKEN` を設定します。これらが欠けていると `実投稿には X_API_KEY / X_API_SECRET / X_ACCESS_TOKEN が必要です。` と表示して終了します。

```powershell
$env:TWEET_DRY_RUN = "0"
$env:X_API_KEY = "..."
$env:X_API_SECRET = "..."
$env:X_ACCESS_TOKEN = "..."
dotnet run --project TwitterBot -- --once
```

**投稿の仕様**(AppConfig / TweetGenerator / TweetScheduler)

- **投稿時間帯**: 9 時以上 24 時未満(`TweetActiveHourStart`〜`TweetActiveHourEnd`)。時間外は `投稿時間外のためスキップ` と表示。
- **投稿間隔**: 180〜360 分(`TweetMinIntervalMin`〜`TweetMaxIntervalMin`)のランダム(両端含む)。常駐時は 1 回投稿するたびにこの分数だけ待機。
- **生成と検証**: LLM に `{"tweet": "...", "kind": "daily|announce|thanks|question"}` の JSON のみを出力させ、パース → 140 字以内(Unicode コードポイント数で計数、絵文字は 1 文字)→ 禁止ワードフィルタ → `data/memory.json` の直近ツイートとの完全重複、を検証します。失敗したら再生成(最大 3 回)。3 回とも失敗したら今回はスキップ(`生成に3回失敗したため今回はスキップ`)。
- コンテキストには現在日時・曜日・直近の配信メモ・直近ツイート最大 10 件が渡されます。
- 投稿(または dry-run)後、本文を `data/memory.json` の `recent_tweets` に記録(最大 20 件 = `RecentTweetsKeep`)して重複を防ぎます。
- `Ctrl+C` で待機ループを抜けて終了します。

### 4.4 GameCommentary — ゲーム実況

```powershell
# 対象ウィンドウをタイトルの一部で指定
dotnet run --project GameCommentary -- --window "VALORANT"

# 位置引数でも可
dotnet run --project GameCommentary -- "Minecraft"

# 発話せずコンソール出力のみ
dotnet run --project GameCommentary -- --window "VALORANT" --no-voice

# デバイス一覧を表示して終了
dotnet run --project GameCommentary -- --list-devices

# LLM 切り替え
dotnet run --project GameCommentary -- --window "VALORANT" --provider gemini
```

**オプション一覧**

| オプション | 意味 |
|---|---|
| `--window <タイトルの一部>` | 実況対象ウィンドウをタイトル部分一致で指定 |
| `--no-voice` | 発話せずコンソール出力のみ |
| `--provider <claude\|gemini\|openai>` | LLM プロバイダを上書き |
| `--device <名前>` | 出力デバイスを部分一致で指定 |
| `--select-device` | 起動時にデバイスを対話選択 |
| `--list-devices` | 出力デバイス一覧を表示して終了 |

**ウィンドウの解決順**: `--window` → 位置引数 → 環境変数 `WINDOW_TITLE_FRAGMENT`。いずれも無ければ使い方を表示して終了します。

**動作**

- 12 秒間隔(`CaptureIntervalSec`)で対象ウィンドウをキャプチャ(Win32 `PrintWindow`)。処理にかかった時間を差し引いて間隔を保ちます。
- 画像は幅 800px(`MaxImageWidth`)にリサイズし、JPEG 品質 80 でエンコードして LLM の Vision に渡します。
- 直近 4 件(`CommentaryHistoryLimit`)の実況を文脈として渡し、繰り返しを防ぎます。
- Vision に対応した LLM とモデルが必要です(Claude なら既定モデルで対応)。
- `Ctrl+C` で終了します。

---

## 5. 感情タグとスタイル切替

配信・ゲーム実況の音声モードでは、キャラクターが発話の**先頭に感情タグを 1 つ**付けます(`prompts/character.md` で定義)。タグは読み上げられず、VOICEVOX のスタイル(声色)切り替えに使われます。本文はタグの後に続きます。

### タグ一覧と既定スタイル ID

`AppConfig.EmotionStyleIds` の既定マッピング(speaker 3 = ずんだもん のスタイルを想定):

| タグ | 意味 | 既定スタイル ID | スタイル名(想定) |
|---|---|---|---|
| `[fun]` | 楽しい・ふつうのテンション(基本) | 3 | ノーマル |
| `[joy]` | すごく嬉しい・はしゃいでいる | 1 | あまあま |
| `[sad]` | しんみり・ちょっと寂しい | 22 | ささやき |
| `[angry]` | むむっとした・すねている | 7 | ツンツン |
| `[surprised]` | びっくり・意外 | 3 | ノーマル |

タグが無い、または未知のタグの場合は `VOICEVOX_SPEAKER_ID`(既定 3)にフォールバックします。

例: `[joy]えへへ、それめっちゃ嬉しい!` → スタイル ID 1 で「えへへ、それめっちゃ嬉しい!」を合成。

### スタイル ID の上書き

`VOICEVOX_EMOTION_STYLES` 環境変数で、`タグ=スタイルID` をカンマ区切りで指定して上書きできます。

```powershell
$env:VOICEVOX_EMOTION_STYLES = "joy=1,sad=22,angry=7"
```

- 書式は `joy=1,sad=22` のように `タグ=整数ID`。空白やタグ名の大小文字は許容されます。
- 指定した組だけでなく**マップ全体が置き換わります**(未指定タグは既定マップではなく、その環境変数に含めたものだけになります)。使うタグはまとめて列挙してください。
- 空文字または有効な組が 1 つも無い場合は既定マップが使われます。

> 別の話者を使う場合は、`GET http://127.0.0.1:50021/speakers` でその話者のスタイル ID を調べ、`VOICEVOX_SPEAKER_ID` と `VOICEVOX_EMOTION_STYLES` を合わせて設定してください。

---

## 6. キャラクターのカスタマイズ

キャラクターの人格・口調はすべて `prompts/` 配下の Markdown が唯一の設定元です。**C# コードを変更する必要はありません**(人格をコードにハードコードしない設計)。

| ファイル | 役割 |
|---|---|
| `prompts/character.md` | **共通人格 = キャラの魂**(名前・一人称・口調・性格・生活背景・感情タグ規約・禁止事項)。すべてのモードで読み込まれる唯一の真実 |
| `prompts/live_system.md` | 配信モード専用の指示(2〜3 文で返す、記号を使わない、複数コメントから 1 つ選ぶ、感情タグを先頭に付ける等) |
| `prompts/tweet_system.md` | ツイートモード専用の指示(生活感の出し方、140 字以内、JSON 形式で出力する等) |
| `prompts/game_system.md` | ゲーム実況モード専用の指示(画面を見て 1〜2 文で実況、繰り返し回避等) |

各モードは「character.md + そのモードの system md」を結合してシステムプロンプトにします。口調や設定を変えたいときは `character.md` を、モードごとの振る舞いを変えたいときは各 system md を編集してください。

`CHARACTER_NAME` 環境変数はコンソールログの表示名を変えるだけで、人格には影響しません。

---

## 7. データファイル

### data/memory.json

配信と TwitterBot が共有する簡易メモリです。`DataDir`(既定 `data`)配下に自動生成されます。日本語はエスケープされずそのまま保存されます。

```json
{
  "stream_notes": [
    { "date": "2026-07-11 22:30", "summary": "今日は新しいスープゲームに挑戦した話をした。" }
  ],
  "recent_tweets": [
    "スープ作りすぎて鍋が渋滞してる。えへへ。"
  ]
}
```

| キー | 書き込み側 | 読み取り側 | 内容 |
|---|---|---|---|
| `stream_notes` | Live(配信終了時) | TwitterBot | 配信内容の要約。最大 10 件保持 |
| `recent_tweets` | TwitterBot(投稿後) | TwitterBot | 投稿済みツイート本文(新しい順)。最大 20 件保持。重複投稿の回避に使う |

TwitterBot は配信メモを読んで「配信お礼 / 次回予告」ツイートに活用し、直近ツイートを読んで重複を避けます。手で編集しても構いませんが、有効な JSON を保ってください。

---

## 8. トラブルシューティング

実装済みのエラーメッセージに基づく代表的な事象と対処です。

| 事象 / メッセージ | 原因 | 対処 |
|---|---|---|
| `環境変数 ANTHROPIC_API_KEY が未設定です (LLM_PROVIDER=claude)` | 選択中プロバイダの API キーが未設定 | 該当キーを環境変数に設定(3.1 節)。プロバイダを変えるなら `--provider` か `LLM_PROVIDER` |
| `未知の LLM プロバイダです: xxx` | `--provider` / `LLM_PROVIDER` が claude/gemini/openai 以外 | 3 つのいずれかを指定 |
| VOICEVOX 合成でエラー / 接続できない | VOICEVOX 未起動、または `VOICEVOX_URL` が違う | VOICEVOX を起動(`http://127.0.0.1:50021`)。URL を確認 |
| `'CABLE Input' を含む出力デバイスが見つかりません。 利用可能: [...]` | 指定デバイス名に一致する再生デバイスが無い | 例外に列挙されるデバイス名から正しい部分文字列を `--device` / `VOICEVOX_OUTPUT_DEVICE` に指定。VB-CABLE 未インストールなら導入 |
| Live で `'xxx' を含む出力デバイスが見つかりません。デバイスを選択してください。` | 同上(Live/GameCommentary は対話ピッカーにフォールバック) | 一覧から番号を入力。`--list-devices` で事前確認も可 |
| `デバイスが選択されなかったため中止します。` | 対話ピッカーで有効な選択をしなかった | 番号を入力するか、既定デバイスが存在する状態にする |
| `YT_VIDEO_ID が未設定です。--console でローカルテスト、または --youtube <videoId\|URL> を指定してください。` | Live を非コンソール・非 YouTube で起動 | `--console` か `--youtube <videoId|URL>` を付ける |
| `環境変数 YOUTUBE_API_KEY が未設定です` | YouTube モードだが API キー未設定 | YouTube Data API v3 のキーを `YOUTUBE_API_KEY` に設定 |
| `この動画はライブ配信ではありません (videoId=...)` | 通常動画を指定 | ライブ配信中の枠の videoId を指定 |
| `アクティブなライブチャットがありません (videoId=...)` | 配信未開始 or チャット無効 | 配信を開始し、ライブチャットを有効化 |
| `動画が見つかりません (videoId=...)` | videoId が誤り | videoId / URL を確認 |
| `[youtube] コメント取得エラー: ...(5秒後に再試行)` | 一時的な HTTP エラー等 | 自動再試行されるので基本は待つ。頻発するなら API キー・クォータを確認 |
| Live で `[skip] ...` / `[skip] フィルタに掛かった応答: ...` | 応答が禁止ワードに該当 | 仕様どおり応答とそのターン履歴を破棄して継続。対処不要 |
| `'xxx' を含むウィンドウが見つかりません。 現在開いているウィンドウ一覧: [...]` | 実況対象ウィンドウのタイトルが不一致 | 一覧の文字列に合わせて `--window` / `WINDOW_TITLE_FRAGMENT` を修正。対象アプリを起動しておく |
| `対象ウィンドウのサイズが不正です (...)。最小化されていませんか?` | 対象ウィンドウが最小化 | ウィンドウを表示状態にする |
| TwitterBot `実投稿には X_API_KEY / X_API_SECRET / X_ACCESS_TOKEN が必要です。` | `TWEET_DRY_RUN=0` だが X キー不足 | 3 つを設定するか、`TWEET_DRY_RUN=1`(dry-run)に戻す |
| TwitterBot `生成に3回失敗したため今回はスキップ` | JSON パース・140 字・フィルタ・重複の検証に 3 回連続で失敗 | 一時的なら次サイクルで回復。頻発するなら `tweet_system.md` や禁止ワードを見直す |
| Setup 保存後も各モードに反映されない | ユーザー環境変数は既存セッションに反映されない | ターミナルを開き直してから起動 |

---

## 9. ビルドとテスト

リポジトリのルートから、全プロジェクトをまとめてビルド・テストできます(`AiTuber.sln` が全プロジェクトを束ねています)。

```powershell
dotnet build
dotnet test
```

テストプロジェクトは `AiTuber.Core.Tests` / `Live.Tests` / `MultiLLMClient.Tests` / `Voicevox.Tests` の 4 つ(xUnit)です。

---

## 10. 未実施の手動テスト

自動テストでは検証できない、実環境が必要な項目です。運用開始前に手動で確認してください。

- **YouTube 実配信枠での動作テスト(未実施)**
  - 実際の YouTube Live 枠(限定公開でも可)を立て、`dotnet run --project Live -- --youtube <videoId|URL>` で接続。
  - 接続時に `YouTube Live に接続しました (videoId=...)` が表示されること。
  - **接続後に**投稿されたチャットのみが拾われること(接続前の既存コメントは流れない仕様)。
  - スーパーチャットなど `displayMessage` を持たないメッセージがスキップされること。
  - `pollingIntervalMillis` に従ってポーリングし、クォータ(1 日 10,000 ユニット)を圧迫しないこと。
  - `YOUTUBE_API_KEY` のクォータ・権限が十分か、長時間配信で安定するか。
- **VB-CABLE → PuruPuruPNGTuber → OBS の口パク一連**: 実機で音声がアバターの口パクに反映されるか(Phase A の完了条件)。
- **X 本番投稿(`TWEET_DRY_RUN=0`)**: 実アカウントへの投稿成功と、`data/memory.json` への記録・重複回避。
- **ゲーム実況の実機キャプチャ**: 実際のゲームウィンドウで `PrintWindow` が中身を取得できるか(フルスクリーン/DWM 合成アプリでの挙動確認)。

---

## 参考

- 設計と動作仕様: [architecture.md](architecture.md)
- タスクと進捗: [implementation-plan.md](implementation-plan.md)
- 動作仕様の元となった Python 実装: `reference/python_v2/`
