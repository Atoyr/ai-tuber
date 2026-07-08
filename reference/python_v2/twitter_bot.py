"""Twitter(X) 自律投稿ボット

キャラクター人格でツイートを自動生成し、ランダムな間隔で投稿する。
配信スクリプトが残した「配信メモ」があれば、お礼・告知系のツイートも混ぜる。

安全設計:
- デフォルトは DRY RUN(実投稿せずコンソール表示のみ)。config.TWEET_DRY_RUN=0 で本番化
- 直近ツイート履歴をプロンプトに渡して重複を回避
- 禁止ワードフィルタに掛かったら再生成(最大3回)、ダメなら投稿スキップ
- 投稿時間帯と間隔を制限(深夜連投などを防ぐ)

実行:
  set X_API_KEY=... ほか4つの環境変数をセット
  python twitter_bot.py          ← 常駐してスケジュール投稿
  python twitter_bot.py --once   ← 1回だけ生成・投稿して終了(動作確認用)
"""
import json
import random
import sys
import time
from datetime import datetime

import config
from core.persona import PersonaClient, is_safe
from core import memory


def get_x_client():
    import tweepy

    return tweepy.Client(
        consumer_key=config.X_API_KEY,
        consumer_secret=config.X_API_SECRET,
        access_token=config.X_ACCESS_TOKEN,
        access_token_secret=config.X_ACCESS_SECRET,
    )


def build_context() -> str:
    """ツイート生成用のコンテキスト(時刻・配信メモ・直近ツイート)を組み立てる"""
    mem = memory.load()
    now = datetime.now()
    parts = [f"現在日時: {now.strftime('%Y-%m-%d %H:%M')} ({['月','火','水','木','金','土','日'][now.weekday()]}曜日)"]

    if mem["stream_notes"]:
        latest = mem["stream_notes"][-1]
        parts.append(f"直近の配信メモ ({latest['date']}): {latest['summary']}")

    if mem["recent_tweets"]:
        joined = "\n".join(f"- {t}" for t in mem["recent_tweets"][:10])
        parts.append(f"自分の直近ツイート(繰り返し禁止):\n{joined}")

    parts.append("この状況に合うツイートを1つ、指示されたJSON形式で生成してください。")
    return "\n\n".join(parts)


def generate_tweet(persona: PersonaClient) -> str | None:
    for attempt in range(3):
        raw = persona.generate(
            [{"role": "user", "content": build_context()}], max_tokens=300
        )
        try:
            data = json.loads(raw.replace("```json", "").replace("```", "").strip())
            text = data["tweet"].strip()
        except (json.JSONDecodeError, KeyError):
            print(f"[retry] JSONパース失敗: {raw[:80]}")
            continue

        if len(text) > 140:
            print(f"[retry] 140字超過: {len(text)}字")
            continue
        if not is_safe(text):
            print(f"[retry] フィルタに掛かった: {text}")
            continue
        if text in memory.load()["recent_tweets"]:
            print("[retry] 完全重複")
            continue
        return text
    return None


def post_once(persona: PersonaClient):
    text = generate_tweet(persona)
    if text is None:
        print("生成に3回失敗したため今回はスキップ")
        return

    if config.TWEET_DRY_RUN:
        print(f"[DRY RUN] 投稿内容: {text}")
    else:
        client = get_x_client()
        client.create_tweet(text=text)
        print(f"[投稿完了] {text}")

    memory.add_tweet(text)


def in_active_hours() -> bool:
    start, end = config.TWEET_ACTIVE_HOURS
    return start <= datetime.now().hour < end


def main():
    persona = PersonaClient("tweet_system.md")

    if "--once" in sys.argv:
        post_once(persona)
        return

    print("=== Twitter自律投稿ボット起動 (Ctrl+Cで終了) ===")
    print(f"DRY RUN: {config.TWEET_DRY_RUN} / 間隔: {config.TWEET_MIN_INTERVAL_MIN}〜{config.TWEET_MAX_INTERVAL_MIN}分")
    while True:
        if in_active_hours():
            post_once(persona)
        else:
            print(f"[{datetime.now():%H:%M}] 投稿時間外のためスキップ")

        wait_min = random.randint(
            config.TWEET_MIN_INTERVAL_MIN, config.TWEET_MAX_INTERVAL_MIN
        )
        print(f"次の投稿まで約{wait_min}分待機...")
        time.sleep(wait_min * 60)


if __name__ == "__main__":
    main()
