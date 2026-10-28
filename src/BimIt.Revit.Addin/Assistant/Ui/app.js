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
  renderPattern(snapshot.patternReview, snapshot.patternPrepared === true);
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
let patternState = { generation: "", selected: [], options: {}, heights: {}, dirty: true };

function renderPattern(review, prepared) {
  const node = document.getElementById("pattern");
  if (!review || !Array.isArray(review.boundary) || !Array.isArray(review.devices)) {
    node.hidden = true;
    node.innerHTML = "";
    selectedRoom = 0;
    patternState = { generation: "", selected: [], options: {}, heights: {}, dirty: true };
    return;
  }
  const rooms = Array.isArray(review.rooms) ? review.rooms : [];
  if (patternState.generation !== review.generation) {
    const suggested = Array.isArray(review.suggestedRooms) ? review.suggestedRooms.filter((index) => rooms[index] && rooms[index].band !== "rejected") : [];
    const options = {};
    const heights = {};
    (review.roles || []).forEach((role) => {
      if (role && role.role && role.suggestedOption) {
        options[role.role] = role.suggestedOption;
      }
    });
    patternState = {
      generation: review.generation || "",
      selected: suggested,
      options,
      heights,
      roleNames: (review.roles || []).map((role) => role && role.role).filter(Boolean),
      dirty: true
    };
  }
  if (selectedRoom >= rooms.length) {
    selectedRoom = 0;
  }
  const counts = `${review.candidateCount || 0} candidate rooms · ${review.highCount || 0} high · ${review.lowerCount || 0} lower · ${review.rejectedCount || 0} rejected`;
  const image = patternImage(review);
  const ready = prepared === true && patternState.dirty !== true;
  const understoodLines = Array.isArray(review.understood) ? review.understood : [];
  const understood = understoodLines.length ? `<section class="understood"><h3>Pattern logic</h3><ul>${understoodLines.map((line) => `<li>${escapeHtml(line)}</li>`).join("")}</ul></section>` : "";
  node.hidden = false;
  node.innerHTML = `<h2>Pattern review</h2>${understood}<p class="sub">${escapeHtml(counts)}</p><div class="pattern-grid"><div class="sheet">${image}${patternSvg(review.boundary, review.entrance, review.devices, true, false)}</div><div class="rooms"><div class="room-list">${rooms.map((room, index) => roomButton(room, index)).join("")}</div><div class="room-preview">${rooms.length ? roomPreview(rooms[selectedRoom]) : "<p>No Revit rooms were compared.</p>"}</div></div></div>${roleConfig(review)}<div class="pattern-actions"><button type="button" id="prepare-pattern">Prepare Revit pattern preview</button><button type="button" id="review-pattern" ${ready ? "" : "disabled"}>Review in Revit</button><button type="button" id="apply-pattern" ${ready ? "" : "disabled"}>Apply approved pattern</button></div>`;
  node.querySelectorAll("[data-room]").forEach((button) => {
    button.addEventListener("click", () => {
      selectedRoom = Number(button.dataset.room);
      renderPattern(review, prepared);
    });
  });
  node.querySelectorAll("[data-target]").forEach((input) => {
    input.addEventListener("change", () => {
      const index = Number(input.dataset.target);
      const selected = new Set(patternState.selected);
      if (input.checked) {
        selected.add(index);
      } else {
        selected.delete(index);
      }
      patternState.selected = [...selected];
      patternState.dirty = true;
      renderPattern(review, prepared);
    });
  });
  node.querySelectorAll("[data-option]").forEach((select) => {
    select.addEventListener("change", () => {
      patternState.options[select.dataset.option] = select.value;
      patternState.dirty = true;
      renderPattern(review, prepared);
    });
  });
  node.querySelectorAll("[data-height]").forEach((input) => {
    input.addEventListener("change", () => {
      patternState.heights[input.dataset.height] = input.value;
      patternState.dirty = true;
      renderPattern(review, prepared);
    });
  });
  document.getElementById("prepare-pattern").addEventListener("click", () => {
    patternState.dirty = false;
    post({ type: "preparePatternPreview", text: patternConfiguration() });
  });
  document.getElementById("review-pattern").addEventListener("click", () => {
    if (!ready) {
      return;
    }
    post({ type: "reviewPatternInRevit" });
  });
  document.getElementById("apply-pattern").addEventListener("click", () => {
    if (!ready) {
      return;
    }
    post({ type: "applyApprovedPattern" });
  });
}

function roomButton(room, index) {
  const incomplete = typeof room.adaptationStatus === "string" && room.adaptationStatus !== "resolved";
  const rejected = room.band === "rejected" || room.source === "link" || incomplete;
  const checked = patternState.selected.includes(index);
  return `<div class="room-row ${index === selectedRoom ? "selected" : ""}"><label><input type="checkbox" data-target="${index}" ${rejected ? "disabled" : ""} ${checked && !rejected ? "checked" : ""} /> ${escapeHtml(room.band || "rejected")}</label><button type="button" data-room="${index}"><strong>${escapeHtml(room.label || "Room")}</strong><span>${escapeHtml(roomBand(room))}</span></button></div>`;
}

