const statusNode = document.getElementById("status");
const welcome = document.getElementById("welcome");
const timeline = document.getElementById("timeline");
const diagnostics = document.getElementById("diagnostics-panel");
const command = document.getElementById("command");
const retry = document.getElementById("retry");

function post(message) {
  window.chrome.webview.postMessage(message);
}

function chip(label, value, state) {
  const tone = state || value;
  const bad = tone === "unavailable";
  const wait = tone === "starting";
  return `<span class="chip${bad ? " bad" : ""}${wait ? " wait" : ""}">${label}: ${escapeHtml(value)}</span>`;
}

function render(snapshot) {
  const status = snapshot.status || {};
  statusNode.innerHTML = [
    chip("Revit / Bridge", status.revitBridge || "unavailable"),
    chip("Assistant", status.assistant || "unavailable"),
    chip("MCP", status.mcp || "unavailable"),
    chip("AI Provider", status.aiProvider || "Not configured", status.aiProviderState)
  ].join("");
  const unavailable = status.assistant === "unavailable" || status.mcp === "unavailable";
  retry.hidden = !unavailable;
  const messages = snapshot.messages || [];
  welcome.hidden = messages.length > 0;
  timeline.innerHTML = messages.map((message) => {
    const role = message.role === "error" ? "error" : message.role === "user" ? "user" : "assistant";
    const used = message.used ? `<p class="used">${escapeHtml(message.used)}</p>` : "";
    return `<li class="msg ${role}"><h3>${escapeHtml(message.title || "")}</h3><p>${escapeHtml(message.body || "")}</p>${used}</li>`;
  }).join("");
  if (status.diagnostics) {
    const names = (status.toolNames || []).join("\n");
    diagnostics.hidden = false;
    diagnostics.textContent = `Tools: ${status.toolCount ?? 0}\n${names}${status.error ? "\n" + status.error : ""}${status.providerError ? "\n" + status.providerError : ""}`;
  } else {
    diagnostics.hidden = true;
  }
  if (status.busy) {
    timeline.insertAdjacentHTML("beforeend", `<li class="msg"><h3>Working</h3><p>Waiting for the Assistant.</p></li>`);
  }
  renderAttachment(snapshot.attachment);
  renderPattern(snapshot.patternReview);
}

function renderAttachment(attachment) {
  const node = document.getElementById("attachment");
  if (!attachment || !attachment.displayName) {
    node.hidden = true;
    node.innerHTML = "";
    return;
  }
  node.hidden = false;
  node.innerHTML = `<span class="clip" aria-hidden="true">📎</span><span class="attachment-copy"><strong>${escapeHtml(attachment.displayName)}</strong><span class="sub">${escapeHtml(attachment.summary || "")}</span></span><button id="remove-attachment" type="button" aria-label="Remove document">Remove</button>`;
  document.getElementById("remove-attachment").addEventListener("click", () => post({ type: "removeDocument" }));
}

let selectedRoom = 0;

function renderPattern(review) {
  const node = document.getElementById("pattern");
  if (!review || !Array.isArray(review.boundary) || !Array.isArray(review.devices)) {
    node.hidden = true;
    node.innerHTML = "";
    selectedRoom = 0;
    return;
  }
  const rooms = Array.isArray(review.rooms) ? review.rooms : [];
  if (selectedRoom >= rooms.length) {
    selectedRoom = 0;
  }
  const counts = `${review.candidateCount || 0} candidate rooms · ${review.highCount || 0} high · ${review.lowerCount || 0} lower · ${review.rejectedCount || 0} rejected`;
  const image = patternImage(review);
  node.hidden = false;
  node.innerHTML = `<h2>Pattern review</h2><p class="sub">${escapeHtml(counts)}</p><div class="pattern-grid"><div class="sheet">${image}${patternSvg(review.boundary, review.entrance, review.devices, true, false)}</div><div class="rooms"><div class="room-list">${rooms.map((room, index) => `<button type="button" data-room="${index}" class="${index === selectedRoom ? "selected" : ""}"><strong>${escapeHtml(room.label || "Room")}</strong><span>${escapeHtml(roomBand(room))}</span></button>`).join("")}</div><div class="room-preview">${rooms.length ? roomPreview(rooms[selectedRoom]) : "<p>No Revit rooms were compared.</p>"}</div></div></div>`;
  node.querySelectorAll("[data-room]").forEach((button) => {
    button.addEventListener("click", () => {
      selectedRoom = Number(button.dataset.room);
      renderPattern(review);
    });
  });
}

