using System.Data;
using Dapper;
using Safir.Shared.Models.Treasury;

namespace Safir.Server.Treasury
{
    // ═══════════════════════════════════════════════════════════════════
    //  نوشتنِ یک چک در PAY_GETD (دریافتی) و PAY_GETP (پرداختی) — مشترکِ
    //  خزانه (GETCHEK / PAYCHEK) و صدورِ سند (SGETCHEK / SPAYCHEK در DEED_HEAD).
    //  اعتبارسنجی و «در گردش» بودن را فراخوان بررسی می‌کند؛ اینجا فقط تکراری‌بودن،
    //  ردیفِ دفتر، سابقه (TR_) و لاگ — همه داخلِ تراکنشِ فراخوان.
    // ═══════════════════════════════════════════════════════════════════
    public sealed partial class TreasuryService
    {
        internal sealed class ReceivedChequeWrite
        {
            /// <summary>چکِ فعلیِ سطر (اصلاح)؛ null = چکِ تازه.</summary>
            public long? CurrentId { get; set; }
            public double Serial { get; set; }
            public int Bank { get; set; }
            public long DateS { get; set; }
            public long Date { get; set; }
            public string? Shobeh { get; set; }
            public double Mabl { get; set; }
            public string NameTah { get; set; } = "";
            public string? NHesab { get; set; }
            /// <summary>صاحبِ چک (PAY_GETD.CUST_NO).</summary>
            public string? CustNo { get; set; }
            public int? ListNo { get; set; }
            /// <summary>۱ تجاری (ADA)، ۰ غیرتجاری (ADV).</summary>
            public int Kind { get; set; }
            public int Sandugh { get; set; }
            public string? Sayadi { get; set; }
            /// <summary>واگذاری به بانک هنگامِ دریافت (زیرِ BANKHA)؛ null = نزد صندوق.</summary>
            public int[]? Hes1 { get; set; }
            public string? Hes1Text { get; set; }
        }

        internal sealed class PaidChequeWrite
        {
            public long? CurrentId { get; set; }
            public double Serial { get; set; }
            public int Bank { get; set; }
            public long DateS { get; set; }
            public long Date { get; set; }
            public string? Shobeh { get; set; }
            public double Mabl { get; set; }
            public string NameTah { get; set; } = "";
            public string? NHesab { get; set; }
            /// <summary>۱ تجاری (APA)، ۰ غیرتجاری (APV).</summary>
            public int Kind { get; set; }
            public string? Sayadi { get; set; }
            /// <summary>حسابِ بانکیِ پرداخت (پیش‌فرض اولین تفصیلیِ BANKHA).</summary>
            public int[]? Hes1 { get; set; }
            public string? Hes1Text { get; set; }
        }

        /// <summary>دفتر اسناد دریافتنی (DAFT_ASN): شماره‌ی شروع و شماره‌ی دفتر — اگر نیست (۱، ۱) ساخته می‌شود.</summary>
        internal static async Task<(int First, int Book)> DaftAsync(IDbConnection conn, IDbTransaction tx)
        {
            var daft = (await conn.QueryAsync<(int First, int Book)>(
                "SELECT TOP 1 FIRSTNUM, BOOKNUM FROM dbo.DAFT_ASN ORDER BY BOOKNUM DESC", transaction: tx)).FirstOrDefault();
            if (daft == default)
            {
                await conn.ExecuteAsync("INSERT INTO dbo.DAFT_ASN (FIRSTNUM, BOOKNUM) VALUES (1, 1)", transaction: tx);
                daft = (1, 1);
            }
            return daft;
        }

