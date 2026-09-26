// @ts-check
const { test, expect } = require('@playwright/test');
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const webRoot = path.resolve(__dirname, '../../../Client/wwwroot');
const updateScript = fs.readFileSync(path.join(webRoot, 'js/app-update.js'), 'utf8');
const workerScript = fs.readFileSync(path.join(webRoot, 'service-worker.published.js'), 'utf8');
const appHtml = fs.readFileSync(path.join(webRoot, 'index.html'), 'utf8');
const updateModuleUrl = appHtml.match(/import \{ initialize \} from '([^']+)'/)[1];
let server, origin, release, delayAssetMs, failAsset, assetRequested, moduleCacheControl;

function moduleSource(version) {
  // Change the module itself, not just the HTML, to catch stale JavaScript.
  return `${updateScript}\nexport const fixtureRelease = ${version};`;
}

function html(version) {
  return `<!doctype html><html><head><base href="/"></head><body>
    <p id="version">${version}</p>
    <script type="module">
      import * as updater from '${updateModuleUrl}';
      window.updater = updater;
      updater.initialize().catch(error => console.error(error));
    </script></body></html>`;
}

test.beforeAll(async () => {
  server = http.createServer(async (req, res) => {
    res.setHeader('Cache-Control', 'no-store');
    const url = new URL(req.url, 'http://localhost');
    const version = release;
    if (url.pathname === '/service-worker.js') {
      res.setHeader('Content-Type', 'text/javascript');
      res.end(workerScript);
    } else if (url.pathname === '/service-worker-assets.js') {
      const assets = [
        { url: 'index.html', body: html(version) },
        { url: 'js/app-update.js', body: moduleSource(version) },
        { url: `release-${version}.js`, body: `// release ${version}` },
      ].map(({ url, body }) => ({
        url, hash: `sha256-${crypto.createHash('sha256').update(body).digest('base64')}`,
      }));
      res.setHeader('Content-Type', 'text/javascript');
      res.end(`self.assetsManifest = ${JSON.stringify({ version: String(version), assets })};`);
    } else if (url.pathname === '/js/app-update.js') {
      res.setHeader('Content-Type', 'text/javascript');
      res.setHeader('Cache-Control', moduleCacheControl);
      res.end(moduleSource(version));
    } else if (url.pathname.startsWith('/release-')) {
      assetRequested = true;
      if (delayAssetMs) await new Promise(resolve => setTimeout(resolve, delayAssetMs));
      res.setHeader('Content-Type', 'text/javascript');
      res.statusCode = failAsset ? 503 : 200;
      res.end(`// release ${version}`);
    } else {
      res.setHeader('Content-Type', 'text/html; charset=utf-8');
      res.end(html(version));
    }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  // A secure local origin that exercises production registration, not the
  // application's localhost development bypass. No SQL Server is involved.
  origin = `http://app.localhost:${server.address().port}`;
});

test.afterAll(async () => {
  await new Promise(resolve => server.close(resolve));
});

test.beforeEach(async ({ page }) => {
  release = 1;
  delayAssetMs = 0;
  failAsset = false;
  assetRequested = false;
  moduleCacheControl = 'no-store';
  await page.goto(`${origin}/login?returnUrl=payroll#form`);
  await page.waitForFunction(() => navigator.serviceWorker.controller);
  await page.evaluate(() => {
    localStorage.setItem('connection-settings', 'keep-me');
    sessionStorage.setItem('auth-state', 'keep-me-too');
  });
});

test('manual update joins a slow installation, reloads once, and keeps the route and settings', async ({ page }) => {
  let prompts = 0;
  let navigations = 0;
  page.on('dialog', async dialog => { prompts++; await dialog.dismiss(); });
  page.on('framenavigated', frame => { if (frame === page.mainFrame()) navigations++; });
  release = 2;
  delayAssetMs = 11_000; // Longer than the old registration timeout in the video.
  assetRequested = false;
  await page.evaluate(async () => (await navigator.serviceWorker.getRegistration()).update());
  await expect.poll(() => assetRequested).toBe(true);
  await page.evaluate(() => {
    window.updater.refresh().catch(error => { window.updateError = error.message; });
    // Repeated clicks must share the same operation.
    window.updater.refresh().catch(error => { window.updateError = error.message; });
  });
  await expect(page.locator('#version')).toHaveText('2');
  await page.waitForFunction(() => window.updater);
  expect(await page.evaluate(() => window.updater.fixtureRelease)).toBe(2);
  expect(prompts).toBe(0);
  expect(navigations).toBe(1);
  expect(new URL(page.url()).pathname).toBe('/login');
  expect(new URL(page.url()).searchParams.get('returnUrl')).toBe('payroll');
  expect(new URL(page.url()).hash).toBe('#form');
  expect(await page.evaluate(() => localStorage.getItem('connection-settings'))).toBe('keep-me');
  expect(await page.evaluate(() => sessionStorage.getItem('auth-state'))).toBe('keep-me-too');
  expect(await page.evaluate(() => navigator.serviceWorker.getRegistration().then(r => !!r.active))).toBe(true);
});

test('a declined automatic prompt can be applied manually without another prompt', async ({ page }) => {
  let prompts = 0;
  page.on('dialog', async dialog => { prompts++; await dialog.dismiss(); });
  release = 2;
  await page.evaluate(async () => (await navigator.serviceWorker.getRegistration()).update());
  await expect.poll(() => prompts).toBe(1);
  await page.waitForFunction(async () => !!(await navigator.serviceWorker.getRegistration()).waiting);
  await page.evaluate(() => { window.updater.refresh(); });
  await expect(page.locator('#version')).toHaveText('2');
  expect(prompts).toBe(1);
});

test('automatic update activates the published worker and reloads once', async ({ page }) => {
  let prompts = 0;
  let navigations = 0;
  page.on('dialog', async dialog => { prompts++; await dialog.accept(); });
  page.on('framenavigated', frame => { if (frame === page.mainFrame()) navigations++; });
  release = 2;
  await page.evaluate(async () => (await navigator.serviceWorker.getRegistration()).update());
  await expect(page.locator('#version')).toHaveText('2');
  expect(prompts).toBe(1);
  expect(navigations).toBe(1);
});

test('a failed download keeps the working cache and allows retry', async ({ page }) => {
  release = 2;
  failAsset = true;
  const error = await page.evaluate(() => window.updater.refresh().catch(error => error.message));
  expect(error).toBeTruthy();
  await expect(page.locator('#version')).toHaveText('1');
  expect(await page.evaluate(() => caches.has('offline-cache-1'))).toBe(true);
  expect(await page.evaluate(() => navigator.serviceWorker.getRegistration().then(r => !!r.active))).toBe(true);
  failAsset = false;
  await page.evaluate(() => { window.updater.refresh(); });
  await expect(page.locator('#version')).toHaveText('2');
});

test('manual refresh without a new release reloads once and keeps caches', async ({ page }) => {
  let navigations = 0;
  page.on('framenavigated', frame => { if (frame === page.mainFrame()) navigations++; });
  await page.evaluate(async () => {
    const cache = await caches.open('another-application');
    await cache.put('/unrelated', new Response('keep'));
  });
  await page.evaluate(() => { window.updater.refresh(); });
  await page.waitForURL(url => url.searchParams.has('_appUpdate'));
  await expect(page.locator('#version')).toHaveText('1');
  expect(await page.evaluate(() => caches.has('offline-cache-1'))).toBe(true);
  expect(await page.evaluate(() => caches.has('another-application'))).toBe(true);
  expect(navigations).toBe(1);
});

test('manual refresh catches up after another tab has activated the update', async ({ page, context }) => {
  const otherTab = await context.newPage();
  await otherTab.goto(`${origin}/login`);
  await otherTab.waitForFunction(() => window.updater && navigator.serviceWorker.controller);
  page.on('dialog', dialog => dialog.dismiss());
  otherTab.on('dialog', dialog => dialog.dismiss());
  release = 2;
  await page.evaluate(() => { window.updater.refresh(); });
  await expect(page.locator('#version')).toHaveText('2');
  // Applying an update in one tab must not discard another tab's open form.
  await expect(otherTab.locator('#version')).toHaveText('1');
  await otherTab.evaluate(() => { window.updater.refresh(); });
  await expect(otherTab.locator('#version')).toHaveText('2', { timeout: 5_000 });
});

test('updater loads offline and works after connectivity returns', async ({ page, context }) => {
  const offlineTab = await context.newPage();
  const errors = [];
  offlineTab.on('pageerror', error => errors.push(error.message));
  page.on('dialog', dialog => dialog.dismiss());
  offlineTab.on('dialog', dialog => dialog.dismiss());
  await context.setOffline(true);
  await offlineTab.goto(`${origin}/login`);
  await expect(offlineTab.locator('#version')).toHaveText('1');
  await offlineTab.waitForFunction(() => window.updater, null, { timeout: 5_000 });
  expect(errors).toEqual([]);
  await context.setOffline(false);
  release = 2;
  await offlineTab.evaluate(() => { window.updater.refresh(); });
  await expect(offlineTab.locator('#version')).toHaveText('2');
});

test('updates the same JavaScript URL despite a long-lived HTTP cache', async ({ browser }) => {
  moduleCacheControl = 'public, max-age=31536000';
  const freshContext = await browser.newContext();
  try {
    const warmPage = await freshContext.newPage();
    warmPage.on('dialog', dialog => dialog.dismiss());
    await warmPage.goto(`${origin}/login`);
    await warmPage.waitForFunction(() => window.updater && navigator.serviceWorker.controller);
    expect(await warmPage.evaluate(() => window.updater.fixtureRelease)).toBe(1);
    release = 2;
    await warmPage.evaluate(() => { window.updater.refresh(); });
    await expect(warmPage.locator('#version')).toHaveText('2');
    await warmPage.waitForFunction(() => window.updater);
    expect(await warmPage.evaluate(() => window.updater.fixtureRelease)).toBe(2);
    expect(await warmPage.evaluate(() => caches.has('offline-cache-1'))).toBe(false);
    expect(await warmPage.evaluate(() => caches.has('offline-cache-2'))).toBe(true);
  } finally {
    await freshContext.close();
  }
});
