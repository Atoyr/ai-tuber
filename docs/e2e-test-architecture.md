# 配信 E2E テストと画面表示の AI 検証 設計

「実際に AITuber として配信できる」状態に到達するための最終テスト計画と、
その中の**画面表示・音声出力の確認を AI(Claude Code)が実行できる仕組み(StageCheck)**の設計。

## 背景と課題

パイプラインの前段(コメント取得 → LLM → フィルタ → VOICEVOX 合成)はユニットテストと
dry-run で検証済み。一方、最終段は実機でしか確認できず、これまで人の目と耳が前提だった:

```
VOICEVOX → VB-CABLE ("CABLE Input")          ← 音が本当に流れたか(耳)
        → PuruPuruPNGTuber(口パク)          ← 口が動いているか(目)
        → OBS(シーン合成 → 配信)            ← レイアウトが正しいか(目)
```

**Claude Code は画像ファイルを Read で直接読める**。したがって
「実機の画面をファイルに落とし、機械判定できるメトリクスを添えるツール」が1つあれば、
この最終段の確認を AI が代行できる。それが StageCheck。

## StageCheck(実機検証ツール)

### 位置づけ

- プロジェクト: `StageCheck/`(console、`net10.0-windows`、名前空間 `Medoz.StageCheck`)
- 依存: `GameCommentary`(`WindowCapture` を再利用)、`Voicevox`(`VoicevoxClient` / `AudioPlayer`)、NAudio
- **外部投稿・配信開始は一切しない**(ローカルのキャプチャ・録音・合成のみ。dry-run 原則の心配が無い)
- 出力先: `artifacts/stagecheck/<yyyyMMdd-HHmmss>/` に JPEG フレームと `report.json`。
  スクリーンショットにはデスクトップの個人情報が写り込みうるため **`artifacts/` は .gitignore に入れる**

### コマンド

| コマンド | 動作 |
|---|---|
| `--list-windows` | 可視ウィンドウのタイトル一覧を表示(`WindowCapture.GetVisibleWindowTitles` をそのまま公開) |
| `--snapshot "<タイトル片>" [...]` | 指定ウィンドウ(複数可・部分一致)を1枚ずつキャプチャして保存 |
| `--screen` | デスクトップ全体をキャプチャ(`PrintWindow` が黒画像になる GPU 系アプリのフォールバック) |
| `--lipsync "<タイトル片>" [--text "こんにちは"]` | 口パク検証。下記参照 |
| `--audio [--device "CABLE Input"] [--text "..."]` | 音声経路検証。下記参照 |

### `--lipsync` — 口パクの自動検証

人が「喋らせて口が動くのを見る」行為をそのまま自動化する:

1. 対象ウィンドウ(PuruPuruPNGTuber)を**無音状態**で N フレーム(例: 10 枚 / 200ms 間隔)キャプチャ → ベースライン
2. `--text` を VOICEVOX で合成し `CABLE Input` へ再生**しながら** N フレームキャプチャ
3. 連続フレーム間の差分メトリクスを算出し、`無音時` と `発話時` を比較
4. 全フレーム + `report.json` を保存。**発話時の平均差分 > 無音時の平均差分 × 3 かつ絶対下限以上**なら exit code 0、そうでなければ 1

差分計算は pure static クラス `MotionMetrics` に分離してユニットテスト対象にする
(`TwitchCommentSource.ParseLine` と同じ方針):

```csharp
public static class MotionMetrics
{
    // JPEG → グレースケール縮小(幅64程度)は呼び出し側で行い、輝度配列を渡す
    public static double MeanAbsDiff(byte[] grayA, byte[] grayB);
    // silent/speaking 各フレーム列の平均差分から合否を返す
    public static LipsyncVerdict Judge(IReadOnlyList<double> silentDiffs,
                                       IReadOnlyList<double> speakingDiffs,
                                       double ratioThreshold = 3.0, double absFloor = 1.0);
}
```

### `--audio` — 音声経路の自動検証

「音が本当に VB-CABLE に流れたか」を耳の代わりに数値で確認する:

1. NAudio の `WasapiLoopbackCapture` で対象再生デバイス(既定 `CABLE Input`)のループバック録音を開始
2. `--text` を VOICEVOX で合成して同デバイスへ再生
3. 再生中の RMS を測定し、無音時ノイズフロアと比較して report.json に記録。閾値未満なら exit code 1

これで「VOICEVOX が合成した」だけでなく「OS の再生デバイスまで音が届いた」ことまで検証できる。
PuruPuruPNGTuber 側のマイク入力(CABLE Output)設定が正しいかは `--lipsync` が兼ねる
(音が届いていても口が動かなければ設定ミス)。

### report.json(AI が読む契約)

```json
{
  "mode": "lipsync",
  "window": "PuruPuruPNGTuber",
  "text": "こんにちは、テスト中です",
  "silentMeanDiff": 0.42,
  "speakingMeanDiff": 6.81,
  "ratio": 16.2,
  "pass": true,
  "frames": {
    "silent": ["silent-01.jpg", "..."],
    "speaking": ["speaking-01.jpg", "..."]
  }
}
```

### AI(Claude Code)による検証ワークフロー

Claude Code に「画面表示をテストして」と頼んだときの標準手順:

