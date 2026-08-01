#!/bin/bash
# ==============================================================================
# SessionStart hook — Safir
# نصب .NET 8 SDK تا در session های Claude Code روی وب بتوان پروژه را build گرفت.
#
# چرا apt به‌جای اسکریپت رسمی مایکروسافت؟
#   سیاست شبکه‌ی محیط اجرا، دامنه‌های دانلود مایکروسافت
#   (builds.dotnet.microsoft.com و dotnetcli.azureedge.net) را مسدود می‌کند،
#   ولی مخزن رسمی Ubuntu 24.04 خودش بسته‌ی dotnet-sdk-8.0 را دارد و در دسترس است.
# ==============================================================================
set -euo pipefail

# فقط در محیط ریموت (Claude Code on the web) اجرا شود، نه روی ماشین توسعه‌دهنده
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

echo "[session-start] بررسی .NET SDK ..."

# ── ۱. نصب SDK (idempotent) ───────────────────────────────────────────────────
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
  echo "[session-start] .NET 8 SDK از قبل نصب است: $(dotnet --version)"
else
  echo "[session-start] در حال نصب dotnet-sdk-8.0 از مخزن Ubuntu ..."
  export DEBIAN_FRONTEND=noninteractive
  # خطاهای مخازن جانبی (PPA ها) نباید کل نصب را متوقف کنند
  sudo apt-get update -qq || true
  if sudo apt-get install -y -qq --no-install-recommends dotnet-sdk-8.0; then
    echo "[session-start] نصب شد: $(dotnet --version)"
  else
    echo "[session-start] ⚠️  نصب .NET SDK ناموفق بود — دستورهای dotnet در دسترس نخواهند بود." >&2
    exit 0   # session نباید به‌خاطر این شکست بخورد
  fi
fi

# ── ۲. متغیرهای محیطی برای بقیه‌ی session ─────────────────────────────────────
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo 'export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1'
  } >> "$CLAUDE_ENV_FILE"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# ── ۳. گرم‌کردن کش NuGet ──────────────────────────────────────────────────────
# وضعیت کانتینر بعد از اتمام hook کش می‌شود، پس restore اینجا یعنی
# اولین dotnet build در session بدون انتظار شبکه اجرا می‌شود.
cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
if [ -f Safir.sln ]; then
  echo "[session-start] در حال restore بسته‌های NuGet ..."
  dotnet restore Safir.sln --nologo >/dev/null 2>&1 \
    && echo "[session-start] بسته‌ها آماده‌اند." \
    || echo "[session-start] ⚠️  restore ناموفق بود؛ اولین build کندتر خواهد بود." >&2
fi

echo "[session-start] آماده. برای بررسی کامپایل: dotnet build Safir.sln -c Debug"
