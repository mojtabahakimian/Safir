using Dapper;
using Dbf;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary.Reports;
using System.Data;
using System.IO.Compression;
using System.Text;

namespace Safir.Server.Services
{
    public class Pay2DisketteService
    {
        private readonly IDatabaseService _db;

        public Pay2DisketteService(IDatabaseService db)
        {
            _db = db;
        }

        public async Task<(byte[] ZipBytes, string FileName)?> GenerateInsuranceDisketteAsync(int runId)
        {
            // ۱. خواندن اطلاعات هدر (کارگاه و دوره)
            const string headSql = @"
                SELECT 
                    P.PERIOD_DATE, P.TENDAR_APPLY, W.WS_CODE, W.WS_NAME, W.EMPLOYER_NAME, 
                    W.ADDRESS, W.SSO_BRANCH
                FROM PAY2_RUN R
                INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId AND R.STATUS >= 2";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            string wsCode = head.WS_CODE?.ToString() ?? "";

            // تمام مبالغ از Snapshot مؤثر همان Run خوانده می‌شوند.
            var lines = (await _db.DoGetDataSQLAsync<dynamic>(Pay2PayrollSnapshotQuery.Sql, new { runId }))
                .Where(x => (byte)x.INS_TYPE != 3).ToList();
            ValidateLegalInsuranceSnapshots(lines);
            ValidateInsuranceIdentities(lines);

            // ─── آماده‌سازی لیست‌های DBF ───
            var dskworList = new List<Dictionary<string, object>>();

            long totalDailyWage = 0, totalMonthlyWage = 0, totalOtherBenefits = 0;
            long totalMash = 0, totalTotl = 0, totalWorkerIns = 0;
            long totalMarital = 0, totalSeniority = 0;

            foreach (var line in lines)
            {
                int empId = (int)line.EMP_ID;
                decimal workDays = (decimal)line.INSURANCE_DAYS;
                long baseMonthly = (long)line.BASE_WAGE_MONTHLY;
                long seniorityMonthly = (long)line.SENIORITY_MONTHLY;
                long dailyWage = workDays > 0 ? (long)Math.Round(baseMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                long seniorityBase = workDays > 0 ? (long)Math.Round(seniorityMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                long monthlyWage = (long)Math.Round((dailyWage + seniorityBase) * workDays, MidpointRounding.AwayFromZero);
                long maritalAllowance = (long)line.MARITAL_ALLOWANCE;
                long otherBenefits = (long)line.DBF_GENERAL_BENEFITS;
                long insBase = (long)line.INS_BASE;
                long grossPay = (long)line.NOMINAL_GROSS;
                long workerIns = (long)line.INS_WORKER;

                // جمع‌زن‌ها برای هدر کارگاه
                totalDailyWage += dailyWage;
                totalMonthlyWage += monthlyWage;
                totalOtherBenefits += otherBenefits;
                totalMash += insBase;
                totalTotl += grossPay;
                totalWorkerIns += workerIns;
                totalMarital += maritalAllowance;
                totalSeniority += seniorityBase;

                // ساخت یک رکورد کارمند
                var wor = new Dictionary<string, object>
                {
                    ["DSW_ID"] = wsCode,
                    ["DSW_YY"] = year % 100, // دو رقم آخر سال
                    ["DSW_MM"] = month,
                    ["DSW_LISTNO"] = "01",
                    ["DSW_ID1"] = PadInsuranceCode(line.INS_CODE?.ToString()),
                    ["DSW_FNAME"] = line.FIRST_NAME?.ToString() ?? "",
                    ["DSW_LNAME"] = line.LAST_NAME?.ToString() ?? "",
                    ["DSW_DNAME"] = line.FATHER_NAME?.ToString() ?? "",
                    ["DSW_IDNO"] = line.ID_NUMBER?.ToString() ?? "",
                    ["DSW_IDPLC"] = line.BIRTH_PLACE?.ToString() ?? "",
                    ["DSW_IDATE"] = "", // خالی 
                    ["DSW_BDATE"] = line.BIRTH_DATE != null ? line.BIRTH_DATE.ToString() : "",
                    ["DSW_SEX"] = (byte)line.GENDER == 1 ? "مرد" : "زن",
                    ["DSW_NAT"] = (byte)line.NATIONALITY == 1 ? "ایران" : "اتباع",
                    ["DSW_OCP"] = line.JOB_CODE.ToString(),
                    ["DSW_SDATE"] = DateInOccurrenceMonth(line.HIRE_DATE, periodDate),
                    ["DSW_EDATE"] = DateInOccurrenceMonth(line.FIRE_DATE, periodDate),
                    ["DSW_DD"] = (int)workDays,
                    ["DSW_ROOZ"] = dailyWage,
                    ["DSW_MAH"] = monthlyWage,
                    ["DSW_MAZ"] = otherBenefits,
                    ["DSW_MASH"] = insBase,
                    ["DSW_TOTL"] = grossPay,
                    ["DSW_BIME"] = workerIns,
                    ["DSW_PRATE"] = 0,
                    ["DSW_JOB"] = "1",
                    ["PER_NATCOD"] = line.NATIONAL_CODE?.ToString() ?? "",
                    ["DSW_INC"] = seniorityBase, // پایه سنوات (قانون ۱۴۰۵)
                    ["DSW_SPOUS"] = maritalAllowance.ToString() // حق تاهل (در فرمت بیمه String است)
                };
                dskworList.Add(wor);
            }

            if (dskworList.Count != lines.Count)
                throw new InvalidOperationException("فایل بیمه قابل تولید نیست: تعداد رکوردهای DSKWOR با پرسنل واجد شرایط تراز نیست.");

            // اجزای حق بیمه دقیقاً از Snapshot همان Run خوانده می‌شوند.
            long totalKarf = lines.Sum(l => (long)l.INS_EMPLOYER_BASE);
            long totalBikari = lines.Sum(l => (long)l.INS_UNEMPLOYMENT);

            var dskkarList = new List<Dictionary<string, object>>();
            var kar = new Dictionary<string, object>
            {
                ["DSK_ID"] = wsCode,
                ["DSK_NAME"] = head.WS_NAME?.ToString() ?? "",
                ["DSK_FARM"] = head.EMPLOYER_NAME?.ToString() ?? "",
                ["DSK_ADRS"] = head.ADDRESS?.ToString() ?? "",
                ["DSK_KIND"] = 1,
                ["DSK_YY"] = year % 100,
                ["DSK_MM"] = month,
                ["DSK_LISTNO"] = "01",
                ["DSK_DISC"] = "تولید شده توسط سیستم سفیر",
                ["DSK_NUM"] = lines.Count,
                ["DSK_TDD"] = (int)lines.Sum(x => (decimal)x.INSURANCE_DAYS),
                ["DSK_TROOZ"] = totalDailyWage,
                ["DSK_TMAH"] = totalMonthlyWage,
                ["DSK_TMAZ"] = totalOtherBenefits,
                ["DSK_TMASH"] = totalMash,
                ["DSK_TTOTL"] = totalTotl,
                ["DSK_TBIME"] = totalWorkerIns,
                ["DSK_TKARF"] = totalKarf,
                ["DSK_TBIC"] = totalBikari,
                ["DSK_RATE"] = 30,
                ["DSK_PRATE"] = 0,
                ["DSK_BIMH"] = 0, // مشاغل سخت و زیان‌آور (اگر نیاز بود بعداً افزوده شود)
                ["MON_PYM"] = "000",
                ["DSK_INC"] = totalSeniority,
                ["DSK_SPOUS"] = totalMarital.ToString()
            };
            dskkarList.Add(kar);

            // ─── تولید فایل‌های DBF و زیپ کردن ───
            // استفاده از انکودینگ ویندوز 1256 (رایج‌ترین فرمت برای پورتال جدید بیمه)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(1256);

            string tempDir = Path.Combine(Path.GetTempPath(), $"Bimeh_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            string pathKar = Path.Combine(tempDir, "DSKKAR00.DBF");
            string pathWor = Path.Combine(tempDir, "DSKWOR00.DBF");

            try
            {
                // استفاده از هلپر DbfFile که در پروژه شما وجود دارد
                DbfFile.Write(pathKar, dskkarList, encoding, overwirte: true);
                DbfFile.Write(pathWor, dskworList, encoding, overwirte: true);

                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    archive.CreateEntryFromFile(pathKar, "DSKKAR00.DBF");
                    archive.CreateEntryFromFile(pathWor, "DSKWOR00.DBF");
                }

                return (memoryStream.ToArray(), $"Diskette_{year}_{month:D2}.zip");
            }
            finally
            {
                // پاک‌سازی فایل‌های موقت
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        // شماره بیمه در تامین اجتماعی باید دقیقاً ۱۰ کاراکتر باشد و معمولاً با صفرهای پیشرو پر می‌شود
        private static string PadInsuranceCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("شماره بیمه خالی است و جایگزینی با شماره ساختگی مجاز نیست.");
            return code.Trim().PadLeft(10, '0');
        }

        // متد جدید: پیش‌نمایش دیسکت (DBF) به صورت JSON
        public async Task<DiskettePreviewDto?> GetInsuranceDiskettePreviewAsync(int runId)
        {
            var result = new DiskettePreviewDto();

            const string headSql = @"
                SELECT 
                    P.PERIOD_DATE, W.WS_CODE, W.WS_NAME, W.EMPLOYER_NAME
                FROM PAY2_RUN R
                INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            string wsCode = head.WS_CODE?.ToString() ?? "";

            var lines = (await _db.DoGetDataSQLAsync<dynamic>(Pay2PayrollSnapshotQuery.Sql, new { runId }))
                .Where(x => (byte)x.INS_TYPE != 3).ToList();
            ValidateLegalInsuranceSnapshots(lines);
            ValidateInsuranceIdentities(lines);

            long totalMash = 0, totalTotl = 0, totalWorkerIns = 0;
            long totalMarital = 0, totalSeniority = 0;

            foreach (var line in lines)
            {
                int empId = (int)line.EMP_ID;
                decimal workDays = (decimal)line.INSURANCE_DAYS;
                long baseMonthly = (long)line.BASE_WAGE_MONTHLY;
                long seniorityMonthly = (long)line.SENIORITY_MONTHLY;
                long dailyWage = workDays > 0 ? (long)Math.Round(baseMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                long seniorityBase = workDays > 0 ? (long)Math.Round(seniorityMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                long monthlyWage = (long)Math.Round((dailyWage + seniorityBase) * workDays, MidpointRounding.AwayFromZero);
                long maritalAllowance = (long)line.MARITAL_ALLOWANCE;
                long otherBenefits = (long)line.DBF_GENERAL_BENEFITS;
                long insBase = (long)line.INS_BASE;

                totalMash += insBase;
                totalTotl += (long)line.NOMINAL_GROSS;
                totalWorkerIns += (long)line.INS_WORKER;
                totalMarital += maritalAllowance;
                totalSeniority += seniorityBase;

                string insCodeStr = line.INS_CODE?.ToString() ?? "";

                result.WorList.Add(new DisketteWorDto
                {
                    DSW_ID1 = PadInsuranceCode(insCodeStr),
                    FULL_NAME = $"{line.LAST_NAME} {line.FIRST_NAME}",
                    PER_NATCOD = line.NATIONAL_CODE?.ToString() ?? "",
                    DSW_OCP = line.JOB_CODE.ToString(),
                    DSW_DD = (int)workDays,
                    DSW_ROOZ = dailyWage,
                    DSW_MAH = monthlyWage,
                    DSW_MAZ = otherBenefits,
                    DSW_MASH = insBase,
                    DSW_TOTL = (long)line.NOMINAL_GROSS,
                    DSW_BIME = (long)line.INS_WORKER,
                    DSW_INC = seniorityBase,
                    DSW_SPOUS = maritalAllowance.ToString(),
                    DSW_SDATE = DateInOccurrenceMonth(line.HIRE_DATE, periodDate),
                    DSW_EDATE = DateInOccurrenceMonth(line.FIRE_DATE, periodDate)
                });
            }

            long totalKarf = lines.Sum(l => (long)l.INS_EMPLOYER_BASE);
            long totalBikari = lines.Sum(l => (long)l.INS_UNEMPLOYMENT);

            result.Kar = new DisketteKarDto
            {
                DSK_ID = wsCode,
                DSK_NAME = head.WS_NAME?.ToString() ?? "",
                DSK_FARM = head.EMPLOYER_NAME?.ToString() ?? "",
                DSK_NUM = lines.Count,
                DSK_TDD = (int)lines.Sum(x => (decimal)x.INSURANCE_DAYS),
                DSK_TMASH = totalMash,
                DSK_TTOTL = totalTotl,
                DSK_TBIME = totalWorkerIns,
                DSK_TKARF = totalKarf,
                DSK_TBIC = totalBikari,
                DSK_INC = totalSeniority,
                DSK_SPOUS = totalMarital.ToString()
            };

            if (result.WorList.Count != lines.Count)
                throw new InvalidOperationException("پیش‌نمایش بیمه ناقص است: تعداد رکوردهای DSKWOR با پرسنل واجد شرایط تراز نیست.");
            return result;
        }

        // ===================================================================
        // تولید دیسکت مالیات بر درآمد حقوق (قوانین جدید ۱۴۰۵ - سامانه Salary)
        // فایل‌های WP (اطلاعات پرسنل) و WH (ریز کارکرد) با فرمت UTF-8
        // فایل WK به طور کامل منسوخ و حذف شده است.
        // ===================================================================
        public async Task<(byte[] ZipBytes, string FileName)?> GenerateTaxDisketteAsync(int runId)
        {
            const string headSql = @"
                SELECT P.PERIOD_DATE, W.WS_CODE
                FROM PAY2_RUN R
                INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId AND R.STATUS >= 2";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;
            await ValidateEmployeeSnapshotsAsync(runId);

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            string wsCode = head.WS_CODE?.ToString() ?? "";

            var lines = (await _db.DoGetDataSQLAsync<dynamic>(Pay2PayrollSnapshotQuery.Sql, new { runId }))
                .Where(x => !(bool)x.TAX_EXEMPT).ToList();
            ValidateTaxIdentities(lines);
            if (lines.Any(x => x.INSURANCE_DAYS is null || x.NOMINAL_GROSS is null))
                throw new InvalidOperationException("خروجی مالیات ممکن نیست: Snapshot اسمی روزکرد/ناخالص در Run کامل نیست.");
            var missingNominalDetails = await _db.DoGetDataSQLAsyncSingle<int>(
                @"SELECT COUNT(*) FROM PAY2_RUN_DETAIL D INNER JOIN PAY2_RUN R ON R.RUN_ID=D.RUN_ID
                  WHERE D.RUN_ID=@runId AND (D.NOMINAL_AMOUNT IS NULL OR D.ITEM_CODE_SNAP IS NULL
                    OR (R.PAYROLL_ENGINE_VERSION>=3 AND D.ITEM_NAME_SNAP IS NULL) OR D.CALC_BASIS_SNAP IS NULL
                    OR D.INS_SUBJECT_AMOUNT IS NULL OR D.TAX_SUBJECT_AMOUNT IS NULL)", new { runId });
            if (missingNominalDetails > 0)
                throw new InvalidOperationException("خروجی مالیات ممکن نیست: Snapshot مبلغ اسمی اقلام Run کامل نیست.");
            var illegalOfficialSubjects = await _db.DoGetDataSQLAsyncSingle<int>(
                "SELECT COUNT(*) FROM PAY2_RUN_DETAIL WHERE RUN_ID=@runId AND ITEM_CODE_SNAP='BASE_SAL_B' AND (ISNULL(INS_SUBJECT_AMOUNT,0)<>0 OR ISNULL(TAX_SUBJECT_AMOUNT,0)<>0)", new { runId });
            if (illegalOfficialSubjects > 0)
                throw new InvalidOperationException("خروجی قانونی ممکن نیست: BASE_SAL_B رسمی دارای مبلغ مشمول بیمه یا مالیات است.");

            const string detailsSql = @"
                SELECT D.EMP_ID, D.ITEM_CODE_SNAP ITEM_CODE, D.TAX_SUBJECT_AMOUNT AMOUNT, D.CALC_BASIS_SNAP CALC_BASIS
                FROM PAY2_RUN_DETAIL D
                WHERE D.RUN_ID = @runId AND D.ITEM_CODE_SNAP <> 'BASE_SAL_B' AND D.TAX_SUBJECT_AMOUNT > 0 AND D.NOMINAL_AMOUNT IS NOT NULL";

            var details = await _db.DoGetDataSQLAsync<dynamic>(detailsSql, new { runId });
            var groupedDetails = details.GroupBy(x => (int)x.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            var wpLines = new List<string>();
            var whLines = new List<string>();

            // فرمت UTF-8 بدون BOM (پیش‌نیاز اجباری سامانه دارایی)
            var utf8WithoutBom = new UTF8Encoding(false);

            foreach (var line in lines)
            {
                int empId = (int)line.EMP_ID;
                string natCode = line.NATIONAL_CODE.ToString();

                // ─── تولید خط فایل WP (اطلاعات هویتی) ───
                string wpLine = string.Join(",",
                    natCode,
                    ((byte)line.NATIONALITY == 1 ? "1" : "2"),
                    "",
                    line.FIRST_NAME?.ToString() ?? "",
                    line.LAST_NAME?.ToString() ?? "",
                    line.FATHER_NAME?.ToString() ?? "",
                    line.ID_NUMBER?.ToString() ?? "",
                    line.BIRTH_PLACE?.ToString() ?? "",
                    line.BIRTH_DATE?.ToString() ?? "",
                    ((byte)line.MARITAL == 1 ? "1" : "2"),
                    "0",
                    "",
                    line.INS_CODE?.ToString() ?? "",
                    "",
                    line.JOB_NAME?.ToString() ?? "",
                    "1",
                    DateInOccurrenceMonth(line.HIRE_DATE, periodDate),
                    DateInOccurrenceMonth(line.FIRE_DATE, periodDate),
                    "", // 🚀 کد پستی خالی ارسال می‌شود
                    "", // 🚀 آدرس خالی ارسال می‌شود
                    "",
                    line.MOBILE?.ToString() ?? "",
                    "0"
                );
                wpLines.Add(wpLine);

                // ─── تولید خط فایل WH (اطلاعات عملکرد ریالی) ───
                long baseSalary = 0, mostamar = 0, gheyreMostamar = 0, eydi = 0, sanavat = 0;

                if (groupedDetails.TryGetValue(empId, out var empDetails))
                {
                    foreach (var det in empDetails)
                    {
                        string code = det.ITEM_CODE.ToString().ToUpper();
                        long amt = (long)det.AMOUNT;
                        byte basis = (byte)det.CALC_BASIS;

                        if (code == "BASE_SAL_B")
                            throw new InvalidOperationException("BASE_SAL_B رسمی نباید در BASE_SALARY، MOSTAMAR یا GHEYRE_MOSTAMAR مالیات وارد شود.");
                        if (code == "BASE_SAL") baseSalary += amt;
                        else if (code == "SANOVAT_PAYE") baseSalary += amt;
                        else if (code == "EIDI") eydi += amt;
                        else if (code == "SANAVAT" || code == "SENIORITY") sanavat += amt;
                        else if (basis == 2) mostamar += amt;
                        else gheyreMostamar += amt;
                    }
                }

                string whLine = string.Join(",",
                    natCode,
                    year.ToString(),
                    month.ToString("D2"),
                    "",
                    ((decimal)line.INSURANCE_DAYS).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    baseSalary.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    mostamar.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    gheyreMostamar.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "0",
                    "0",
                    ((long)line.INS_WORKER).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ((long)line.TAX_AMOUNT).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "0",
                    "0",
                    eydi.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    sanavat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ((long)line.TAX_BASE).ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
                whLines.Add(whLine);
            }

            if (wpLines.Count != lines.Count || whLines.Count != lines.Count)
                throw new InvalidOperationException("فایل مالیات قابل تولید نیست: تعداد رکوردهای WP/WH با پرسنل واجد شرایط تراز نیست.");

            string tempDir = Path.Combine(Path.GetTempPath(), $"TaxDiskette_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            string prefix = $"{year}{month:D2}";
            string pathWp = Path.Combine(tempDir, $"WP{prefix}.txt");
            string pathWh = Path.Combine(tempDir, $"WH{prefix}.txt");

            try
            {
                await System.IO.File.WriteAllLinesAsync(pathWp, wpLines, utf8WithoutBom);
                await System.IO.File.WriteAllLinesAsync(pathWh, whLines, utf8WithoutBom);

                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    archive.CreateEntryFromFile(pathWp, $"WP{prefix}.txt");
                    archive.CreateEntryFromFile(pathWh, $"WH{prefix}.txt");
                }

                return (memoryStream.ToArray(), $"Tax_Diskette_{prefix}.zip");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }


        // متد جدید: پیش‌نمایش دیسکت مالیات به صورت JSON
        public async Task<TaxDiskettePreviewDto?> GetTaxDiskettePreviewAsync(int runId)
        {
            var result = new TaxDiskettePreviewDto();

            const string headSql = @"
                SELECT P.PERIOD_DATE, W.WS_CODE
                FROM PAY2_RUN R
                INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;
            await ValidateEmployeeSnapshotsAsync(runId);

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);

            var lines = (await _db.DoGetDataSQLAsync<dynamic>(Pay2PayrollSnapshotQuery.Sql, new { runId }))
                .Where(x => !(bool)x.TAX_EXEMPT).ToList();
            ValidateTaxIdentities(lines);
            if (lines.Any(x => x.INSURANCE_DAYS is null || x.NOMINAL_GROSS is null))
                throw new InvalidOperationException("خروجی مالیات ممکن نیست: Snapshot اسمی روزکرد/ناخالص در Run کامل نیست.");
            var missingNominalDetails = await _db.DoGetDataSQLAsyncSingle<int>(
                @"SELECT COUNT(*) FROM PAY2_RUN_DETAIL D INNER JOIN PAY2_RUN R ON R.RUN_ID=D.RUN_ID
                  WHERE D.RUN_ID=@runId AND (D.NOMINAL_AMOUNT IS NULL OR D.ITEM_CODE_SNAP IS NULL
                    OR (R.PAYROLL_ENGINE_VERSION>=3 AND D.ITEM_NAME_SNAP IS NULL) OR D.CALC_BASIS_SNAP IS NULL
                    OR D.INS_SUBJECT_AMOUNT IS NULL OR D.TAX_SUBJECT_AMOUNT IS NULL)", new { runId });
            if (missingNominalDetails > 0)
                throw new InvalidOperationException("خروجی مالیات ممکن نیست: Snapshot مبلغ اسمی اقلام Run کامل نیست.");
            var illegalOfficialSubjects = await _db.DoGetDataSQLAsyncSingle<int>(
                "SELECT COUNT(*) FROM PAY2_RUN_DETAIL WHERE RUN_ID=@runId AND ITEM_CODE_SNAP='BASE_SAL_B' AND (ISNULL(INS_SUBJECT_AMOUNT,0)<>0 OR ISNULL(TAX_SUBJECT_AMOUNT,0)<>0)", new { runId });
            if (illegalOfficialSubjects > 0)
                throw new InvalidOperationException("خروجی قانونی ممکن نیست: BASE_SAL_B رسمی دارای مبلغ مشمول بیمه یا مالیات است.");

            const string detailsSql = @"
                SELECT D.EMP_ID, D.ITEM_CODE_SNAP ITEM_CODE, D.TAX_SUBJECT_AMOUNT AMOUNT, D.CALC_BASIS_SNAP CALC_BASIS
                FROM PAY2_RUN_DETAIL D
                WHERE D.RUN_ID = @runId AND D.ITEM_CODE_SNAP <> 'BASE_SAL_B' AND D.TAX_SUBJECT_AMOUNT > 0 AND D.NOMINAL_AMOUNT IS NOT NULL";

            var details = await _db.DoGetDataSQLAsync<dynamic>(detailsSql, new { runId });
            var groupedDetails = details.GroupBy(x => (int)x.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var line in lines)
            {
                int empId = (int)line.EMP_ID;
                string natCode = line.NATIONAL_CODE.ToString();

                // ── پر کردن مدل WP ──
                result.WpList.Add(new TaxDisketteWpDto
                {
                    NATIONAL_CODE = natCode,
                    NATIONALITY = (byte)line.NATIONALITY == 1 ? "1 (ایرانی)" : "2 (اتباع)",
                    FIRST_NAME = line.FIRST_NAME?.ToString() ?? "",
                    LAST_NAME = line.LAST_NAME?.ToString() ?? "",
                    FATHER_NAME = line.FATHER_NAME?.ToString() ?? "",
                    ID_NUMBER = line.ID_NUMBER?.ToString() ?? "",
                    BIRTH_PLACE = line.BIRTH_PLACE?.ToString() ?? "",
                    BIRTH_DATE = line.BIRTH_DATE?.ToString() ?? "",
                    MARITAL = (byte)line.MARITAL == 1 ? "1 (متاهل)" : "2 (مجرد)",
                    INS_CODE = line.INS_CODE?.ToString() ?? "",
                    JOB_NAME = line.JOB_NAME?.ToString() ?? "",
                    HIRE_DATE = DateInOccurrenceMonth(line.HIRE_DATE, periodDate),
                    FIRE_DATE = DateInOccurrenceMonth(line.FIRE_DATE, periodDate),
                    POSTAL_CODE = "", // 🚀 به جای دیتابیس رشته خالی ارسال می‌شود
                    MOBILE = line.MOBILE?.ToString() ?? ""
                });

                // ── پر کردن مدل WH ──
                long baseSalary = 0, mostamar = 0, gheyreMostamar = 0, eydi = 0, sanavat = 0;

                if (groupedDetails.TryGetValue(empId, out var empDetails))
                {
                    foreach (var det in empDetails)
                    {
                        string code = det.ITEM_CODE.ToString().ToUpper();
                        long amt = (long)det.AMOUNT;
                        byte basis = (byte)det.CALC_BASIS;

                        if (code == "BASE_SAL_B")
                            throw new InvalidOperationException("BASE_SAL_B رسمی نباید در BASE_SALARY، MOSTAMAR یا GHEYRE_MOSTAMAR مالیات وارد شود.");
                        if (code == "BASE_SAL") baseSalary += amt;
                        else if (code == "SANOVAT_PAYE") baseSalary += amt;
                        else if (code == "EIDI") eydi += amt;
                        else if (code == "SANAVAT" || code == "SENIORITY") sanavat += amt;
                        else if (basis == 2) mostamar += amt;
                        else gheyreMostamar += amt;
                    }
                }

                result.WhList.Add(new TaxDisketteWhDto
                {
                    NATIONAL_CODE = natCode,
                    YEAR = year.ToString(),
                    MONTH = month.ToString("D2"),
                    WORK_DAYS = (decimal)line.INSURANCE_DAYS,
                    BASE_SALARY = baseSalary,
                    MOSTAMAR = mostamar,
                    GHEYRE_MOSTAMAR = gheyreMostamar,
                    INS_WORKER = (long)line.INS_WORKER,
                    TAX_AMOUNT = (long)line.TAX_AMOUNT,
                    EYDI = eydi,
                    SANAVAT = sanavat,
                    TAX_BASE = (long)line.TAX_BASE
                });
            }

            if (result.WpList.Count != lines.Count || result.WhList.Count != lines.Count)
                throw new InvalidOperationException("پیش‌نمایش مالیات ناقص است: تعداد رکوردهای WP/WH با پرسنل واجد شرایط تراز نیست.");
            return result;
        }
        private static void ValidateLegalInsuranceSnapshots(IEnumerable<dynamic> lines)
        {
            if (lines.Any(x => !(bool)x.HAS_NOMINAL_RAIL || !(bool)x.HAS_COMPLETE_NOMINAL_SNAPSHOT || !(bool)x.HAS_COMPLETE_EMP_SNAPSHOT))
                throw new InvalidOperationException("خروجی قانونی بیمه ممکن نیست: Snapshot کامل ریل اسمی برای حداقل یک پرسنل وجود ندارد.");
            if (lines.Any(x => !(bool)x.PREMIUM_SNAPSHOT_AVAILABLE))
                throw new InvalidOperationException("DBF قابل تولید نیست: تفکیک Snapshot سهم کارفرما و بیمه بیکاری برای این Run ذخیره نشده است؛ تخمین مجاز نیست.");
        }

        private async Task ValidateEmployeeSnapshotsAsync(int runId)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM PAY2_RUN_LINE RL
                LEFT JOIN PAY2_RUN_EMP_SNAPSHOT ES ON ES.RUN_ID=RL.RUN_ID AND ES.EMP_ID=RL.EMP_ID
                INNER JOIN PAY2_RUN R ON R.RUN_ID=RL.RUN_ID
                WHERE RL.RUN_ID=@runId AND R.PAYROLL_ENGINE_VERSION>=3 AND ES.RUN_ID IS NULL";
            if (await _db.DoGetDataSQLAsyncSingle<int>(sql, new { runId }) > 0)
                throw new InvalidOperationException("خروجی قانونی ممکن نیست: Snapshot مشخصات پرسنل این Run کامل نیست.");
        }

        private static void ValidateTaxIdentities(IEnumerable<dynamic> lines)
        {
            foreach (var line in lines)
            {
                string employee = $"EMP_ID={(int)line.EMP_ID}، EMP_CODE={line.EMP_CODE?.ToString() ?? "-"}";
                if (string.IsNullOrWhiteSpace(line.NATIONAL_CODE?.ToString()))
                    throw new InvalidOperationException($"فایل مالیات قابل تولید نیست: کد ملی پرسنل {employee} خالی است.");
                if (string.IsNullOrWhiteSpace(line.FIRST_NAME?.ToString()) || string.IsNullOrWhiteSpace(line.LAST_NAME?.ToString()))
                    throw new InvalidOperationException($"فایل مالیات قابل تولید نیست: نام یا نام خانوادگی پرسنل {employee} خالی است.");
            }
        }

        private static void ValidateInsuranceIdentities(IEnumerable<dynamic> lines)
        {
            foreach (var line in lines)
            {
                string employee = $"EMP_ID={(int)line.EMP_ID}، EMP_CODE={line.EMP_CODE?.ToString() ?? "-"}";
                if (string.IsNullOrWhiteSpace(line.INS_CODE?.ToString()))
                    throw new InvalidOperationException($"فایل بیمه قابل تولید نیست: شماره بیمه پرسنل {employee} خالی است.");
                if (string.IsNullOrWhiteSpace(line.FIRST_NAME?.ToString()) || string.IsNullOrWhiteSpace(line.LAST_NAME?.ToString()))
                    throw new InvalidOperationException($"فایل بیمه قابل تولید نیست: نام یا نام خانوادگی پرسنل {employee} خالی است.");
                if (string.IsNullOrWhiteSpace(line.JOB_CODE?.ToString()))
                    throw new InvalidOperationException($"فایل بیمه قابل تولید نیست: کد شغل پرسنل {employee} خالی است.");
            }
        }

        internal static string DateInOccurrenceMonth(object? value, long periodDate)
        {
            if (value is null || value is DBNull) return string.Empty;
            return long.TryParse(value.ToString(), out var date) && date > 0 && date / 100 == periodDate / 100
                ? date.ToString()
                : string.Empty;
        }

    }
}
