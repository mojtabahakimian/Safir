#!/usr/bin/env python3
"""
راستی‌آزمایی حسابی موتور حقوق و دستمزد PAY2 روی داده‌های واقعی تولید.
هیچ دیتابیسی لازم نیست — فقط بررسی این‌که اعداد ذخیره‌شده با هم می‌خوانند یا نه.
"""
import argparse
import json
import sys
from collections import defaultdict
from decimal import Decimal

parser = argparse.ArgumentParser(
    description="راستی‌آزمایی حسابی موتور حقوق و دستمزد PAY2 روی خروجی JSON دیتابیس"
)
parser.add_argument("export", nargs="?", default="pay2_export.json",
                    help="فایل JSON خروجی گرفته‌شده از دیتابیس (پیش‌فرض: pay2_export.json)")
parser.add_argument("--strict", action="store_true",
                    help="هشدارهای داده و گردش کار هم باعث خروج با کد خطا شوند")
args = parser.parse_args()

try:
    raw = open(args.export, encoding="utf-8-sig").read()
except OSError as ex:
    sys.exit(f"✗ فایل خوانده نشد: {ex}")

# خروجی SSMS معمولاً سطرها را می‌شکند؛ شکست سطر داخل JSON بی‌معناست پس حذفش می‌کنیم
raw = raw[raw.index("{"):raw.rindex("}") + 1].replace("\n", "").replace("\r", "")
try:
    D = json.loads(raw)
except json.JSONDecodeError as ex:
    sys.exit(f"✗ JSON نامعتبر است: {ex}")

REQUIRED = ["PAY2_CONFIG", "PAY2_RUN_LINE", "PAY2_RUN", "PAY2_EMPLOYEE"]
missing = [t for t in REQUIRED if t not in D]
if missing:
    sys.exit("✗ جدول‌های لازم در خروجی نیستند: " + "، ".join(missing))
D = {k: D.get(k, []) for k in
     ["PAY2_CONFIG", "PAY2_TAX_BRACKET", "PAY2_PERIOD", "PAY2_RUN", "PAY2_RUN_LINE",
      "PAY2_RUN_DETAIL", "PAY2_EMPLOYEE", "PAY2_ITEM_DEF", "PAY2_ATTENDANCE",
      "PAY2_DECREE", "PAY2_DECREE_LINE"]}

cfg = {c["CFG_KEY"]: c["CFG_VALUE"] for c in D["PAY2_CONFIG"]}
INS_W = Decimal(cfg["INS_WORKER_RATE"]) / 100
INS_E = Decimal(cfg["INS_EMPLOYER_RATE"]) / 100
INS_U = Decimal(cfg["INS_UNEMP_RATE"]) / 100
CEIL = int(cfg["INS_CEILING_MONTHLY"])
CEIL_ON = cfg["INS_CEILING_APPLY"] == "1"
ROUND = int(cfg["ROUND_MODE"])
TAX_EXEMPT = int(cfg["TAX_EXEMPT_MONTHLY"])
TAX_YEAR = int(cfg["TAX_YEAR"])
TAX_DED_INS = cfg["TAX_DEDUCT_INS"] == "1"

emp = {e["EMP_ID"]: e for e in D["PAY2_EMPLOYEE"]}
run = {r["RUN_ID"]: r for r in D["PAY2_RUN"]}
per = {p["PER_ID"]: p for p in D["PAY2_PERIOD"]}
itemdef = {i["ITEM_ID"]: i for i in D["PAY2_ITEM_DEF"]}

detail = defaultdict(list)
for d in D["PAY2_RUN_DETAIL"]:
    detail[(d["RUN_ID"], d["EMP_ID"])].append(d)

brackets = sorted(
    (b for b in D["PAY2_TAX_BRACKET"] if b["TAX_YEAR"] == TAX_YEAR),
    key=lambda b: b["SORT_ORDER"],
)


def g(row, key, default=0):
    v = row.get(key, default)
    return Decimal(str(v if v is not None else default))


