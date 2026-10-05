const hostToUi = document.querySelector("#host-to-ui");
const uiToHost = document.querySelector("#ui-to-host");
const detail = document.querySelector("#detail");
const pingButton = document.querySelector("#ping");

function setStatus(element, text, ok) {
  element.textContent = text;
  element.className = ok ? "pass" : "fail";
}

function renderHostReady(message) {
  document.querySelector("#revit").textContent = message.revitVersion + " " + message.revitBuild;
  document.querySelector("#dotnet").textContent = message.dotnetRuntime || "unknown";
  document.querySelector("#sdk").textContent = message.webView2SdkVersion || "unknown";
  document.querySelector("#browser").textContent = message.webView2RuntimeVersion || "unknown";
  document.querySelector("#origin").textContent = message.contentOrigin || "unknown";
  document.querySelector("#initialized").textContent = message.coreWebView2Initialized ? "initialized" : "not initialized";
  setStatus(hostToUi, "PASS", true);

  const list = document.querySelector("#assemblies");
  list.replaceChildren();
  const assemblies = Array.isArray(message.assemblies) ? message.assemblies : [];
  if (assemblies.length === 0) {
    const item = document.createElement("li");
    item.textContent = "No Microsoft.Web.WebView2 assemblies were reported.";
    list.append(item);
    return;
  }

  for (const assembly of assemblies) {
    const item = document.createElement("li");
    item.textContent = assembly.name + " " + assembly.version + " " + assembly.location;
    list.append(item);
  }
}

function onHostMessage(event) {
  const message = event.data;
  if (!message || typeof message.type !== "string") {
    setStatus(hostToUi, "FAIL", false);
    detail.textContent = "The host sent a message without a type.";
    return;
  }

  if (message.type === "hostReady") {
    renderHostReady(message);
    return;
  }

  if (message.type === "pong") {
    setStatus(hostToUi, "PASS", true);
    setStatus(uiToHost, "PASS", true);
    detail.textContent = "Round trip completed.";
    return;
  }

  if (message.type === "rejected") {
    setStatus(uiToHost, "FAIL", false);
    detail.textContent = "Host rejected the message (" + (message.reason || "unknown") + ").";
    return;
  }

  setStatus(hostToUi, "FAIL", false);
  detail.textContent = "Ignored an unexpected host message.";
}

if (!window.chrome || !window.chrome.webview) {
  setStatus(hostToUi, "FAIL", false);
  detail.textContent = "WebView2 messaging is unavailable.";
  pingButton.disabled = true;
} else {
  window.chrome.webview.addEventListener("message", onHostMessage);
  pingButton.addEventListener("click", function () {
    uiToHost.textContent = "sending";
    uiToHost.className = "";
    window.chrome.webview.postMessage({ type: "ping" });
  });
}
