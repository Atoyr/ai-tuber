"""ゲーム実況パイプライン(ユーザーは喋らない前提)

特定ウィンドウを一定間隔でキャプチャ -> Claude(Vision)で実況コメント生成
-> VOICEVOX -> VB-CABLE -> PuruPuruPNGTuber口パク

※ 元は v1 の aituber_pipeline.py のヘルパーを使っていたが、
   v2 構成に合わせて config / live_aituber の speak を使う形に統合。

事前準備:
  1. VOICEVOX起動 (http://127.0.0.1:50021)
  2. VB-CABLEインストール、PuruPuruPNGTuberのマイクを"CABLE Output"に設定
  3. pip install -r requirements.txt
  4. 環境変数 ANTHROPIC_API_KEY をセット
  5. 下の WINDOW_TITLE_FRAGMENT を実際のゲームのウィンドウタイトルの一部に書き換える

実行:
  python game_commentary.py
"""
import sys
import time
import base64
import io

import pygetwindow as gw
import mss
from PIL import Image
from anthropic import Anthropic

import config
from live_aituber import find_output_device, speak

# ==== 設定 ====
WINDOW_TITLE_FRAGMENT = "ゲームのウィンドウタイトルの一部をここに"  # 例: "VALORANT", "Minecraft"
CAPTURE_INTERVAL_SEC = 12  # 実況の頻度(10〜15秒目安)
MAX_IMAGE_WIDTH = 800      # トークン節約のためリサイズ
HISTORY_LIMIT = 4          # 直近何件の実況を文脈として渡すか

SYSTEM_PROMPT = (
    "あなたはゲーム実況者のAI『ぷる乃』です。"
    "渡されたゲーム画面のスクリーンショットを見て、"
    "視聴者向けに明るく簡潔な実況コメントを1〜2文の日本語で生成してください。"
    "直前の実況と同じ内容の繰り返しは避け、画面の変化に注目してください。"
    "実況文以外の説明や前置きは一切書かないでください。"
)


def find_target_window(title_fragment: str):
    windows = [w for w in gw.getAllWindows() if title_fragment in w.title and w.title]
    if not windows:
        all_titles = [w.title for w in gw.getAllWindows() if w.title]
        raise RuntimeError(
            f"'{title_fragment}' を含むウィンドウが見つかりません。\n"
            f"現在開いているウィンドウ一覧: {all_titles}\n"
            f"WINDOW_TITLE_FRAGMENT をこの中の文字列に書き換えてください。"
        )
    return windows[0]


def capture_window_image(window) -> bytes:
    """指定ウィンドウの領域をキャプチャしてJPEGバイト列で返す"""
    with mss.mss() as sct:
        monitor = {
            "left": window.left,
            "top": window.top,
            "width": window.width,
            "height": window.height,
        }
        shot = sct.grab(monitor)
        img = Image.frombytes("RGB", shot.size, shot.bgra, "raw", "BGRX")

    if img.width > MAX_IMAGE_WIDTH:
        ratio = MAX_IMAGE_WIDTH / img.width
        img = img.resize((MAX_IMAGE_WIDTH, int(img.height * ratio)))

    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=80)
    return buf.getvalue()


def get_claude_commentary(client: Anthropic, image_bytes: bytes, history: list) -> str:
    b64_image = base64.b64encode(image_bytes).decode("utf-8")

    history_note = ""
    if history:
        recent = " / ".join(history[-HISTORY_LIMIT:])
        history_note = f"直近の実況: {recent}\n"

    resp = client.messages.create(
        model=config.CLAUDE_MODEL,
        max_tokens=150,
        system=SYSTEM_PROMPT,
        messages=[
            {
                "role": "user",
                "content": [
                    {
                        "type": "image",
                        "source": {
                            "type": "base64",
                            "media_type": "image/jpeg",
                            "data": b64_image,
                        },
                    },
                    {
                        "type": "text",
                        "text": f"{history_note}今の画面を実況してください。",
                    },
                ],
            }
        ],
    )
    return "".join(block.text for block in resp.content if block.type == "text").strip()


def main():
    if not config.ANTHROPIC_API_KEY:
        print("環境変数 ANTHROPIC_API_KEY が未設定です。")
        sys.exit(1)

    client = Anthropic(api_key=config.ANTHROPIC_API_KEY)

    try:
        window = find_target_window(WINDOW_TITLE_FRAGMENT)
        print(f"対象ウィンドウ: {window.title}")
    except RuntimeError as e:
        print(e)
        sys.exit(1)

    try:
        device_index = find_output_device(config.OUTPUT_DEVICE_NAME)
        print(f"出力デバイス: index={device_index}")
    except RuntimeError as e:
        print(e)
        sys.exit(1)

    history: list[str] = []
    print("=== ゲーム実況ループ開始 (Ctrl+Cで終了) ===")
    try:
        while True:
            start = time.time()
            try:
                image_bytes = capture_window_image(window)
                comment = get_claude_commentary(client, image_bytes, history)
                history.append(comment)
                history[:] = history[-HISTORY_LIMIT:]
                print(f"ぷる乃: {comment}")
                speak(comment, device_index)
            except Exception as e:
                print(f"[error] {e} (次のキャプチャで再試行)")

            elapsed = time.time() - start
            wait = max(0, CAPTURE_INTERVAL_SEC - elapsed)
            time.sleep(wait)
    except KeyboardInterrupt:
        print("\nゲーム実況ループ終了")


if __name__ == "__main__":
    main()