function patternImage(review) {
  if ((review.imageMime !== "image/jpeg" && review.imageMime !== "image/png") || typeof review.imageBase64 !== "string" || !/^[A-Za-z0-9+/=]+$/.test(review.imageBase64)) {
    return "";
  }
  return `<img alt="Attached drawing page" src="data:${review.imageMime};base64,${review.imageBase64}" />`;
}

function patternSvg(boundary, entrance, devices, showRoles, flipY) {
  const yOf = (value) => {
    const y = unit(value);
    return flipY ? 1 - y : y;
  };
  const points = boundary.map((point) => `${unit(point.x)},${yOf(point.y)}`).join(" ");
  const markers = (devices || []).map((device) => `<circle cx="${unit(device.x)}" cy="${yOf(device.y)}" r="0.012"></circle>${showRoles ? `<text x="${unit(device.x) + 0.015}" y="${yOf(device.y)}">${escapeHtml(device.role || "")}</text>` : ""}`).join("");
  const door = entrance ? `<rect x="${unit(entrance.x) - 0.01}" y="${yOf(entrance.y) - 0.01}" width="0.02" height="0.02"></rect>` : "";
  return `<svg viewBox="0 0 1 1" role="img" aria-label="Room pattern">${points ? `<polygon points="${points}"></polygon>` : ""}${door}${markers}</svg>`;
}

function roomPreview(room) {
  const adaptation = `${room.rotationDegrees || 0}°${room.mirrored ? " · mirrored" : ""}`;
  const outline = Array.isArray(room.outline) ? room.outline : [];
  const devices = Array.isArray(room.devices) ? room.devices : [];
  return `<p><strong>${escapeHtml(room.label || "Room")}</strong> ${escapeHtml(room.level || "")}</p><p class="sub">${escapeHtml(room.band || "rejected")} · ${escapeHtml(String(room.score ?? 0))} · ${escapeHtml(adaptation)}${room.reason ? " · " + escapeHtml(room.reason) : ""}</p>${patternSvg(outline, null, devices, true, true)}`;
}

function roomBand(room) {
  const mirror = room.mirrored ? " · mirrored" : "";
  return `${room.band || "rejected"} · ${room.rotationDegrees || 0}°${mirror}`;
}

function unit(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) {
    return 0;
  }
  return Math.min(1, Math.max(0, number));
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#39;"
  })[character]);
}

window.chrome.webview.addEventListener("message", (event) => {
  const data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
  if (data && data.type === "snapshot") {
    render(data);
  }
});

document.querySelectorAll("[data-action]").forEach((button) => {
  button.addEventListener("click", () => post({ type: "runQuickAction", action: button.dataset.action }));
});

document.getElementById("new-chat").addEventListener("click", () => post({ type: "newConversation" }));
retry.addEventListener("click", () => post({ type: "retryConnection" }));
document.getElementById("diagnostics").addEventListener("click", () => post({ type: "openDiagnostics" }));
document.getElementById("attach").addEventListener("click", () => post({ type: "attachDocument" }));
document.getElementById("confirm-axes").addEventListener("click", () => post({ type: "confirmProjectAxesPreview" }));
document.getElementById("review-placements").addEventListener("click", () => post({ type: "reviewInRevit" }));
document.getElementById("apply-placements").addEventListener("click", () => post({ type: "applyApprovedPlacements" }));
document.getElementById("composer").addEventListener("submit", (event) => {
  event.preventDefault();
  const text = command.value.trim();
  if (!text) {
    return;
  }
  command.value = "";
  post({ type: "sendCommand", text });
});

post({ type: "pageReady" });
