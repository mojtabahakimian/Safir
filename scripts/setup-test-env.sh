#!/usr/bin/env bash
# =============================================================================
# ساخت محیط تست کامل Safir — SQL Server، دیتابیس، کاربران آزمایشی، و اجرای برنامه
#
# برای عامل‌های هوش مصنوعی (Jules، Codex، Claude Code) و همچنین توسعه‌دهنده‌ها.
# نتیجه: یک دیتابیس تستِ ایزوله که می‌شود با سه کاربر مختلف واردش شد و
#        کنترل دسترسی حقوق و دستمزد را واقعاً آزمایش کرد.
#
# متغیرهای لازم:
#   ConnectionStrings__DefaultConnection   رشته اتصال (باید به localhost باشد)
#   Jwt__Key                               کلید امضای توکن
#   MSSQL_SA_PASSWORD                      (اختیاری) وگرنه از رشته اتصال خوانده می‌شود
# =============================================================================
set -Eeuo pipefail
trap 'echo "❌ راه‌اندازی در خط $LINENO شکست خورد." >&2' ERR

REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
DB_DIR="$REPO_ROOT/Server/Database"
CONNECTION_STRING="${ConnectionStrings__DefaultConnection:-}"
APP_URL="${APP_URL:-http://127.0.0.1:5080}"

read_connection_value() {
  python3 - "$CONNECTION_STRING" "$1" <<'PY'
import sys
conn, aliases = sys.argv[1], {x.strip().lower() for x in sys.argv[2].split('|')}
for part in conn.split(';'):
    if '=' in part:
        k, v = part.split('=', 1)
        if k.strip().lower() in aliases:
            print(v.strip()); break
PY
}

DB_NAME="$(read_connection_value 'database|initial catalog')"; DB_NAME="${DB_NAME:-SafirTestDb}"
SQL_HOST="$(read_connection_value 'server|data source')";      SQL_HOST="${SQL_HOST:-localhost,1433}"
SA_PASSWORD="${MSSQL_SA_PASSWORD:-$(read_connection_value 'password|pwd')}"

# ── گاردهای ایمنی — این اسکریپت دیتابیس را DROP می‌کند ───────────────────────
[[ -n "$SA_PASSWORD" ]] || { echo "❌ رمز SQL تعیین نشده است." >&2; exit 1; }
[[ -n "${Jwt__Key:-}" ]] || { echo "❌ Jwt__Key تعیین نشده است." >&2; exit 1; }
[[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] || { echo "❌ نام دیتابیس نامعتبر: $DB_NAME" >&2; exit 1; }
[[ "$DB_NAME" == SafirTest* ]] || { echo "❌ توقف ایمنی: نام دیتابیس باید با SafirTest شروع شود." >&2; exit 1; }
case "${SQL_HOST,,}" in
  localhost*|127.0.0.1*|\(local\)*) ;;
  *) echo "❌ توقف ایمنی: فقط SQL Server محلی مجاز است، نه '$SQL_HOST'." >&2; exit 1 ;;
esac

sqlcmd_local() {
  /opt/mssql-tools18/bin/sqlcmd -S "$SQL_HOST" -U sa -P "$SA_PASSWORD" -C -I -b -V 16 "$@"
}

step() { echo; echo "══════════════════════════════════════════════════"; echo "  $*"; echo "══════════════════════════════════════════════════"; }

# ═══════════════════════════════════════════════════════════════════════════
step "۱) .NET 8 SDK"
# ═══════════════════════════════════════════════════════════════════════════
if ! command -v dotnet >/dev/null 2>&1; then
  # اسکریپت رسمی مایکروسافت اول؛ اگر شبکه دامنه‌های دانلود را بست، از مخزن
  # خود اوبونتو نصب می‌کنیم (روی Ubuntu 24.04 بسته dotnet-sdk-8.0 موجود است).
  if curl -fsSL --max-time 30 https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh 2>/dev/null; then
    chmod +x /tmp/dotnet-install.sh && /tmp/dotnet-install.sh --channel 8.0
  else
    echo "⚠️  dot.net در دسترس نیست — نصب از مخزن اوبونتو."
    sudo apt-get update -qq
    sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-8.0
  fi
