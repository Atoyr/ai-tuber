# TwitterBot の Linux (ラズパイ / 軽量VM) 常時運用設計

## 目的

X への自律投稿 (TwitterBot) を、配信用 Windows PC とは独立した常時稼働の
Raspberry Pi または軽量 Linux VM 上で動かし、ランダムな時間 (180〜360分間隔・9〜24時) に投稿する。

## 前提の確認 (現状コードの Linux 互換性)

- `TwitterBot` の依存は `AiTuber.Core` / `MultiLLMClient` / `Medoz.X` のみで、
  すべて HttpClient + System.Text.Json ベース。**Windows 依存 (NAudio / Win32) は無く、そのまま Linux で動く**
- 必要なランタイムファイル: 実行バイナリ + ペルソナディレクトリ (`character.md` + `tweet_system.md` + `persona.json`。`PERSONA_DIR` で指す) + `data/` (memory.json)
- `TweetScheduler.InActiveHours` は `DateTime.Now` を使うため、**ホストのタイムゾーンが Asia/Tokyo であることが必須**
  (UTC のままの VM だと 9〜24時判定がずれる)

## 全体像

```
[Windows 配信PC]                     [ラズパイ / Linux VM (常時稼働)]
 Live / GameCommentary                systemd timer (ランダム間隔)
   │ 配信メモを書く                        │ 起動ごとに1回投稿
   ▼                                      ▼
 data/memory.json  ──(同期: 後述)──▶  TwitterBot --scheduled
                                          │
                                          ▼
                                     X API v2 (Medoz.X)
```

## 実行方式: systemd timer + 1回実行モード (推奨)

現在の常駐ループ (`Task.Delay` でランダム待機) をそのまま `systemd service` (Restart=always) で
動かすこともできるが、ラズパイ運用では以下の理由で **timer + 1回実行** を推奨する:

- 電源断・再起動・OOM でプロセスが死んでも、次の timer 発火で自然に復帰する (待機状態を持たない)
- 常駐 (Restart=always) だと再起動のたびにループ先頭から始まり即投稿されるため、
  クラッシュループ時に連続投稿するリスクがある
- 待機中のメモリをゼロにできる (ラズパイに優しい)

### ランダム間隔の実現

systemd timer の組み合わせで Python 版の仕様 (前回投稿から 180〜360 分後) をそのまま表現できる:

```
OnUnitInactiveSec=180min   # 前回のサービス終了から180分後
RandomizedDelaySec=180min  # さらに 0〜180分 の一様ランダムを加算 → 合計 180〜360分
```

時間帯 (9〜24時) の判定はアプリ側の `TweetScheduler.InActiveHours` に残す
(timer は時間帯を気にせず発火し、時間外ならアプリが「投稿時間外のためスキップ」してすぐ終了する。
常駐版ループの挙動と同じ)。

### 必要なコード変更 (最小)

`--once` は動作確認用で時間帯チェックをしないため、timer 用に **`--scheduled` オプションを追加**する:

- `--scheduled`: 時間帯 (9〜24時) を判定してから 1 回だけ生成・投稿して終了。時間外なら何もせず正常終了
- 既存の `--once` (無条件で1回投稿) と常駐モードは変更しない

変更は `TwitterBot/Program.cs` に数行のみ。`TweetScheduler` はそのまま使える。

## デプロイ

### 発行 (Windows 側で実行)

.NET ランタイムをラズパイに入れずに済む self-contained 単一ファイルで発行する:

```powershell
# Raspberry Pi (64bit OS / Pi 3 以降) 向け
dotnet publish TwitterBot -c Release -r linux-arm64 --self-contained -p:PublishSingleFile=true -o publish/twitterbot

# x64 の軽量VM向けなら -r linux-x64
```

注意: 32bit Raspberry Pi OS (armv6/armv7 の Pi Zero / Pi 1) は .NET 公式サポート外。64bit OS を使う。

### 配置 (ラズパイ側)

```
/opt/ai-tuber/
├── TwitterBot              # 発行した単一バイナリ (chmod +x)
├── persona/                # ペルソナディレクトリ (ai-tuber-potofu を clone/転送。PERSONA_DIR で指す)
│   ├── persona.json
│   ├── character.md
│   └── tweet_system.md
└── data/
    └── memory.json         # 無ければ初回に自動生成される
```

`AppConfig` の `DataDir` は相対パスなので、systemd の `WorkingDirectory` を
`/opt/ai-tuber` にすることで解決する。ペルソナは env の `PERSONA_DIR=/opt/ai-tuber/persona` で指す。

