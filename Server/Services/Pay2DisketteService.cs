using Dapper;
using Dbf;
using Safir.Shared.Interfaces;
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

            // ۲. خواندن اطلاعات پرسنل
            const string linesSql = @"
                SELECT 
                    RL.EMP_ID, E.FIRST_NAME, E.LAST_NAME, E.FATHER_NAME,
                    E.NATIONAL_CODE, E.INS_CODE, E.BIRTH_DATE, E.GENDER, E.NATIONALITY,
                    E.BIRTH_PLACE, E.ID_NUMBER,
                    J.JOB_NAME, J.JOB_CODE,
                    RL.WORK_DAYS, RL.INS_BASE, RL.INS_WORKER, RL.INS_EMPLOYER, RL.GROSS_PAY,
                    A.DAYSB
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                LEFT JOIN PAY2_ATTENDANCE A WITH (NOLOCK) ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = (SELECT PER_ID FROM PAY2_RUN WHERE RUN_ID = @runId)
                LEFT JOIN PAY2_JOB J WITH (NOLOCK) ON E.JOB_ID = J.JOB_ID
                WHERE RL.RUN_ID = @runId AND E.INS_TYPE <> 3 -- حذف معافین کامل
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            var lines = (await _db.DoGetDataSQLAsync<dynamic>(linesSql, new { runId })).ToList();

            // ۳. خواندن ریزمبالغ برای تفکیک (حق تاهل، سنوات، حقوق پایه)
            const string detailsSql = @"
                SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId AND I.INS_SUBJECT = 1";

            var details = await _db.DoGetDataSQLAsync<dynamic>(detailsSql, new { runId });
            var groupedDetails = details.GroupBy(x => (int)x.EMP_ID).ToDictionary(g => g.Key, g => g.ToList());

            // ─── آماده‌سازی لیست‌های DBF ───
            var dskworList = new List<Dictionary<string, object>>();

            long totalDailyWage = 0, totalMonthlyWage = 0, totalOtherBenefits = 0;
            long totalMash = 0, totalTotl = 0, totalWorkerIns = 0;
            long totalMarital = 0, totalSeniority = 0;

            foreach (var line in lines)
            {
                int empId = (int)line.EMP_ID;
                decimal workDays = (decimal)line.WORK_DAYS;

                long monthlyWage = 0;
                long maritalAllowance = 0;
                long seniorityBase = 0;

                if (groupedDetails.TryGetValue(empId, out var empDetails))
                {
                    foreach (var det in empDetails)
                    {
                        string code = det.ITEM_CODE.ToString().ToUpper();
                        long amt = (long)det.AMOUNT;

                        if (code == "BASE_SAL_B" || code == "BASE_SAL") monthlyWage += amt;
                        else if (code == "FAMILY_ALLOW") maritalAllowance += amt;
                        else if (code == "SENIORITY" || code == "SANOVAT_PAYE") seniorityBase += amt;
                    }
                }

                long dailyWage = workDays > 0 ? (long)(monthlyWage / workDays) : 0;
                long insBase = (long)line.INS_BASE;
                long otherBenefits = insBase - monthlyWage - maritalAllowance - seniorityBase;
                if (otherBenefits < 0) otherBenefits = 0;

                long grossPay = (long)line.GROSS_PAY;
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
                    ["DSW_SDATE"] = "", // فیلد استخدام در ماه جاری (پیچیدگی اضافی، خالی می‌ماند)
                    ["DSW_EDATE"] = "", // فیلد ترک کار
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
                ["DSK_TDD"] = (int)lines.Sum(x => (decimal)x.WORK_DAYS),
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
    }
}