"""
تولید اسکریپت MERGE از تنظیمات.

⚠️ نسخه‌ی اول مقادیر را به‌صورت متن از sqlcmd می‌گرفت و دوباره می‌ساخت؛
   آزمونِ برگشت‌پذیری نشان داد شش جدول عوض می‌شوند: sqlcmd فاصله‌ی انتهایی
   را می‌بُرد (-W) و datetime/float را کوتاه می‌کرد. اینجا خودِ SQL Server
   لیترالِ هر ردیف را می‌سازد، پس هیچ تبدیلی بیرون از موتور انجام نمی‌شود.
"""
import io, subprocess, datetime

SERVER = r"DESKTOP-GLPOA91\SQL2022"
DB     = "YAZDSEPAR1405"
OUT    = r"C:\prg\Safir\Server\Database\tools\costclose-settings.sql"

TABLES = [
    ("CC_Unit",               ["UnitId"],          True,  "واحدهای تولیدی"),
    ("CC_UnitAnbar",          ["UnitId","Anbar"],  False, "انبارهای هر واحد و نقششان"),
    ("CC_UnitAcc",            ["Id"],              True,  "حساب‌های هزینه‌ی هر واحد"),
    ("CC_AnbarHes",           ["Anbar"],           False, "حساب معین هر انبار"),
    ("CC_ExpenseAcc",         ["Id"],              True,  "حساب‌های هزینه"),
    ("CC_LaborAbsorptionRate",["UnitId","CODE"],   False, "ضریب جذب دستمزد و سربار هر کالا"),
    ("CC_MarginTarget",       ["Id"],              True,  "هدف حاشیه سود"),
    ("CC_RebalancePref",      ["Id"],              True,  "ترجیح توزیع مجدد مواد"),
    ("CC_CheckRule",          ["RuleCode"],        False, "قواعد کنترلی (آستانه و فعال/غیرفعال)"),
    ("AI_UserAccess",         ["UserCo"],          False, "دسترسی کاربران به دستیار"),
    ("AI_Config",             ["Id"],              False, "تنظیمات سرویس هوش مصنوعی"),
]

SKIP_COLS = {("AI_Config", "ApiKey")}   # کلید سرویس — دستی ثبت شود

INT_T   = {"int","bigint","smallint","tinyint"}
FLOAT_T = {"float","real"}
DEC_T   = {"decimal","numeric","money","smallmoney"}
DATE_T  = {"datetime","datetime2","date","smalldatetime","datetimeoffset","time"}


