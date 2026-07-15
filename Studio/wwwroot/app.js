"use strict";

/* ==========================================================================
 * AITuber Studio - フロントエンド
 * 素の HTML/CSS/JS。ビルド不要・外部ライブラリ不使用。
 * API 契約は docs/studio-architecture.md を参照。
 * ========================================================================== */

const MAX_LOG_ENTRIES = 500;

/* ---------- 小さなユーティリティ ---------- */

function qs(id) {
  return document.getElementById(id);
}

function formatTime(iso) {
  if (!iso) return "--:--";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "--:--";
  const hh = String(d.getHours()).padStart(2, "0");
  const mm = String(d.getMinutes()).padStart(2, "0");
  return `${hh}:${mm}`;
}

/* fetch ラッパー。失敗はログ欄に出しつつ null を返す(UI を止めない) */
async function apiRequest(method, path, body) {
  try {
    const res = await fetch(path, {
      method,
      headers: body !== undefined ? { "Content-Type": "application/json" } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    let data = null;
    try {
      data = await res.json();
    } catch (_) {
      data = null;
    }
    if (!res.ok) {
      const msg = (data && data.error) ? data.error : `HTTP ${res.status}`;
      appendLog({ at: new Date().toISOString(), level: "error", message: `${method} ${path} 失敗: ${msg}` }, "log-error");
      return { ok: false, data, error: msg };
    }
    return { ok: true, data };
  } catch (err) {
    appendLog({ at: new Date().toISOString(), level: "error", message: `${method} ${path} 通信エラー: ${err.message}` }, "log-error");
    return { ok: false, data: null, error: err.message };
  }
}

const api = {
  get: (path) => apiRequest("GET", path),
  post: (path, body) => apiRequest("POST", path, body === undefined ? {} : body),
  put: (path, body) => apiRequest("PUT", path, body),
};

/* ==========================================================================
 * 会話ログタイムライン
 * ========================================================================== */

const logTimeline = qs("log-timeline");
const jumpLatestBtn = qs("jump-latest-btn");
let followLatest = true;

logTimeline.addEventListener("scroll", () => {
  const distanceFromBottom =
    logTimeline.scrollHeight - logTimeline.scrollTop - logTimeline.clientHeight;
  followLatest = distanceFromBottom < 24;
  jumpLatestBtn.classList.toggle("hidden", followLatest);
});

jumpLatestBtn.addEventListener("click", () => {
  followLatest = true;
  scrollLogToBottom();
  jumpLatestBtn.classList.add("hidden");
});

function scrollLogToBottom() {
  logTimeline.scrollTop = logTimeline.scrollHeight;
}

const LOG_LABELS = {
  comments: "コメント",
  reply: "発話",
  skip: "skip",
  freetalk: "フリートーク",
  note: "配信メモ",
  "log-info": "info",
  "log-error": "error",
};

function describeLogPayload(kind, payload) {
  switch (kind) {
    case "comments": {
      const list = Array.isArray(payload.comments) ? payload.comments : [];
      return list.map((c) => `${c.author}: ${c.text}`).join(" / ") || "(コメントなし)";
    }
    case "reply":
      return payload.text || "";
    case "skip":
      return payload.reason || "";
    case "freetalk":
      return "(自動フリートーク)";
    case "note":
      return payload.summary || "";
    case "log-info":
    case "log-error":
      return payload.message || "";
    default:
      return JSON.stringify(payload);
  }
}

function appendLog(payload, kind) {
  const entry = document.createElement("div");
  entry.className = `log-entry type-${kind}`;

  const time = document.createElement("span");
  time.className = "log-time";
  time.textContent = formatTime(payload.at);

  const tag = document.createElement("span");
  tag.className = "log-tag";
  tag.textContent = LOG_LABELS[kind] || kind;

  const body = document.createElement("span");
  body.className = "log-body";
  body.textContent = describeLogPayload(kind, payload);

  entry.appendChild(time);
  entry.appendChild(tag);
  entry.appendChild(body);
  logTimeline.appendChild(entry);

  while (logTimeline.children.length > MAX_LOG_ENTRIES) {
    logTimeline.removeChild(logTimeline.firstChild);
  }

  if (followLatest) {
    scrollLogToBottom();
  }
}

/* ==========================================================================
 * 状態バッジ
 * ========================================================================== */

const STATE_LABELS = {
  NotConfigured: "未設定",
  Stopped: "停止",
  Starting: "起動中",
  Running: "稼働中",
  RunningExternal: "稼働中(外部)",
  Stopping: "停止中",
  Faulted: "異常",
};

function setBadge(el, prefix, state, tooltip) {
  el.className = `badge state-${(state || "unknown").toLowerCase()}`;
  const label = STATE_LABELS[state] || state || "-";
  el.textContent = `${prefix}: ${label}`;
  el.title = tooltip || (state === "NotConfigured" ? "パス未設定" : "");
}

/* ==========================================================================
 * ステータス取得・起動パネルの反映
 * ========================================================================== */

const badgeVoicevox = qs("badge-voicevox");
const badgePurupuru = qs("badge-purupuru");
const badgeLive = qs("badge-live");
const personaNameEl = qs("persona-name");
const liveInfoEl = qs("live-info");

const voicevoxStartBtn = qs("voicevox-start-btn");
const voicevoxStopBtn = qs("voicevox-stop-btn");
const purupuruStartBtn = qs("purupuru-start-btn");
const purupuruStopBtn = qs("purupuru-stop-btn");
const startAllBtn = qs("start-all-btn");
const liveStartBtn = qs("live-start-btn");
const liveStopBtn = qs("live-stop-btn");

function appStartDisabled(state) {
  return state === "Starting" || state === "Running" || state === "RunningExternal" || state === "NotConfigured";
}
function appStopDisabled(state) {
  return state === "NotConfigured" || state === "Stopped" || state === "RunningExternal" || !state;
}

function applyStatus(status) {
  if (!status) return;

  const vv = status.voicevox || {};
  const pp = status.purupuru || {};
  const live = status.live || {};

  setBadge(badgeVoicevox, "VOICEVOX", vv.state, vv.version ? `version ${vv.version}` : undefined);
  setBadge(badgePurupuru, "PuruPuru", pp.state);
  setBadge(badgeLive, "Live", live.state);

  voicevoxStartBtn.disabled = appStartDisabled(vv.state);
  voicevoxStopBtn.disabled = appStopDisabled(vv.state);
  purupuruStartBtn.disabled = appStartDisabled(pp.state);
  purupuruStopBtn.disabled = appStopDisabled(pp.state);

  const liveBusy = live.state === "Starting" || live.state === "Stopping";
  liveStartBtn.disabled = liveBusy || live.state === "Running";
  liveStopBtn.disabled = liveBusy || !live.state || live.state === "Stopped" || live.state === "Faulted";

  if (live.persona) {
    personaNameEl.textContent = live.persona;
  }

  const infoParts = [];
  if (live.source) infoParts.push(`ソース: ${live.source}`);
  if (live.startedAt) infoParts.push(`開始: ${formatTime(live.startedAt)}`);
  liveInfoEl.textContent = infoParts.join(" / ");
}

async function refreshStatus() {
  const res = await api.get("/api/status");
  if (res.ok) {
    applyStatus(res.data);
  }
}

/* ---------- 起動パネルのボタン ---------- */

startAllBtn.addEventListener("click", async () => {
  startAllBtn.disabled = true;
  await api.post("/api/apps/start-all");
  startAllBtn.disabled = false;
  refreshStatus();
});

voicevoxStartBtn.addEventListener("click", async () => {
  await api.post("/api/apps/voicevox/start");
  refreshStatus();
});
voicevoxStopBtn.addEventListener("click", async () => {
  await api.post("/api/apps/voicevox/stop");
  refreshStatus();
});
purupuruStartBtn.addEventListener("click", async () => {
  await api.post("/api/apps/purupuru/start");
  refreshStatus();
});
purupuruStopBtn.addEventListener("click", async () => {
  await api.post("/api/apps/purupuru/stop");
  refreshStatus();
});

/* ==========================================================================
 * 配信パネル
 * ========================================================================== */

const liveSourceSelect = qs("live-source");
const liveTargetRow = qs("live-target-row");
const liveTargetInput = qs("live-target");

function updateLiveTargetVisibility() {
  liveTargetRow.classList.toggle("hidden", liveSourceSelect.value === "manual");
}
liveSourceSelect.addEventListener("change", updateLiveTargetVisibility);
updateLiveTargetVisibility();

liveStartBtn.addEventListener("click", async () => {
  const source = liveSourceSelect.value;
  const body = { source };
  if (source !== "manual" && liveTargetInput.value.trim()) {
    body.target = liveTargetInput.value.trim();
  }
  await api.post("/api/live/start", body);
  refreshStatus();
});

liveStopBtn.addEventListener("click", async () => {
  await api.post("/api/live/stop");
  refreshStatus();
});

/* ---------- コメント注入フォーム ---------- */

const commentForm = qs("comment-form");
const commentAuthorInput = qs("comment-author");
const commentTextInput = qs("comment-text");

commentForm.addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const author = commentAuthorInput.value.trim() || "テスト";
  const text = commentTextInput.value.trim();
  if (!text) return;
  const res = await api.post("/api/live/comment", { author, text });
  if (res.ok) {
    commentTextInput.value = "";
  }
});