1. `dotnet run --project StageCheck -- --list-windows` で対象ウィンドウの存在確認
2. `dotnet run --project StageCheck -- --audio` → report.json を読み、音声経路 OK を確認
3. `dotnet run --project StageCheck -- --lipsync "PuruPuru"` → report.json の `pass` と数値を確認
4. 保存された JPEG を Read で開き、**目視相当の確認**を行う:
   - アバターが映っているか(真っ黒・素材欠け・ウィンドウ枠だけになっていないか)
   - silent と speaking のフレームで口の形が実際に違うか(数値の裏取り)
5. `--snapshot "OBS"` で OBS ウィンドウを取り、配信レイアウトを確認:
   - アバターがシーン内に収まっているか(見切れ・はみ出し)
   - 意図しないソース(デスクトップ通知、個人情報の写り込み)が無いか
6. 結果を合否 + 根拠(数値と、どのフレームで何を確認したか)で報告する

数値判定(3・4 の pass)が一次判定、画像の目視確認が二次判定。数値が通っても
「アバターの代わりにエラーダイアログが揺れていた」ようなケースは画像側で弾く。

### 将来拡張(v1 ではやらない)

- **obs-websocket(v5、OBS 28+ 標準搭載)対応**: `GetSourceScreenshot` で配信合成後の絵を直接取得
  (フルスクリーンゲームで `PrintWindow` が使えない場合の決定打)。`StartStream`/`StopStream` で
  リハーサル配信の開始・停止まで自動化できる
- `--judge` オプション: 既存 `IChatClient.GenerateWithImageAsync` でスクショを LLM に判定させ、
  Claude Code を介さない無人セルフチェックにする(定期実行・起動前チェック向け)
- 感情タグ検証: `[joy]` 等を含むテキストで `--lipsync` を回し、スタイル切替時も音が途切れないこと

## 最終テスト計画(配信リハーサル)

実配信までを独立に確認できる 5 段階に分ける。各段は前段の合格が前提。
Stage 3 で [manual.md「未実施の手動テスト」](manual.md) と Phase E の残項目を消化する。

| Stage | 内容 | 合格条件 | AI 検証 |
|---|---|---|---|
| 0. セルフチェック | `dotnet build` / `dotnet test`、`--list-devices` に CABLE Input、VOICEVOX `/version` 応答、`PERSONA_DIR` ロード成功 | 全部エラー無し | Claude Code が全コマンド実行・確認可 |
| 1. 音声・口パク単体 | StageCheck `--audio` → `--lipsync` | 両方 pass | **全自動**(本設計の主目的) |
| 2. ローカル フルループ | `Live -- --console` でコメント→応答→発話。並行して StageCheck `--snapshot` | 応答が発話され口パクする。Ctrl+C で配信メモが `data/<slug>/memory.json` に保存される | Claude Code がコメント入力・スクショ確認・memory.json 確認まで可 |
| 3. プラットフォーム接続リハーサル | (a) 自分の Twitch チャンネルに `--twitch` で接続しチャット取得 (b) YouTube 限定公開枠に `--youtube` で接続 (c) OBS から Twitch 帯域テストモード(stream key に `?bandwidthtest=true`)or YouTube 限定公開へ**実際に配信** | コメントが拾われ応答する。視聴側で映像・音声・口パクが確認できる | コメント取得ログの確認と、視聴側ブラウザを `--screen` でキャプチャして画面確認まで可。配信枠の作成・開始は人が行う |
| 4. 本番初配信 | 告知なしの短時間枠で通し。終了時の配信メモ保存 → 翌日 TwitterBot(dry-run)が配信メモを反映したツイートを生成することまで確認 | 事故なく 30 分完走 | 配信中の定点 `--snapshot` と事後の memory.json / dry-run 出力確認 |

### Stage 3〜4 のチェックリスト(人+AI)

- [ ] Twitch: `[twitch] #<channel> に参加しました` 表示、著者名付きコメント取得、PING/PONG で長時間切断なし
- [ ] YouTube: 接続後のチャットのみ拾う、`pollingIntervalMillis` 準拠、クォータ消費が想定内
- [ ] 遅延: コメント投稿 → 発話開始までの体感遅延を記録(ストリーミング経路の効果測定)
- [ ] フィルタ: わざと禁止ワードを含むコメントを投げ、`[skip]` で握りつぶされること
- [ ] 配信終了: Ctrl+C → 配信メモ保存 → TwitterBot dry-run に反映
- [ ] OBS: 音ズレ・口パク遅延が視聴側で許容範囲か(ここだけは最終的に人の感覚で判断)

## 残タスクとの関係

- 実装タスクは [implementation-plan.md](implementation-plan.md) の **Phase L(StageCheck)** と
  **Phase M(配信リハーサル)** に起こす
- Phase E の「YouTube / Twitch 実配信での動作テスト」は Stage 3 と同一(二重管理しない。Phase M 側で消化したら E にも [x] を付ける)
- ペルソナ外部化の残り(Phase K)・ブログ公開(Phase I)・ラズパイ配置(Phase J)は配信テストとは独立に進められるが、
  **Stage 4 は Phase K 完了後(= ぽとふリポジトリを PERSONA_DIR で指した状態)で行う**のが望ましい