def expected_tax(annual_base):
    """مالیات ماهانه از روی مبنای سالانه، مطابق جدول PAY2_TAX_BRACKET."""
    if annual_base <= 0:
        return Decimal(0)
    prev = Decimal(0)
    for b in brackets:
        upper = Decimal(str(b["UPPER_LIMIT"]))
        if annual_base <= upper:
            return Decimal(str(b["FIXED_TAX"])) + (annual_base - prev) * Decimal(
                str(b["RATE_PCT"])
            ) / 100
        prev = upper
    b = brackets[-1]
    return Decimal(str(b["FIXED_TAX"])) + (annual_base - prev) * Decimal(
        str(b["RATE_PCT"])
    ) / 100


findings = defaultdict(list)


def flag(kind, msg):
    findings[kind].append(msg)


lines = sorted(D["PAY2_RUN_LINE"], key=lambda r: (r["RUN_ID"], r["EMP_ID"]))
print(f"بررسی {len(lines)} سطر فیش حقوقی از {len({l['RUN_ID'] for l in lines})} اجرای محاسبه\n")

for L in lines:
    rid, eid = L["RUN_ID"], L["EMP_ID"]
    who = f"RUN {rid} / EMP {eid} ({emp.get(eid,{}).get('LAST_NAME','?')})"

    gross = g(L, "GROSS_PAY")
    net = g(L, "NET_PAY")
    total_ded = g(L, "TOTAL_DED")
    ins_base = g(L, "INS_BASE")
    ins_worker = g(L, "INS_WORKER")
    ins_emp = g(L, "INS_EMPLOYER")
    ins_emp_base = g(L, "INS_EMPLOYER_BASE")
    ins_unemp = g(L, "INS_UNEMPLOYMENT")
    tax_base = g(L, "TAX_BASE")
    tax_amt = g(L, "TAX_AMOUNT")
    loan = g(L, "LOAN_DED")
    adv = g(L, "ADVANCE_DED")
    other = g(L, "OTHER_DED")
    radj = g(L, "ROUNDING_ADJ")
    wdays = g(L, "WORK_DAYS")

    # ۱) NET = GROSS − TOTAL_DED (موتور v3 مانده گردکردن را در ناخالص می‌ریزد)
    resid = net - (gross - total_ded)
    if "ROUNDING_ADJ" in L:
        if resid != 0:
            flag("NET", f"{who}: NET {net} ≠ GROSS {gross} − DED {total_ded} (اختلاف {resid})")
    else:
        if abs(resid) >= ROUND / 2:
            flag("NET", f"{who}: NET {net} از گردکردن {ROUND} تایی خارج است (باقی‌مانده {resid})")
        elif resid != 0:
            flag("LEGACY", f"{who}: باقی‌مانده گردکردن {resid} ریال در ناخالص ثبت نشده "
                           f"(موتور قدیمی، بدون ROUNDING_ADJ)")

    # ۲) TOTAL_DED = مجموع اجزا
    parts = ins_worker + tax_amt + loan + adv + other
    if total_ded != parts:
        flag("DED", f"{who}: TOTAL_DED {total_ded} ≠ مجموع اجزا {parts} "
                    f"(اختلاف {total_ded-parts})")

    # ۳) بیمه سهم کارگر = INS_BASE × نرخ
    exp_w = (ins_base * INS_W).quantize(Decimal(1))
    if abs(ins_worker - exp_w) > 1:
        flag("INSW", f"{who}: INS_WORKER {ins_worker} ≠ {INS_W*100}% از {ins_base} "
                     f"(انتظار {exp_w})")

    # ۴) سهم کارفرما = بیمه + بیکاری، و هرکدام با نرخ خودش
    if ins_emp_base or ins_unemp:
        if ins_emp != ins_emp_base + ins_unemp:
            flag("INSE", f"{who}: INS_EMPLOYER {ins_emp} ≠ {ins_emp_base} + {ins_unemp}")
        exp_e = (ins_base * INS_E).quantize(Decimal(1))
        exp_u = (ins_base * INS_U).quantize(Decimal(1))
        if abs(ins_emp_base - exp_e) > 1:
            flag("INSE", f"{who}: INS_EMPLOYER_BASE {ins_emp_base} ≠ {INS_E*100}% (انتظار {exp_e})")
        if abs(ins_unemp - exp_u) > 1:
            flag("INSE", f"{who}: INS_UNEMPLOYMENT {ins_unemp} ≠ {INS_U*100}% (انتظار {exp_u})")

    # ۵) گرد کردن خالص پرداختی
    if net % ROUND != 0:
        flag("ROUND", f"{who}: NET_PAY {net} مضرب {ROUND} نیست")

    # ۶) سقف بیمه
    if CEIL_ON and wdays > 0:
        cap = Decimal(CEIL) * wdays / 30
        if ins_base > cap + 1:
            flag("CEIL", f"{who}: INS_BASE {ins_base} از سقف {cap.quantize(Decimal(1))} "
                         f"برای {wdays} روز بیشتر است")

    # ۷) GROSS در برابر جمع آیتم‌ها
    ds = detail.get((rid, eid))
    if ds and any("ITEM_TYPE_SNAP" in d for d in ds):
        codes = {d.get("ITEM_CODE_SNAP") for d in ds}
        both = {"BASE_SAL", "BASE_SAL_B"} <= codes
        # موتور زنده: ناخالص پرداختی روی ریل «رسمی» ⇒ BASE_SAL حذف می‌شود
        earn = sum(g(d, "AMOUNT") for d in ds
                   if d.get("ITEM_TYPE_SNAP") in (1, 2)
                   and not (both and d.get("ITEM_CODE_SNAP") == "BASE_SAL"))
        if earn and abs((earn + radj) - gross) > 1:
            flag("GROSS", f"{who}: GROSS {gross} ≠ جمع آیتم‌ها {earn} + گردکردن {radj} "
                          f"(اختلاف {gross-earn-radj})")
        # ناخالص اسمی روی ریل «اسمی» ⇒ BASE_SAL_B حذف می‌شود
        nom_gross = g(L, "NOMINAL_GROSS")
        if nom_gross:
            nom = sum(g(d, "NOMINAL_AMOUNT") for d in ds
                      if d.get("ITEM_TYPE_SNAP") in (1, 2)
                      and not (both and d.get("ITEM_CODE_SNAP") == "BASE_SAL_B"))
            if nom and abs(nom - nom_gross) > 1:
                flag("GROSS", f"{who}: NOMINAL_GROSS {nom_gross} ≠ جمع مبالغ اسمی {nom}")
        # اختلاف معنادار دو ریل — چه چیزی واقعاً پرداخت می‌شود
        if both:
            bs  = next(g(d,"AMOUNT") for d in ds if d.get("ITEM_CODE_SNAP")=="BASE_SAL")
            bsb = next(g(d,"AMOUNT") for d in ds if d.get("ITEM_CODE_SNAP")=="BASE_SAL_B")
            if bs != bsb:
                flag("RAIL", f"{who}: حقوق اسمی {bs} ≠ حقوق رسمی {bsb} — "
                             f"پرداختی روی «رسمی» محاسبه شده (اختلاف {bs-bsb} ریال)")

    # ۷ب) مبنای بیمه از روی مبالغ مشمول
    if ds and any("INS_SUBJECT_AMOUNT" in d for d in ds):
        isum = sum(g(d, "INS_SUBJECT_AMOUNT") for d in ds
                   if d.get("ITEM_TYPE_SNAP") in (1, 2))
        cap = Decimal(CEIL) * wdays / 30 if (CEIL_ON and wdays > 0) else None
        exp_ib = min(isum, cap) if cap is not None else isum
        if isum and abs(ins_base - exp_ib) > 1:
            flag("INSBASE", f"{who}: INS_BASE {ins_base} ≠ جمع مشمول بیمه {isum}"
                            + (f" (با سقف {cap.quantize(Decimal(1))})" if cap is not None else ""))

    # ۸) مبنای مالیات
    if ds and any("TAX_SUBJECT_AMOUNT" in d for d in ds):
        taxable = sum(g(d, "TAX_SUBJECT_AMOUNT") for d in ds)
        base = taxable - (ins_worker if TAX_DED_INS else 0) - TAX_EXEMPT
        base = max(base, Decimal(0))
        if base > 0:
            exp_t = expected_tax(base * 12) / 12
            if abs(tax_amt - exp_t) > 1000:
                flag("TAX", f"{who}: TAX_AMOUNT {tax_amt} ≠ انتظار {exp_t.quantize(Decimal(1))} "
                            f"(مبنای ماهانه {base.quantize(Decimal(1))})")
        elif tax_amt > 0:
            flag("TAX", f"{who}: مالیات {tax_amt} گرفته شده ولی مبنا صفر است")

    # ۹) هشدارهای معنایی
    if gross > 0 and wdays == 0:
        flag("SEMANTIC", f"{who}: WORK_DAYS=0 ولی GROSS_PAY={gross} و NET_PAY={net}")
    if net < 0:
        flag("SEMANTIC", f"{who}: خالص پرداختی منفی: {net}")
    if 0 < gross < 1_000_000:
        flag("SEMANTIC", f"{who}: ناخالص بسیار کوچک ({gross} ریال) — احتمالاً فقط اثر گردکردن")

