#!/usr/bin/env node
// Serves docs/features (design packages and their mocks) at a local URL, so people and agents
// can open mocks in a browser. Read-only, no dependencies, localhost-only by default.
//   npm run gallery            -> http://127.0.0.1:4178/
//   GALLERY_PORT=5000 npm run gallery
import { createServer } from "node:http";
import { readFile, readdir, stat } from "node:fs/promises";
import { extname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("../docs/features", import.meta.url)));
const host = process.env.GALLERY_HOST || "127.0.0.1";
const port = Number(process.env.GALLERY_PORT || 4178);

const types = {
  ".html": "text/html; charset=utf-8",
  ".md": "text/plain; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".webp": "image/webp",
  ".gif": "image/gif",
  ".woff2": "font/woff2",
};

const escape = (text) => String(text).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

async function exists(path) {
  try { await stat(path); return true; } catch { return false; }
}

async function packages() {
  const entries = await readdir(root, { withFileTypes: true });
  const found = [];
  for (const entry of entries) {
    if (!entry.isDirectory() || entry.name.startsWith(".")) continue;
    const dir = join(root, entry.name);
    const brief = await readFile(join(dir, "README.md"), "utf8").catch(() => null);
    if (brief === null) continue;
    const title = (brief.match(/^#\s+(.+)$/m)?.[1] ?? entry.name).trim();
    const status = brief.match(/^\s*-\s+\*\*Status:\*\*\s+(.+)$/m)?.[1]?.trim() ?? "—";
    const fixtures = (await readdir(join(dir, "fixtures")).catch(() => [])).filter((f) => f.endsWith(".json")).sort();
    found.push({ slug: entry.name, title, status, fixtures, mock: await exists(join(dir, "mock.html")), review: await exists(join(dir, "review.md")) });
  }
  return found.sort((a, b) => (a.slug === "_template") - (b.slug === "_template") || a.slug.localeCompare(b.slug));
}

async function indexPage() {
  const list = await packages();
  const rows = list.map((p) => {
    const base = `/${encodeURIComponent(p.slug)}/`;
    const links = [
      p.mock && `<a href="${base}mock.html">Mock</a>`,
      `<a href="${base}README.md">Brief</a>`,
      p.review && `<a href="${base}review.md">Review</a>`,
    ].filter(Boolean).join(" · ");
    const fixtures = p.fixtures.length
      ? `<details><summary>${p.fixtures.length} fixture${p.fixtures.length === 1 ? "" : "s"}</summary><ul>${p.fixtures.map((f) => `<li><a href="${base}fixtures/${encodeURIComponent(f)}">${escape(f)}</a></li>`).join("")}</ul></details>`
      : "";
    return `<li class="pkg${p.slug === "_template" ? " template" : ""}"><div class="row"><h2>${escape(p.slug === "_template" ? "Template" : p.title)}</h2><span class="status">${escape(p.slug === "_template" ? "starter files" : p.status)}</span></div><p>${links}</p>${fixtures}<code>${escape(base)}</code></li>`;
  }).join("");
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Design gallery · Homelab Control</title>
<style>
:root{color-scheme:light;--bg:#f7f8fa;--card:#fff;--fg:#24312e;--muted:#52655b;--line:#e0e7e2;--link:#246348}
@media (prefers-color-scheme:dark){:root{color-scheme:dark;--bg:#0f1713;--card:#19251f;--fg:#e4eee7;--muted:#b2c3b8;--line:#43574b;--link:#8dd6b2}}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 Inter,ui-sans-serif,system-ui,sans-serif}
main{max-width:860px;margin:0 auto;padding:40px 16px 64px}
h1{font-size:28px;letter-spacing:-.8px;margin:6px 0 4px}h2{font-size:17px;margin:0}
.eyebrow{font-size:11px;letter-spacing:2px;font-weight:600;color:var(--muted);margin:0}
.lede{color:var(--muted);margin:0 0 28px}
ul.list{list-style:none;margin:0;padding:0;display:grid;gap:12px}
.pkg{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:18px 20px;display:grid;gap:8px}
.pkg.template{opacity:.8}
.row{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:8px}
.status{font-size:12px;font-weight:600;padding:2px 9px;border-radius:999px;border:1px solid var(--line);color:var(--muted)}
.pkg p{margin:0}a{color:var(--link);font-weight:600}a:focus-visible,summary:focus-visible{outline:2px solid var(--link);outline-offset:2px}
code{font-size:12px;color:var(--muted)}details ul{margin:6px 0 0;padding-left:18px}summary{cursor:pointer;color:var(--muted)}
</style></head><body><main><p class="eyebrow">HOMELAB CONTROL</p><h1>Design gallery</h1>
<p class="lede">Feature packages from <code>docs/features</code>. Files are read from disk on every request, so reload after an edit.</p>
<ul class="list">${rows || "<li>No packages yet.</li>"}</ul></main></body></html>`;
}

function send(res, status, body, type = "text/plain; charset=utf-8") {
  res.writeHead(status, { "Content-Type": type, "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" });
  res.end(body);
}

const server = createServer(async (req, res) => {
  if (req.method !== "GET" && req.method !== "HEAD") return send(res, 405, "Read-only gallery.");
  let pathname;
  try { pathname = decodeURIComponent(new URL(req.url, "http://gallery").pathname); } catch { return send(res, 400, "Bad request."); }
  if (pathname === "/") return send(res, 200, await indexPage(), types[".html"]);

  const target = resolve(root, "." + pathname);
  const rel = relative(root, target);
  // Stay inside docs/features and never serve dotfiles.
  if (rel.startsWith("..") || rel.split(sep).some((part) => part.startsWith("."))) return send(res, 404, "Not found.");
  try {
    const info = await stat(target);
    if (info.isDirectory()) {
      const next = (await exists(join(target, "mock.html"))) ? "mock.html" : "README.md";
      res.writeHead(302, { Location: `/${rel.split(sep).map(encodeURIComponent).join("/")}/${next}` });
      return res.end();
    }
    const type = types[extname(target).toLowerCase()];
    if (!type) return send(res, 404, "Not found.");
    send(res, 200, req.method === "HEAD" ? "" : await readFile(target), type);
  } catch {
    send(res, 404, "Not found.");
  }
});

server.on("error", (error) => {
  console.error(error.code === "EADDRINUSE" ? `Port ${port} is in use. Try GALLERY_PORT=<port> npm run gallery.` : error.message);
  process.exit(1);
});
server.listen(port, host, () => {
  console.log(`Design gallery: http://${host}:${port}/`);
  console.log(`Serving ${root}`);
});