        /// <summary>
        /// درج یا اصلاحِ یک چکِ دریافتی. تکراری (همان سریال و بانک) خطاست، مگر چکِ پارک‌شده روی ۹۱۱ که
        /// دوباره به کار می‌رود. چکِ تازه ردیفِ دفتر می‌گیرد — متنِ «شماره دفتر: X» برگردانده می‌شود.
        /// </summary>
        internal static async Task<string?> WriteReceivedChequeAsync(IDbConnection conn, IDbTransaction tx, ReceivedChequeWrite w,
                                                                     (int First, int Book) daft, int? bankha, string userName, string? clientIp)
        {
            var curId = w.CurrentId;
            // تکراری: همان سریال و بانک (GETCHEK: «چکی با همین سریال و بانک قبلاً ثبت شده است»).
            // چکِ پارک‌شده روی ۹۱۱ (سطرِ دریافتش حذف شده) دوباره به کار می‌رود.
            var dup = (await conn.QueryAsync<ChequeKey>(
                "SELECT TOP 1 ID Id, RADIF Radif, N_KOL NKol FROM dbo.PAY_GETD WITH (UPDLOCK, HOLDLOCK) WHERE N_SERI = @serial AND BANK = @bank AND ID <> ISNULL(@curId, -1)",
                new { serial = w.Serial, bank = w.Bank, curId }, tx)).FirstOrDefault();
            if (dup is not null)
            {
                if (dup.NKol == 911 && curId is null) curId = dup.Id;
                else throw new UserError($"چکی با سریالِ {ChequeSerial(w.Serial)} و همین بانک قبلاً ثبت شده است (ردیف دفتر {dup.Radif:0}).");
            }

            var existing = curId is null ? null : await ChequeByIdAsync(conn, tx, false, curId.Value);
            var keepAssign = existing is not null && existing.InCirculation(bankha) && existing.NKol != 911;
            double? radif = existing?.Radif;
            double? anbar = null;
            string? info = null;
            if (radif is null or 0)
            {
                var maxR = await conn.ExecuteScalarAsync<double?>(
                    "SELECT MAX(RADIF) FROM dbo.PAY_GETD WITH (UPDLOCK, HOLDLOCK) WHERE ANBAR = @dfn", new { dfn = daft.Book }, tx);
                radif = maxR is null ? daft.First : maxR + 1;
                anbar = daft.Book;
                info = $"شماره دفتر: {radif:0}";
            }

            var custNo = w.CustNo?.Trim();
            var args = new
            {
                ID = curId,
                N_SERI = w.Serial, BANK = w.Bank, DATE_S = w.DateS, DATE = w.Date,
                SHOBEH = w.Shobeh?.Trim(), MABL = w.Mabl, NAME_TAH = w.NameTah.Trim(), N_HESAB = w.NHesab?.Trim(),
                ANBAR = anbar, RADIF = radif,
                CUST_NO = string.IsNullOrEmpty(custNo) ? null : custNo.Length > 20 ? custNo[..20] : custNo,
                VAZ = existing is not null && existing.NKol != 911 ? existing.Vaz ?? 1 : 1,
                LIST_NO = w.ListNo, KIND = w.Kind, SANDUGH = w.Sandugh, SAYADI = w.Sayadi?.Trim(),
                N_KOL = keepAssign ? existing!.NKol : (int?)w.Hes1?[0],
                KEEP = keepAssign ? 1 : 0,
                N_MOIN = w.Hes1?[1], N_TAF = w.Hes1?[2], HES1 = w.Hes1Text
            };

            if (curId is not null)
            {
                await CopyToHistoryAsync(conn, tx, "PAY_GETD", "ID = @id", new { id = curId }, true, userName, clientIp);
                // در گردش: حسابِ واگذاری فقط با FORCHEK/BAKCHEK/وصول عوض می‌شود
                await conn.ExecuteAsync(@"
                    UPDATE dbo.PAY_GETD SET N_SERI = @N_SERI, BANK = @BANK, DATE_S = @DATE_S, DATE = @DATE, SHOBEH = @SHOBEH,
                           MABL = @MABL, NAME_TAH = @NAME_TAH, N_HESAB = @N_HESAB,
                           ANBAR = ISNULL(@ANBAR, ANBAR), RADIF = @RADIF, CUST_NO = @CUST_NO, VAZ = @VAZ, LIST_NO = @LIST_NO,
                           KIND = @KIND, SANDUGH = @SANDUGH, SAYADI = @SAYADI,
                           N_KOL = CASE WHEN @KEEP = 1 THEN N_KOL ELSE @N_KOL END,
                           N_MOIN = CASE WHEN @KEEP = 1 THEN N_MOIN ELSE @N_MOIN END,
                           N_TAF = CASE WHEN @KEEP = 1 THEN N_TAF ELSE @N_TAF END,
                           HES1 = CASE WHEN @KEEP = 1 THEN HES1 ELSE @HES1 END
                    WHERE ID = @ID", args, tx);
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO dbo.PAY_GETD (N_SERI, BANK, DATE_S, DATE, SHOBEH, MABL, NAME_TAH, ANBAR, RADIF, CUST_NO, VAZ,
                                              LIST_NO, KIND, SANDUGH, N_HESAB, SAYADI, N_KOL, N_MOIN, N_TAF, HES1)
                    VALUES (@N_SERI, @BANK, @DATE_S, @DATE, @SHOBEH, @MABL, @NAME_TAH, @ANBAR, @RADIF, @CUST_NO, @VAZ,
                            @LIST_NO, @KIND, @SANDUGH, @N_HESAB, @SAYADI, @N_KOL, @N_MOIN, @N_TAF, @HES1)", args, tx);
            }

            await LogReceivedAsync(conn, tx, w.Serial, w.Bank, w.DateS, 1, w.Sandugh, userName, setVaz: false);
            return info;
        }

        /// <summary>
        /// درج یا اصلاحِ یک چکِ خودمان. تکراری خطاست، مگر چکِ پارک‌شده روی ۹۱۱. اصلاح، ستون‌های وصول و
        /// برگشت را خالی می‌کند (همان UPDATE ِ PAYCHEK) — پس فراخوان باید «در گردش» را از قبل رد کرده باشد.
        /// </summary>
        internal static async Task WritePaidChequeAsync(IDbConnection conn, IDbTransaction tx, PaidChequeWrite w, string userName, string? clientIp)
        {
            var curId = w.CurrentId;
            var dup = (await conn.QueryAsync<ChequeKey>(
                "SELECT TOP 1 ID Id, CAST(RADIF AS float) Radif, N_KOL NKol FROM dbo.PAY_GETP WITH (UPDLOCK, HOLDLOCK) WHERE N_SERI = @serial AND BANK = @bank AND ID <> ISNULL(@curId, -1)",
                new { serial = w.Serial, bank = w.Bank, curId }, tx)).FirstOrDefault();
            if (dup is not null)
            {
                if (dup.NKol == 911 && curId is null) curId = dup.Id;
                else throw new UserError($"چکی با سریالِ {ChequeSerial(w.Serial)} و همین بانک قبلاً پرداخت شده است.");
            }

            var args = new
            {
                ID = curId, N_SERI = w.Serial, BANK = w.Bank, DATE_S = w.DateS, DATE = w.Date,
                SHOBEH = w.Shobeh?.Trim() ?? "", MABL = w.Mabl, NAME_TAH = w.NameTah.Trim(), N_HESAB = w.NHesab?.Trim() ?? "",
                KIND = w.Kind, HES1 = w.Hes1Text, SAYADI = string.IsNullOrWhiteSpace(w.Sayadi) ? "0" : w.Sayadi.Trim(),
                N_KOL = w.Hes1?[0], N_MOIN = w.Hes1?[1], N_TAF = w.Hes1?[2]
            };
            if (curId is not null)
            {
                await CopyToHistoryAsync(conn, tx, "PAY_GETP", "ID = @id", new { id = curId }, true, userName, clientIp);
                await conn.ExecuteAsync(@"
                    UPDATE dbo.PAY_GETP SET N_SERI = @N_SERI, BANK = @BANK, DATE_S = @DATE_S, DATE = @DATE, SHOBEH = @SHOBEH,
                           MABL = @MABL, NAME_TAH = @NAME_TAH, N_HESAB = @N_HESAB, KIND = @KIND, HES1 = @HES1, SAYADI = @SAYADI,
                           N_KOL = @N_KOL, N_MOIN = @N_MOIN, N_TAF = @N_TAF,
                           N_S = NULL, N_KOL2 = NULL, N_MOIN2 = NULL, N_TAF2 = NULL, N_KOL3 = NULL, N_MOIN3 = NULL, N_TAF3 = NULL,
                           NUMBER = NULL, TAG = NULL, ANBAR = NULL, RADIF = NULL, CUST_NO = DEFAULT, VAZ = NULL, HES2 = NULL, HES3 = NULL
                    WHERE ID = @ID", args, tx);
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO dbo.PAY_GETP (N_SERI, BANK, DATE_S, DATE, SHOBEH, MABL, NAME_TAH, N_HESAB, N_S, N_KOL, N_MOIN, N_TAF,
                                              N_KOL2, N_MOIN2, N_TAF2, N_KOL3, N_MOIN3, N_TAF3, NUMBER, TAG, ANBAR, RADIF, CUST_NO, KIND, VAZ,
                                              HES1, HES2, HES3, SAYADI)
                    VALUES (@N_SERI, @BANK, @DATE_S, @DATE, @SHOBEH, @MABL, @NAME_TAH, @N_HESAB, NULL, @N_KOL, @N_MOIN, @N_TAF,
                            NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, @KIND, NULL,
                            @HES1, NULL, NULL, @SAYADI)", args, tx);
            }
        }
    }
}
