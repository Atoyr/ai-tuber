"use strict";

/* ==========================================================================
 * AITuber Studio - 配信画面 (OBS ブラウザソース用オーバーレイ)
 *
 * - アバターは PuruPuruPNGTuber の OBS モード (mode=obs&transparent=1) を iframe で埋め込む
 * - 字幕 (reply / commentary) とコメント一覧 (comments) を SSE で受けて重ねる
 * - 配信に映る画面なのでエラー表示は一切しない (落ちても黙って再接続する)
 * - SSE は ?replay=0 で購読する (接続時に過去ログが一気に流れないようにする)
 *
 * URL クエリ:
 *   avatar=0 / comments=0 / subtitle=0  各レイヤを消す
 *   side=left|right                      コメントの表示位置 (既定 left)
 *   avatarUrl=<URL>                      アバターの埋め込み先を上書き
 * ========================================================================== */

const PARAMS = new URLSearchParams(location.search);
const SHOW_AVATAR = PARAMS.get("avatar") !== "0";
const SHOW_COMMENTS = PARAMS.get("comments") !== "0";
const SHOW_SUBTITLE = PARAMS.get("subtitle") !== "0";
const COMMENT_SIDE = PARAMS.get("side") === "right" ? "right" : "left";

const MAX_COMMENTS = 6;            // 同時に表示するコメント数
const COMMENT_TTL_MS = 45000;      // コメント1件の表示時間
const SUBTITLE_MIN_MS = 4000;      // 字幕の最短表示時間
const SUBTITLE_MAX_MS = 16000;     // 字幕の最長表示時間
const SUBTITLE_MS_PER_CHAR = 140;  // 字幕の1文字あたり表示時間 (読み切れる速さの目安)

const stage = document.getElementById("stage");
const avatarFrame = document.getElementById("avatar");
const commentsBox = document.getElementById("comments");
const subtitleBox = document.getElementById("subtitle");
const subtitleText = document.getElementById("subtitle-text");

/* ---------- ステージのフィット (1920x1080 → ブラウザソースのサイズ) ---------- */

function fitStage() {
  const scale = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
  stage.style.setProperty("--scale", String(scale));
}

window.addEventListener("resize", fitStage);
fitStage();

/* ---------- 字幕 ---------- */

// 先頭の感情タグ ([joy] など) は発話には乗らないので字幕からも外す
function stripEmotionTag(text) {
  return String(text || "").replace(/^\s*\[[a-zA-Z]+\]\s*/, "").trim();
}

function subtitleDuration(text) {
  const ms = SUBTITLE_MIN_MS + text.length * SUBTITLE_MS_PER_CHAR;
  return Math.min(SUBTITLE_MAX_MS, Math.max(SUBTITLE_MIN_MS, ms));
}

let subtitleTimer = null;

function showSubtitle(rawText) {
  if (!SHOW_SUBTITLE) return;
  const text = stripEmotionTag(rawText);
  if (!text) return;

  subtitleText.textContent = text;
  subtitleBox.hidden = false;
  // いったんクラスを外して付け直し、連続発話でもアニメーションが再生されるようにする
  subtitleBox.classList.remove("visible");
  void subtitleBox.offsetWidth;
  subtitleBox.classList.add("visible");

  if (subtitleTimer) clearTimeout(subtitleTimer);
  subtitleTimer = setTimeout(hideSubtitle, subtitleDuration(text));
}

function hideSubtitle() {
  subtitleTimer = null;
  subtitleBox.classList.remove("visible");
  // フェードアウト (CSS transition 220ms) を待ってから隠す
  setTimeout(() => {
    if (!subtitleBox.classList.contains("visible")) subtitleBox.hidden = true;
  }, 260);
}

/* ---------- コメント一覧 ---------- */

function removeComment(el) {
  if (!el.isConnected || el.classList.contains("leaving")) return;
  el.classList.add("leaving");
  setTimeout(() => el.remove(), 400);
}

function appendComment(author, text) {
  if (!SHOW_COMMENTS) return;
  const body = String(text || "").trim();
  if (!body) return;

  const el = document.createElement("div");
  el.className = "comment";

  const authorEl = document.createElement("span");
  authorEl.className = "comment-author";
  authorEl.textContent = String(author || "名無し");

  const textEl = document.createElement("span");
  textEl.className = "comment-text";
  textEl.textContent = body;

  el.append(authorEl, textEl);
  commentsBox.appendChild(el);

  // 古いものから溢れさせる + 時間経過でも消す
  while (commentsBox.querySelectorAll(".comment:not(.leaving)").length > MAX_COMMENTS) {
    const oldest = commentsBox.querySelector(".comment:not(.leaving)");
    if (!oldest) break;
    removeComment(oldest);
  }
  setTimeout(() => removeComment(el), COMMENT_TTL_MS);
}

/* ---------- アバター (PuruPuru OBS モード) ---------- */

async function setupAvatar() {
  if (!SHOW_AVATAR) return;

  let url = PARAMS.get("avatarUrl");
  if (!url) {
    try {
      const res = await fetch("/api/overlay/config");
      const config = await res.json();
      url = config.avatarUrl;
    } catch (_) {
      // Studio に繋がらない場合はアバター無しで字幕だけ動かす (配信を止めない)
      return;
    }
  }
  if (!url) return;
  avatarFrame.src = url;
  avatarFrame.hidden = false;
}

/* ---------- SSE ---------- */

function connectEvents() {
  // replay=0: 接続直後のリプレイを受け取らない (過去の発話が配信画面に出ないようにする)
  const source = new EventSource("/api/events?replay=0");

  const bind = (eventName, handler) => {
    source.addEventListener(eventName, (ev) => {
      let payload;
      try {
        payload = JSON.parse(ev.data);
      } catch (_) {
        return;
      }
      handler(payload);
    });
  };

  bind("reply", (payload) => showSubtitle(payload.text));
  bind("commentary", (payload) => showSubtitle(payload.text));
  bind("comments", (payload) => {
    for (const comment of payload.comments || []) {
      appendComment(comment.author, comment.text);
    }
  });

  // 切断は EventSource が自動再接続する。配信画面には何も出さない
  return source;
}

/* ---------- 初期化 ---------- */

commentsBox.classList.add(`side-${COMMENT_SIDE}`);
commentsBox.hidden = !SHOW_COMMENTS;
setupAvatar();
connectEvents();