fi
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:/opt/mssql-tools18/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
LINE='export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:/opt/mssql-tools18/bin:$PATH"'
grep -qxF "$LINE" "$HOME/.bashrc" 2>/dev/null || echo "$LINE" >> "$HOME/.bashrc"
dotnet --version

# ═══════════════════════════════════════════════════════════════════════════
step "۲) SQL Server و sqlcmd"
# ═══════════════════════════════════════════════════════════════════════════
sudo apt-get update -qq
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq curl ca-certificates gnupg debconf-utils

if [[ ! -f /usr/share/keyrings/microsoft-prod.gpg ]]; then
  curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
    | gpg --dearmor | sudo tee /usr/share/keyrings/microsoft-prod.gpg >/dev/null
fi
# نکته: برای Ubuntu 24.04 فقط mssql-server-2025 وجود دارد؛ mssql-server-2022 خطای 404 می‌دهد.
[[ -f /etc/apt/sources.list.d/mssql-server-2025.list ]] || \
  curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list \
    | sudo tee /etc/apt/sources.list.d/mssql-server-2025.list >/dev/null
[[ -f /etc/apt/sources.list.d/msprod.list ]] || \
  curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/prod.list \
    | sudo tee /etc/apt/sources.list.d/msprod.list >/dev/null
sudo apt-get update -qq

[[ -x /opt/mssql/bin/sqlservr ]] || sudo DEBIAN_FRONTEND=noninteractive apt-get install -y mssql-server
[[ -f /var/opt/mssql/mssql.conf ]] || sudo env ACCEPT_EULA=Y MSSQL_PID=Developer \
  MSSQL_SA_PASSWORD="$SA_PASSWORD" MSSQL_TCP_PORT=1433 /opt/mssql/bin/mssql-conf -n setup

if [[ ! -x /opt/mssql-tools18/bin/sqlcmd ]]; then
  echo "msodbcsql18 msodbcsql18/ACCEPT_EULA boolean true" | sudo debconf-set-selections
  echo "msodbcsql18 msodbcsql18/ACCEPT_EULA seen true"    | sudo debconf-set-selections
  sudo env ACCEPT_EULA=Y DEBIAN_FRONTEND=noninteractive \
    apt-get install -y msodbcsql18 mssql-tools18 unixodbc-dev
fi

# ═══════════════════════════════════════════════════════════════════════════
step "۳) ابزارهای Excel و PDF"
# ═══════════════════════════════════════════════════════════════════════════
if ! command -v libreoffice >/dev/null 2>&1; then
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    libreoffice-calc libreoffice-common python3-uno poppler-utils fontconfig fonts-noto-core
fi
# فونت فارسی خود Safir را در دسترس LibreOffice بگذار وگرنه PDF فارسی خراب می‌شود
FONT_DIR="$HOME/.local/share/fonts"; mkdir -p "$FONT_DIR"
[[ -d "$REPO_ROOT/Server/Fonts" ]] && find "$REPO_ROOT/Server/Fonts" -maxdepth 1 -type f \
  \( -iname '*.ttf' -o -iname '*.otf' \) -exec cp -f {} "$FONT_DIR/" \;
fc-cache -f >/dev/null
libreoffice --headless --version

# ═══════════════════════════════════════════════════════════════════════════
step "۴) بالا آوردن SQL Server"
# ═══════════════════════════════════════════════════════════════════════════
if ! pgrep -x sqlservr >/dev/null 2>&1; then
  sudo systemctl start mssql-server 2>/dev/null \
    || sudo -u mssql nohup /opt/mssql/bin/sqlservr >/tmp/sqlservr.log 2>&1 &
