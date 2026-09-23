using System.Data;
using System.Text;
using Dbf;
using Safir.Server.Services;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// ساختار دیسکت بیمه باید با دیسکتی یکی بماند که نرم‌افزار دیگرِ همین کارگاه (یزدسپار، تیر ۱۴۰۵)
/// ساخته و تأمین اجتماعی پذیرفته است. خروجی قبلی سفیر همه‌ی ستون‌ها را C255 می‌نوشت، متن فارسی را
/// Windows-1256 (نه ایران‌سیستم)، سهم کارفرما را با نام DSK_TKARF (نه DSK_TKOSO) و دو ستون اضافه
/// DSW_INC/DSW_SPOUS داشت. اعداد پایین از خودِ فایل پذیرفته‌شده برداشته شده‌اند.
/// </summary>
public class Pay2DisketteFormatTests
{
    public Pay2DisketteFormatTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void RecordLengthsMatchAcceptedDiskette()
    {
        // طول رکورد = ۱ بایت پرچم حذف + جمع طول ستون‌ها
        Assert.Equal(564, 1 + Pay2DisketteService.KarLayout.Sum(c => c.Length));
        Assert.Equal(689, 1 + Pay2DisketteService.WorLayout.Sum(c => c.Length));
    }

    [Fact]
    public void EmployerShareAndUnemploymentUseLegacyFieldNames()
    {
        var names = Pay2DisketteService.KarLayout.Select(c => c.Name).ToList();
        Assert.Contains("DSK_TKOSO", names);
        Assert.Contains("DSK_BIC", names);
        Assert.DoesNotContain("DSK_TKARF", names);
        Assert.DoesNotContain(Pay2DisketteService.WorLayout, c => c.Name is "DSW_INC" or "DSW_SPOUS");
    }

    // رگرسیون: با MaxLength روی DataColumn، یک مقدار بلندتر از ستون کل دیسکت را با خطای انگلیسی
    // «Cannot set column ...» می‌خواباند؛ خروجی قبلی متن بلند را بی‌صدا می‌برید.
    [Fact]
    public void LongFreeTextIsTruncatedLikeBefore()
    {
        var kar = Pay2DisketteService.NewDbfTable(Pay2DisketteService.KarLayout);
        var values = KarRow();
        values[3] = new string('آ', 150);                       // DSK_ADRS (۱۰۰)

        Pay2DisketteService.AddDbfRow(kar, "کارگاه", values);

        Assert.Equal(100, ((string)kar.Rows[0]["DSK_ADRS"]).Length);
    }

    [Theory]
    [InlineData(0, "68421200241")]          // DSK_ID (۱۰): کد کارگاه
    [InlineData(15, "1234567890123")]       // DSK_TTOTL (۱۲): مبلغ
    public void OverlongIdentifierOrAmountFailsWithPersianMessage(int index, string value)
    {
        var kar = Pay2DisketteService.NewDbfTable(Pay2DisketteService.KarLayout);
        var values = KarRow();
        values[index] = value;

        var ex = Assert.Throws<InvalidOperationException>(() => Pay2DisketteService.AddDbfRow(kar, "کارگاه", values));
        Assert.Contains("فایل بیمه قابل تولید نیست", ex.Message);
        Assert.Contains(Pay2DisketteService.KarLayout[index].Name, ex.Message);
        Assert.Empty(kar.Rows);
    }

    private static string[] KarRow() =>
        Pay2DisketteService.KarLayout.Select(c => c.Name == "DSK_ID" ? "6842120024" : "0").ToArray();

    [Fact]
    public void PersianTextIsIranSystemAndNumbersStayAscii()
    {
        var table = new DataTable();
        table.Columns.Add(new DataColumn("DSW_SEX", typeof(string)) { MaxLength = 3 });
        table.Columns.Add(new DataColumn("DSW_NAT", typeof(string)) { MaxLength = 10 });
        table.Columns.Add(new DataColumn("DSW_MASH", typeof(string)) { MaxLength = 12 });
        table.Rows.Add("مرد", "ایرانی", "233189327");

        var path = Path.Combine(Path.GetTempPath(), $"dbf_{Guid.NewGuid():N}.dbf");
        try
        {
            DbfFile.Write(path, table, Encoding.GetEncoding(1256), overwrite: true, useIranSystemEncoding: true);
            var bytes = File.ReadAllBytes(path);
            int start = BitConverter.ToUInt16(bytes, 8) + 1;

            // همان بایت‌های دیسکت پذیرفته‌شده
            Assert.Equal(new byte[] { 0xa2, 0xa4, 0xf5 }, bytes[start..(start + 3)]);
            Assert.Equal(new byte[] { 0xfc, 0xf7, 0x91, 0xa4, 0xfe, 0x90 }, bytes[(start + 3)..(start + 9)]);
            Assert.Equal("233189327", Encoding.ASCII.GetString(bytes, start + 13, 12).TrimEnd('\0'));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
