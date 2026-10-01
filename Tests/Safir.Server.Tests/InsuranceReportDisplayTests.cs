using Safir.Shared.Models.Salary.Reports;
using Xunit;

namespace Safir.Server.Tests;

public class InsuranceReportDisplayTests
{
    // Recorded Shahrivar amounts: the previously invisible deductions must
    // reconcile every displayed row, including cases with tax/loans/advances.
    [Theory]
    [InlineData(238189327L, 16673252L, 0L, 41089117L, 24415865L, 197100210L)]
    [InlineData(223797350L, 15665814L, 0L, 18032252L, 2366438L, 205765098L)]
    [InlineData(223797350L, 15665814L, 0L, 17873481L, 2207667L, 205923869L)]
    [InlineData(100000000L, 7000000L, 2000000L, 15000000L, 6000000L, 85000000L)]
    public void Visible_deductions_reconcile_report_balance(
        long gross, long worker, long tax, long deductions, long other, long net)
    {
        var row = new InsuranceEmployeeRowDto
        {
            TotalGrossPay = gross, WorkerPremium = worker, TaxAmount = tax,
            TotalDeductions = deductions, NetPayable = net,
            EmployerPremium = 20000000, UnemploymentPremium = 3000000
        };
        var report = new InsuranceReportDto { Rows = { row } };
        Assert.Equal(other, row.OtherDeductions);
        Assert.Equal(other, report.TotalOtherDeductions);
        Assert.Equal(report.TotalNetPayable, report.TotalGrossPay - report.TotalWorkerPremium
            - report.TotalTaxAmount - report.TotalOtherDeductions);
    }

    [Theory]
    [InlineData(false, "جمع دستمزد و مزایای مشمول و غیرمشمول بیمه")]
    [InlineData(true, "جمع دستمزد و مزایای مشمول و حق اولاد")]
    public void Gross_title_matches_selected_report_basis(bool subjectOnly, string title)
    {
        Assert.Equal(title, new InsuranceReportDto { SubjectPlusChildOnly = subjectOnly }.GrossPayTitle);
    }
}
