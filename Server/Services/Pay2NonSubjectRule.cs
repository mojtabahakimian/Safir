using Dapper;
using System.Data;

namespace Safir.Server.Services;

/// <summary>
/// قاعده‌ی اختصاصی مشتری برای «حق شیفت و اضافه‌کار»: از یک تاریخ اثر به بعد،
/// این اقلام از مبنای بیمه و/یا مبنای مالیات کنار گذاشته می‌شوند.
///
/// دو کلید مستقل در PAY2_CONFIG آن را کنترل می‌کنند (صفر = خاموش):
///   INS_NON_SUBJECT_EFFECTIVE_FROM — کنار گذاشتن از مبنای بیمه
///   TAX_NON_SUBJECT_EFFECTIVE_FROM — کنار گذاشتن از مبنای مالیات
///
/// عمداً دو کلیدند و نه یکی: دیتابیسی که فقط قاعده‌ی بیمه را روشن کرده نباید
/// رفتار مالیاتی‌اش عوض شود. ولی توجه: روشن کردنِ تنهای کلید بیمه، مالیات را
/// **بیشتر** می‌کند نه کمتر، چون بیمه‌ی سهم کارگرِ کسرشدنی از مبنای مالیات
/// کوچک‌تر می‌شود در حالی که خود اقلام هنوز در آن مبنا هستند.
///
/// اعمال واقعی قاعده در موتور محاسبه (SP_PAY2_CALC_RUN در مخزن ScriptSqly)
/// انجام می‌شود و بعد از حل شدن Overrideهای حکم/پرسنل اجرا می‌شود؛ یعنی
/// INS_OV/TAX_OV روی حکمِ قفل‌شده را هم خنثی می‌کند. نقش این کلاس فقط این است
/// که جلوی ثبتِ Overrideهایی را بگیرد که موتور بی‌صدا نادیده‌شان می‌گیرد.
/// </summary>
public readonly record struct Pay2NonSubjectRule(long InsuranceEffectiveFrom, long TaxEffectiveFrom)
{
    public const string InsuranceDecreeError =
        "این آیتم از تاریخ اثر قاعده اختصاصی مشتری الزاماً غیرمشمول بیمه است و Override مشمول برای آن مجاز نیست.";
    public const string TaxDecreeError =
        "این آیتم از تاریخ اثر قاعده اختصاصی مشتری الزاماً غیرمشمول مالیات است و Override مشمول برای آن مجاز نیست.";
    public const string InsuranceOverrideError =
        "این آیتم از تاریخ اثر قاعده اختصاصی مشتری الزاماً غیرمشمول بیمه است و ثبت Override مشمول مجاز نیست.";
    public const string TaxOverrideError =
        "این آیتم از تاریخ اثر قاعده اختصاصی مشتری الزاماً غیرمشمول مالیات است و ثبت Override مشمول مجاز نیست.";
    public const string InsuranceTemplateError =
        "این آیتم طبق قاعده اختصاصی مشتری الزاماً غیرمشمول بیمه است و Override مشمول در قالب مجاز نیست.";
    public const string TaxTemplateError =
        "این آیتم طبق قاعده اختصاصی مشتری الزاماً غیرمشمول مالیات است و Override مشمول در قالب مجاز نیست.";

    private const string LoadSql = @"
SELECT
    ISNULL(MAX(CASE WHEN CFG_KEY='INS_NON_SUBJECT_EFFECTIVE_FROM' THEN TRY_CAST(CFG_VALUE AS BIGINT) END),0) AS InsuranceEffectiveFrom,
    ISNULL(MAX(CASE WHEN CFG_KEY='TAX_NON_SUBJECT_EFFECTIVE_FROM' THEN TRY_CAST(CFG_VALUE AS BIGINT) END),0) AS TaxEffectiveFrom
FROM PAY2_CONFIG
WHERE CFG_KEY IN ('INS_NON_SUBJECT_EFFECTIVE_FROM','TAX_NON_SUBJECT_EFFECTIVE_FROM')";

    /// <summary>چهار قلمی که قاعده شاملشان می‌شود — همان لیستی که موتور محاسبه به‌کار می‌برد.</summary>
    public static bool IsRuleItem(string? itemCode) =>
        itemCode is "SHIFT" or "OT_NORMAL" or "OT_HOLIDAY" or "OT_ADMIN";

    public static async Task<Pay2NonSubjectRule> LoadAsync(IDbConnection conn, IDbTransaction? tran = null) =>
        await conn.QuerySingleAsync<Pay2NonSubjectRule>(LoadSql, transaction: tran);

    /// <summary>آیا ثبت «مشمول بیمه» برای بازه‌ای که در <paramref name="validTo"/> تمام می‌شود ممنوع است؟</summary>
    public bool BlocksInsurance(long? validTo) => Blocks(InsuranceEffectiveFrom, validTo);

    /// <summary>آیا ثبت «مشمول مالیات» برای بازه‌ای که در <paramref name="validTo"/> تمام می‌شود ممنوع است؟</summary>
    public bool BlocksTax(long? validTo) => Blocks(TaxEffectiveFrom, validTo);

    // بازه‌ای که کاملاً قبل از تاریخ اثر تمام شده آزاد است؛ بازه‌ی باز (NULL) یا
    // بازه‌ای که به ماه اثر یا بعد از آن می‌رسد مسدود می‌شود. مقایسه در سطح ماه
    // انجام می‌شود چون خود موتور هم PERIOD_DATE را در سطح ماه می‌سنجد.
    private static bool Blocks(long effectiveFrom, long? validTo) =>
        effectiveFrom > 0 && (!validTo.HasValue || validTo.Value / 100 >= effectiveFrom / 100);
}