/* ==========================================================================
 * 設定パネル
 * ========================================================================== */

const settingInputs = {
  freetalkAfterSec: qs("setting-freetalk-after-sec"),
  commentBatchSec: qs("setting-comment-batch-sec"),
  freetalkEnabled: qs("setting-freetalk-enabled"),
  speakerId: qs("setting-speaker-id"),
  paused: qs("setting-paused"),
};

const nextSessionInputs = {
  outputDevice: qs("setting-output-device"),
  llmProvider: qs("setting-llm-provider"),
  llmModel: qs("setting-llm-model"),
  personaDir: qs("setting-persona-dir"),
};

const secretsListEl = qs("secrets-list");
let pendingOutputDevice = null;

function readInputValue(input) {
  if (input.type === "checkbox") return input.checked;
  if (input.type === "number") return input.value === "" ? null : Number(input.value);
  return input.value;
}

function writeInputValue(input, value) {
  if (input.type === "checkbox") {
    input.checked = Boolean(value);
  } else {
    input.value = value === null || value === undefined ? "" : value;
  }
}

/* 変更 change 時に PUT /api/settings(変更項目のみ)。失敗したら元の値に戻す */
function wireSettingInput(input, group, key) {
  let lastKnownValue = readInputValue(input);
  input.addEventListener("change", async () => {
    const newValue = readInputValue(input);
    const body = { [group]: { [key]: newValue } };
    const res = await api.put("/api/settings", body);
    if (res.ok) {
      lastKnownValue = newValue;
    } else {
      writeInputValue(input, lastKnownValue);
    }
  });
  return {
    setKnownValue(value) {
      lastKnownValue = value;
      writeInputValue(input, value);
    },
  };
}

