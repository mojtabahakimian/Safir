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
                FROM PAY2_RUN R WITH (NOLOCK)
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            string wsCode = head.WS_CODE?.ToString() ?? "";

            // تمام مبالغ از Snapshot مؤثر همان Run خوانده می‌شوند.
            var lines = (await _db.DoGetDataSQLAsync<dynamic>(Pay2PayrollSnapshotQuery.Sql, new { runId }))
                .Where(x => (byte)x.INS_TYPE != 3).ToList();

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
                long insBase = monthlyWage + otherBenefits + maritalAllowance;
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
                    ["DSW_OCP"] = line.JOB_CODE?.ToString() ?? "000000",
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

            // محاسبه سهم کارفرما (با رعایت دقیق آنچه در دیتابیس ذخیره شده)
            long totalEmployerInsRaw = lines.Sum(l => (long)l.INS_EMPLOYER);

            // حق بیمه بیکاری (۳٪) همیشه ۳/۲۳ سهم کارفرماست (مگر اینکه معافیت یا ماده ۷ باشد)
            // به عنوان تقریب امن، مستقیماً از پایه مشمول حساب می‌کنیم.
            long totalBikari = (long)(totalMash * 0.03m);
            long totalKarf = (long)(totalMash * 0.20m);

            // 🚀 جادوی ماده ۷ (معافیت کارگاه تا ۵ نفر): در SP ما ذخیره شده، اینجا فقط استخراج می‌کنیم
            if (totalEmployerInsRaw < (totalKarf + totalBikari))
            {
                // اگر در دیتابیس کسر کمتری خورده، یعنی کارگاه معافیت داشته.
                // تامین اجتماعی خودش می‌داند، ما فقط عدد کارفرما را می‌فرستیم.
                totalKarf = totalEmployerInsRaw - totalBikari;
                if (totalKarf < 0) totalKarf = 0;
            }

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
            if (string.IsNullOrWhiteSpace(code)) return "0000000000";
            return code.Trim().PadLeft(10, '0');
        }

        // متد جدید: پیش‌نمایش دیسکت (DBF) به صورت JSON
        public async Task<DiskettePreviewDto?> GetInsuranceDiskettePreviewAsync(int runId)
        {
            var result = new DiskettePreviewDto();

            const string headSql = @"
                SELECT 
                    P.PERIOD_DATE, W.WS_CODE, W.WS_NAME, W.EMPLOYER_NAME
                FROM PAY2_RUN R WITH (NOLOCK)
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            string wsCode = head.WS_CODE?.ToString() ?? "";

            var lines = (await _db.DoGetDataSQLAsync<dynamic>(Pay2PayrollSnapshotQuery.Sql, new { runId }))
                .Where(x => (byte)x.INS_TYPE != 3).ToList();

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
                long insBase = monthlyWage + otherBenefits + maritalAllowance;

                totalMash += insBase;
                totalTotl += (long)line.NOMINAL_GROSS;
                totalWorkerIns += (long)line.INS_WORKER;
                totalMarital += maritalAllowance;
                totalSeniority += seniorityBase;

                string insCodeStr = line.INS_CODE?.ToString() ?? "";

                result.WorList.Add(new DisketteWorDto
                {
                    DSW_ID1 = string.IsNullOrWhiteSpace(insCodeStr) ? "0000000000" : insCodeStr.Trim().PadLeft(10, '0'),
                    FULL_NAME = $"{line.LAST_NAME} {line.FIRST_NAME}",
                    PER_NATCOD = line.NATIONAL_CODE?.ToString() ?? "",
                    DSW_OCP = line.JOB_CODE?.ToString() ?? "000000",
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

            long totalEmployerInsRaw = lines.Sum(l => (long)l.INS_EMPLOYER);
            long totalBikari = (long)(totalMash * 0.03m);
            long totalKarf = (long)(totalMash * 0.20m);

            if (totalEmployerInsRaw < (totalKarf + totalBikari))
            {
                totalKarf = totalEmployerInsRaw - totalBikari;
                if (totalKarf < 0) totalKarf = 0;
            }

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
                FROM PAY2_RUN R WITH (NOLOCK)
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            string wsCode = head.WS_CODE?.ToString() ?? "";

            // 🚀 اصلاح شد: ADDRESS و POSTAL_CODE از کوئری حذف شدند
            const string linesSql = @"
                SELECT 
                    RL.EMP_ID, E.EMP_CODE, E.NATIONAL_CODE, E.FIRST_NAME, E.LAST_NAME, E.FATHER_NAME,
                    E.ID_NUMBER, E.BIRTH_PLACE, E.BIRTH_DATE, E.NATIONALITY, COALESCE(RL.HIRE_DATE_SNAP,E.HIRE_DATE) HIRE_DATE, COALESCE(RL.FIRE_DATE_SNAP,E.FIRE_DATE) FIRE_DATE,
                    E.INS_CODE, E.MOBILE, E.MARITAL,
                    J.JOB_NAME,
                    COALESCE(RL.NOMINAL_DAYS,RL.WORK_DAYS) WORK_DAYS, COALESCE(RL.NOMINAL_GROSS,RL.GROSS_PAY) GROSS_PAY, RL.INS_WORKER, RL.TAX_BASE, RL.TAX_AMOUNT
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                LEFT JOIN PAY2_JOB J WITH (NOLOCK) ON E.JOB_ID = J.JOB_ID
                WHERE RL.RUN_ID = @runId AND E.TAX_EXEMPT = 0
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            var lines = (await _db.DoGetDataSQLAsync<dynamic>(linesSql, new { runId })).ToList();

            const string detailsSql = @"
                SELECT D.EMP_ID, COALESCE(D.ITEM_CODE_SNAP,I.ITEM_CODE) ITEM_CODE, COALESCE(D.NOMINAL_AMOUNT,D.AMOUNT) AMOUNT, COALESCE(D.CALC_BASIS_SNAP,I.CALC_BASIS) CALC_BASIS
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId AND D.TAX_SUBJECT = 1";

            var details = await _db.DoGetDataSQLAsync<dynamic>(detailsSql, new { runId });
            var groupedDetails = details.GroupBy(x => (int)x.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            var wpLines = new List<string>();
            var whLines = new List<string>();

            // فرمت UTF-8 بدون BOM (پیش‌نیاز اجباری سامانه دارایی)
            var utf8WithoutBom = new UTF8Encoding(false);

            foreach (var line in lines)
            {
                int empId = (int)line.EMP_ID;
                string natCode = line.NATIONAL_CODE?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(natCode)) continue;

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

                        if (code == "BASE_SAL") baseSalary += amt;
                        else if (code == "SANOVAT_PAYE") sanavat += amt;
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
                    ((decimal)line.WORK_DAYS).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
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
                FROM PAY2_RUN R WITH (NOLOCK)
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID = W.WS_ID
                WHERE R.RUN_ID = @runId";

            var head = await _db.DoGetDataSQLAsyncSingle<dynamic>(headSql, new { runId });
            if (head == null) return null;

            long periodDate = (long)head.PERIOD_DATE;
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);

            // 🚀 اصلاح شد: ADDRESS و POSTAL_CODE از کوئری حذف شدند
            const string linesSql = @"
                SELECT 
                    RL.EMP_ID, E.EMP_CODE, E.NATIONAL_CODE, E.FIRST_NAME, E.LAST_NAME, E.FATHER_NAME,
                    E.ID_NUMBER, E.BIRTH_PLACE, E.BIRTH_DATE, E.NATIONALITY, COALESCE(RL.HIRE_DATE_SNAP,E.HIRE_DATE) HIRE_DATE, COALESCE(RL.FIRE_DATE_SNAP,E.FIRE_DATE) FIRE_DATE,
                    E.INS_CODE, E.MOBILE, E.MARITAL,
                    J.JOB_NAME,
                    COALESCE(RL.NOMINAL_DAYS,RL.WORK_DAYS) WORK_DAYS, COALESCE(RL.NOMINAL_GROSS,RL.GROSS_PAY) GROSS_PAY, RL.INS_WORKER, RL.TAX_BASE, RL.TAX_AMOUNT
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                LEFT JOIN PAY2_JOB J WITH (NOLOCK) ON E.JOB_ID = J.JOB_ID
                WHERE RL.RUN_ID = @runId AND E.TAX_EXEMPT = 0
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            var lines = (await _db.DoGetDataSQLAsync<dynamic>(linesSql, new { runId })).ToList();

            const string detailsSql = @"
                SELECT D.EMP_ID, COALESCE(D.ITEM_CODE_SNAP,I.ITEM_CODE) ITEM_CODE, COALESCE(D.NOMINAL_AMOUNT,D.AMOUNT) AMOUNT, COALESCE(D.CALC_BASIS_SNAP,I.CALC_BASIS) CALC_BASIS
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId AND D.TAX_SUBJECT = 1";

            var details = await _db.DoGetDataSQLAsync<dynamic>(detailsSql, new { runId });
            var groupedDetails = details.GroupBy(x => (int)x.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var line in lines)
            {
                int empId = (int)line.EMP_ID;
                string natCode = line.NATIONAL_CODE?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(natCode)) continue;

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

                        if (code == "BASE_SAL") baseSalary += amt;
                        else if (code == "SANOVAT_PAYE") sanavat += amt;
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
                    WORK_DAYS = (decimal)line.WORK_DAYS,
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

            return result;
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