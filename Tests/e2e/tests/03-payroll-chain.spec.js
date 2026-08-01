// @ts-check
const { test, expect } = require('@playwright/test');
const { databaseAvailable, apiLogin } = require('../helpers/app');

/**
 * زنجیره‌ی کاملِ کسب‌وکارِ حقوق و دستمزد، از صفر تا سند حسابداری:
 *
 *   کارگاه → قالب آیتم → پرسنل → حکم (+اقلام) → کارکرد → بستن دوره
 *          → محاسبه → تأیید نهایی → پیش‌نمایش سند → صدور سند
 *
 * چرا سطح API و نه کلیک روی UI: هر گام اینجا یک قانونِ کسب‌وکار است، نه یک
 * دکمه. همین مسیر تمام لایه‌های واقعی را اجرا می‌کند — مسیریابی، JWT،
 * [Pay2Authorize]، Pay2ScopeResolver، کنترلرها، و مهم‌تر از همه خودِ موتور
 * محاسبه در SQL Server. پوسته‌ی Blazor در 01/02 پوشش داده شده است.
 *
 * این تست‌ها به ترتیب اجرا می‌شوند و حالت را بین خودشان رد می‌کنند، چون
 * ذاتاً یک سناریوی پیوسته‌اند: بدون حکم، کارکردی معنا ندارد؛ بدون کارکرد،
 * محاسبه‌ای نیست.
 */
test.describe.configure({ mode: 'serial' });

/**
 * یک شناسه‌ی یکتا تا اجرای دوباره‌ی تست روی همان دیتابیس تداخل نکند.
 * فقط رقم — چون کنترلر کارگاه، کدِ غیرعددی را رد می‌کند.
 */
const RUN_TAG = Date.now().toString().slice(-6);

/** دوره‌ی آزمایشی: فروردین ۱۴۰۶ — از داده‌ی seed جداست تا تداخل نکند. */
const PERIOD_DATE = 14060100;

/** وضعیت مشترک بین گام‌ها. */
const ctx = {
  token: null,
  wsId: 0,
  tmplId: 0,
  empId: 0,
  empCode: 0,
  decId: 0,
  perId: 0,
  runId: 0,
};

let auth;

test.beforeAll(async ({ request }) => {
  if (!await databaseAvailable(request)) return;
  ctx.token = await apiLogin(request, 'admin');
  auth = { Authorization: `Bearer ${ctx.token}` };
});

test.beforeEach(async ({ request }) => {
  test.skip(!await databaseAvailable(request),
    'دیتابیس تست در دسترس نیست — ابتدا scripts/setup-test-env.sh را اجرا کنید.');
});

/** بدنه‌ی خطا را هم نشان می‌دهد؛ وگرنه «expected 200, got 400» چیزی نمی‌گوید. */
async function ok(res, what) {
  if (!res.ok()) {
    throw new Error(`${what} — HTTP ${res.status()}: ${await res.text()}`);
  }
  return res;
}

