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
  renderSkills(snapshot.skills);
  renderAttachment(snapshot.attachment);
  patternTypes = snapshot.patternTypes && typeof snapshot.patternTypes === "object" ? snapshot.patternTypes : {};
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

const settings = document.getElementById("settings");
const skillBadges = { built_in: "BUILT-IN", custom: "CUSTOM", external: "EXTERNAL" };
let selectedSkill = "";

function openSettings() {
  settings.hidden = false;
  document.getElementById("main").hidden = true;
  post({ type: "skills", action: "reload" });
}

function closeSettings() {
  settings.hidden = true;
  document.getElementById("main").hidden = false;
}

function skillBadge(source) {
  const label = skillBadges[source] || "UNKNOWN";
  return `<span class="badge badge-${escapeHtml(source || "unknown")}">${label}</span>`;
}

function renderSkills(state) {
  const list = document.getElementById("skills-list");
  const sourcesNode = document.getElementById("skills-sources");
  const messageNode = document.getElementById("skills-message");
  const detailNode = document.getElementById("skill-detail");
  const view = state && state.view && typeof state.view === "object" ? state.view : null;
  const skills = view && Array.isArray(view.skills) ? view.skills : [];
  const sources = view && Array.isArray(view.sources) ? view.sources : [];
  const notes = [];
  if (state && state.busy) {
    notes.push("Working…");
  }
  if (state && state.error) {
    notes.push(state.error);
  }
  if (view && view.message) {
    notes.push(view.message);
  }
  if (view && view.diagnostic) {
    notes.push(view.diagnostic);
  }
  messageNode.hidden = notes.length === 0;
  messageNode.className = `skills-message${(state && state.error) || (view && view.isError) ? " search-error" : ""}`;
  messageNode.textContent = notes.join(" · ");
  sourcesNode.innerHTML = sources.map((source) => {
    const remove = source.removable ? `<button type="button" data-remove-source="${escapeHtml(source.id || "")}">Remove</button>` : "";
    const diagnostic = source.diagnostic ? ` · ${escapeHtml(source.diagnostic)}` : "";
    return `<div class="skill-source">${skillBadge(source.kind)}<strong>${escapeHtml(source.label || "")}</strong><span class="sub">${escapeHtml(String(source.skillCount ?? 0))} skills · ${escapeHtml(source.status || "")}${diagnostic}</span>${remove}</div>`;
  }).join("");
  list.innerHTML = skills.length ? skills.map((skill) => {
    const selected = skill.key === selectedSkill ? " selected" : "";
    const toggle = skill.status === "invalid" ? "" : `<button type="button" data-toggle-skill="${escapeHtml(skill.key || "")}" data-enabled="${skill.enabled ? "true" : "false"}">${skill.enabled ? "Disable" : "Enable"}</button>`;
    const diagnostic = skill.diagnostic ? `<p class="skill-diagnostic">${escapeHtml(skill.diagnostic)}</p>` : "";
    return `<li class="skill-row status-${escapeHtml(skill.status || "")}${selected}"><button type="button" class="skill-open" data-skill="${escapeHtml(skill.key || "")}"><span class="skill-title"><strong>${escapeHtml(skill.name || "")}</strong>${skillBadge(skill.source)}<span class="skill-status">${escapeHtml(skill.status || "")}</span></span><span class="sub">${escapeHtml(skill.description || "")}</span></button>${diagnostic}${toggle}</li>`;
  }).join("") : `<li class="sub">No Skills found. Import one, or add a skills folder.</li>`;
  const detail = view && view.detail && view.detail.key === selectedSkill ? view.detail : null;
  const entry = skills.find((skill) => skill.key === selectedSkill);
  if (!entry) {
    detailNode.hidden = true;
    detailNode.innerHTML = "";
  } else {
    detailNode.hidden = false;
    const files = (detail && Array.isArray(detail.files) ? detail.files : entry.files || []).filter((file) => file !== "SKILL.md");
    const content = detail && typeof detail.content === "string" ? `<pre class="skill-markdown">${escapeHtml(detail.content)}</pre>${detail.truncated ? "<p class=\"sub\">Truncated.</p>" : ""}` : `<p class="sub">${escapeHtml(detail && detail.message ? detail.message : "Loading SKILL.md…")}</p>`;
    const readOnly = entry.source === "built_in" ? "<p class=\"sub\">Built-in Skills are read-only and update with BIM-IT releases.</p>" : "";
    detailNode.innerHTML = `<h3>${escapeHtml(entry.name || "")} ${skillBadge(entry.source)}</h3><p>${escapeHtml(entry.description || "")}</p><dl class="skill-meta"><dt>Source</dt><dd>${escapeHtml(entry.sourceLabel || "")}</dd><dt>Status</dt><dd>${escapeHtml(entry.status || "")}</dd><dt>SHA-256</dt><dd class="hash">${escapeHtml(entry.sha256 || "not available")}</dd><dt>Reference files</dt><dd>${files.length ? files.map((file) => escapeHtml(file)).join("<br />") : "None"}</dd></dl>${readOnly}${content}`;
  }
  list.querySelectorAll("[data-skill]").forEach((button) => {
    button.addEventListener("click", () => {
      selectedSkill = button.dataset.skill;
      post({ type: "skills", action: "detail", text: selectedSkill });
    });
  });
  list.querySelectorAll("[data-toggle-skill]").forEach((button) => {
    button.addEventListener("click", () => post({ type: "skills", action: button.dataset.enabled === "true" ? "disable" : "enable", text: button.dataset.toggleSkill }));
  });
  sourcesNode.querySelectorAll("[data-remove-source]").forEach((button) => {
    button.addEventListener("click", () => post({ type: "skills", action: "removeSource", text: button.dataset.removeSource }));
  });
}

