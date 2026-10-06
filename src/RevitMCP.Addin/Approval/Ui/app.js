(function () {
  var idle = document.getElementById("idle");
  var items = document.getElementById("items");
  var approve = document.getElementById("approve");
  var reject = document.getElementById("reject");
  var cancel = document.getElementById("cancel");
  var buttons = [approve, reject, cancel];
  var sessionRef = "";
  var pending = false;

  function setEnabled(enabled) {
    for (var index = 0; index < buttons.length; index++) {
      buttons[index].disabled = !enabled;
    }
  }

  function textLine(label, value) {
    var line = document.createElement("p");
    var name = document.createElement("strong");
    name.textContent = label;
    line.appendChild(name);
    line.appendChild(document.createTextNode(" " + value));
    return line;
  }

  function formatValue(value) {
    if (!value) {
      return "No value";
    }

    if (value.kind === "quantity") {
      return String(value.value) + " (" + value.unitTypeId + ")";
    }

    return String(value.value);
  }

  function clearReview() {
    sessionRef = "";
    pending = false;
    items.textContent = "";
    idle.hidden = false;
    setEnabled(false);
  }

  function renderReview(message) {
    sessionRef = typeof message.sessionRef === "string" ? message.sessionRef : "";
    pending = false;
    items.textContent = "";
    idle.hidden = true;
    var list = message.items || [];
    for (var index = 0; index < list.length; index++) {
      var item = list[index];
      var article = document.createElement("article");
      var heading = document.createElement("h2");
      heading.textContent = (item.elementName || "") + (item.elementNameTruncated ? "…" : "")
        + " / "
        + (item.categoryName || "")
        + (item.categoryNameTruncated ? "…" : "");
      article.appendChild(heading);
      article.appendChild(textLine("Parameter", (item.parameterName || "") + (item.parameterNameTruncated ? "…" : "")));
      var before = item.before && item.before.hasValue ? formatValue(item.before.value) : "No value";
      article.appendChild(textLine("Current value", before));
      article.appendChild(textLine("Proposed value", formatValue(item.proposed)));
      if (item.dataType && item.dataType.kind) {
        article.appendChild(textLine("Data type", String(item.dataType.kind)));
      }
      items.appendChild(article);
    }

    setEnabled(sessionRef.length > 0);
  }

  function send(type) {
    if (!sessionRef || pending || !window.chrome || !window.chrome.webview) {
      return;
    }

    pending = true;
    setEnabled(false);
    window.chrome.webview.postMessage({ type: type, sessionRef: sessionRef });
  }

  function onHostMessage(event) {
    var message = event.data;
    if (!message || typeof message.type !== "string") {
      return;
    }

    if (message.type === "renderReview") {
      renderReview(message);
      return;
    }

    if (message.type === "clearReview" || message.type === "unavailable") {
      clearReview();
      return;
    }

    if (message.type === "actionResult" && message.reviewRemains && sessionRef) {
      pending = false;
      setEnabled(true);
      return;
    }

    if (message.type === "actionResult") {
      clearReview();
    }
  }

  approve.addEventListener("click", function () { send("approveCurrent"); });
  reject.addEventListener("click", function () { send("rejectCurrent"); });
  cancel.addEventListener("click", function () { send("dismissCurrent"); });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", onHostMessage);
  }

  setEnabled(false);
}());
