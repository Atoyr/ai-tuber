# Studio 使い方ガイド(配信コントロールパネル)

配信中に使うローカル Web UI です。ブラウザから **会話ログの閲覧・テストコメントの注入・
設定変更・配信の開始/停止・VOICEVOX / PuruPuruPNGTuber の起動** を行えます。
設計の詳細は [studio-architecture.md](studio-architecture.md) を参照してください。

- 待ち受けは `http://127.0.0.1:5100` 固定(**localhost のみ**。認証なし・LAN には公開されません)
- 外部投稿(X・ブログ)は行いません。発話・キャプチャ・プロセス起動のみです
- APIキーの表示・編集はできません(配信中に画面共有しても映らないようにするため。
  変更は Setup ツールで行います)

## 事前準備

1. APIキー(`ANTHROPIC_API_KEY` など使用する LLM のもの)をユーザー環境変数に設定する
   (`dotnet run --project Setup` の GUI が便利)
2. UI から VOICEVOX / PuruPuruPNGTuber を起動したい場合は、exe パスを環境変数に設定する:

| 環境変数 | 例 | 備考 |
|---|---|---|
| `VOICEVOX_EXE_PATH` | `C:\Users\you\AppData\Local\Programs\VOICEVOX\vv-engine\run.exe` | エンジン単体 (`vv-engine/run.exe`) 推奨。エディタ exe でも可 |
| `PURUPURU_EXE_PATH` | `C:\Tools\PuruPuruPNGTuber\PuruPuruPNGTuber.exe` | |
| `STUDIO_PORT` | `5100`(既定) | ポートを変えたい場合のみ |

未設定でもヘッダのバッジが「パス未設定」になるだけで、**手動で起動済みのアプリはそのまま使えます**
(外部起動として自動検出されます)。

> **注意**: 環境変数はプロセス起動時に読み込まれます。Setup で保存した直後は、
> **新しく開いたターミナル**から Studio を起動してください。シークレット欄が「未設定」と
> 表示される場合は、Studio を起動したターミナルからその環境変数が見えていません。

## 起動

```
dotnet run --project Studio            # http://127.0.0.1:5100 をブラウザで開く
dotnet run --project Studio -- --open  # 既定ブラウザを自動で開く
```

終了は `Ctrl+C`。配信セッションが動いていれば、停止して配信メモを保存してから終了します。

## 画面の見方

```
┌──────────────────────────────────────────────────────┐
│ ヘッダ: ペルソナ名 | [VOICEVOX●] [PuruPuru●] [Live●] | SSE接続状態 │
├──────────────────────────────┬───────────────────────┤
│ 会話ログ (タイムライン)        │ 起動パネル / 配信パネル / 設定パネル │
│ コメント注入フォーム           │                       │
└──────────────────────────────┴───────────────────────┘
```

- **状態バッジの色**: 緑=Running / 青緑=RunningExternal(手動起動を検出。Studio からは停止不可)/
  黄=Starting / 灰=Stopped・NotConfigured(パス未設定)/ 赤=Faulted
- **会話ログ**: 視聴者コメント・発話・[skip](フィルタ違反)・フリートーク・配信メモ・システムログを
  色分け表示。自動スクロールは上にスクロールすると一時停止し、「最新へ」で再開。
  ページをリロードしても直近200件が再表示されます(ファイルには永続化されません)

## 典型的な配信フロー

1. **[まとめて起動]** — VOICEVOX 起動 → 応答待ち → PuruPuru 起動(すでに起動済みのものはスキップ)
2. 配信パネルで **ソースを選択**して **[配信開始]**
   - `manual` — コメント注入フォームからのみコメントが入る(ローカルテスト用)
   - `twitch` — target にチャンネル名 / URL(APIキー不要)
   - `youtube` — target に videoId / URL(要 `YOUTUBE_API_KEY`)
   - どのソースでもコメント注入フォームは併用できます(配信中のテストコメント差し込み)
