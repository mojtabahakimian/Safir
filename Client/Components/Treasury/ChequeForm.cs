using System.Globalization;
using Safir.Shared.Models.Treasury;

namespace Safir.Client.Components.Treasury
{
    /// <summary>
    /// فرمِ مشخصاتِ چک (GETCHEK / PAYCHEK) — متن‌ها همان‌طور که کاربر تایپ می‌کند؛ ساختِ
    /// <see cref="TreasuryChequeInput"/> و پیام‌های خطا اینجا.
    /// </summary>
    public sealed class ChequeForm
    {
        public string? NSeri { get; set; }
        public int? Bank { get; set; }
        /// <summary>سررسید (۱۴۰۵/۰۷/۱۰).</summary>
        public string? DateS { get; set; }
        /// <summary>تاریخ دریافت/پرداخت؛ خالی = تاریخِ خزانه.</summary>
        public string? Date { get; set; }
        public string? Shobeh { get; set; }
        public string? ListNo { get; set; }
        public string? NameTah { get; set; }
        public string? NHesab { get; set; }
        public string? Sayadi { get; set; }
        public int? Sandugh { get; set; } = 1;
        public string? Hes1 { get; set; }
        /// <summary>«اضافه چکِ گروهی» (CREATE_CHEKDP / CREATE_CHEKPDP).</summary>
        public bool Group { get; set; }
        public int Count { get; set; } = 2;
        public int Gap { get; set; } = 1;

        public static ChequeForm From(TreasuryChequeDto c) => new()
        {
            NSeri = c.NSeri.ToString("0", CultureInfo.InvariantCulture),
            Bank = c.Bank,
            DateS = FormatDate(c.DateS),
            Date = FormatDate(c.Date),
            Shobeh = c.Shobeh?.Trim(),
            ListNo = c.ListNo?.ToString(CultureInfo.InvariantCulture),
            NameTah = c.NameTah?.Trim(),
            NHesab = c.NHesab?.Trim(),
            Sayadi = c.Sayadi is "0" ? null : c.Sayadi?.Trim(),
            Sandugh = c.Sandugh ?? 1,
            Hes1 = string.IsNullOrWhiteSpace(c.Hes1) || c.Hes1.Trim() == "911-1-1" ? null : c.Hes1.Trim()
        };

        public (TreasuryChequeInput? Input, string? Error) Build(bool receive, bool allowGroup)
        {
            var serial = Digits(NSeri);
            if (serial.Length == 0) return (null, "شماره سریال چک را وارد کنید.");
            if (Bank is not > 0) return (null, "بانک را انتخاب کنید.");
            var dateS = ParseDate(DateS);
            if (dateS is null) return (null, "تاریخ سررسید را کامل وارد کنید.");
            long? date = null;
            if (!string.IsNullOrWhiteSpace(Date))
            {
                date = ParseDate(Date);
                if (date is null) return (null, receive ? "تاریخ دریافت صحیح نیست." : "تاریخ پرداخت صحیح نیست.");
            }
            int? listNo = null;
            if (receive)
            {
                var ln = Digits(ListNo);
                if (ln.Length == 0 || !int.TryParse(ln, out var l)) return (null, "کد شعبه صحیح نیست.");
                listNo = l;
            }
            if (string.IsNullOrWhiteSpace(NameTah)) return (null, receive ? "نام پرداخت‌کننده‌ی چک را وارد کنید." : "نام گیرنده‌ی چک را وارد کنید.");
            var sayadi = Digits(Sayadi);
            if (sayadi.Length is > 0 and < 16 && sayadi != "0") return (null, "شماره صیادی باید ۱۶ رقم باشد.");

            return (new TreasuryChequeInput
            {
                NSeri = double.Parse(serial, CultureInfo.InvariantCulture),
                Bank = Bank.Value,
                DateS = dateS.Value,
                Date = date,
                Shobeh = Shobeh?.Trim(),
                ListNo = listNo,
                NameTah = NameTah.Trim(),
                NHesab = NHesab?.Trim(),
                Sayadi = sayadi.Length == 0 ? null : sayadi,
                Sandugh = receive ? Sandugh ?? 1 : null,
                Hes1 = string.IsNullOrWhiteSpace(Hes1) ? null : Hes1,
                Count = allowGroup && Group ? Math.Clamp(Count, 1, 60) : 1,
                GapMonths = Math.Clamp(Gap, 1, 12)
            }, null);
        }

        public static string Digits(string? s)
            => new((s ?? "").Select(c => c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) : c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c)
                            .Where(char.IsAsciiDigit).ToArray());

        public static long? ParseDate(string? s)
        {
            var d = Digits(s);
            return d.Length == 8 && long.TryParse(d, out var v) ? v : null;
        }

        public static string FormatDate(long d)
        {
            var s = d.ToString(CultureInfo.InvariantCulture);
            return s.Length == 8 ? $"{s[..4]}/{s.Substring(4, 2)}/{s.Substring(6, 2)}" : s;
        }
    }
}
