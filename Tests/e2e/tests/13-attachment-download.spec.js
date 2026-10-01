// @ts-check
const { test, expect } = require('@playwright/test');
const fs = require('fs/promises');
const path = require('path');

const cases = [
  { name: 'existing PDF callers', fileName: 'report.pdf', contentType: undefined, expected: 'application/pdf' },
  { name: 'Excel workbook', fileName: 'attachment.xlsx', contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', expected: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
  { name: 'legacy Excel workbook', fileName: 'attachment.xls', contentType: 'application/vnd.ms-excel', expected: 'application/vnd.ms-excel' },
  { name: 'existing image attachment', fileName: 'attachment.png', contentType: 'image/png', expected: 'image/png' },
];

for (const item of cases) {
  test(`attachment download preserves bytes, name and MIME: ${item.name}`, async ({ page }) => {
    await page.addScriptTag({ path: path.resolve(__dirname, '../../../Client/wwwroot/js/downloadHelper.js') });
    await page.evaluate(() => {
      const createObjectURL = URL.createObjectURL.bind(URL);
      URL.createObjectURL = blob => {
        window['downloadedMimeType'] = blob.type;
        return createObjectURL(blob);
      };
    });

    const bytes = [0, 1, 127, 128, 255, 80, 75];
    const downloadPromise = page.waitForEvent('download');
    await page.evaluate(({ fileName, contentType, bytes }) => {
      window['downloadFileFromBytes'](fileName, new Uint8Array(bytes), contentType);
    }, { ...item, bytes });
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toBe(item.fileName);
    expect(await page.evaluate(() => window['downloadedMimeType'])).toBe(item.expected);
    expect(await download.failure()).toBeNull();
    expect([...await fs.readFile(await download.path())]).toEqual(bytes);
  });
}
