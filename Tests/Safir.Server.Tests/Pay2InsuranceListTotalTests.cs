using Safir.Server.Services;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// گزینه‌ی INS_TOTL_SUBJECT_PLUS_CHILD: خاموش (پیش‌فرض) یعنی کل دستمزد و مزایا، مثل قبل؛
/// روشن یعنی جمعی که اقلام غیرمشمول به‌جز حق اولاد را ندارد (درخواست یزدسپار).
/// </summary>
public class Pay2InsuranceListTotalTests
{
    [Fact]
    public void Off_KeepsFullGross() =>
        Assert.Equal(21_428_957_580, Pay2InsuranceListTotal.Total(21_428_957_580, 19_449_649_616, subjectPlusChild: false));

    [Fact]
    public void On_UsesSubjectPlusChild() =>
        Assert.Equal(19_449_649_616, Pay2InsuranceListTotal.Total(21_428_957_580, 19_449_649_616, subjectPlusChild: true));
}
