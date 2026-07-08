# AITuber v2 設計書

v1(テキスト入力 → Claude → VOICEVOX → VB-CABLE → PuruPuruPNGTuber)を拡張し、
**配信でのリアルタイム対話**と**Twitterへの自律投稿**に対応した構成。

## 全体アーキテクチャ

```
                    prompts/character.md(共通人格 = キャラの魂)
                          │
          ┌───────────────┴────────────────┐
          │                                │
  live_system.md                    tweet_system.md
  (配信モード指示)                    (投稿モード指示)
          │                                │
┌─────────▼──────────┐          ┌──────────▼─────────┐
│  live_aituber.py    │          │   twitter_bot.py    │
│                     │          │                     │
│ YouTube Liveコメント │          │ スケジューラ(常駐)    │
│   ↓ pytchat         │          │   ↓                 │
│ コメントキュー        │          │ コンテキスト組立      │
│   ↓                 │          │ (時刻/配信メモ/       │
│ Claude応答生成       │          │  直近ツイート)        │
│   ↓                 │          │   ↓                 │
│ VOICEVOX音声合成     │          │ Claudeツイート生成    │
│   ↓                 │          │   ↓ JSON            │
│ VB-CABLE再生        │          │ 検証(字数/フィルタ/    │
│   ↓                 │          │      重複)           │
│ PuruPuruPNGTuber    │          │   ↓                 │
│ 口パク → OBS配信     │          │ X API v2で投稿       │
└─────────┬──────────┘          └──────────▲─────────┘
          │      data/memory.json          │
          └───── 配信メモを書く/読む ────────┘
```

**設計の要点**
- キャラ人格は `prompts/character.md` の1ファイルに集約。配信の発話もツイートも同じ人格から生成されるため、キャラがブレない
- `data/memory.json` を介して配信とTwitterが連携。配信終了時に「今日の配信メモ」が自動保存され、Twitterボットがそれを読んで配信お礼・次回予告ツイートを作れる
- Twitterボットはデフォルト **DRY RUN**(実投稿しない)。動作を確認してから本番化する安全設計

## v1からの改善点

| 項目 | v1 | v2 |
|---|---|---|
| コメント入力 | コンソール手入力 | YouTube Liveから自動取得(pytchat) |
| 文脈 | 毎回単発 | 直近12ターンの会話履歴を保持 |
| 無言時間 | 黙る | 45秒コメントが無ければ自動フリートーク |
| 人格 | スクリプト内に1行 | 外部ファイル化した詳細キャラシート+禁止事項 |
| Twitter | なし | 自律投稿ボット(時間帯制限・重複回避・フィルタ付き) |
| 安全性 | なし | 禁止ワードフィルタ+再生成、DRY RUNモード |

## セットアップ(Windows)

### 共通
1. `pip install -r requirements.txt`
2. `prompts/character.md` を自分のキャラ設定に書き換える(名前・口調・好きなもの)
3. 環境変数 `ANTHROPIC_API_KEY` をセット

### 配信側
1. VOICEVOX起動(`http://127.0.0.1:50021`)、`config.py` の `SPEAKER_ID` をキャラに合わせる
2. VB-CABLEインストール、PuruPuruPNGTuber(`http://127.0.0.1:8223`)のマイクを「CABLE Output」に
3. まずローカルテスト: `python live_aituber.py --console`
4. 本番: YouTubeで配信枠を作り `set YT_VIDEO_ID=動画ID` → `python live_aituber.py`
5. OBSでPuruPuruPNGTuberのウィンドウとVB-CABLE音声をキャプチャして配信

### ゲーム実況側
1. `game_commentary.py` の `WINDOW_TITLE_FRAGMENT` を実際のゲームのウィンドウタイトルの一部に書き換える
2. `python game_commentary.py`(10〜15秒ごとにキャプチャして実況)

### Twitter側
1. [X Developer Portal](https://developer.x.com/) でアプリ作成、**Read and Write** 権限にして
   API Key / API Secret / Access Token / Access Secret の4つを取得
2. 環境変数 `X_API_KEY` `X_API_SECRET` `X_ACCESS_TOKEN` `X_ACCESS_SECRET` をセット
3. 動作確認(投稿されない): `python twitter_bot.py --once`
4. 出力に問題なければ `set TWEET_DRY_RUN=0` にして本番投稿
5. 常駐運用: `python twitter_bot.py`(3〜6時間ごとにランダム投稿、9〜24時のみ)

> 注意: X APIの無料プランは投稿数に月間上限があります(数百件程度)。
> `TWEET_MIN_INTERVAL_MIN` を短くしすぎないこと。

## 運用の流れ(1日の例)

1. 日中: `twitter_bot.py` が常駐し、日常ツイートを数回投稿
2. 配信前: 手動で `--once` を叩けば告知ツイートも出せる
3. 配信中: `live_aituber.py` がコメントに応答、暇なときはフリートーク
4. 配信終了(Ctrl+C): 配信内容の要約が `data/memory.json` に自動保存
5. 配信後: Twitterボットが配信メモを拾い、お礼ツイートを自然に混ぜる

## 今後の拡張候補(v3ロードマップ Phase 3〜)

- ストリーミングAPI+文単位分割で発話遅延を削減
- 合成キュー+再生キューの2キュー式TTSパイプライン
- 感情タグ出力 → VOICEVOXスタイル切替・PuruPuruPNGTuberの表情差分切替
- `comment_picker.py` によるコメント選択の優先度ロジック
- memory.jsonをSQLiteに置き換えて長期記憶化
