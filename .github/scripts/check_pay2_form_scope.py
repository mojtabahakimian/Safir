#!/usr/bin/env python3
"""
نگهبان دامنه‌ی فرم در کوئری‌های TFORMS

باگ واقعی که این اسکریپت را لازم کرد: Pay2AccessService.GetAccessAsync
(همان کوئری‌ای که Pay2AuthorizeAttribute برای هر تصمیم مجوز صدا می‌زند)
فقط فرم‌هایی را برمی‌گرداند که با یک الگوی LIKE ثابت (مثلاً 'PAY2!_%')
مطابقت داشته باشند. وقتی ماژول «بستن ماه بهای تمام‌شده» فرم‌های خودش را
با پیشوند COST_ اضافه کرد، آن الگو را کسی به‌روز نکرد — نتیجه: هیچ
کاربری، حتی مدیر کامل، هرگز نمی‌توانست از هیچ endpoint محافظت‌شده با
[Pay2Authorize(CostForms.*, ...)] عبور کند، چون آن فرم‌ها هرگز در
Forms برنمی‌گشتند. تست‌های موجود این را نگرفتند چون InMemoryDatabase
(دیتابیس آزمایشی) این فیلتر LIKE را اصلاً شبیه‌سازی نمی‌کند — فقط با
دیتابیس واقعی SQL Server قابل کشف بود.

این اسکریپت آن دسته‌ی خطا را برای همیشه می‌بندد: هر پیشوند فرم که واقعاً
در یک [Pay2Authorize(XyzForms.*, ...)] استفاده شده باید در کوئری‌های
اصلیِ TFORMS (که تصمیم مجوز و مدیریت دسترسی از آن‌ها می‌آید) هم پوشش
داشته باشد.

عمداً پرِست‌های PAY2 (ApplyPreset) را بررسی نمی‌کند — آن‌ها از قصد فقط
به فرم‌های PAY2_* محدودند و نباید دامنه‌شان به‌طور ضمنی به COST_* یا
ماژول بعدی گسترش پیدا کند؛ آن محدودیت عمدی است، نه این باگ.
"""
import re
import sys
from pathlib import Path

CONTROLLER_DIR = Path("Server/Controllers")
CONSTANTS_DIR = Path("Shared/Constants")

# کوئری‌هایی که مجوز واقعی از آن‌ها می‌آید یا صفحه‌ی مدیریت دسترسی از
# آن‌ها فرم قابل‌اعطا می‌سازد — نه هر جایی که TFORMS دیده می‌شود.
SCOPED_FILES = {
    Path("Server/Services/Pay2AccessService.cs"): ["GetAccessAsync"],
    Path("Server/Controllers/Pay2AccessController.cs"): ["GetForms", "GetUserAccess", "SaveUserAccess"],
}

FORM_CLASS_RE = re.compile(r"class\s+(\w+)Forms")
FORM_VALUE_RE = re.compile(r'"([A-Za-z][A-Za-z0-9]*)_')
USAGE_RE = re.compile(r"\[Pay2Authorize\(\s*(\w+)Forms\.")
LIKE_RE = re.compile(r"LIKE\s+N?'([A-Za-z0-9]+)!?_%'", re.IGNORECASE)


def form_prefixes() -> dict[str, str]:
    """{ClassPrefix: FORMNAME_prefix} — مثلاً {'Cost': 'COST'}."""
    result = {}
    for path in CONSTANTS_DIR.glob("*.cs"):
        text = path.read_text(encoding="utf-8-sig")
        cls = FORM_CLASS_RE.search(text)
        if not cls:
            continue
        values = FORM_VALUE_RE.findall(text)
        if not values:
            continue
        # پیشوند مشترک همه‌ی مقادیر همان کلاس (COST_DASHBOARD, COST_ACT_... -> COST)
        result[cls.group(1)] = values[0]
    return result


def used_prefixes() -> set[str]:
    used = set()
    for path in CONTROLLER_DIR.glob("*.cs"):
        text = path.read_text(encoding="utf-8-sig")
        used.update(USAGE_RE.findall(text))
    return used


def function_body(text: str, name: str) -> str | None:
    m = re.search(rf"\b{name}\s*\(", text)
    if not m:
        return None
    # از شروع تابع تا اولین تعریف تابع/متد عمومی بعدی (تقریبی ولی برای
    # کوئری‌های SQL تک‌خطی/چندخطیِ این فایل‌ها کافی است)
    rest = text[m.start():]
    nxt = re.search(r"\n\s*(?:public|private|internal)\s+.*\(", rest[1:])
    return rest[: (nxt.start() + 1) if nxt else len(rest)]


def main() -> int:
    prefixes = form_prefixes()
    used = used_prefixes()
    problems = []

    for path, funcs in SCOPED_FILES.items():
        if not path.is_file():
            problems.append(f"{path}: فایل پیدا نشد")
            continue
        text = path.read_text(encoding="utf-8-sig")

        for func in funcs:
            body = function_body(text, func)
            if body is None:
                problems.append(f"{path.name}: تابع {func} پیدا نشد")
                continue

            covered = {m.upper() for m in LIKE_RE.findall(body)}

            for cls_prefix in used:
                formname_prefix = prefixes.get(cls_prefix)
                if not formname_prefix:
                    continue
                if formname_prefix.upper() not in covered:
                    problems.append(
                        f"{path.name}::{func}  فرم‌های {cls_prefix}Forms.* (پیشوند "
                        f"{formname_prefix}_) جایی با [Pay2Authorize] استفاده شده‌اند "
                        f"ولی کوئری این تابع الگوی LIKE '{formname_prefix}!_%' ندارد — "
                        f"یعنی هیچ کاربری هرگز نمی‌تواند از آن‌ها عبور کند."
                    )

    if problems:
        print(f"\n✗ نگهبان دامنه‌ی فرم — {len(problems)} مشکل:\n", file=sys.stderr)
        for p in problems:
            print(f"   • {p}", file=sys.stderr)
        print("", file=sys.stderr)
        return 1

    print(f"✓ نگهبان دامنه‌ی فرم — همه‌ی پیشوندهای استفاده‌شده ({', '.join(sorted(used))}) "
          f"در کوئری‌های اصلی TFORMS پوشش دارند.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
