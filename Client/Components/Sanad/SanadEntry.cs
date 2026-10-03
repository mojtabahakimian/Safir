using System.Globalization;
using Safir.Client.Components.Treasury;
using Safir.Shared.Models.Sanad;
using Safir.Shared.Models.Treasury;

namespace Safir.Client.Components.Sanad
{
    /// <summary>
    /// ردیفِ در حالِ ورود/اصلاح — متن‌ها همان‌طور که کاربر تایپ می‌کند؛ ساختِ
    /// <see cref="SanadRowSaveRequest"/> و پیامِ خطا اینجا.
    /// </summary>
    public sealed class SanadEntry
    {
        public long? RowId { get; set; }
        public string? Hes { get; set; }
        public string? HesName { get; set; }
        public string? Sharh { get; set; }
        public string? Bed { get; set; }
        public string? Bes { get; set; }
        public int? MhazNo { get; set; }

        /// <summary>«ثبتِ مشخصاتِ چک» — مثلِ باز شدنِ SGETCHEK/SPAYCHEK؛ خاموش = ردیفِ بی‌چک.</summary>
        public bool WithCheque { get; set; } = true;
        public ChequeForm Chq { get; set; } = new();
        /// <summary>صاحبِ چکِ دریافتی (CUST_NO).</summary>
        public string? Owner { get; set; }
        public string? OwnerName { get; set; }
        /// <summary>چکِ فعلیِ ردیفِ در حالِ اصلاح.</summary>
        public TreasuryChequeDto? Current { get; set; }

        public double BedValue => ParseMoney(Bed) ?? 0;
        public double BesValue => ParseMoney(Bes) ?? 0;
        public bool IsEmpty => string.IsNullOrWhiteSpace(Hes) && string.IsNullOrWhiteSpace(Sharh) && BedValue == 0 && BesValue == 0;

        public SanadChequeRole Role(SanadMetaDto m) => SanadRules.RoleOf(Hes, BedValue, BesValue, m.Ada, m.Adv, m.Apa, m.Apv);

        public static SanadEntry From(SanadRowDto r) => new()
        {
            RowId = r.Id,
            Hes = r.Hes,
            HesName = r.HesName,
            Sharh = r.Sharh,
            Bed = r.Bed > 0 ? r.Bed.ToString("0", CultureInfo.InvariantCulture) : null,
            Bes = r.Bes > 0 ? r.Bes.ToString("0", CultureInfo.InvariantCulture) : null,
            MhazNo = r.MhazNo,
            WithCheque = r.Cheque is not null,
            Chq = r.Cheque is { } c ? ChequeForm.From(c) : new ChequeForm(),
            Owner = r.Cheque?.CustNo?.Trim(),
            OwnerName = r.ChequeOwnerName,
            Current = r.Cheque
        };

        public (SanadRowSaveRequest? Request, string? Error) Build(SanadMetaDto m)
        {
            if (string.IsNullOrWhiteSpace(Hes)) return (null, "حساب را انتخاب کنید.");
            var bed = BedValue;
            var bes = BesValue;
            if (SanadRules.AmountError(bed, bes) is { } ae) return (null, ae);
            if ((Sharh?.Trim().Length ?? 0) > 250) return (null, "طول شرح حداکثر می‌تواند ۲۵۰ نویسه باشد.");

            var req = new SanadRowSaveRequest { Hes = Hes.Trim(), Sharh = Sharh?.Trim(), Bed = bed, Bes = bes, MhazNo = MhazNo };
            var role = Role(m);
            if (role != SanadChequeRole.None && WithCheque)
            {
                var (input, err) = Chq.Build(role == SanadChequeRole.Received, allowGroup: false);
                if (err is not null) return (null, err);
                req.Cheque = input;
                if (role == SanadChequeRole.Received) req.ChequeOwner = string.IsNullOrWhiteSpace(Owner) ? null : Owner.Trim();
            }
            return (req, null);
        }

        public static double? ParseMoney(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var digits = ChequeForm.Digits(s);
            return double.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
    }
}
