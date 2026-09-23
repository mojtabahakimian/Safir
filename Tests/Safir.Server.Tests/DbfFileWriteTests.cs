using System.Text;
using Dbf;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// رگرسیون‌تست دیسکت بیمه (DSKKAR00/DSKWOR00): از اولین نسخه‌ی خروجی DBF، همه‌ی
/// ستون‌های فایل نام اولین فیلد را می‌گرفتند (در DSKKAR همه «DSK_ID» و در DSKWOR همه
/// «DSW_ID») چون شمارنده‌ی ستون در DbfFile.Write هرگز جلو نمی‌رفت. داده‌ی هر ستون
/// درست بود، ولی فایلی با ۲۵ ستونِ هم‌نام برای سامانه‌ی تأمین اجتماعی قابل خواندن نیست.
/// </summary>
public class DbfFileWriteTests
{
    // سازنده‌ی ایستای DbfFile به code page 1256 نیاز دارد؛ Pay2DisketteService هم همین را قبل از نوشتن ثبت می‌کند.
    public DbfFileWriteTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void EachColumnKeepsItsOwnName()
    {
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["DSW_ID"] = "2185000000", ["DSW_DD"] = 31, ["DSW_MASH"] = 348040648L },
        };

        var header = WriteAndReadFieldNames(rows);

        Assert.Equal(new[] { "DSW_ID", "DSW_DD", "DSW_MASH" }, header);
    }

    [Fact]
    public void EachValueLandsInItsOwnColumn()
    {
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["A"] = "x", ["B"] = 31, ["C"] = 348040648L },
        };

        var path = Path.Combine(Path.GetTempPath(), $"dbf_{Guid.NewGuid():N}.dbf");
        try
        {
            DbfFile.Write(path, rows, Encoding.ASCII, overwirte: true);
            var bytes = File.ReadAllBytes(path);
            int headerLength = BitConverter.ToUInt16(bytes, 8);
            int fieldLength = bytes[32 + 16];            // همه‌ی ستون‌ها هم‌طول‌اند (C255)
            string Field(int i) => Encoding.ASCII.GetString(bytes, headerLength + 1 + i * fieldLength, fieldLength).TrimEnd('\0', ' ');

            Assert.Equal("x", Field(0));
            Assert.Equal("31", Field(1));
            Assert.Equal("348040648", Field(2));
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, "cpg"));
        }
    }

    private static List<string> WriteAndReadFieldNames(List<Dictionary<string, object>> rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dbf_{Guid.NewGuid():N}.dbf");
        try
        {
            DbfFile.Write(path, rows, Encoding.ASCII, overwirte: true);
            var bytes = File.ReadAllBytes(path);
            var names = new List<string>();
            for (int pos = 32; bytes[pos] != 0x0D; pos += 32)
                names.Add(Encoding.ASCII.GetString(bytes, pos, 11).TrimEnd('\0'));
            return names;
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, "cpg"));
        }
    }
}
