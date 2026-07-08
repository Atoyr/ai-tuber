"""配信とTwitterで共有する簡易メモリ(JSONファイル)。

- 配信スクリプトが「今日何を配信したか」を書き込む
- Twitterボットがそれを読んで「配信お礼/次回予告」ツイートに使う
- Twitterボットは投稿履歴を書き込み、重複ツイートを防ぐ
"""
import json
import sys
from datetime import datetime
from pathlib import Path

sys.path.append(str(Path(__file__).parent.parent))
import config

MEMORY_PATH = config.DATA_DIR / "memory.json"

DEFAULT = {
    "stream_notes": [],   # [{"date": "...", "summary": "..."}]
    "recent_tweets": [],  # ["本文", ...] 新しい順
}


def load() -> dict:
    if MEMORY_PATH.exists():
        return json.loads(MEMORY_PATH.read_text(encoding="utf-8"))
    return json.loads(json.dumps(DEFAULT))


def save(mem: dict):
    MEMORY_PATH.parent.mkdir(parents=True, exist_ok=True)
    MEMORY_PATH.write_text(
        json.dumps(mem, ensure_ascii=False, indent=2), encoding="utf-8"
    )


def add_stream_note(summary: str):
    mem = load()
    mem["stream_notes"].append(
        {"date": datetime.now().strftime("%Y-%m-%d %H:%M"), "summary": summary}
    )
    mem["stream_notes"] = mem["stream_notes"][-10:]
    save(mem)


def add_tweet(text: str):
    mem = load()
    mem["recent_tweets"].insert(0, text)
    mem["recent_tweets"] = mem["recent_tweets"][: config.RECENT_TWEETS_KEEP]
    save(mem)
