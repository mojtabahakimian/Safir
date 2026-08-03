using Safir.Server.Reports;
using Safir.Shared.Models.Salary;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Text;
using Xunit;

namespace Safir.Server.Tests;

public class LeaveStatementDocumentTests
{
    [Theory]
    [InlineData(0, 440, "0 روز، 0 ساعت و 0 دقیقه")]
    [InlineData(440, 440, "1 روز، 0 ساعت و 0 دقیقه")]
    [InlineData(945, 440, "2 روز، 1 ساعت و 5 دقیقه")]
    [InlineData(-505, 440, "منفی 1 روز، 1 ساعت و 5 دقیقه")]
    [InlineData(480, 0, "1 روز، 0 ساعت و 40 دقیقه")]
    public void FormatDuration_converts_minutes_to_plain_Persian_units(
        int minutes, int minutesPerDay, string expected)
    {
        Assert.Equal(expected, LeaveStatementDocument.FormatDuration(minutes, minutesPerDay));
    }

    [Fact]
    public void GeneratePdf_creates_a_real_document_with_carryover_and_reconciliation()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        EnsurePersianFont();
        var statement = new Pay2LeaveStatementDto
        {
            EmployeeName = "کارگر آزمایشی",
            EmployeeCode = "1001",
            Year = 1405,
            PrintDate = "1405/05/12 - 14:35:21",
            LeaveMinsPerDay = 440,
            LeaveCarryoverMax = 9,
            EntitlementMin = 11440,
            CarriedInMin = 3960,
            UsedMin = 1760,
            BalanceMin = 13640,
            PreviousYearBalanceMin = 6600,
            CarryoverLimitMin = 3960,
            EligibleCarryoverMin = 3960,
            ExpiredCarryoverMin = 2640,
            HistoryRequestedMin = 1760,
            History =
            {
                new Pay2LeaveStatementLineDto
                {
                    START_DATE = 14050116,
                    END_DATE = 14050120,
                    REQ_DAYS = 4,
                    DESCRIPTION = "یک روز تعطیل رسمی در بازه",
                    TotalDeductedMinutes = 1760
                }
            }
        };

        var pdf = new LeaveStatementDocument(statement).GeneratePdf();

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Difference_exposes_mismatch_between_requests_and_authoritative_balance()
    {
        var statement = new Pay2LeaveStatementDto { UsedMin = 1_320, HistoryRequestedMin = 880 };
        Assert.Equal(440, statement.HistoryToBalanceDifferenceMin);
    }

    private static void EnsurePersianFont()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var fontPath = Path.Combine(directory.FullName, "Server", "Fonts", "IRANYekanFN.TTF");
            if (File.Exists(fontPath))
            {
                FontManager.RegisterFont(File.OpenRead(fontPath));
                return;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("فونت فارسی مورد نیاز تست PDF یافت نشد.");
    }
}