const wiredImmediate = {};
for (const [key, input] of Object.entries(settingInputs)) {
  wiredImmediate[key] = wireSettingInput(input, "immediate", key);
}
const wiredNextSession = {};
for (const [key, input] of Object.entries(nextSessionInputs)) {
  wiredNextSession[key] = wireSettingInput(input, "nextSession", key);
}

function renderSecrets(secrets) {
  secretsListEl.textContent = "";
  if (!secrets) return;
  for (const [key, value] of Object.entries(secrets)) {
    const badge = document.createElement("span");
    const isSet = value === "set";
    badge.className = `secret-badge ${isSet ? "set" : "unset"}`;
    badge.textContent = `${key}: ${isSet ? "設定済み" : "未設定"}`;
    secretsListEl.appendChild(badge);
  }
}

async function loadSettings() {
  const res = await api.get("/api/settings");
  if (!res.ok || !res.data) return;
  const { immediate, nextSession, secrets } = res.data;

  if (immediate) {
    for (const [key, wired] of Object.entries(wiredImmediate)) {
      if (key in immediate) wired.setKnownValue(immediate[key]);
    }
  }
  if (nextSession) {
    // outputDevice はデバイス一覧ロード後に反映するため保持しておく
    pendingOutputDevice = nextSession.outputDevice;
    for (const [key, wired] of Object.entries(wiredNextSession)) {
      if (key === "outputDevice") continue;
      if (key in nextSession) wired.setKnownValue(nextSession[key]);
    }
    if (nextSession.source) {
      liveSourceSelect.value = nextSession.source;
      updateLiveTargetVisibility();
    }
    if (nextSession.target) {
      liveTargetInput.value = nextSession.target;
    }
  }
  renderSecrets(secrets);
}

