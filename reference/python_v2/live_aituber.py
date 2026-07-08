"""AITuber 配信本体 (v2)

YouTube Liveコメント取得 → Claude(人格)応答 → VOICEVOX → VB-CABLE
→ PuruPuruPNGTuber が口パク。

v1からの変更点:
- YouTube Liveのコメントを pytchat で自動取得(手入力不要)
- 会話履歴を保持し、文脈のある返答をする
- コメントが無い時間が続いたら自動でフリートーク
- 喋っている間に来たコメントはキューに溜め、直近数件から1つ選んで返答
- 配信終了時に「今日の配信メモ」を生成してTwitterボットと共有

実行:
  set YT_VIDEO_ID=配信のvideoId
  python live_aituber.py
  (video IDは引数でも可: python live_aituber.py VIDEO_ID)

配信なしのローカルテスト:
  python live_aituber.py --console   ← コンソール入力をコメント代わりにする
"""
import io
import sys
import time
import threading
import queue

import requests
import sounddevice as sd
import soundfile as sf

import config
from core.persona import PersonaClient, is_safe
from core import memory


# ---------- 音声まわり ----------
def find_output_device(name_fragment: str) -> int:
    devices = sd.query_devices()
    for i, d in enumerate(devices):
        if name_fragment.lower() in d["name"].lower() and d["max_output_channels"] > 0:
            return i
    raise RuntimeError(
        f"'{name_fragment}' を含む出力デバイスが見つかりません。\n"
        f"利用可能: {[d['name'] for d in devices]}"
    )


def speak(text: str, device_index: int):
    q = requests.post(
        f"{config.VOICEVOX_URL}/audio_query",
        params={"text": text, "speaker": config.SPEAKER_ID},
        timeout=30,
    )
    q.raise_for_status()
    synth = requests.post(
        f"{config.VOICEVOX_URL}/synthesis",
        params={"speaker": config.SPEAKER_ID},
        json=q.json(),
        timeout=60,
    )
    synth.raise_for_status()
    data, sr = sf.read(io.BytesIO(synth.content), dtype="float32")
    sd.play(data, sr, device=device_index)
    sd.wait()


# ---------- コメント取得 ----------
def comment_worker_youtube(video_id: str, comment_q: queue.Queue, stop: threading.Event):
    import pytchat

    chat = pytchat.create(video_id=video_id)
    while chat.is_alive() and not stop.is_set():
        for c in chat.get().sync_items():
            comment_q.put({"author": c.author.name, "message": c.message})
        time.sleep(1)


def comment_worker_console(comment_q: queue.Queue, stop: threading.Event):
    """配信なしテスト用: コンソール入力をコメントとして流し込む"""
    print("コンソールモード: コメント役のテキストを入力してください (Ctrl+Cで終了)")
    while not stop.is_set():
        try:
            text = input("> ").strip()
        except EOFError:
            break
        if text:
            comment_q.put({"author": "テスト太郎", "message": text})


# ---------- メイン ----------
def pick_comment(comment_q: queue.Queue) -> list[dict]:
    """溜まっているコメントを最大5件まで取り出す"""
    picked = []
    while not comment_q.empty() and len(picked) < 5:
        picked.append(comment_q.get_nowait())
    return picked


def build_user_message(comments: list[dict]) -> str:
    lines = [f"{c['author']}: {c['message']}" for c in comments]
    return "視聴者コメント:\n" + "\n".join(lines)


def main():
    console_mode = "--console" in sys.argv
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    video_id = args[0] if args else config.YOUTUBE_VIDEO_ID

    if not console_mode and not video_id:
        print("YT_VIDEO_ID が未設定です。--console でローカルテストもできます。")
        sys.exit(1)

    persona = PersonaClient("live_system.md")
    device_index = find_output_device(config.OUTPUT_DEVICE_NAME)
    print(f"出力デバイス: index={device_index}")

    comment_q: queue.Queue = queue.Queue()
    stop = threading.Event()

    if console_mode:
        worker = threading.Thread(
            target=comment_worker_console, args=(comment_q, stop), daemon=True
        )
    else:
        worker = threading.Thread(
            target=comment_worker_youtube, args=(video_id, comment_q, stop), daemon=True
        )
    worker.start()

    history: list[dict] = []      # Claudeに渡す会話履歴
    topics_log: list[str] = []    # 配信メモ用の発話ログ
    last_activity = time.time()

    print("=== 配信ループ開始 (Ctrl+Cで終了) ===")
    try:
        while True:
            time.sleep(config.COMMENT_BATCH_SEC)

            comments = pick_comment(comment_q)
            if comments:
                user_msg = build_user_message(comments)
                last_activity = time.time()
            elif time.time() - last_activity > config.FREETALK_AFTER_SEC:
                user_msg = (
                    "(コメントが途切れています。フリートークをしてください。"
                    "直近の話題の続きか、好きなものの話を自然に)"
                )
                last_activity = time.time()
            else:
                continue

            history.append({"role": "user", "content": user_msg})
            history[:] = history[-config.HISTORY_TURNS * 2:]

            reply = persona.generate(history, max_tokens=200)

            if not is_safe(reply):
                print(f"[skip] フィルタに掛かった応答: {reply}")
                history.pop()
                continue

            history.append({"role": "assistant", "content": reply})
            topics_log.append(reply)
            print(f"ぷる乃: {reply}")
            speak(reply, device_index)

    except KeyboardInterrupt:
        pass
    finally:
        stop.set()
        # 配信メモを生成してTwitterボットと共有
        if topics_log:
            summary_req = [
                {
                    "role": "user",
                    "content": "以下は今日の配信での自分の発話ログです。"
                    "配信の内容を1〜2文で要約してください(後でツイートの材料にします)。\n\n"
                    + "\n".join(topics_log[-50:]),
                }
            ]
            try:
                summary = persona.generate(summary_req, max_tokens=150)
                memory.add_stream_note(summary)
                print(f"配信メモ保存: {summary}")
            except Exception as e:
                print(f"配信メモ生成失敗: {e}")
        print("配信ループ終了")


if __name__ == "__main__":
    main()
