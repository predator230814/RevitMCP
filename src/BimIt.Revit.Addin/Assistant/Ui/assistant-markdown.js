// Bounded presentation for untrusted Assistant text.
// Every text fragment is escaped before it is placed in a tag this file emits.
// The tag set is closed. Model text never becomes raw HTML.
function renderAssistantMarkdown(source) {
  const text = String(source == null ? "" : source).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  const lines = text.split("\n");
  let html = "";
  let index = 0;
  while (index < lines.length) {
    if (lines[index].trim() === "") {
      index += 1;
      continue;
    }
    if (lines[index].startsWith("```")) {
      const body = [];
      index += 1;
      while (index < lines.length && !lines[index].startsWith("```")) {
        body.push(lines[index]);
        index += 1;
      }
      if (index < lines.length) {
        index += 1;
      }
      html += "<pre><code>" + escapeHtml(body.join("\n")) + "</code></pre>";
      continue;
    }
    const heading = /^(#{1,6})[ \t]+(\S.*)$/.exec(lines[index]);
    if (heading) {
      const level = heading[1].length <= 1 ? "h4" : heading[1].length === 2 ? "h5" : "h6";
      html += "<" + level + ">" + renderInline(heading[2]) + "</" + level + ">";
      index += 1;
      continue;
    }
    if (isTableStart(lines, index)) {
      const width = splitRow(lines[index]).length;
      let table = "<div class=\"table-scroll\"><table><thead><tr>" + cells(splitRow(lines[index]), "th", width) + "</tr></thead><tbody>";
      index += 2;
      while (index < lines.length && isTableRow(lines[index])) {
        table += "<tr>" + cells(splitRow(lines[index]), "td", width) + "</tr>";
        index += 1;
      }
      html += table + "</tbody></table></div>";
      continue;
    }
    if (/^[-*][ \t]+\S/.test(lines[index])) {
      let list = "<ul>";
      while (index < lines.length && /^[-*][ \t]+\S/.test(lines[index])) {
        list += "<li>" + renderInline(lines[index].replace(/^[-*][ \t]+/, "")) + "</li>";
        index += 1;
      }
      html += list + "</ul>";
      continue;
    }
    if (/^\d+\.[ \t]+\S/.test(lines[index])) {
      let list = "<ol>";
      while (index < lines.length && /^\d+\.[ \t]+\S/.test(lines[index])) {
        list += "<li>" + renderInline(lines[index].replace(/^\d+\.[ \t]+/, "")) + "</li>";
        index += 1;
      }
      html += list + "</ol>";
      continue;
    }
    const paragraph = [];
    while (index < lines.length && lines[index].trim() !== "" && !startsStructuralBlock(lines, index)) {
      paragraph.push(lines[index]);
      index += 1;
    }
    html += "<p>" + paragraph.map(renderInline).join("<br>") + "</p>";
  }
  return html;
}

function startsStructuralBlock(lines, index) {
  const line = lines[index];
  return line.startsWith("```")
    || /^(#{1,6})[ \t]+\S/.test(line)
    || isTableStart(lines, index)
    || /^[-*][ \t]+\S/.test(line)
    || /^\d+\.[ \t]+\S/.test(line);
}

function isTableStart(lines, index) {
  return index + 1 < lines.length && isTableRow(lines[index]) && isSeparator(lines[index + 1]);
}

function isTableRow(line) {
  return line.indexOf("|") >= 0 && line.trim() !== "" && !isSeparator(line);
}

function isSeparator(line) {
  if (line.indexOf("|") < 0 && line.indexOf("-") < 0) {
    return false;
  }
  const cells = splitRow(line);
  return cells.length > 0 && cells.every(function (cell) {
    return /^:?-{3,}:?$/.test(cell);
  });
}

function splitRow(line) {
  const protectedPipes = line.replace(/\\\|/g, "\u0000");
  const parts = protectedPipes.split("|").map(function (part) {
    return part.replace(/\u0000/g, "|").trim();
  });
  if (parts.length && parts[0] === "") {
    parts.shift();
  }
  if (parts.length && parts[parts.length - 1] === "") {
    parts.pop();
  }
  return parts;
}

function cells(values, tag, width) {
  let html = "";
  const count = Math.max(width, values.length);
  for (let index = 0; index < count; index += 1) {
    html += "<" + tag + ">" + renderInline(values[index] || "") + "</" + tag + ">";
  }
  return html;
}

function renderInline(source) {
  let html = "";
  let index = 0;
  while (index < source.length) {
    if (source[index] === "`") {
      const end = source.indexOf("`", index + 1);
      if (end > index) {
        html += "<code>" + escapeHtml(source.slice(index + 1, end)) + "</code>";
        index = end + 1;
        continue;
      }
    }
    if (source.startsWith("**", index) || source.startsWith("__", index)) {
      const marker = source.slice(index, index + 2);
      const end = source.indexOf(marker, index + 2);
      if (end > index + 1) {
        html += "<strong>" + renderInline(source.slice(index + 2, end)) + "</strong>";
        index = end + 2;
        continue;
      }
    }
    if (source[index] === "*" && source[index + 1] !== "*") {
      const end = source.indexOf("*", index + 1);
      if (end > index + 1 && source[end + 1] !== "*") {
        html += "<em>" + renderInline(source.slice(index + 1, end)) + "</em>";
        index = end + 1;
        continue;
      }
    }
    const link = /^\[([^\]\n]+)\]\(([^()\s]+)\)/.exec(source.slice(index));
    if (link && !source.startsWith("![", index)) {
      html += renderInline(link[1]);
      index += link[0].length;
      continue;
    }
    const image = /^!\[([^\]]*)\]\([^()\s]*\)/.exec(source.slice(index));
    if (image) {
      html += renderInline(image[1]);
      index += image[0].length;
      continue;
    }
    let next = index + 1;
    while (next < source.length && source[next] !== "`" && source[next] !== "*" && source[next] !== "_" && source[next] !== "[" && source[next] !== "!") {
      next += 1;
    }
    if (source[next] === "_" && !source.startsWith("__", next)) {
      next += 1;
    }
    html += escapeHtml(source.slice(index, next));
    index = next;
  }
  return html;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, function (character) {
    return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[character];
  });
}
