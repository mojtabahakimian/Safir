// @ts-check
const { defineConfig, devices } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

/**
 * پیدا کردن Chromium بدون دانلود دوباره.
 *
 * بعضی محیط‌ها (Jules، Claude Code on the web) از قبل مرورگر را در
 * PLAYWRIGHT_BROWSERS_PATH دارند ولی شماره‌ی build آن با نسخه‌ی
 * @playwright/test نصب‌شده یکی نیست. در آن حالت Playwright ادعا می‌کند
 * مرورگر نصب نیست. اینجا اگر مرورگری آنجا باشد مستقیم مسیرش را می‌دهیم.
 */
function findChromium() {
  if (process.env.CHROMIUM_PATH) return process.env.CHROMIUM_PATH;
  const root = process.env.PLAYWRIGHT_BROWSERS_PATH;
  if (!root || !fs.existsSync(root)) return undefined;   // بگذار خود Playwright تصمیم بگیرد
  for (const dir of fs.readdirSync(root).filter(d => d.startsWith('chromium-')).sort().reverse()) {
    for (const rel of ['chrome-linux/chrome', 'chrome-linux/headless_shell']) {
      const p = path.join(root, dir, rel);
      if (fs.existsSync(p)) return p;
    }
  }
  return undefined;
}

const executablePath = findChromium();
const baseURL = process.env.APP_URL || 'http://127.0.0.1:5080';

module.exports = defineConfig({
  testDir: './tests',
  // Blazor WebAssembly اولین بار باید کل runtime دات‌نت را دانلود کند.
  timeout: 120_000,
  expect: { timeout: 30_000 },
  fullyParallel: false,          // همه‌ی تست‌ها روی یک دیتابیس مشترک کار می‌کنند
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL,
    locale: 'fa-IR',
    timezoneId: 'Asia/Tehran',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
    ...(executablePath ? { launchOptions: { executablePath } } : {}),
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
