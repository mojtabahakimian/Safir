// @ts-check
const { test, expect } = require('@playwright/test');
const { execFileSync } = require('child_process');
const { databaseAvailable, apiLogin } = require('../helpers/app');

test.describe.configure({ mode: 'serial' });

const YEAR = 1599;
const PREVIOUS_YEAR = YEAR - 1;
const SQLCMD = process.env.SQLCMD || '/opt/mssql-tools18/bin/sqlcmd';
const SQL_HOST = process.env.SQL_HOST || 'localhost,1433';
const SQL_DATABASE = process.env.SQL_DATABASE || 'SafirTestLeave';
const SQL_PASSWORD = process.env.SQLCMDPASSWORD || process.env.MSSQL_SA_PASSWORD;

const ctx = { empId: 0, wsId: 0, employeeCode: '', adminToken: '', selfToken: '' };

function sql(query) {
  if (!SQL_PASSWORD) throw new Error('SQLCMDPASSWORD برای آماده‌سازی سناریوی مرخصی تنظیم نشده است.');
  return execFileSync(SQLCMD, [
    '-S', SQL_HOST, '-U', 'sa', '-P', SQL_PASSWORD, '-C',
    '-d', SQL_DATABASE, '-h', '-1', '-W', '-Q', `SET NOCOUNT ON; ${query}`,
  ], { encoding: 'utf8' }).trim();
}

test.beforeAll(async ({ request }) => {
  if (!await databaseAvailable(request) || !SQL_PASSWORD) return;

  const selected = sql(`
    DECLARE @EMP_ID INT = (
      SELECT TOP (1) EMP_ID FROM dbo.PAY2_EMPLOYEE
      WHERE WS_ID = 1 AND NULLIF(LTRIM(RTRIM(ACC_T)), N'') IS NOT NULL
      ORDER BY EMP_ID
    );
    IF @EMP_ID IS NULL THROW 54001, 'No employee in workshop 1 for leave E2E test.', 1;

    DELETE FROM dbo.PAY2_LEAVE WHERE EMP_ID = @EMP_ID AND START_DATE / 10000 IN (${PREVIOUS_YEAR}, ${YEAR});
    DELETE FROM dbo.PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND [YEAR] IN (${PREVIOUS_YEAR}, ${YEAR});

    -- پانزده روز مانده‌ی واقعی در سال قبل؛ فقط نه روز باید منتقل شود.
    INSERT dbo.PAY2_LEAVE_BAL
      (EMP_ID, [YEAR], ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN, CARRIED_OUT_MIN)
    VALUES (@EMP_ID, ${PREVIOUS_YEAR}, 6600, 0, 0, 0);

    EXEC dbo.SP_PAY2_CARRYOVER_LEAVE
      @FROM_YEAR = ${PREVIOUS_YEAR}, @TO_YEAR = ${YEAR}, @WS_ID = 1;

    -- بازه پنج‌روزه است ولی به علت تعطیلی، فقط چهار روز درخواست/کسر شده است.
    INSERT dbo.PAY2_LEAVE
      (EMP_ID, LEV_TYPE, REQUEST_DATE, START_DATE, END_DATE, REQ_DAYS, REQ_HOURS,
       REQ_MINUTES, DESCRIPTION, STATUS, CREATED_AT, CREATED_BY)
    VALUES
      (@EMP_ID, 1, ${YEAR}0101, ${YEAR}0110, ${YEAR}0114, 4, 0, 0,
       N'بازه پنج‌روزه با یک روز تعطیل رسمی', 4, GETDATE(), 9001),
      (@EMP_ID, 6, ${YEAR}0201, ${YEAR}0202, ${YEAR}0202, 0, 1, 30,
       N'مرخصی ساعتی', 4, GETDATE(), 9001),
      (@EMP_ID, 1, ${YEAR}0301, ${YEAR}0302, ${YEAR}0302, 1, 0, 0,
       N'درخواست ردشده نباید کسر شود', 3, GETDATE(), 9001);

    -- موتور کارکرد همین جمع تأییدشده را در مانده نگه می‌دارد: ۴ روز + ۱:۳۰.
    UPDATE dbo.PAY2_LEAVE_BAL SET USED_MIN = 1850 WHERE EMP_ID = @EMP_ID AND [YEAR] = ${YEAR};

    UPDATE dbo.SALA_DTL
      SET HES = (SELECT ACC_T FROM dbo.PAY2_EMPLOYEE WHERE EMP_ID = @EMP_ID)
      WHERE IDD = 9002;
    DELETE FROM dbo.PAY2_USER_WS WHERE USERCO = 9003 AND WS_ID = 1;

    SELECT CONCAT(E.EMP_ID, '|', E.WS_ID, '|', E.EMP_CODE)
    FROM dbo.PAY2_EMPLOYEE E WHERE E.EMP_ID = @EMP_ID;
  `);
  const resultLine = selected.split(/\r?\n/).find(line => /^\d+\|\d+\|/.test(line.trim()));
  if (!resultLine) throw new Error(`شناسه سناریوی مرخصی از SQL دریافت نشد: ${selected}`);
  [ctx.empId, ctx.wsId, ctx.employeeCode] = resultLine.trim().split('|');
  ctx.empId = Number(ctx.empId);
  ctx.wsId = Number(ctx.wsId);

  ctx.adminToken = await apiLogin(request, 'admin');
  ctx.selfToken = await apiLogin(request, 'viewer');
});

