"""共通設定。環境変数と定数をここに集約。"""
import os
from pathlib import Path

BASE_DIR = Path(__file__).parent
PROMPT_DIR = BASE_DIR / "prompts"
DATA_DIR = BASE_DIR / "data"

# --- Claude ---
ANTHROPIC_API_KEY = os.environ.get("ANTHROPIC_API_KEY", "")
CLAUDE_MODEL = "claude-sonnet-4-6"

# --- VOICEVOX / 音声 ---
VOICEVOX_URL = "http://127.0.0.1:50021"
SPEAKER_ID = 3                      # GET /speakers で確認して変更
OUTPUT_DEVICE_NAME = "CABLE Input"  # VB-CABLEへの出力(部分一致)

# --- YouTube Live ---
# 配信URLの watch?v=XXXX の部分。配信ごとに書き換えるか、起動時引数で渡す
YOUTUBE_VIDEO_ID = os.environ.get("YT_VIDEO_ID", "")
COMMENT_BATCH_SEC = 4        # コメントをまとめて拾う間隔(秒)
FREETALK_AFTER_SEC = 45      # この秒数コメントが無ければフリートーク
HISTORY_TURNS = 12           # Claudeに渡す直近会話ターン数

# --- Twitter (X API v2) ---
X_API_KEY = os.environ.get("X_API_KEY", "")
X_API_SECRET = os.environ.get("X_API_SECRET", "")
X_ACCESS_TOKEN = os.environ.get("X_ACCESS_TOKEN", "")
X_ACCESS_SECRET = os.environ.get("X_ACCESS_SECRET", "")

TWEET_MIN_INTERVAL_MIN = 180   # 最短投稿間隔(分)
TWEET_MAX_INTERVAL_MIN = 360   # 最長投稿間隔(分)
TWEET_ACTIVE_HOURS = (9, 24)   # 投稿してよい時間帯(9時〜24時)
TWEET_DRY_RUN = os.environ.get("TWEET_DRY_RUN", "1") == "1"  # 1なら実投稿せずコンソール表示のみ
RECENT_TWEETS_KEEP = 20        # 重複回避のため記憶する直近ツイート数

# 禁止ワード(含まれていたら投稿・発話を破棄して作り直し)
BANNED_WORDS = ["死ね", "殺す", "http://", "@"]