# ── بررسی‌های سطح‌بالاتر ───────────────────────────────────────────────────
# آیتم‌هایی که در تعریف مشمول‌اند ولی در فیش مبلغ مشمول صفر خورده‌اند
zero_subj = defaultdict(set)
for d in D["PAY2_RUN_DETAIL"]:
    code = d.get("ITEM_CODE_SNAP")
    if not code or "TAX_SUBJECT_AMOUNT" not in d:
        continue
    idf = itemdef.get(d["ITEM_ID"])
    if idf and idf["TAX_SUBJECT"] and g(d, "AMOUNT") > 0 and g(d, "TAX_SUBJECT_AMOUNT") == 0:
        zero_subj[code].add(d["RUN_ID"])
for code, runs in sorted(zero_subj.items()):
    flag("SUBJECT", f"آیتم «{code}» در تعریف مشمول مالیات است ولی در "
                    f"{len(runs)} اجرا مبلغ مشمولش صفر ثبت شده (override حکم)")

# ناهماهنگی روزهای کارکرد — ریشه‌ی فیش‌های صفر یا بدون بیمه
att = {(a["PER_ID"], a["EMP_ID"]): a for a in D["PAY2_ATTENDANCE"]}
runper = {r["RUN_ID"]: r["PER_ID"] for r in D["PAY2_RUN"]}
latest = {r["RUN_ID"] for r in D["PAY2_RUN"] if r.get("IS_LATEST")}

