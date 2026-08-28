using Dapper;
using System.Data;
using System.Globalization;

namespace Safir.Server.CostClose.GroupDocuments
{
    // ═══════════════════════════════════════════════════════════════════════
    //  رزرو دسته‌ای شماره سند (DEED_HED.N_S/BAYEG) — مشترک بین همه‌ی
    //  سرویس‌های «سند گروهی» (خروج مواد، انتقالی، فروش، برگشت فروش،
    //  انبارگردانی، خروج سایر، تولید).
    //
    //  از MaterialIssueRebuildService.cs استخراج شده تا در ۶ سرویس دیگر
    //  تکرار نشود — منطق دقیقاً همان است (قفل صریح روی همان منبع
    //  نام‌گذاری‌شده‌ای که Pay2RunController برای شماره‌گذاری DEED_HED
    //  استفاده می‌کند)، فقط NO_S پارامتری شده چون هر نوع سند کد خودش را
    //  دارد (۸=خروج مواد، ۱۰=انتقالی، ...).
    // ═══════════════════════════════════════════════════════════════════════

    public sealed class SanadHeaderRequest
    {
        public long    DATE_S    { get; set; }
        public string? SHARH_S   { get; set; }
        public string? USER_NAME { get; set; }
    }

    public static class SanadNumbering
    {
        private static string SqlNum(double v) => v.ToString("0.##########", CultureInfo.InvariantCulture);
        private static string SqlText(string? v) => (v ?? string.Empty).Replace("'", "''");

        public static async Task<List<double>> ReserveBatchAsync(
            Safir.Shared.Interfaces.IDatabaseService db, byte noS, List<SanadHeaderRequest> headers)
        {
            var reserved = new List<double>(headers.Count);
            if (headers.Count == 0) return reserved;

            const int batchSize = 5000;
            for (int start = 0; start < headers.Count; start += batchSize)
            {
                var length = Math.Min(batchSize, headers.Count - start);
                var chunkReserved = await db.ExecuteInTransactionAsync(async (conn, tx) =>
                {
                    var chunk = new List<double>(length);

                    await conn.ExecuteAsync(
                        "EXEC sp_getapplock @Resource = 'DeedNumberAllocation', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
                        transaction: tx, commandTimeout: 3600);

                    var maxNs = (await conn.QueryAsync<double?>("SELECT MAX(N_S) FROM dbo.DEED_HED WITH (UPDLOCK)", transaction: tx)).FirstOrDefault();
                    var maxBg = (await conn.QueryAsync<double?>("SELECT MAX(BAYEG) FROM dbo.DEED_HED WITH (UPDLOCK)", transaction: tx)).FirstOrDefault();

                    var nextNs = (maxNs ?? 0) + 1;
                    var nextBg = maxBg.HasValue ? maxBg.Value + 1 : 100000000;

                    var values = new List<string>(length);
                    for (int i = 0; i < length; i++)
                    {
                        var h = headers[start + i];
                        var ns = nextNs + i;
                        var bg = nextBg + i;
                        chunk.Add(ns);
                        values.Add($"({SqlNum(ns)},{h.DATE_S},N'{SqlText(h.SHARH_S)}',0,{noS},1,N'{SqlText(h.USER_NAME)}',GETDATE(),NULL,{SqlNum(bg)})");
                    }

                    const int insertChunk = 500;
                    for (int off = 0; off < values.Count; off += insertChunk)
                    {
                        var sql = "INSERT INTO dbo.DEED_HED (N_S,DATE_S,SHARH_S,GHATEI,NO_S,OKF,USER_NAME,CRT,uid,BAYEG) VALUES " +
                                   string.Join(",", values.Skip(off).Take(insertChunk));
                        await conn.ExecuteAsync(sql, transaction: tx, commandTimeout: 3600);
                    }

                    return chunk;
                }, IsolationLevel.Serializable);

                reserved.AddRange(chunkReserved);
            }

            return reserved;
        }
    }
}