let selectedRoom = 0;
let patternTypes = {};
const patternQueries = {};
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
  node.querySelectorAll("[data-type-search]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const role = form.dataset.typeSearch;
      const input = form.querySelector("input");
      const query = input ? input.value.trim() : "";
      patternQueries[role] = query;
      if (query) {
        post({ type: "searchPatternTypes", action: role, text: query });
      }
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
  const rejected = room.band === "rejected" || incomplete;
  const checked = patternState.selected.includes(index);
  return `<div class="room-row ${index === selectedRoom ? "selected" : ""}"><label><input type="checkbox" data-target="${index}" ${rejected ? "disabled" : ""} ${checked && !rejected ? "checked" : ""} /> ${escapeHtml(room.band || "rejected")}</label><button type="button" data-room="${index}"><strong>${escapeHtml(room.label || "Room")}</strong><span>${escapeHtml(roomBand(room))}</span></button></div>`;
}

function roleConfig(review) {
  const roles = Array.isArray(review.roles) ? review.roles : [];
  if (!roles.length) {
    return "<p class=\"sub\">No loaded type choices are available yet.</p>";
  }
  const linkedWalls = linkedBoundaryRoles(review);
  return `<div class="roles">${roles.map((role) => {
    const options = roleOptions(role);
    const search = patternTypes[role.role] || null;
    const selected = patternState.options[role.role] || "";
    const height = patternState.heights[role.role] ?? "";
    const linked = linkedWalls.has(role.role);
    const omitted = selected === "omit";
    const choices = [`<option value="">Choose a loaded type</option>`, `<option value="omit" ${omitted ? "selected" : ""}>Omit this role (place nothing)</option>`].concat(options.map((option) => {
      const blocked = linked && option.placementClassification === "one_level_based_hosted";
      return `<option value="${escapeHtml(option.key || "")}" ${option.key === selected ? "selected" : ""} ${blocked ? "disabled" : ""}>${escapeHtml(option.familyName || "")} / ${escapeHtml(option.typeName || "")} · ${escapeHtml(option.placementClassification || "")}${blocked ? " · host walls only" : ""}</option>`;
    }));
    const chosen = options.find((option) => option.key === selected);
    const warning = linked && chosen && chosen.placementClassification === "one_level_based_hosted" ? " · linked walls need a work-plane-based or level-based type" : "";
    return `<section><h3>${escapeHtml(role.role || "")}</h3><p class="sub">${role.familyHint ? "Hint: " + escapeHtml(role.familyHint) : "No family hint"}${role.ambiguous ? " · choose one" : ""}${escapeHtml(warning)}</p><form class="type-search" data-type-search="${escapeHtml(role.role || "")}"><label>Search loaded types <input type="search" maxlength="80" placeholder="Family or type name" value="${escapeHtml(patternQueries[role.role] || "")}" /></label><button type="submit">Search</button></form>${search && search.message ? `<p class="sub${search.isError ? " search-error" : ""}">${escapeHtml(search.message)}</p>` : ""}<label>Loaded type <select data-option="${escapeHtml(role.role || "")}">${choices.join("")}</select></label><label>Mounting height (mm) <input data-height="${escapeHtml(role.role || "")}" type="number" min="0" max="6000" step="1" value="${omitted ? "" : escapeHtml(height)}" ${omitted ? "disabled" : ""} /></label></section>`;
  }).join("")}</div>`;
}

function roleOptions(role) {
  const merged = [];
  const seen = new Set();
  const add = (option) => {
    if (option && typeof option.key === "string" && !seen.has(option.key)) {
      seen.add(option.key);
      merged.push(option);
    }
  };
  (Array.isArray(role.options) ? role.options : []).forEach(add);
  const search = patternTypes[role.role];
  const found = search && search.result && Array.isArray(search.result.options) ? search.result.options : [];
  found.forEach(add);
  return merged;
}

function linkedBoundaryRoles(review) {
  const rooms = Array.isArray(review.rooms) ? review.rooms : [];
  const roles = new Set();
  patternState.selected.forEach((index) => {
    const room = rooms[index];
    if (!room || room.source !== "link" || !Array.isArray(room.devices)) {
      return;
    }
    room.devices.forEach((device) => {
      if (device && device.placement === "boundary" && device.role) {
        roles.add(device.role);
      }
    });
  });
  return roles;
}

function patternConfiguration() {
  const roles = (patternState.roleNames || []).map((role) => {
    const height = patternState.heights[role];
    const option = patternState.options[role] || "";
    if (option === "omit") {
      return { role, option };
    }
    return {
      role,
      option,
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
      let labelY = Math.max(0.04, y - 0.015);
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
document.getElementById("settings-open").addEventListener("click", openSettings);
document.getElementById("settings-close").addEventListener("click", closeSettings);
document.getElementById("skills-reload").addEventListener("click", () => post({ type: "skills", action: "reload" }));
document.getElementById("skills-import").addEventListener("click", () => post({ type: "skills", action: "import" }));
document.getElementById("skills-add-source").addEventListener("click", () => post({ type: "skills", action: "addSource" }));
document.getElementById("skills-open-custom").addEventListener("click", () => post({ type: "skills", action: "openCustomFolder" }));
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