def sqlcmd(query, wide=False):
    args = ["sqlcmd", "-S", SERVER, "-d", DB, "-E", "-y", "0",
            "-s", "\x1f", "-Q", "SET NOCOUNT ON; " + query]
    p = subprocess.run(args, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if p.returncode != 0:
        raise RuntimeError(p.stdout + p.stderr)
    lines = p.stdout.splitlines()
    # بدون -h، دو سطر اولْ نامِ ستون و خطِ زیرش است
    if len(lines) >= 2 and set(lines[1].strip()) <= {"-", " "}:
        lines = lines[2:]
    return [l for l in lines if l.strip() and not l.startswith("(")]


def columns(table):
    q = (f"SELECT c.name + '\x1f' + ty.name + '\x1f' + CAST(c.is_identity AS VARCHAR(1)) "
         f"FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id "
         f"WHERE c.object_id=OBJECT_ID('dbo.{table}') ORDER BY c.column_id")
    out = []
    for line in sqlcmd(q):
        n, t, i = line.split("\x1f")
        out.append((n.strip(), t.strip().lower(), i.strip() == "1"))
    return out


def literal_expr(name, t):
    """عبارتی که خودِ SQL Server لیترالِ این ستون را با آن می‌سازد."""
    c = f"[{name}]"
    if t == "bit":
        return f"ISNULL(CAST(CAST({c} AS INT) AS VARCHAR(1)), 'NULL')"
    if t in INT_T:
        return f"ISNULL(CAST({c} AS VARCHAR(20)), 'NULL')"
    if t in FLOAT_T:
        # style 3 = ۱۷ رقم، تنها شکلی که float را بی‌کم‌وکاست برمی‌گرداند
        return f"ISNULL(CONVERT(VARCHAR(50), {c}, 3), 'NULL')"
    if t in DEC_T:
        return f"ISNULL(CONVERT(VARCHAR(50), {c}), 'NULL')"
    if t in DATE_T:
        # style 126 = ISO-8601، بدون وابستگی به تنظیمات زبان سرور
        return f"ISNULL('''' + CONVERT(VARCHAR(33), {c}, 126) + '''', 'NULL')"
    if t == "uniqueidentifier":
        return f"ISNULL('''' + CAST({c} AS VARCHAR(36)) + '''', 'NULL')"
    # متن — تک‌کوتیشن escape می‌شود و فاصله‌ی انتهایی دست نمی‌خورد
    return f"ISNULL('N''' + REPLACE(CAST({c} AS NVARCHAR(MAX)), '''', '''''') + '''', 'NULL')"


def emit(table, keys, has_identity, title):
    cols = [c for c in columns(table) if (table, c[0]) not in SKIP_COLS]
    names = [c[0] for c in cols]

    tuple_expr = " + ', ' + ".join(literal_expr(n, t) for n, t, _ in cols)
    order = ", ".join(f"[{k}]" for k in keys)
    rows = sqlcmd(f"SELECT '  (' + {tuple_expr} + ')' FROM dbo.{table} ORDER BY {order}",
                  wide=True)
    # -W فقط فاصله‌ی بعد از پرانتزِ پایانی را می‌بُرد که بی‌اثر است
    rows = [r.rstrip() for r in rows]

    o = []
    o.append("/* " + "─" * 66)
    o.append(f"   {table} — {title}   ({len(rows)} ردیف)")
    o.append("   " + "─" * 66 + " */")
    if not rows:
        o.append(f"PRINT N'{table}: منبع خالی بود — رد شد.';\n")
        return "\n".join(o)

    if has_identity:
        o.append(f"SET IDENTITY_INSERT dbo.{table} ON;")
    upd = [n for n in names if n not in keys]
    o.append(f"MERGE dbo.{table} AS t")
    o.append("USING (VALUES")
    o.append(",\n".join(rows))
    o.append(f") AS s ({', '.join('['+n+']' for n in names)})")
    o.append("ON " + " AND ".join(f"t.[{k}] = s.[{k}]" for k in keys))
    if upd:
        o.append("WHEN MATCHED THEN UPDATE SET")
        o.append(",\n".join(f"    t.[{n}] = s.[{n}]" for n in upd))
    o.append("WHEN NOT MATCHED BY TARGET THEN")
    o.append(f"    INSERT ({', '.join('['+n+']' for n in names)})")
    o.append(f"    VALUES ({', '.join('s.['+n+']' for n in names)});")
    o.append(f"PRINT N'{table}: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N' ردیف';")
    if has_identity:
        o.append(f"SET IDENTITY_INSERT dbo.{table} OFF;")
    o.append("")
    return "\n".join(o)


header = f"""/* ═══════════════════════════════════════════════════════════════════════
   تنظیمات ماژول بستن ماه و دستیار — انتقال به پایگاه دیگر

   از {DB} روی {SERVER}
   گرفته شده در {datetime.datetime.now():%Y-%m-%d %H:%M}.

   ── چه می‌کند ──
   هر ردیفِ تنظیمات را اگر نبود می‌سازد و اگر بود به‌روز می‌کند (MERGE).
   هیچ ردیفی را حذف نمی‌کند: تنظیمی که روی مقصد هست و اینجا نیست،
   دست‌نخورده می‌ماند.

   ── چه چیزی نمی‌آورد ──
   داده‌ی اجرا: CC_Run، CC_RunLog، CC_RunStep، CC_Exception، CC_Snapshot،
   CC_ItemCost، CC_ItemMargin*، CC_Variance*، CC_FormulaChange و
   CC_ConversionCost (ستون RunId دارد، یعنی به‌ازای هر اجراست نه تنظیم).

   ⚠️ کلید سرویس هوش مصنوعی (AI_Config.ApiKey) عمداً اینجا نیست — کلید
      نباید در فایلی بگردد که کپی و ایمیل می‌شود. بعد از اجرا از صفحه‌ی
      «تنظیمات سرویس هوش مصنوعی» ثبتش کنید.

   ── پیش از اجرا ──
   ۱) جدول‌ها باید از قبل ساخته شده باشند (ScriptSqly).
   ۲) روی پایگاه مقصد اجرا کنید.
   ۳) تراکنش دارد: یا همه می‌نشیند یا هیچ‌کدام.

   ⚠️ شناسه‌ها (UnitId و Id) عیناً منتقل می‌شوند تا ارجاع میان جدول‌ها
      نشکند. اگر مقصد از قبل تنظیماتِ متفاوتی با همان شناسه‌ها دارد،
      بازنویسی می‌شوند — اول پشتیبان بگیرید.

   این فایل تولیدشده است؛ دستی ویرایشش نکنید.
   ═══════════════════════════════════════════════════════════════════════ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRAN;
GO

"""

footer = """
COMMIT;
GO

PRINT N'';
PRINT N'تنظیمات منتقل شد.';
PRINT N'یادآوری: کلید سرویس هوش مصنوعی را از صفحه‌ی تنظیمات ثبت کنید.';
GO
"""

parts = [header] + [emit(*t) for t in TABLES] + [footer]
io.open(OUT, "w", encoding="utf-8-sig", newline="\r\n").write("\n".join(parts))
print("written:", OUT)