転送は `scp -r publish/twitterbot/* pi@raspi:/opt/ai-tuber/` + ペルソナリポジトリを別途 clone/転送。
更新時も同じコマンドで上書きすればよい (timer 方式なら実行中プロセスがいない時間がほとんどなので停止不要)。

### 環境変数 (シークレット)

`/etc/ai-tuber/twitterbot.env` (owner root / `chmod 600`) に置き、unit から読み込む:

```ini
TZ=Asia/Tokyo
PERSONA_DIR=/opt/ai-tuber/persona
LLM_PROVIDER=claude
ANTHROPIC_API_KEY=sk-ant-...
X_API_KEY=...
X_API_SECRET=...
X_ACCESS_TOKEN=...
X_ACCESS_SECRET=...
# 動作確認が終わるまで dry-run (原則どおりデフォルトは dry-run)
TWEET_DRY_RUN=1
```

あわせて OS 側も `sudo timedatectl set-timezone Asia/Tokyo` にしておく。

### systemd unit

`/etc/systemd/system/twitterbot.service`:

```ini
[Unit]
Description=AITuber TwitterBot (post once)
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
User=aituber
WorkingDirectory=/opt/ai-tuber
EnvironmentFile=/etc/ai-tuber/twitterbot.env
ExecStart=/opt/ai-tuber/TwitterBot --scheduled
# LLM 生成 + 投稿に十分な上限
TimeoutStartSec=5min
```

`/etc/systemd/system/twitterbot.timer`:

```ini
[Unit]
Description=AITuber TwitterBot random-interval timer

[Timer]
# 前回実行の終了から 180分 + 0〜180分ランダム = 180〜360分後 (Python版仕様と同一)
OnUnitInactiveSec=180min
RandomizedDelaySec=180min
# OnUnitInactiveSec は一度も実行されていないと発火しないため、起動後の初回発火を用意する
OnBootSec=10min
Persistent=false

[Install]
WantedBy=timers.target
```

```bash
sudo useradd -r -s /usr/sbin/nologin aituber
sudo systemctl daemon-reload
sudo systemctl enable --now twitterbot.timer
systemctl list-timers twitterbot.timer   # 次回発火時刻の確認
journalctl -u twitterbot.service -f      # ログ
```

## memory.json の同期 (配信PC ⇔ ラズパイ)

`data/memory.json` は Live (Windows) が `stream_notes` を書き、TwitterBot が
`recent_tweets` を読み書きする共有ファイル。ホストが分かれるため段階的に対応する:

### 段階1: ラズパイ単独運用 (コード変更なし・まずこれで開始)

ラズパイが自分の `memory.json` を持つ。`recent_tweets` による重複回避は完結して動く。
`stream_notes` が空のため「配信お礼」系ツイートの材料が無いだけで、daily / question 等は問題なく生成される。
配信メモを反映したいときは手動で memory.json をコピーしてもよい。

### 段階2: Syncthing で data/ を双方向同期 (コード変更なし・推奨)

Windows とラズパイに [Syncthing](https://syncthing.net/) を入れ、`data/` フォルダを共有する。

- Windows が配信中しか起動していなくても、次回起動時に差分同期される (ラズパイ側は常時受け付け)
- 書き込みタイミングが「配信終了時 (Windows)」と「投稿時 (ラズパイ)」でほぼ重ならないため、
  同一ファイル競合は実運用上まれ。競合時は Syncthing が conflict ファイルを残すので手動解決

### 段階3 (必要になったら): 書き込み単位の分離

競合が実際に問題になった場合のみ、`memory.json` を書き手ごとに分割
(`stream_notes.json` は Live が、`tweets.json` は TwitterBot が所有) して `SharedMemory` を
複数ファイル対応にする。Python 版互換のスキーマを崩すため、必要になるまでやらない。

## 運用フロー

1. `linux-arm64` で publish → scp 配置 → env ファイル作成 (`TWEET_DRY_RUN=1` のまま)
2. `sudo -u aituber /opt/ai-tuber/TwitterBot --once` を手動実行し、dry-run 出力と memory.json 更新を確認
3. timer を enable し、`journalctl` で数回分の発火 (時間外スキップ含む) を確認
4. 問題なければ env の `TWEET_DRY_RUN=0` に変更して本番化
5. 更新時は Windows で publish し直して scp 上書き

## セキュリティ / 運用メモ

- API キーは env ファイル (600) のみ。リポジトリ・バイナリに含めない (既存原則どおり)
- 専用ユーザー `aituber` で実行し、書き込みは `/opt/ai-tuber/data` のみに限定
- ラズパイの死活が気になる場合は Phase 5 (エラー復旧・監視) で
  `OnFailure=` による通知 unit の追加を検討する
