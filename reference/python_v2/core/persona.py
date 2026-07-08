"""キャラクター人格の読み込みとClaude呼び出しの共通部。

配信もTwitterもここを通ることで、同じ"魂"から発話が生成される。
"""
import sys
from pathlib import Path

sys.path.append(str(Path(__file__).parent.parent))

from anthropic import Anthropic
import config


def load_prompt(name: str) -> str:
    path = config.PROMPT_DIR / name
    return path.read_text(encoding="utf-8")


def build_system_prompt(mode_file: str) -> str:
    """character.md(共通人格) + モード別指示 を結合してシステムプロンプトにする"""
    character = load_prompt("character.md")
    mode = load_prompt(mode_file)
    return f"{character}\n\n---\n\n{mode}"


class PersonaClient:
    def __init__(self, mode_file: str):
        if not config.ANTHROPIC_API_KEY:
            raise RuntimeError("環境変数 ANTHROPIC_API_KEY が未設定です")
        self.client = Anthropic(api_key=config.ANTHROPIC_API_KEY)
        self.system = build_system_prompt(mode_file)

    def generate(self, messages: list[dict], max_tokens: int = 300) -> str:
        resp = self.client.messages.create(
            model=config.CLAUDE_MODEL,
            max_tokens=max_tokens,
            system=self.system,
            messages=messages,
        )
        return "".join(b.text for b in resp.content if b.type == "text").strip()


def is_safe(text: str) -> bool:
    """最低限の安全フィルタ。引っかかったら破棄して作り直す用。"""
    return not any(w in text for w in config.BANNED_WORDS)