for a in D["PAY2_ATTENDANCE"]:
    days, daysb, wd = g(a, "DAYS"), g(a, "DAYSB"), g(a, "WORK_DAYS")
    name = emp.get(a["EMP_ID"], {}).get("LAST_NAME", "?")
    tag = f"دوره {per.get(a['PER_ID'],{}).get('PERIOD_DATE', a['PER_ID'])} / EMP {a['EMP_ID']} ({name})"
    if days > 0 and daysb == 0:
        flag("DAYS", f"{tag}: روز رسمی {days} ولی روز اسمی صفر — "
                     f"پرداختی صفر می‌شود در حالی که بیمه کامل کسر می‌شود")
    elif daysb > 0 and days == 0:
        flag("DAYS", f"{tag}: روز اسمی {daysb} ولی روز رسمی صفر — "
                     f"حقوق پرداخت می‌شود ولی بیمه و مالیات صفر می‌ماند")
    elif wd > 0 and days == 0 and daysb == 0:
        flag("DAYS", f"{tag}: WORK_DAYS={wd} ولی هر دو ستون DAYS و DAYSB صفرند")

# فیش‌های نهایی‌شده‌ای که خالص منفی دارند و به سند حسابداری رفته‌اند
for L in D["PAY2_RUN_LINE"]:
    r = run.get(L["RUN_ID"], {})
    if g(L, "NET_PAY") < 0 and r.get("STATUS") == 3:
        a = att.get((runper.get(L["RUN_ID"]), L["EMP_ID"]), {})
        extra = ""
        if a and g(a, "DAYS") > 0 and g(a, "DAYSB") == 0:
            extra = " — علتش صفر بودن ستون روز اسمی در کارکرد است، نه ترک کار"
        flag("POSTED", f"RUN {L['RUN_ID']} / EMP {L['EMP_ID']} "
                       f"({emp.get(L['EMP_ID'],{}).get('LAST_NAME','?')}): "
                       f"فیش نهایی‌شده با خالص منفی {g(L,'NET_PAY')} به سند رفته است{extra}")