test.afterAll(() => {
  if (!SQL_PASSWORD || !ctx.empId) return;
  sql(`
    UPDATE dbo.SALA_DTL SET HES = NULL WHERE IDD = 9002;
    IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_USER_WS WHERE USERCO = 9003 AND WS_ID = 1)
      INSERT dbo.PAY2_USER_WS (USERCO, WS_ID) VALUES (9003, 1);
    DELETE FROM dbo.PAY2_LEAVE WHERE EMP_ID = ${ctx.empId} AND START_DATE / 10000 IN (${PREVIOUS_YEAR}, ${YEAR});
    DELETE FROM dbo.PAY2_LEAVE_BAL WHERE EMP_ID = ${ctx.empId} AND [YEAR] IN (${PREVIOUS_YEAR}, ${YEAR});
  `);
});

test.beforeEach(async ({ request }) => {
  test.skip(!await databaseAvailable(request) || !SQL_PASSWORD,
    'SQL Server واقعی و SQLCMDPASSWORD برای این سناریو لازم است.');
});

test('انتقال قانونی، کسر تعطیلی و مرخصی ساعتی در صورت‌حساب دقیق است', async ({ request }) => {
  const response = await request.get(`/api/pay2/employees/${ctx.empId}/leave-statement?year=${YEAR}`, {
    headers: { Authorization: `Bearer ${ctx.adminToken}` },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
  const statement = await response.json();

  expect(statement.employeeCode).toBe(ctx.employeeCode);
  expect(statement.leaveMinsPerDay).toBe(440);
  expect(statement.leaveCarryoverMax).toBe(9);
  expect(statement.previousYearBalanceMin).toBe(6600);       // ۱۵ روز
  expect(statement.carryoverLimitMin).toBe(3960);            // سقف ۹ روز
  expect(statement.eligibleCarryoverMin).toBe(3960);
  expect(statement.carriedInMin).toBe(3960);
  expect(statement.expiredCarryoverMin).toBe(2640);          // سوخت ۶ روز
  expect(statement.entitlementMin).toBe(11440);              // استحقاق ۲۶ روز
  expect(statement.usedMin).toBe(1850);                      // ۴ روز + ۱ ساعت و ۳۰ دقیقه
  expect(statement.historyRequestedMin).toBe(1850);
  expect(statement.balanceMin).toBe(13550);

  expect(statement.history).toHaveLength(2);                 // درخواست ردشده حذف می‌شود
  const fullDay = statement.history.find(x => x.reQ_DAYS === 4);
  expect(fullDay).toBeTruthy();
  expect(fullDay.starT_DATE).toBe(Number(`${YEAR}0110`));
  expect(fullDay.enD_DATE).toBe(Number(`${YEAR}0114`));       // ۵ روز تقویمی
  expect(fullDay.totalDeductedMinutes).toBe(1760);            // فقط ۴ روز کسر
  const hourly = statement.history.find(x => x.reQ_HOURS === 1);
  expect(hourly.totalDeductedMinutes).toBe(90);
});

test('کارمند فقط صورت‌حساب پرونده متصل به حساب خودش را می‌بیند', async ({ request }) => {
  const response = await request.get(`/api/pay2/employees/me/leave-statement?year=${YEAR}`, {
    headers: { Authorization: `Bearer ${ctx.selfToken}` },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
  expect((await response.json()).employeeCode).toBe(ctx.employeeCode);
});

test('کاربر خارج از محدوده کارگاه به صورت‌حساب دسترسی ندارد', async ({ request }) => {
  const scopedToken = await apiLogin(request, 'scoped');
  const response = await request.get(`/api/pay2/employees/${ctx.empId}/leave-statement?year=${YEAR}`, {
    headers: { Authorization: `Bearer ${scopedToken}` },
  });
  expect(response.status()).toBe(403);
});

test('PDF واقعی تولید می‌شود و از مسیر خودخدمتی هم قابل دانلود است', async ({ request }) => {
  const response = await request.get(`/api/pay2/employees/me/leave-statement/pdf?year=${YEAR}`, {
    headers: { Authorization: `Bearer ${ctx.selfToken}` },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
  expect(response.headers()['content-type']).toContain('application/pdf');
  const bytes = await response.body();
  expect(bytes.subarray(0, 4).toString()).toBe('%PDF');
  expect(bytes.length).toBeGreaterThan(10_000);
});
