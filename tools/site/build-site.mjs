import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const docsRoot = path.join(root, "docs");
const outRoot = path.join(root, "site");

fs.rmSync(outRoot, { recursive: true, force: true });
fs.mkdirSync(outRoot, { recursive: true });
fs.cpSync(path.join(docsRoot, "assets"), path.join(outRoot, "assets"), { recursive: true });

const pages = fs.readdirSync(docsRoot)
  .filter((file) => file.endsWith(".md"))
  .map((file) => {
    const fullPath = path.join(docsRoot, file);
    const raw = fs.readFileSync(fullPath, "utf8");
    const { frontMatter, body } = parseFrontMatter(raw);
    return {
      file,
      slug: file.replace(/\.md$/, ".html"),
      title: frontMatter.title ?? titleFromMarkdown(body) ?? file,
      navOrder: Number(frontMatter.nav_order ?? 999),
      body
    };
  })
  .sort((a, b) => a.navOrder - b.navOrder || a.title.localeCompare(b.title));

for (const page of pages) {
  const html = renderPage(page, pages);
  fs.writeFileSync(path.join(outRoot, page.slug), html, "utf8");
}

fs.copyFileSync(path.join(outRoot, "index.html"), path.join(outRoot, "404.html"));
console.log(`Built ${pages.length} page(s) into ${outRoot}`);

function parseFrontMatter(raw) {
  if (!raw.startsWith("---")) {
    return { frontMatter: {}, body: raw };
  }

  const end = raw.indexOf("\n---", 3);
  if (end < 0) {
    return { frontMatter: {}, body: raw };
  }

  const frontMatterText = raw.slice(3, end).trim();
  const body = raw.slice(end + 4).trimStart();
  const frontMatter = {};
  for (const line of frontMatterText.split(/\r?\n/)) {
    const index = line.indexOf(":");
    if (index > 0) {
      frontMatter[line.slice(0, index).trim()] = line.slice(index + 1).trim();
    }
  }

  return { frontMatter, body };
}

function titleFromMarkdown(body) {
  const match = body.match(/^#\s+(.+)$/m);
  return match?.[1];
}

function renderPage(page, pages) {
  const nav = pages.map((item) => `<a href="${item.slug}">${escapeHtml(item.title)}</a>`).join("\n");
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(page.title)} - Rusty XR Companion</title>
  <link rel="icon" href="assets/favicon.ico">
  <link rel="stylesheet" href="assets/site.css">
</head>
<body>
  <header>
    <div class="brand">
      <img src="assets/rusty-xr-companion-icon.png" alt="" width="64" height="64">
      <div>
        <h1>Rusty XR Companion</h1>
        <p>Windows and Android companion utilities for public Quest development workflows.</p>
      </div>
    </div>
  </header>
  <div class="layout">
    <nav>${nav}</nav>
    <main>${markdownToHtml(page.body)}</main>
  </div>
</body>
</html>
`;
}

function markdownToHtml(markdown) {
  const lines = markdown.replace(/\r\n/g, "\n").split("\n");
  const html = [];
  let inCode = false;
  let inList = false;
  let inParagraph = false;

  const closeParagraph = () => {
    if (inParagraph) {
      html.push("</p>");
      inParagraph = false;
    }
  };
  const closeList = () => {
    if (inList) {
      html.push("</ul>");
      inList = false;
    }
  };

  for (const line of lines) {
    if (line.startsWith("```")) {
      closeParagraph();
      closeList();
      if (inCode) {
        html.push("</code></pre>");
        inCode = false;
      } else {
        html.push("<pre><code>");
        inCode = true;
      }
      continue;
    }

    if (inCode) {
      html.push(escapeHtml(line));
      continue;
    }

    if (line.trim().length === 0) {
      closeParagraph();
      closeList();
      continue;
    }

    const heading = line.match(/^(#{1,3})\s+(.+)$/);
    if (heading) {
      closeParagraph();
      closeList();
      const level = heading[1].length;
      html.push(`<h${level}>${inlineMarkdown(heading[2])}</h${level}>`);
      continue;
    }

    if (line.startsWith("- ")) {
      closeParagraph();
      if (!inList) {
        html.push("<ul>");
        inList = true;
      }
      html.push(`<li>${inlineMarkdown(line.slice(2))}</li>`);
      continue;
    }

    if (!inParagraph) {
      closeList();
      html.push("<p>");
      inParagraph = true;
    } else {
      html.push(" ");
    }
    html.push(inlineMarkdown(line.trim()));
  }

  closeParagraph();
  closeList();
  if (inCode) {
    html.push("</code></pre>");
  }

  return html.join("\n");
}

function inlineMarkdown(text) {
  return escapeHtml(text)
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2">$1</a>')
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}