test.describe('زنجیره‌ی کامل حقوق و دستمزد', () => {

  test('۱) تعریف کارگاه به‌همراه سرفصل‌های حسابداری', async ({ request }) => {
    // عمداً OTHER_DED_HES خالی گذاشته می‌شود: در گام ۹ باید صدور سند را
    // متوقف کند (همان اتفاقی که برای کاربر واقعی افتاد) و بعد پرش کنیم.
    const res = await request.post('/api/pay2/workshops/save', {
      headers: auth,
      data: {
        Workshop: {
          WS_ID: 0,
          WS_CODE: RUN_TAG,
          WS_NAME: `کارگاه آزمون سرتاسری ${RUN_TAG}`,
          IS_ACTIVE: true,
          INS_MODE: 1,
          DEFAULT_DEED_MODE: 1,
        },
        Accounts: {
          SALARY_EXP_TOLID: '71-1-1',
          SALARY_EXP_EDARI: '71-1-2',
          SALARY_EXP_FOROSH: '71-1-3',
          SALARY_EXP_KHADAMAT: '71-1-4',
          INS_EXP: '71-1-5',
          SALARY_PAYABLE: '213-2-1',
          LOAN_HES: '213-2-2',
          ADV_HES: '213-2-3',
          INS_PAYABLE: '218-1-1',
          TAX_PAYABLE: '218-1-2',
          BANK_PAY_HES: '112-1-1',
          // OTHER_DED_HES عمداً تنظیم نشده
        },
      },
    });
    await ok(res, 'ساخت کارگاه');

    const list = await (await request.get('/api/pay2/workshops', { headers: auth })).json();
    const mine = list.find(w => (w.wS_CODE ?? w.WS_CODE) === RUN_TAG);
    expect(mine, 'کارگاه تازه باید در فهرست باشد').toBeTruthy();
    ctx.wsId = mine.wS_ID ?? mine.WS_ID;
    expect(ctx.wsId).toBeGreaterThan(0);
  });

  test('۲) تعریف قالب آیتم‌های حکم', async ({ request }) => {
    await ok(await request.post('/api/pay2/employees/template/save', {
      headers: auth,
      data: {
        TMPL_ID: 0,
        TMPL_CODE: `E2E_TMPL_${RUN_TAG}`,
        TMPL_NAME: `قالب آزمون ${RUN_TAG}`,
        WS_ID: ctx.wsId,
        IS_ACTIVE: true,
      },
    }), 'ساخت قالب');

    const tmpls = await (await request.get('/api/pay2/employees/templates', { headers: auth })).json();
    const mine = tmpls.find(t => (t.tmpL_CODE ?? t.TMPL_CODE) === `E2E_TMPL_${RUN_TAG}`);
    expect(mine, 'قالب تازه باید در فهرست باشد').toBeTruthy();
    ctx.tmplId = mine.tmpL_ID ?? mine.TMPL_ID;
  });

  test('۳) تعریف پرسنل در همان کارگاه', async ({ request }) => {
    // کد پرسنلی باید در دفتر حساب (213-1-<code>) وجود داشته باشد وگرنه صدور
    // سند رد می‌شود. test_chart_of_accounts.sql محدوده‌ی 99000..99049 را از
    // پیش می‌سازد؛ اینجا اولین کدِ آزادِ همان محدوده انتخاب می‌شود تا اجرای
    // دوباره‌ی آزمون روی همان دیتابیس هم بدون برخورد کار کند.
    const existing = await (await request.get('/api/pay2/employees', { headers: auth })).json();
    const used = new Set(existing.map(e => String(e.emP_CODE ?? e.EMP_CODE)));
    ctx.empCode = null;
    for (let c = 99000; c <= 99049; c++) {
      if (!used.has(String(c))) { ctx.empCode = c; break; }
    }
    expect(ctx.empCode, 'محدوده‌ی کد پرسنلی آزمون پر شده است').toBeTruthy();
    const res = await request.post('/api/pay2/employees/save', {
      headers: auth,
      data: {
        EMP_ID: 0,
        EMP_CODE: String(ctx.empCode),
        WS_ID: ctx.wsId,
        FIRST_NAME: 'آزمون',
        LAST_NAME: `سرتاسری${RUN_TAG}`,
        // کد ملی هم یکتاست؛ از کد پرسنلیِ انتخاب‌شده می‌سازیمش تا هر اجرا متفاوت باشد.
        NATIONAL_CODE: String(ctx.empCode).padStart(10, '4'),
        HIRE_DATE: 14050101,
        GENDER: 1,
        NATIONALITY: 1,
        MARITAL: 2,
        EDU_LEVEL: 2,
        INS_TYPE: 1,
        ACC_T: `213-1-${ctx.empCode}`,
        IS_ACTIVE: true,
      },
    });
    await ok(res, 'ساخت پرسنل');
    ctx.empId = await res.json();
    expect(ctx.empId, 'شناسه پرسنل باید برگردد').toBeGreaterThan(0);
  });

  test('۴) صدور حکم کارگزینی و ثبت اقلام ریالی', async ({ request }) => {
    const res = await request.post('/api/pay2/employees/decree/save', {
      headers: auth,
      data: {
        DEC_ID: 0,
        EMP_ID: ctx.empId,
        WS_ID: ctx.wsId,
        ISSUED_DATE: 14060101,
        EFF_FROM: 14060101,
        MARITAL: 2,
        EDU_LEVEL: 2,
        IS_MANAGER: false,
        IS_CONFIRMED: false,
      },
    });
    await ok(res, 'ثبت حکم');
    ctx.decId = await res.json();
    expect(ctx.decId).toBeGreaterThan(0);

    // حقوق پایه روی هر دو ریل (اسمی و رسمی) + مزایای ماهانه
    const items = await (await request.get('/api/pay2/employees/itemdefs-lookup', { headers: auth })).json();
    const idOf = code => {
      const hit = items.find(i => (i.name ?? i.Name ?? '').includes(code) || String(i.id ?? i.Id) === code);
      return hit ? (hit.id ?? hit.Id) : null;
    };
    // شناسه‌های ثابتِ seed: 1=BASE_SAL_B (رسمی)، 2=BASE_SAL (اسمی)، 3=HOME، 7=GROCERY
    const lines = [
      { ITEM_ID: 1, AMOUNT: 100000000 },
      { ITEM_ID: 2, AMOUNT: 100000000 },
      { ITEM_ID: 3, AMOUNT: 30000000 },
      { ITEM_ID: 7, AMOUNT: 22000000 },
    ];
    for (const l of lines) {
      await ok(await request.post('/api/pay2/employees/decree/line/save', {
        headers: auth,
        data: { DEC_ID: ctx.decId, ITEM_ID: l.ITEM_ID, AMOUNT: l.AMOUNT },
      }), `ثبت قلم ${l.ITEM_ID}`);
    }

    const saved = await (await request.get(`/api/pay2/employees/decree/${ctx.decId}/lines`, { headers: auth })).json();
    expect(saved.length, 'هر چهار قلم باید ثبت شده باشد').toBeGreaterThanOrEqual(4);
    expect(idOf).toBeTruthy(); // نگه‌داشتن lookup در مسیر اجرا

    // تأیید نهاییِ حکم — موتور محاسبه فقط حکم‌های IS_CONFIRMED=1 را می‌بیند
    // (کرسر cur_dec در SP_PAY2_CALC_RUN). بدون این گام، محاسبه اجرا می‌شود
    // ولی فیش خالی درمی‌آید؛ همان کاری که کاربر با تیک «تأیید نهایی این حکم»
    // انجام می‌دهد. اقلام عمداً پیش از تأیید ثبت می‌شوند.
    await ok(await request.post('/api/pay2/employees/decree/save', {
      headers: auth,
      data: {
        DEC_ID: ctx.decId,
        EMP_ID: ctx.empId,
        WS_ID: ctx.wsId,
        ISSUED_DATE: 14060101,
        EFF_FROM: 14060101,
        MARITAL: 2,
        EDU_LEVEL: 2,
        IS_MANAGER: false,
        IS_CONFIRMED: true,
      },
    }), 'تأیید نهایی حکم');
  });

  test('۵) باز کردن دوره کارکرد و ثبت کارکرد ماه', async ({ request }) => {
    const init = await ok(await request.get(
      `/api/pay2/attendance/init?wsId=${ctx.wsId}&periodDate=${PERIOD_DATE}`, { headers: auth }),
      'باز کردن دوره');
    const data = await init.json();

    const period = data.period ?? data.Period;
    const lines = data.lines ?? data.Lines;
    ctx.perId = period.peR_ID ?? period.PER_ID;
    expect(ctx.perId, 'دوره باید ساخته شود').toBeGreaterThan(0);

    const mine = lines.find(l => (l.emP_ID ?? l.EMP_ID) === ctx.empId);
    expect(mine, 'پرسنل تازه باید در فهرست کارکرد باشد').toBeTruthy();

    // ۳۱ روز کار کامل + یک مبلغ «سایر کسورات» تا گام ۹ واقعاً به آن حساب نیاز پیدا کند.
    Object.assign(mine, {
      WORK_DAYS: 31, DAYS: 31, DAYSB: 31,
      DAYS_TOLID: 31, DAYS_EDARI: 0, DAYS_KHADAMAT: 0, DAYS_FOROSH: 0,
      OT_NORMAL_H: 0, OT_HOLIDAY_H: 0, OT_ADMIN_H: 0,
      LEAVE_DAYS: 0, ABSENT_DAYS: 0, MISSION_DAYS: 0, SHORTAGE_H: 0,
      PERF_AMOUNT: 0, TRANSP_AMOUNT: 0,
      KASR_OTHER: 5000000,
      LOCKED: false,
    });

    await ok(await request.post('/api/pay2/attendance/save', {
      headers: auth,
      data: { Period: period, Lines: [mine] },
    }), 'ذخیره کارکرد');
  });

  test('۶) بستن دوره کارکرد', async ({ request }) => {
    await ok(await request.post(`/api/pay2/attendance/close-period/${ctx.perId}`, { headers: auth }),
      'بستن دوره');
  });

  test('۷) محاسبه حقوق و درستی حسابِ فیش', async ({ request }) => {
    const res = await ok(await request.post('/api/pay2/run/calculate', {
      headers: auth,
      data: { WS_ID: ctx.wsId, PER_ID: ctx.perId, IsReRun: false },
    }), 'محاسبه حقوق');
    ctx.runId = await res.json();
    expect(ctx.runId).toBeGreaterThan(0);

    // این اندپوینت یک شیء برمی‌گرداند (ستون‌های پویا + ردیف‌ها)، نه آرایه‌ی خام.
    const payload = await (await request.get(`/api/pay2/run/${ctx.runId}/lines`, { headers: auth })).json();
    const lines = payload.lines ?? payload.Lines ?? [];
    const me = lines.find(l => (l.emP_ID ?? l.EMP_ID) === ctx.empId);
    expect(me, 'فیش پرسنل باید ساخته شده باشد').toBeTruthy();

    const n = v => Number(v ?? 0);
    const gross = n(me.grosS_PAY ?? me.GROSS_PAY);
    const insBase = n(me.inS_BASE ?? me.INS_BASE);
    const insWorker = n(me.inS_WORKER ?? me.INS_WORKER);
    const tax = n(me.taX_AMOUNT ?? me.TAX_AMOUNT);
    const other = n(me.otheR_DED ?? me.OTHER_DED);
    const totalDed = n(me.totaL_DED ?? me.TOTAL_DED);
    const net = n(me.neT_PAY ?? me.NET_PAY);
    const rounding = n(me.roundinG_ADJ ?? me.ROUNDING_ADJ);

    expect(gross, 'ناخالص باید مثبت باشد').toBeGreaterThan(0);

    // نرخ بیمه کارگر ۷٪ است (INS_WORKER_RATE در seed)
    expect(insWorker, 'بیمه کارگر باید ۷٪ مبنای بیمه باشد')
      .toBe(Math.round(insBase * 0.07));

    // «سایر کسورات» همان چیزی است که در کارکرد ثبت شد
    expect(other, 'سایر کسورات باید از کارکرد بیاید').toBe(5000000);

    // معادله‌ی تراز فیش
    expect(totalDed, 'جمع کسورات باید برابر اجزایش باشد')
      .toBe(insWorker + tax + n(me.loaN_DED ?? me.LOAN_DED) + n(me.advancE_DED ?? me.ADVANCE_DED) + other);
    expect(net, 'خالص = ناخالص − کسورات (با احتساب گِرد کردن)')
      .toBe(gross + rounding - totalDed);
  });

  test('۸) تأیید نهایی محاسبه', async ({ request }) => {
    await ok(await request.put(`/api/pay2/run/${ctx.runId}/finalize`, { headers: auth }), 'تأیید نهایی');

    const latest = await (await request.get(
      `/api/pay2/run/latest?wsId=${ctx.wsId}&perId=${ctx.perId}`, { headers: auth })).json();
    expect(Number(latest.status ?? latest.STATUS), 'وضعیت باید «تأیید نهایی» شود').toBe(2);
  });

  test('۹) سند حسابداری بدون حساب «سایر کسورات» متوقف می‌شود', async ({ request }) => {
    // دقیقاً همان چیزی که مشتری گزارش کرد. پیش‌نمایش باید همین را بگوید،
    // و صدور واقعی هم باید رد شود — نه اینکه سندی ناتراز بسازد.
    const preview = await (await request.get(
      `/api/pay2/run/${ctx.runId}/preview-deed`, { headers: auth })).json();

    const errors = (preview.validationErrors ?? preview.ValidationErrors ?? []).join(' | ');
    expect(errors, 'پیش‌نمایش باید نبودِ حساب سایر کسورات را گزارش کند').toContain('سایر کسورات');

    const gen = await request.post(`/api/pay2/run/${ctx.runId}/generate-deed`, { headers: auth });
    expect(gen.ok(), 'صدور سند نباید با حساب خالی موفق شود').toBeFalsy();
  });

  test('۱۰) با تنظیم همان حساب، سند صادر می‌شود', async ({ request }) => {
    const accounts = await (await request.get(
      `/api/pay2/workshops/${ctx.wsId}/accounts`, { headers: auth })).json();
    accounts.OTHER_DED_HES = '213-2-4';
    accounts.WS_ID = ctx.wsId;

    const wsList = await (await request.get('/api/pay2/workshops', { headers: auth })).json();
    const ws = wsList.find(w => (w.wS_ID ?? w.WS_ID) === ctx.wsId);

    await ok(await request.post('/api/pay2/workshops/save', {
      headers: auth, data: { Workshop: ws, Accounts: accounts },
    }), 'تنظیم حساب سایر کسورات');

    const preview = await (await request.get(
      `/api/pay2/run/${ctx.runId}/preview-deed`, { headers: auth })).json();
    const errors = preview.validationErrors ?? preview.ValidationErrors ?? [];
    expect(errors, `پیش‌نمایش باید بی‌خطا شود، ولی: ${errors.join(' | ')}`).toHaveLength(0);

    const articles = preview.articles ?? preview.Articles ?? [];
    const sum = (k1, k2) => articles.reduce((a, x) => a + Number(x[k1] ?? x[k2] ?? 0), 0);
    const debit = sum('bed', 'BED');
    const credit = sum('bes', 'BES');
    expect(debit, 'سند باید مبلغ داشته باشد').toBeGreaterThan(0);
    expect(debit, 'سند حسابداری باید تراز باشد').toBe(credit);

    await ok(await request.post(`/api/pay2/run/${ctx.runId}/generate-deed`, { headers: auth }),
      'صدور قطعی سند');

    const latest = await (await request.get(
      `/api/pay2/run/latest?wsId=${ctx.wsId}&perId=${ctx.perId}`, { headers: auth })).json();
    expect(Number(latest.status ?? latest.STATUS), 'وضعیت باید «سند صادر شده» شود').toBe(3);
    expect(latest.deeD_ID_SAL ?? latest.DEED_ID_SAL, 'شماره سند باید ثبت شود').toBeTruthy();

    // سند با روش «کلی» صادر شد: حقوق پرداختنی یک‌جا روی حساب کارگاه می‌نشیند،
    // نه روی حساب تفصیلیِ خودِ پرسنل. (گام ۱۱ حالت مقابلش را می‌سنجد.)
    const codes = articles.map(a => String(a.heS_CODE ?? a.HES_CODE));
    expect(codes, 'در روش کلی نباید حساب تفصیلی پرسنل در سند بیاید')
      .not.toContain(`213-1-${ctx.empCode}`);
  });

  test('۱۱) تغییر روش صدور در کارگاه، در بازصدور هم اثر می‌گذارد', async ({ request }) => {
    // گزارش واقعی مشتری: سند حقوق فقط هزینه داشت و هیچ آرتیکلی به تفکیک
    // پرسنل نداشت. علتش این بود که PAY2_RUN.DEED_MODE در صدورِ اول نوشته
    // می‌شد و از آن به بعد بر تنظیم کارگاه مقدم بود — یعنی کاربر تنظیم را
    // عوض می‌کرد، بازصدور می‌زد، و باز همان سند تجمیعی را می‌گرفت.
    const wsList = await (await request.get('/api/pay2/workshops', { headers: auth })).json();
    const ws = wsList.find(w => (w.wS_ID ?? w.WS_ID) === ctx.wsId);
    ws.DEFAULT_DEED_MODE = 2;

    const accounts = await (await request.get(
      `/api/pay2/workshops/${ctx.wsId}/accounts`, { headers: auth })).json();
    accounts.WS_ID = ctx.wsId;

    await ok(await request.post('/api/pay2/workshops/save', {
      headers: auth, data: { Workshop: ws, Accounts: accounts },
    }), 'تغییر روش صدور کارگاه به نیمه‌تفصیلی');

    await ok(await request.post(`/api/pay2/run/${ctx.runId}/generate-deed`, { headers: auth }),
      'بازصدور سند با روش جدید');

    const preview = await (await request.get(
      `/api/pay2/run/${ctx.runId}/preview-deed`, { headers: auth })).json();
    const articles = preview.articles ?? preview.Articles ?? [];

    expect(Number(preview.modeUsed ?? preview.ModeUsed),
      'روش اعلام‌شده باید نیمه‌تفصیلی باشد').toBe(2);

    // همان چیزی که مشتری دنبالش بود: حساب تفصیلیِ خودِ پرسنل در سند.
    const codes = articles.map(a => String(a.heS_CODE ?? a.HES_CODE));
    expect(codes, `کد تفصیلی پرسنل باید در سند باشد — ${codes.join(', ')}`)
      .toContain(`213-1-${ctx.empCode}`);

    const sum = (k1, k2) => articles.reduce((a, x) => a + Number(x[k1] ?? x[k2] ?? 0), 0);
    expect(sum('bed', 'BED'), 'سند تفصیلی هم باید تراز باشد').toBe(sum('bes', 'BES'));
  });
});