fi
for i in $(seq 1 60); do
  sqlcmd_local -d master -Q "SET NOCOUNT ON; SELECT 1;" >/dev/null 2>&1 && { echo "✅ آماده است."; break; }
  [[ $i -eq 60 ]] && { echo "❌ SQL Server بالا نیامد." >&2; tail -200 /tmp/sqlservr.log 2>/dev/null; exit 1; }
  echo "   انتظار... ($i/60)"; sleep 2
done

# ═══════════════════════════════════════════════════════════════════════════
step "۵) ساخت دوباره دیتابیس تست"
# ═══════════════════════════════════════════════════════════════════════════
sqlcmd_local -d master -Q "
IF DB_ID(N'$DB_NAME') IS NOT NULL
BEGIN
    ALTER DATABASE [$DB_NAME] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DB_NAME];
END;
CREATE DATABASE [$DB_NAME];"

# ═══════════════════════════════════════════════════════════════════════════
step "۶) وابستگی‌های قدیمی (بدون تداخل با schema.sql)"
# ═══════════════════════════════════════════════════════════════════════════
for f in legacy_dependencies.sql schema.sql pay2_runtime_procedures.sql \
         pay2_acl_migration.sql pay2_seed.sql test_auth_and_acl_users.sql; do
  [[ -f "$DB_DIR/$f" ]] || { echo "❌ فایل پیدا نشد: $DB_DIR/$f" >&2; exit 1; }
done

# legacy_dependencies.sql عمداً کل مجموعه وابستگی‌ها را دارد و بخشی از آن‌ها
# را schema.sql هم می‌سازد. دسته‌هایی که جدولشان در schema.sql هست حذف می‌شوند.
python3 - "$DB_DIR/schema.sql" "$DB_DIR/legacy_dependencies.sql" /tmp/legacy_filtered.sql <<'PYFILTER'
from pathlib import Path
import re, sys

def read_sql(p):
    raw = Path(p).read_bytes()
    text = raw.decode('utf-16') if raw[:2] in (b'\xff\xfe', b'\xfe\xff') else raw.decode('utf-8-sig')
    return text.replace('\r\n', '\n').replace('\r', '\n')

schema, legacy, out = read_sql(sys.argv[1]), read_sql(sys.argv[2]), Path(sys.argv[3])
name = r'(?:\[?dbo\]?\s*\.\s*)?\[?([A-Za-z0-9_]+)\]?'
create = re.compile(r'\bCREATE\s+TABLE\s+' + name, re.I)
alter  = re.compile(r'\bALTER\s+TABLE\s+' + name, re.I)

owned = {m.group(1).upper() for m in create.finditer(schema)}
if not owned:
    raise SystemExit('در schema.sql هیچ CREATE TABLE پیدا نشد.')

kept, kept_names, skipped = [], set(), set()
for batch in re.split(r'(?im)^\s*GO\s*(?:--.*)?$', legacy):
    if not batch.strip():
        continue
    m = create.search(batch) or alter.search(batch)
    if not m:
        continue
    t = m.group(1).upper()
    (skipped if t in owned else kept_names).add(t)
    if t not in owned:
        kept.append(batch.strip())

if not kept:
    raise SystemExit('اسکریپت فیلترشده خالی شد.')

checks = ['SET NOCOUNT ON;'] + [
    f"IF OBJECT_ID(N'dbo.{t}', N'U') IS NULL THROW 53000, 'جدول ساخته نشد: dbo.{t}', 1;"
    for t in sorted(kept_names)
]
out.write_text('\nGO\n'.join(['SET NOCOUNT ON;\nSET XACT_ABORT ON;', *kept, '\n'.join(checks)]) + '\nGO\n',
               encoding='utf-8', newline='\n')