# داده‌های مشکوک در پرسنل
for e in D["PAY2_EMPLOYEE"]:
    nc = e.get("NATIONAL_CODE")
    if not nc:
        flag("DATA", f"EMP {e['EMP_ID']} ({e['LAST_NAME']}): کد ملی ندارد")
    elif len(set(nc)) == 1:
        flag("DATA", f"EMP {e['EMP_ID']} ({e['LAST_NAME']}): کد ملی ساختگی «{nc}»")

# چرخه‌های محاسبه/برگشت
rev = defaultdict(int)
for r in D["PAY2_RUN"]:
    n = (r.get("NOTES") or "")
    rev[r["PER_ID"]] += n.count("Reverted")
for pid, c in sorted(rev.items()):
    runs = [r for r in D["PAY2_RUN"] if r["PER_ID"] == pid]
    if c > 5:
        pd = per.get(pid, {}).get("PERIOD_DATE")
        flag("WORKFLOW", f"دوره {pd} (PER_ID {pid}): {len(runs)} اجرا، {c} بار برگشت")

# ── گزارش ────────────────────────────────────────────────────────────────
TITLES = {
    "NET": "معادله خالص = ناخالص − کسورات",
    "DED": "تفکیک کسورات",
    "INSW": "بیمه سهم کارگر",
    "INSE": "بیمه سهم کارفرما",
    "ROUND": "گرد کردن خالص",
    "CEIL": "سقف دستمزد مشمول بیمه",
    "GROSS": "ناخالص در برابر جمع آیتم‌ها",
    "INSBASE": "بازسازی مبنای بیمه",
    "LEGACY": "موتور قدیمی (قبل از v3)",
    "RAIL": "دو ریل حقوق اسمی/رسمی",
    "TAX": "محاسبه مالیات",
    "SUBJECT": "مشمولیت آیتم‌ها",
    "SEMANTIC": "هشدار معنایی",
    "DATA": "کیفیت داده",
    "DAYS": "هماهنگی روزهای کارکرد",
    "POSTED": "فیش منفیِ نهایی‌شده",
    "WORKFLOW": "گردش کار",
}
ORDER = ["NET", "DED", "INSW", "INSE", "ROUND", "CEIL", "GROSS", "INSBASE", "TAX",
         "DAYS", "POSTED", "LEGACY", "RAIL", "SUBJECT", "SEMANTIC", "DATA", "WORKFLOW"]

HARD = ORDER[:9]
hard = [k for k in HARD if findings[k]]
for k in ORDER:
    msgs = findings[k]
    mark = "✓" if not msgs else ("✗" if k in HARD else "⚠")
    print(f"{mark} {TITLES[k]}: {len(msgs)} مورد")
    for m in msgs[:12]:
        print(f"      • {m}")
    if len(msgs) > 12:
        print(f"      … و {len(msgs)-12} مورد دیگر")

print()
soft = [k for k in ORDER if k not in HARD and findings[k]]
if hard:
    print(f"نتیجه: {len(hard)} دسته خطای حسابی پیدا شد.")
    sys.exit(1)
print("نتیجه: تمام معادله‌های حسابی موتور محاسبه درست‌اند. "
      "موارد ⚠ مربوط به داده و گردش کار است، نه ریاضیات.")
if soft and args.strict:
    sys.exit(2)