async function loadDevices() {
  const res = await api.get("/api/devices");
  const select = nextSessionInputs.outputDevice;
  select.textContent = "";
  const devices = (res.ok && res.data && Array.isArray(res.data.devices)) ? res.data.devices : [];

  for (const name of devices) {
    const opt = document.createElement("option");
    opt.value = name;
    opt.textContent = name;
    select.appendChild(opt);
  }

  if (pendingOutputDevice) {
    if (!devices.includes(pendingOutputDevice)) {
      const opt = document.createElement("option");
      opt.value = pendingOutputDevice;
      opt.textContent = pendingOutputDevice;
      select.appendChild(opt);
    }
    wiredNextSession.outputDevice.setKnownValue(pendingOutputDevice);
  } else if (devices.length > 0) {
    wiredNextSession.outputDevice.setKnownValue(devices[0]);
  }
}

/* ==========================================================================
 * SSE (Server-Sent Events)
 * ========================================================================== */

const connDot = qs("conn-dot");
const connText = qs("conn-text");

function setConnectionState(connected) {
  connDot.classList.toggle("connected", connected);
  connDot.classList.toggle("disconnected", !connected);
  connText.textContent = connected ? "接続中" : "切断";
}

function connectEvents() {
  const source = new EventSource("/api/events");

  source.addEventListener("open", () => setConnectionState(true));
  source.addEventListener("error", () => setConnectionState(false));

  const bind = (eventName, kind, handler) => {
    source.addEventListener(eventName, (ev) => {
      let payload;
      try {
        payload = JSON.parse(ev.data);
      } catch (_) {
        return;
      }
      appendLog(payload, kind);
      if (handler) handler(payload);
    });
  };

  bind("comments", "comments");
  bind("reply", "reply");
  bind("skip", "skip");
  bind("freetalk", "freetalk");
  bind("note", "note");

  source.addEventListener("log", (ev) => {
    let payload;
    try {
      payload = JSON.parse(ev.data);
    } catch (_) {
      return;
    }
    const kind = payload.level === "error" ? "log-error" : "log-info";
    appendLog(payload, kind);
  });

  source.addEventListener("state", (ev) => {
    try {
      const payload = JSON.parse(ev.data);
      applyStatus({
        voicevox: { state: payload.voicevox },
        purupuru: { state: payload.purupuru },
        live: { state: payload.live },
      });
    } catch (_) {
      /* ignore */
    }
  });

  return source;
}

/* ==========================================================================
 * 初期化
 * ========================================================================== */

async function init() {
  setConnectionState(false);
  await refreshStatus();
  await loadSettings();
  await loadDevices();
  connectEvents();
}

init();
