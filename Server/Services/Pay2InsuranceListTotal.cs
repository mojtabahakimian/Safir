using Safir.Shared.Interfaces;

namespace Safir.Server.Services;

/// <summary>
/// ستون «جمع دستمزد و مزایای مشمول و غیرمشمول» در لیست بیمه، دیسکت (DSW_TOTL/DSK_TTOTL) و پیش‌نمایش.
///
/// پیش‌فرض: کل دستمزد و مزایای اسمی. با کلید INS_TOTL_SUBJECT_PLUS_CHILD = 1، اقلامی که غیرمشمول
/// تعریف شده‌اند به‌جز حق اولاد از این ستون کنار می‌روند، مثل نرم‌افزار قبلی (Access)؛ درخواست
/// یزدسپار که اختلاف دو ستون فقط حق اولاد باشد. مازاد سقف بیمه در هر دو حالت در ستون می‌ماند.
/// متن رسمی درباره‌ی ترکیب این ستون ساکت است؛ مسئولیت روشن کردنش با کارفرماست.
/// </summary>
public static class Pay2InsuranceListTotal
{
    public const string ConfigKey = "INS_TOTL_SUBJECT_PLUS_CHILD";

    public static async Task<bool> SubjectPlusChildAsync(IDatabaseService db) =>
        (await db.DoGetDataSQLAsyncSingle<string?>(
            "SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY = @key", new { key = ConfigKey }))?.Trim() == "1";

    /// <summary>عدد ستون «مشمول و غیرمشمول» برای یک ردیف از Pay2PayrollSnapshotQuery.</summary>
    public static long Total(long nominalGross, long subjectPlusChildGross, bool subjectPlusChild) =>
        subjectPlusChild ? subjectPlusChildGross : nominalGross;
}