print(f'{len(skipped)} جدول در schema.sql موجود بود و رد شد؛ {len(kept_names)} جدول قدیمی ساخته می‌شود.')
PYFILTER
sqlcmd_local -d "$DB_NAME" -i /tmp/legacy_filtered.sql

# ═══════════════════════════════════════════════════════════════════════════
step "۷) schema.sql"
# ═══════════════════════════════════════════════════════════════════════════
python3 - "$DB_DIR/schema.sql" "$DB_NAME" /tmp/schema_utf8.sql <<'PY'
from pathlib import Path
import re, sys
raw = Path(sys.argv[1]).read_bytes()
text = raw.decode('utf-16') if raw[:2] in (b'\xff\xfe', b'\xfe\xff') else raw.decode('utf-8-sig')
text = text.replace('\r\n', '\n').replace('\r', '\n')
use = f'USE [{sys.argv[2]}]'
text, n = re.subn(r'^\s*USE\s+\[[^\]]+\]\s*$', use, text, count=1, flags=re.I | re.M)
Path(sys.argv[3]).write_text(text if n else use + '\nGO\n' + text, encoding='utf-8', newline='\n')
PY
sqlcmd_local -d master -i /tmp/schema_utf8.sql

# ═══════════════════════════════════════════════════════════════════════════
step "۸) رویه‌های اجرایی PAY2 و مهاجرت کنترل دسترسی"
# ═══════════════════════════════════════════════════════════════════════════
sqlcmd_local -d "$DB_NAME" -i "$DB_DIR/pay2_runtime_procedures.sql"
sqlcmd_local -d "$DB_NAME" -i "$DB_DIR/pay2_acl_migration.sql"