function roleConfig(review) {
  const roles = Array.isArray(review.roles) ? review.roles : [];
  if (!roles.length) {
    return "<p class=\"sub\">No loaded type choices are available yet.</p>";
  }
  return `<div class="roles">${roles.map((role) => {
    const options = Array.isArray(role.options) ? role.options : [];
    const selected = patternState.options[role.role] || "";
    const height = patternState.heights[role.role] ?? "";
    const choices = [`<option value="">Choose a loaded type</option>`].concat(options.map((option) => `<option value="${escapeHtml(option.key || "")}" ${option.key === selected ? "selected" : ""}>${escapeHtml(option.familyName || "")} / ${escapeHtml(option.typeName || "")} · ${escapeHtml(option.placementClassification || "")}</option>`));
    return `<section><h3>${escapeHtml(role.role || "")}</h3><p class="sub">${role.familyHint ? "Hint: " + escapeHtml(role.familyHint) : "No family hint"}${role.ambiguous ? " · choose one" : ""}</p><label>Loaded type <select data-option="${escapeHtml(role.role || "")}">${choices.join("")}</select></label><label>Mounting height (mm) <input data-height="${escapeHtml(role.role || "")}" type="number" min="0" max="6000" step="1" value="${escapeHtml(height)}" /></label></section>`;
  }).join("")}</div>`;
}

function patternConfiguration() {
  const roles = (patternState.roleNames || []).map((role) => {
    const height = patternState.heights[role];
    return {
      role,
      option: patternState.options[role] || "",
      height_mm: height === undefined || height === "" ? null : Number(height)
    };
  });
  return JSON.stringify({ rooms: patternState.selected, roles });
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
  const labels = [];
  const markers = (devices || []).map((device) => {
    const x = unit(device.x);
    const y = yOf(device.y);
    let label = "";
    if (showRoles) {
      const text = device.role || "";
      const left = x > 0.7;
      let labelY = y - 0.015;
      while (labels.some((placed) => Math.abs(placed.y - labelY) < 0.035 && Math.abs(placed.x - x) < 0.2)) {
        labelY += 0.04;
      }
      labels.push({ x, y: labelY });
      label = `<text x="${left ? x - 0.02 : x + 0.02}" y="${Math.min(0.98, labelY)}"${left ? " text-anchor=\"end\"" : ""}>${escapeHtml(text)}</text>`;
    }
    return `<circle cx="${x}" cy="${y}" r="0.012"></circle>${label}`;
  }).join("");
  const door = entrance ? `<rect x="${unit(entrance.x) - 0.01}" y="${yOf(entrance.y) - 0.01}" width="0.02" height="0.02"></rect>` : "";
  return `<svg viewBox="0 0 1 1" role="img" aria-label="Room pattern">${points ? `<polygon points="${points}"></polygon>` : ""}${door}${markers}</svg>`;
}

function roomPreview(room) {
  const adaptation = `${room.rotationDegrees || 0}°${room.mirrored ? " · mirrored" : ""}`;
  const outline = Array.isArray(room.outline) ? room.outline : [];
  const devices = Array.isArray(room.devices) ? room.devices : [];
  const notes = Array.isArray(room.reasoning) ? room.reasoning : [];
  const reasoning = notes.length ? `<ul class="reasoning">${notes.map((line) => line === "Resolved" || line === "Unresolved" ? `<li class="heading">${escapeHtml(line)}</li>` : `<li>${escapeHtml(line)}</li>`).join("")}</ul>` : "";
  const status = room.adaptationStatus ? ` · adaptation ${escapeHtml(room.adaptationStatus)}` : "";
  return `<p><strong>${escapeHtml(room.label || "Room")}</strong> ${escapeHtml(room.level || "")}</p><p class="sub">geometry ${escapeHtml(room.band || "rejected")} · ${escapeHtml(String(room.score ?? 0))} · ${escapeHtml(adaptation)}${status}${room.reason ? " · " + escapeHtml(room.reason) : ""}</p>${reasoning}${patternSvg(outline, null, devices, true, true)}`;
}

function roomBand(room) {
  const mirror = room.mirrored ? " · mirrored" : "";
  const source = room.source === "link" ? ` · link${room.linkLabel ? " " + room.linkLabel : ""}` : room.source === "host" ? " · host" : "";
  const status = room.adaptationStatus ? ` · ${room.adaptationStatus}` : "";
  return `${room.band || "rejected"} · ${room.rotationDegrees || 0}°${mirror}${source}${status}`;
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