3. コメントに応答して発話し、会話ログに流れます。45秒(変更可)コメントが無ければフリートーク
4. **[配信停止]** — Ctrl+C 相当。配信メモを要約して `data/<ペルソナslug>/memory.json` に保存してから停止

起動失敗(APIキー未設定・ペルソナ不足・出力デバイス無しなど)は、原因がそのまま
エラーメッセージとして会話ログに表示されます。

## 設定パネル

| 反映タイミング | 項目 |
|---|---|
| **即時反映**(配信中に効く) | フリートーク秒数 / コメントバッチ秒数 / フリートークON/OFF / 話者ID / 一時停止 |
| **次回セッションから** | ソースと target / 出力デバイス / LLMプロバイダ / LLMモデル / PERSONA_DIR |
| 表示のみ | シークレット(設定済み/未設定のバッジ。編集は Setup で) |

- **一時停止**: 応答生成そのものを止めます(コメントは溜まったままになり、再開時に処理されます)
- **LLM の切り替え**: LLMプロバイダに `claude` / `gemini` / `openai` を入力(モデルも指定可)。
  次の配信開始から適用。対応する APIキー環境変数(`GEMINI_API_KEY` 等)が必要です
- UI で変更した項目は `data/studio.json` に**その項目だけ**保存され、次回の Studio 起動でも引き継がれます。
  実効値の優先順位は `エンジンデフォルト < persona.json < 環境変数 < studio.json`
  (UI で触っていない項目は persona.json や環境変数の値がそのまま生きます)
- CLI(`dotnet run --project Live -- --console`)は studio.json を読みません(従来どおり)
- studio.json を消せば UI での変更をすべてリセットできます

## API から操作する(curl / AI 検証用)

UI と同じ操作を HTTP で行えます。Claude Code などによる配信状態の確認にも使えます。

```
curl http://127.0.0.1:5100/api/status                 # 各アプリ・配信の状態
curl http://127.0.0.1:5100/api/settings               # 実効設定 (シークレットは set/unset のみ)
curl http://127.0.0.1:5100/api/devices                # 出力デバイス一覧
curl http://127.0.0.1:5100/api/events                 # SSE (直近200件リプレイ + 追記)
curl -X POST http://127.0.0.1:5100/api/apps/start-all
curl -X POST http://127.0.0.1:5100/api/live/start -H "Content-Type: application/json" \
     -d "{\"source\":\"manual\"}"
curl -X POST http://127.0.0.1:5100/api/live/comment -H "Content-Type: application/json" \
     -d "{\"author\":\"テスト\",\"text\":\"こんにちは\"}"
curl -X PUT  http://127.0.0.1:5100/api/settings -H "Content-Type: application/json" \
     -d "{\"immediate\":{\"freetalkAfterSec\":60}}"
curl -X POST http://127.0.0.1:5100/api/live/stop      # 配信メモ保存まで行って停止
```

エラーは非2xx + `{"error":"メッセージ"}` で返ります(未配信時のコメント注入は 409 など)。

## トラブルシューティング

| 症状 | 原因と対処 |
|---|---|
| シークレットが「未設定」表示 | Studio を起動したターミナルから環境変数が見えていない。Setup で保存後、新しいターミナルから起動する |
| VOICEVOX / PuruPuru が「パス未設定」 | `VOICEVOX_EXE_PATH` / `PURUPURU_EXE_PATH` が未設定。手動起動でも可(自動検出される) |
| 停止ボタンが押せない | RunningExternal(手動起動)のアプリは Studio からは停止しない設計。手で閉じる |
| 配信開始が 400 エラー | メッセージどおり(APIキー未設定 / ペルソナのファイル不足 / 出力デバイスが見つからない等)。会話ログに原因が出る |
| VOICEVOX 起動が Faulted | `/version` 応答待ちが60秒でタイムアウト。エンジンの起動が重い場合は手動起動してから Studio を使う |
| 発話しない | 出力デバイス(既定 `CABLE Input`)の存在を確認。`GET /api/devices` または設定パネルのドロップダウンで一覧を確認できる |
