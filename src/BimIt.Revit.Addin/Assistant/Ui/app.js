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

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#39;"
  })[character]);
}

const calibration = document.getElementById("calibration");
const calibrationFrame = document.getElementById("calibration-frame");
const calibrationImage = document.getElementById("calibration-image");
const calibrationStatus = document.getElementById("calibration-status");
let calibrationSession = "";
let calibrationPointA = null;

function hideCalibration() {
  calibration.hidden = true;
  calibrationSession = "";
  calibrationPointA = null;
  calibrationImage.removeAttribute("src");
  calibrationFrame.style.aspectRatio = "";
}

function showCalibration(data) {
  if (!data || data.type !== "calibrationPage" || typeof data.sessionRef !== "string" || typeof data.image !== "string") {
    return;
  }
  if (data.image.indexOf("\\") >= 0 || data.image.indexOf("://") >= 0 || data.mimeType !== "image/jpeg" && data.mimeType !== "image/png") {
    return;
  }
  const width = Number(data.rasterWidth);
  const height = Number(data.rasterHeight);
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
    return;
  }
  calibrationSession = data.sessionRef;
  calibrationPointA = null;
  calibrationFrame.style.aspectRatio = width + " / " + height;
  calibrationImage.src = "data:" + data.mimeType + ";base64," + data.image;
  calibrationStatus.textContent = "Click point A at the selected template, then point B.";
  calibration.hidden = false;
}

window.chrome.webview.addEventListener("message", (event) => {
  const data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
  if (!data) {
    return;
  }
  if (data.type === "snapshot") {
    render(data);
  } else if (data.type === "calibrationPage") {
    showCalibration(data);
  } else if (data.type === "calibrationClosed") {
    hideCalibration();
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
document.getElementById("calibrate-plan").addEventListener("click", () => post({ type: "calibratePlanPreview" }));
document.getElementById("calibration-cancel").addEventListener("click", () => {
  const sessionRef = calibrationSession;
  hideCalibration();
  if (sessionRef) {
    post({ type: "cancelCalibration", sessionRef });
  }
});
calibrationFrame.addEventListener("click", (event) => {
  if (!calibrationSession) {
    return;
  }
  const rect = calibrationFrame.getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0) {
    return;
  }
  const x = (event.clientX - rect.left) / rect.width;
  const y = (event.clientY - rect.top) / rect.height;
  if (x < 0 || x > 1 || y < 0 || y > 1) {
    return;
  }
  if (!calibrationPointA) {
    calibrationPointA = { x, y };
    calibrationStatus.textContent = "Click point B.";
    return;
  }
  const sessionRef = calibrationSession;
  const sourceAX = calibrationPointA.x;
  const sourceAY = calibrationPointA.y;
  hideCalibration();
  post({
    type: "submitCalibrationPoints",
    sessionRef,
    sourceAX,
    sourceAY,
    sourceBX: x,
    sourceBY: y
  });
});
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