# ═══════════════════════════════════════════════════════════════════════════
step "۹) بررسی ساختار PAY2"
# ═══════════════════════════════════════════════════════════════════════════
sqlcmd_local -d "$DB_NAME" -Q "
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.PAY2_EMPLOYEE', N'U')   IS NULL THROW 52100, 'PAY2_EMPLOYEE ساخته نشد.', 1;
IF OBJECT_ID(N'dbo.PAY2_RUN_DETAIL', N'U') IS NULL THROW 52101, 'PAY2_RUN_DETAIL ساخته نشد.', 1;
IF OBJECT_ID(N'dbo.PAY2_USER_WS', N'U')    IS NULL THROW 52102, 'PAY2_USER_WS ساخته نشد — مهاجرت ACL اجرا نشده.', 1;
IF OBJECT_ID(N'dbo.PAY2_SEC_AUDIT', N'U')  IS NULL THROW 52103, 'PAY2_SEC_AUDIT ساخته نشد.', 1;
IF OBJECT_ID(N'dbo.SP_PAY2_CALC_RUN', N'P')   IS NULL THROW 52104, 'SP_PAY2_CALC_RUN ساخته نشد.', 1;
IF OBJECT_ID(N'dbo.SP_PAY2_GEN_DEED', N'P')   IS NULL THROW 52105, 'SP_PAY2_GEN_DEED ساخته نشد.', 1;
IF OBJECT_ID(N'dbo.SP_PAY2_REVERT_RUN', N'P') IS NULL THROW 52106, 'SP_PAY2_REVERT_RUN ساخته نشد.', 1;
IF OBJECT_ID(N'dbo.FN_PAY2_CALC_TAX', N'FN')  IS NULL THROW 52107, 'FN_PAY2_CALC_TAX ساخته نشد.', 1;
IF (SELECT COUNT(*) FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2[_]%') < 20
    THROW 52108, 'ردیف‌های TFORMS مربوط به PAY2 کامل درج نشدند.', 1;
SELECT (SELECT COUNT(*) FROM sys.tables      WHERE name LIKE N'PAY2[_]%')                AS Pay2Tables,
       (SELECT COUNT(*) FROM sys.procedures  WHERE name LIKE N'SP_PAY2[_]%')             AS Pay2Procs,
       (SELECT COUNT(*) FROM dbo.TFORMS      WHERE FORMNAME LIKE N'PAY2[_]%')            AS Pay2Forms;"

# ═══════════════════════════════════════════════════════════════════════════
step "۱۰) داده نمونه و کاربران آزمایشی"
# ═══════════════════════════════════════════════════════════════════════════
sqlcmd_local -d "$DB_NAME" -i "$DB_DIR/pay2_seed.sql"
sqlcmd_local -d "$DB_NAME" -i "$DB_DIR/test_auth_and_acl_users.sql"

# شمارش‌ها را سخت‌گیرانه چک نکن — با تولید دوباره seed عوض می‌شوند.
sqlcmd_local -d "$DB_NAME" -Q "
SET NOCOUNT ON;
IF (SELECT COUNT_BIG(*) FROM dbo.PAY2_EMPLOYEE)   = 0 THROW 52200, 'هیچ پرسنلی درج نشد.', 1;
IF (SELECT COUNT_BIG(*) FROM dbo.PAY2_RUN_DETAIL) = 0 THROW 52201, 'هیچ ردیف فیشی درج نشد.', 1;
SELECT DB_NAME() AS Db,
       (SELECT COUNT_BIG(*) FROM dbo.PAY2_EMPLOYEE)   AS Employees,
       (SELECT COUNT_BIG(*) FROM dbo.PAY2_RUN)        AS Runs,
       (SELECT COUNT_BIG(*) FROM dbo.PAY2_RUN_DETAIL) AS RunDetails,
       (SELECT COUNT_BIG(*) FROM dbo.SALA_DTL)        AS Users;"

# ═══════════════════════════════════════════════════════════════════════════
step "۱۱) کامپایل"
# ═══════════════════════════════════════════════════════════════════════════
cd "$REPO_ROOT"
dotnet restore Safir.sln
dotnet build Safir.sln --no-restore
python3 .github/scripts/check_pay2_acl.py

# ═══════════════════════════════════════════════════════════════════════════
step "۱۲) آزمون دود واقعی — نه فقط صفحه اول"
# ═══════════════════════════════════════════════════════════════════════════
# صفحه‌ی / در Blazor WebAssembly یک فایل استاتیک است و حتی وقتی دیتابیس و کل
# API خراب باشد هم ۲۰۰ برمی‌گرداند. پس سه چیز را جدا بررسی می‌کنیم:
#   الف) بارگذاری WebAssembly  → _framework/blazor.boot.json
#   ب) مسیر احراز هویت + دیتابیس → POST /api/auth/login
#   ج) اعمال شدن کنترل دسترسی   → /api/pay2/access/me با و بدون توکن
(
  set -Eeuo pipefail
  LOG=/tmp/safir-runtime.log
  PID=""
  cleanup() { [[ -n "$PID" ]] && { kill "$PID" 2>/dev/null || true; wait "$PID" 2>/dev/null || true; }; }
  trap cleanup EXIT

  cd "$REPO_ROOT/Server"
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$APP_URL" \
    dotnet bin/Debug/net8.0/Safir.Server.dll >"$LOG" 2>&1 &
  PID=$!

  for i in $(seq 1 90); do
    kill -0 "$PID" 2>/dev/null || { echo "❌ سرور قبل از آماده شدن بسته شد." >&2; tail -100 "$LOG" >&2; exit 1; }
    curl -sSf --max-time 5 -o /dev/null "$APP_URL/" 2>/dev/null && break
    [[ $i -eq 90 ]] && { echo "❌ سرور بالا نیامد." >&2; tail -100 "$LOG" >&2; exit 1; }
    sleep 1
  done

  # الف) هسته‌ی WebAssembly
  curl -sSf --max-time 10 -o /tmp/boot.json "$APP_URL/_framework/blazor.boot.json" \
    || { echo "❌ blazor.boot.json سرو نشد — کلاینت اصلاً بالا نمی‌آید." >&2; exit 1; }
  python3 -c "import json;d=json.load(open('/tmp/boot.json'));assert d, 'boot.json خالی است'" \
    || { echo "❌ blazor.boot.json معتبر نیست." >&2; exit 1; }
  echo "✅ blazor.boot.json سالم است."

  # ب) ورود واقعی — این یعنی دیتابیس، Dapper، JWT و کدگشایی رمز همه کار می‌کنند
  TOKEN="$(curl -sS --max-time 15 -X POST "$APP_URL/api/auth/login" \
      -H 'Content-Type: application/json' \
      -d '{"Username":"payadmin","Password":"111111"}' \
    | python3 -c "import json,sys;print(json.load(sys.stdin).get('token') or '')" 2>/dev/null || true)"
  [[ -n "$TOKEN" ]] || { echo "❌ ورود payadmin شکست خورد — API یا دیتابیس کار نمی‌کند." >&2; tail -100 "$LOG" >&2; exit 1; }
  echo "✅ ورود با کاربر آزمایشی انجام شد و توکن صادر شد."

  # ج) کنترل دسترسی: بدون توکن باید ۴۰۱ بدهد، با توکن ۲۰۰
  ANON="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "$APP_URL/api/pay2/access/me")"
  [[ "$ANON" == "401" ]] || { echo "❌ /api/pay2/access/me بدون توکن $ANON داد، انتظار ۴۰۱ بود." >&2; exit 1; }
  AUTH="$(curl -sS -o /tmp/me.json -w '%{http_code}' --max-time 10 \
            -H "Authorization: Bearer $TOKEN" "$APP_URL/api/pay2/access/me")"
  [[ "$AUTH" == "200" ]] || { echo "❌ /api/pay2/access/me با توکن $AUTH داد، انتظار ۲۰۰ بود." >&2; exit 1; }
  echo "✅ کنترل دسترسی فعال است: بدون توکن ۴۰۱، با توکن ۲۰۰."
  echo "   دسترسی‌های payadmin: $(head -c 300 /tmp/me.json)"
)

# ═══════════════════════════════════════════════════════════════════════════
step "۱۳) مرورگر برای تست انسانی"
# ═══════════════════════════════════════════════════════════════════════════
# تصویر پایه‌ی Jules از قبل Playwright دارد؛ دوباره نصبش نکن.
# ولی «فرض» هم نکن — بررسی کن که واقعاً هست.
if command -v npx >/dev/null 2>&1 && npx --no-install playwright --version >/dev/null 2>&1; then
  echo "✅ Playwright موجود است: $(npx --no-install playwright --version)"
elif python3 -c "import playwright" 2>/dev/null; then
  echo "✅ Playwright (پایتون) موجود است."
elif [[ -x /opt/pw-browsers/chromium ]]; then
  echo "✅ Chromium در /opt/pw-browsers موجود است (PLAYWRIGHT_BROWSERS_PATH را ست کنید)."
else
  echo "⚠️  Playwright پیدا نشد. اگر تست مرورگری لازم دارید نصبش کنید:"
  echo "     npm i -D @playwright/test && npx playwright install --with-deps chromium"
fi

step "✅ محیط تست آماده است"
cat <<EOF
SQL Server : $SQL_HOST
دیتابیس    : $DB_NAME
آدرس برنامه: $APP_URL

کاربران آزمایشی (فقط دیتابیس تست):
  payadmin  / 111111   دسترسی کامل، همه کارگاه‌ها
  payviewer / 222222   فقط مشاهده، همه کارگاه‌ها
  payscoped / 333333   دسترسی کامل، فقط کارگاه ۱

ACL_ENFORCE = 1 و ACL_WS_SCOPE_ENFORCE = 1 روشن شده‌اند.
رمز SQL عمداً چاپ نشد.
EOF
