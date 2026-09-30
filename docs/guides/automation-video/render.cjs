// ساختِ فیلمِ آموزشیِ کارتابل اتوماسیون از روی guide.html
//
//   node docs/guides/automation-video/render.cjs                 ← فیلمِ کامل (MP4 با صدا)
//   node docs/guides/automation-video/render.cjs --shots 5,12,20 ← فقط چند فریمِ PNG برای بازبینی
//
// نیازها: Playwright (همان که Tests/e2e نصب کرده)، ffmpeg در PATH، و python + numpy برای صدا.
// هر فریم با render(t) رسم و جداگانه عکس گرفته می‌شود، پس خروجی به سرعتِ سیستم بستگی ندارد.

const path = require('path');
const fs = require('fs');
const { spawn, spawnSync } = require('child_process');
const { chromium } = require(path.join(__dirname, '../../../Tests/e2e/node_modules/playwright'));

const FPS = 30;
const HERE = __dirname;
const OUT_DIR = path.join(HERE, '../../../Client/wwwroot/media');
const OUT = path.join(OUT_DIR, 'automation-guide.mp4');
const POSTER = path.join(OUT_DIR, 'automation-guide.jpg');
const TMP = path.join(HERE, '.build');

(async () => {
  const args = process.argv.slice(2);
  const shotsArg = args.indexOf('--shots');
  fs.mkdirSync(TMP, { recursive: true });

  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  await page.goto('file://' + path.join(HERE, 'guide.html').replace(/\\/g, '/') + '?capture');
  await page.evaluate(() => document.fonts.ready);

  if (shotsArg >= 0) {
    for (const t of args[shotsArg + 1].split(',').map(Number)) {
      await page.evaluate(x => window.render(x), t);
      const file = path.join(TMP, `shot-${String(t).replace('.', '_')}.png`);
      await page.screenshot({ path: file });
      console.log(file);
    }
    await browser.close();
    return;
  }

  // ── صدا ──
  const duration = await page.evaluate(() => window.DURATION);
  const sfx = await page.evaluate(() => window.SFX);
  const sfxFile = path.join(TMP, 'sfx.json');
  const wav = path.join(TMP, 'audio.wav');
  fs.writeFileSync(sfxFile, JSON.stringify({ duration, events: sfx }));
  const py = spawnSync('python', [path.join(HERE, 'audio.py'), sfxFile, wav], { stdio: 'inherit' });
  if (py.status !== 0) throw new Error('audio.py failed');

  // ── تصویر: JPEGها مستقیم به ffmpeg ──
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const ff = spawn('ffmpeg', [
    '-y', '-loglevel', 'error',
    '-framerate', String(FPS), '-f', 'image2pipe', '-c:v', 'mjpeg', '-i', '-',
    '-i', wav,
    '-c:v', 'libx264', '-preset', 'slow', '-crf', '23', '-pix_fmt', 'yuv420p', '-tune', 'animation',
    '-c:a', 'aac', '-b:a', '128k', '-shortest', '-movflags', '+faststart',
    OUT
  ], { stdio: ['pipe', 'inherit', 'inherit'] });
  const done = new Promise((res, rej) => ff.on('close', c => c === 0 ? res() : rej(new Error('ffmpeg ' + c))));

  const total = Math.round(duration * FPS);
  const started = Date.now();
  for (let f = 0; f < total; f++) {
    const t = f / FPS;
    await page.evaluate(x => window.render(x), t);
    const buf = await page.screenshot({ type: 'jpeg', quality: 93 });
    if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r));
    if (f % 150 === 0) console.log(`frame ${f}/${total}  (${((Date.now() - started) / 1000).toFixed(0)}s)`);
  }
  ff.stdin.end();
  await done;

  // پوستر: فریمی از نمای کلی
  await page.evaluate(() => window.render(6.2));
  await page.screenshot({ path: POSTER, type: 'jpeg', quality: 85 });
  await browser.close();
  console.log('✓', OUT, (fs.statSync(OUT).size / 1e6).toFixed(1) + ' MB');
})().catch(e => { console.error(e); process.exit(1); });
