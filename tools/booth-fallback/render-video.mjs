// glasses-fallback.html を MP4 に書き出す(展示デモの予備の予備: 動画プレーヤーだけで流せる)。
//
//   node tools/booth-fallback/render-video.mjs [出力.mp4] [--fps 60] [--delay 10]
//
// 要るもの: Node 22 以上(WebSocket が組み込み)・Chrome か Edge・ffmpeg(PATH に)。npm のパッケージは使わない。
// ページを ?render=1 で開き、時間を1フレームずつ進めて(window.__video.next)PNG を ffmpeg へ流す —
// 実時間で録画しないので、書き出しが遅いマシンでもコマ落ちしない。乱数も固定なので毎回同じ動画になる。
import { spawn } from "node:child_process";
import { once } from "node:events";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const option = (name, fallback) => {
  const i = args.indexOf(name);
  if (i < 0) return fallback;
  const value = args[i + 1];
  args.splice(i, 2);
  return value;
};
const fps = Number(option("--fps", "60"));
const delay = Number(option("--delay", "10"));
const out = path.resolve(args[0] ?? path.join(here, "glasses-fallback.mp4"));

const browserCandidates = [
  process.env.CHROME,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
  "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
  "/usr/bin/google-chrome", "/usr/bin/chromium",
].filter(Boolean);
const browserPath = browserCandidates.find(p => fs.existsSync(p));
if (!browserPath) throw new Error("Chrome / Edge が見つからない(CHROME=<パス> で指定する)");

const port = 9300 + Math.floor(Math.random() * 600);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), "glasses-render-"));
const browser = spawn(browserPath, [
  "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  "--window-size=1920,1080", "--force-device-scale-factor=1", "--hide-scrollbars",
  "--no-first-run", "--no-default-browser-check", "about:blank",
], { stdio: "ignore" });

const sleep = ms => new Promise(r => setTimeout(r, ms));
async function pageSocketUrl() {
  for (let i = 0; i < 100; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
      const page = list.find(t => t.type === "page");
      if (page) return page.webSocketDebuggerUrl;
    } catch { /* まだ起動中 */ }
    await sleep(100);
  }
  throw new Error("ヘッドレスのブラウザに繋がらない");
}

const ws = new WebSocket(await pageSocketUrl());
await once(ws, "open");
let nextId = 1;
const pending = new Map();
ws.addEventListener("message", e => {
  const msg = JSON.parse(e.data);
  if (msg.id && pending.has(msg.id)) {
    const { resolve, reject } = pending.get(msg.id);
    pending.delete(msg.id);
    msg.error ? reject(new Error(msg.error.message)) : resolve(msg.result);
  }
});
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const id = nextId++;
  pending.set(id, { resolve, reject });
  ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async expression => {
  const r = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.text);
  return r.result.value;
};

let ffmpeg;
try {
  const page = pathToFileURL(path.join(here, "glasses-fallback.html")).href + `?render=1&fps=${fps}&delay=${delay}`;
  await send("Page.navigate", { url: page });
  for (let i = 0; !(await evaluate("!!window.__video").catch(() => false)); i++) {
    if (i > 100) throw new Error("ページの読み込みが終わらない");
    await sleep(100);
  }
  await evaluate("document.fonts.ready.then(() => true)");
  const frames = await evaluate("window.__video.frames");
  console.log(`${frames} フレーム(${(frames / fps).toFixed(1)}秒・${fps}fps)→ ${out}`);

  ffmpeg = spawn("ffmpeg", [
    "-y", "-loglevel", "error", "-f", "image2pipe", "-framerate", String(fps), "-c:v", "png", "-i", "-",
    "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p", "-movflags", "+faststart", out,
  ], { stdio: ["pipe", "inherit", "inherit"] });
  const ffmpegDone = once(ffmpeg, "close");

  const started = Date.now();
  for (let i = 0; i < frames; i++) {
    const dataUrl = await evaluate("window.__video.next()");
    const png = Buffer.from(dataUrl.slice(dataUrl.indexOf(",") + 1), "base64");
    if (!ffmpeg.stdin.write(png)) await once(ffmpeg.stdin, "drain");
    if (i % (fps * 5) === 0) process.stdout.write(`  ${(i / fps).toFixed(0)}秒 / ${(frames / fps).toFixed(0)}秒\r`);
  }
  ffmpeg.stdin.end();
  const [code] = await ffmpegDone;
  if (code !== 0) throw new Error(`ffmpeg が終了コード ${code} で止まった`);
  console.log(`\n書き出し完了(${((Date.now() - started) / 1000).toFixed(0)}秒): ${out}`);
} finally {
  ws.close();
  browser.kill();
  await sleep(300);
  fs.rmSync(profile, { recursive: true, force: true });
}
