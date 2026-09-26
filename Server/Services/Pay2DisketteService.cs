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
            // ساختار (نام، ترتیب و طول ستون‌ها) و مقادیر ثابت، مو‌به‌مو مطابق دیسکتی است که نرم‌افزار دیگرِ
            // همین کارگاه برای تیر ۱۴۰۵ ساخته و سامانه‌ی تأمین اجتماعی پذیرفته است: همه‌ی ستون‌ها متنی،
            // متن فارسی با انکدینگ ایران‌سیستم، سنوات روزانه داخل DSW_ROOZ و حق تأهل داخل DSW_MAZ
            // (ستون جدای سنوات/تأهل ندارد)، سهم کارفرما در DSK_TKOSO و بیکاری در DSK_BIC.
            var wor = NewDbfTable(WorLayout);

            long totalDailyWage = 0, totalMonthlyWage = 0, totalOtherBenefits = 0;
            long totalMash = 0, totalTotl = 0, totalWorkerIns = 0;

            foreach (var line in lines)
            {
                decimal workDays = (decimal)line.INSURANCE_DAYS;
                long baseMonthly = (long)line.BASE_WAGE_MONTHLY;
                long seniorityMonthly = (long)line.SENIORITY_MONTHLY;
                long dailyWage = workDays > 0 ? (long)Math.Round(baseMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                long seniorityBase = workDays > 0 ? (long)Math.Round(seniorityMonthly / workDays, MidpointRounding.AwayFromZero) : 0;
                long monthlyWage = (long)Math.Round((dailyWage + seniorityBase) * workDays, MidpointRounding.AwayFromZero);
                long benefits = (long)line.DBF_GENERAL_BENEFITS + (long)line.MARITAL_ALLOWANCE;
                long insBase = (long)line.INS_BASE;
                long grossPay = (long)line.NOMINAL_GROSS;
                long workerIns = (long)line.INS_WORKER;

                // جمع‌زن‌ها برای هدر کارگاه
                totalDailyWage += dailyWage + seniorityBase;
                totalMonthlyWage += monthlyWage;
                totalOtherBenefits += benefits;
                totalMash += insBase;
                totalTotl += grossPay;
                totalWorkerIns += workerIns;

                AddDbfRow(wor, $"پرسنل کد {line.EMP_CODE}",
                    wsCode,                                            // DSW_ID
                    (year % 100).ToString("00"),                       // DSW_YY
                    month.ToString(),                                  // DSW_MM
                    "01",                                              // DSW_LISTNO
                    CleanInsuranceCode(line.INS_CODE?.ToString()),     // DSW_ID1
                    line.FIRST_NAME?.ToString() ?? "",                 // DSW_FNAME
                    line.LAST_NAME?.ToString() ?? "",                  // DSW_LNAME
                    line.FATHER_NAME?.ToString() ?? "",                // DSW_DNAME
                    line.ID_NUMBER?.ToString() ?? "",                  // DSW_IDNO
                    line.BIRTH_PLACE?.ToString() ?? "",                // DSW_IDPLC
                    "0",                                               // DSW_IDATE
                    line.BIRTH_DATE?.ToString() ?? "",                 // DSW_BDATE
                    (byte)line.GENDER == 1 ? "مرد" : "زن",             // DSW_SEX
                    (byte)line.NATIONALITY == 1 ? "ایرانی" : "اتباع",   // DSW_NAT
                    line.JOB_NAME?.ToString() ?? "",                   // DSW_OCP  (عنوان شغل)
                    DateInOccurrenceMonth(line.HIRE_DATE, periodDate), // DSW_SDATE
                    DateInOccurrenceMonth(line.FIRE_DATE, periodDate), // DSW_EDATE
                    ((int)workDays).ToString(),                        // DSW_DD
                    (dailyWage + seniorityBase).ToString(),            // DSW_ROOZ (پایه + سنوات روزانه)
                    monthlyWage.ToString(),                            // DSW_MAH
                    benefits.ToString(),                               // DSW_MAZ  (مزایای مشمول + حق تأهل)
                    insBase.ToString(),                                // DSW_MASH
                    grossPay.ToString(),                               // DSW_TOTL
                    workerIns.ToString(),                              // DSW_BIME
                    "0",                                               // DSW_PRATE
                    line.JOB_CODE?.ToString() ?? "",                   // DSW_JOB  (کد شغل)
                    line.NATIONAL_CODE?.ToString() ?? "");             // PER_NATCOD
            }

            if (wor.Rows.Count != lines.Count)
                throw new InvalidOperationException("فایل بیمه قابل تولید نیست: تعداد رکوردهای DSKWOR با پرسنل واجد شرایط تراز نیست.");

            // اجزای حق بیمه دقیقاً از Snapshot همان Run خوانده می‌شوند.
            long totalKarf = lines.Sum(l => (long)l.INS_EMPLOYER_BASE);
            long totalBikari = lines.Sum(l => (long)l.INS_UNEMPLOYMENT);

            var kar = NewDbfTable(KarLayout);
            AddDbfRow(kar, "کارگاه",
                wsCode,                                                 // DSK_ID
                head.WS_NAME?.ToString() ?? "",                         // DSK_NAME
                head.EMPLOYER_NAME?.ToString() ?? "",                   // DSK_FARM
                head.ADDRESS?.ToString() ?? "",                         // DSK_ADRS
                "0",                                                    // DSK_KIND
                (year % 100).ToString("00"),                            // DSK_YY
                month.ToString(),                                       // DSK_MM
                "01",                                                   // DSK_LISTNO
                "",                                                     // DSK_DISC
                lines.Count.ToString(),                                 // DSK_NUM
                ((int)lines.Sum(x => (decimal)x.INSURANCE_DAYS)).ToString(), // DSK_TDD
                totalDailyWage.ToString(),                              // DSK_TROOZ
                totalMonthlyWage.ToString(),                            // DSK_TMAH
                totalOtherBenefits.ToString(),                          // DSK_TMAZ
                totalMash.ToString(),                                   // DSK_TMASH
                totalTotl.ToString(),                                   // DSK_TTOTL
                totalWorkerIns.ToString(),                              // DSK_TBIME
                totalKarf.ToString(),                                   // DSK_TKOSO (سهم کارفرما)
                totalBikari.ToString(),                                 // DSK_BIC   (بیمه بیکاری)
                "23",                                                   // DSK_RATE  (۲۰٪ کارفرما + ۳٪ بیکاری)
                "0",                                                    // DSK_PRATE
                "0",                                                    // DSK_BIMH
                "0");                                                   // MON_PYM

            // ─── تولید فایل‌های DBF و زیپ کردن ───
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(1256);

            string tempDir = Path.Combine(Path.GetTempPath(), $"Bimeh_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            string pathKar = Path.Combine(tempDir, "DSKKAR00.DBF");
            string pathWor = Path.Combine(tempDir, "DSKWOR00.DBF");

            try
            {
                DbfFile.Write(pathKar, kar, encoding, overwrite: true, useIranSystemEncoding: true);
                DbfFile.Write(pathWor, wor, encoding, overwrite: true, useIranSystemEncoding: true);

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

        // طول هر ستون (همه از نوع C) عیناً از دیسکت پذیرفته‌شده‌ی تیر ۱۴۰۵ همین کارگاه
        public static readonly (string Name, int Length)[] KarLayout =
        {
            ("DSK_ID", 10), ("DSK_NAME", 100), ("DSK_FARM", 100), ("DSK_ADRS", 100), ("DSK_KIND", 1),
            ("DSK_YY", 2), ("DSK_MM", 2), ("DSK_LISTNO", 12), ("DSK_DISC", 100), ("DSK_NUM", 5),
            ("DSK_TDD", 6), ("DSK_TROOZ", 12), ("DSK_TMAH", 12), ("DSK_TMAZ", 12), ("DSK_TMASH", 12),
            ("DSK_TTOTL", 12), ("DSK_TBIME", 12), ("DSK_TKOSO", 12), ("DSK_BIC", 12), ("DSK_RATE", 12),
            ("DSK_PRATE", 2), ("DSK_BIMH", 12), ("MON_PYM", 3),
        };

        public static readonly (string Name, int Length)[] WorLayout =
        {
            ("DSW_ID", 10), ("DSW_YY", 2), ("DSW_MM", 2), ("DSW_LISTNO", 12), ("DSW_ID1", 10),
            ("DSW_FNAME", 100), ("DSW_LNAME", 100), ("DSW_DNAME", 100), ("DSW_IDNO", 15), ("DSW_IDPLC", 100),
            ("DSW_IDATE", 8), ("DSW_BDATE", 8), ("DSW_SEX", 3), ("DSW_NAT", 10), ("DSW_OCP", 100),
            ("DSW_SDATE", 8), ("DSW_EDATE", 8), ("DSW_DD", 2), ("DSW_ROOZ", 12), ("DSW_MAH", 12),
            ("DSW_MAZ", 12), ("DSW_MASH", 12), ("DSW_TOTL", 12), ("DSW_BIME", 12), ("DSW_PRATE", 2),
            ("DSW_JOB", 6), ("PER_NATCOD", 10),
        };

        public static DataTable NewDbfTable((string Name, int Length)[] layout)
        {
            var table = new DataTable();
            foreach (var (name, length) in layout)
                table.Columns.Add(new DataColumn(name, typeof(string)) { MaxLength = length });
            return table;
        }

        // متن آزاد اگر از طول ستون بیشتر شد بریده می‌شود (مثل خروجی قبلی)؛ شناسه و مبلغ بریده نمی‌شود،
        // چون عدد یا کد بریده‌شده فایل قانونی را بی‌صدا غلط می‌کند — به‌جایش خطای روشن فارسی.
        private static readonly HashSet<string> TruncatableDbfFields = new()
        {
            "DSK_NAME", "DSK_FARM", "DSK_ADRS", "DSK_DISC",
            "DSW_FNAME", "DSW_LNAME", "DSW_DNAME", "DSW_IDPLC", "DSW_OCP",
        };

        public static void AddDbfRow(DataTable table, string owner, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                var column = table.Columns[i];
                if (values[i].Length <= column.MaxLength) continue;
                if (!TruncatableDbfFields.Contains(column.ColumnName))
                    throw new InvalidOperationException(
                        $"فایل بیمه قابل تولید نیست: مقدار «{values[i]}» برای ستون {column.ColumnName} ({owner}) از {column.MaxLength} کاراکتر مجاز بیشتر است.");
                values[i] = values[i][..column.MaxLength];
            }
            table.Rows.Add(values);
        }

        // شماره بیمه بدون صفر پیشرو، حتی اگر در پرونده با صفر ثبت شده باشد — دیسکت پذیرفته‌شده هم «80277820» دارد نه «0080277820»
        public static string CleanInsuranceCode(string? code)
        {
            if (IsMissingInsuranceCode(code))
                throw new InvalidOperationException("شماره بیمه خالی است و جایگزینی با شماره ساختگی مجاز نیست.");
            return code!.Trim().TrimStart('0');
        }

        // «0» (یا هر تعداد صفر) همان خالی است: در فایل به 0000000000 تبدیل می‌شد و سامانه‌ی تأمین اجتماعی ردش می‌کرد.
        public static bool IsMissingInsuranceCode(string? code) =>
            string.IsNullOrWhiteSpace(code) || code.Trim().All(c => c == '0');

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
                    DSW_ID1 = CleanInsuranceCode(insCodeStr),
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
            var gaps = new List<InsuranceIdentityGap>();
            foreach (var line in lines)
            {
                string first = line.FIRST_NAME?.ToString()?.Trim() ?? "";
                string last = line.LAST_NAME?.ToString()?.Trim() ?? "";
                string code = line.EMP_CODE?.ToString()?.Trim() ?? "";
                gaps.Add(new InsuranceIdentityGap(
                    $"{first} {last}".Trim(),
                    string.IsNullOrEmpty(code) ? $"شناسه {(int)line.EMP_ID}" : code,
                    IsMissingInsuranceCode(line.INS_CODE?.ToString()),
                    first.Length == 0 || last.Length == 0,
                    string.IsNullOrWhiteSpace(line.JOB_CODE?.ToString())));
            }

            var message = DescribeInsuranceIdentityGaps(gaps);
            if (message != null)
                throw new InvalidOperationException(message);
        }

        public sealed record InsuranceIdentityGap(string Name, string EmpCode, bool MissingInsCode, bool MissingName, bool MissingJobCode);

        // همه‌ی پرسنلِ ناقص یک‌جا، با نام و کد پرسنلی و راه رفع — نه فقط اولین نفر با EMP_ID.
        // کاربر قبلاً با هر بار اصلاح فقط نفر بعدی را می‌دید، و چون لیست از اطلاعاتِ ثبت‌شده
        // در محاسبه‌ی همان ماه ساخته می‌شود، بدون بازمحاسبه اصلاح پرونده هم بی‌اثر به‌نظر می‌رسید.
        public static string? DescribeInsuranceIdentityGaps(IEnumerable<InsuranceIdentityGap> gaps)
        {
            const int MaxNames = 10;
            var list = gaps.ToList();
            var groups = new (string Title, string Fix, List<InsuranceIdentityGap> People)[]
            {
                ("شماره بیمه ندارند (خالی یا صفر)",
                 "اگر بیمه نمی‌شوند «نوع بیمه» را «معاف از بیمه» بگذارید؛ اگر بیمه می‌شوند «شماره بیمه» را وارد کنید.",
                 list.Where(g => g.MissingInsCode).ToList()),
                ("نام یا نام خانوادگی ندارند",
                 "نام و نام خانوادگی را کامل کنید.",
                 list.Where(g => g.MissingName).ToList()),
                ("کد شغل ندارند",
                 "در پرونده «شغل» را انتخاب کنید و اگر انتخاب شده، در «مدیریت مشاغل» کد شغلِ آن را وارد کنید.",
                 list.Where(g => g.MissingJobCode).ToList()),
            };

            var people = groups.SelectMany(g => g.People).Distinct().Count();
            if (people == 0) return null;

            var sb = new StringBuilder();
            sb.Append($"لیست بیمه ساخته نشد: اطلاعات {people} نفر ناقص است.");
            foreach (var (title, fix, group) in groups.Where(g => g.People.Count > 0))
            {
                sb.Append($"\n\n{group.Count} نفر {title}:");
                foreach (var g in group.Take(MaxNames))
                    sb.Append(g.Name.Length > 0 ? $"\n• {g.Name} (کد پرسنلی {g.EmpCode})" : $"\n• کد پرسنلی {g.EmpCode}");
                if (group.Count > MaxNames)
                    sb.Append($"\n• و {group.Count - MaxNames} نفر دیگر");
                sb.Append($"\nراه رفع: {fix}");
            }
            sb.Append("\n\nمراحل:");
            sb.Append("\n۱. در «پرسنل و احکام» پرونده‌ی این افراد را طبق بالا اصلاح کنید.");
            sb.Append("\n۲. در «محاسبه حقوق» همین ماه را دوباره محاسبه کنید؛ لیست بیمه از اطلاعات همان محاسبه ساخته می‌شود و بدون بازمحاسبه اصلاح پرونده اثر نمی‌کند.");
            sb.Append(" اگر سند صادر شده: لغو صدور سند ← لغو تأیید ← بازمحاسبه حقوق ← تأیید نهایی ← صدور قطعی سند.");
            sb.Append("\n۳. دوباره لیست بیمه را بگیرید.");
            return sb.ToString();
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
