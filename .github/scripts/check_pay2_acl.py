#!/usr/bin/env python3
"""
نگهبان کنترل دسترسی حقوق و دستمزد (PAY2)

اگر کسی — انسان یا عامل خودکار — یک endpoint جدید به کنترلرهای Pay2 اضافه کند
و یادش برود مجوز بگذارد، یا مجوز موجودی را حذف کند، این اسکریپت CI را قرمز می‌کند.

سه چیز را بررسی می‌کند:
  ۱. هر اکشن باید یا [Pay2Authorize] داشته باشد یا داخل بدنه بررسی مجوز کند
     (HasAndAuditAsync — یا HasAsync که سابقه‌ی امنیتی نمی‌نویسد).
  ۲. هیچ اکشنی نباید [AllowAnonymous] داشته باشد.
  ۳. اتریبیوت تکراری روی یک اکشن نباشد (باعث ثبت دوباره‌ی لاگ می‌شود).

استثناهای مجاز در ALLOWLIST پایین با دلیل ثبت شده‌اند.
"""
import re
import sys
from collections import Counter
from pathlib import Path

CONTROLLER_DIR = Path("Server/Controllers")

# اکشن‌هایی که عمداً بدون [Pay2Authorize] هستند — هرکدام با دلیل
ALLOWLIST = {
    ("Pay2AccessController.cs", "me"):
        "هر کاربر لاگین‌کرده باید دسترسی‌های خودش را بخواند تا UI بداند چه نمایش دهد",
}

HTTP_RE = re.compile(r'^\s*\[Http(Get|Post|Put|Delete|Patch)\((?:"([^"]*)")?')
PUBLIC_RE = re.compile(r'^\s*public\s')
COMMENT_RE = re.compile(r'//.*$')
# فقط HasAsync خالی؛ HasAndAuditAsync با این الگو مطابقت نمی‌کند
SILENT_RE = re.compile(r'\.\s*HasAsync\s*\(')


def scan(path: Path):
    """هر اکشن را به‌صورت (شماره خط، مسیر، اتریبیوت‌ها، بدنه) برمی‌گرداند."""
    lines = path.read_text(encoding="utf-8-sig").split("\n")
    actions, attrs, start, route = [], [], None, None

    for i, line in enumerate(lines):
        m = HTTP_RE.match(line)
        if m:
            start, route, attrs = i, (m.group(2) or "(root)"), []
        elif start is not None and "Pay2Authorize" in line:
            attrs.append(COMMENT_RE.sub("", line).strip())
        elif start is not None and "AllowAnonymous" in line:
            attrs.append("__ANON__")
        elif start is not None and PUBLIC_RE.match(line):
            # بدنه‌ی اکشن تا شروع اکشن بعدی
            end = next((j for j in range(i + 1, len(lines)) if HTTP_RE.match(lines[j])), len(lines))
            actions.append((start + 1, route, attrs, "\n".join(lines[i:end])))
            start = None

    return actions


def main() -> int:
    if not CONTROLLER_DIR.is_dir():
        print(f"✗ پوشه پیدا نشد: {CONTROLLER_DIR}", file=sys.stderr)
        return 1

    problems = []
    checked = 0

    for path in sorted(CONTROLLER_DIR.glob("Pay2*.cs")):
        for line_no, route, attrs, body in scan(path):
            checked += 1
            name = path.name
            has_attr = any(a != "__ANON__" for a in attrs)
            # HasAndAuditAsync هم مجوز را بررسی می‌کند و هم در PAY2_SEC_AUDIT
            # ردی می‌گذارد؛ HasAsync خالی فقط بررسی می‌کند و رد شدن را
            # بی‌صدا می‌گذارد. یک بدنه می‌تواند هر دو را داشته باشد، پس
            # به‌جای «هست/نیست» تعداد فراخوانی‌های خاموش را می‌شماریم.
            silent_calls = SILENT_RE.findall(body)
            has_inline = bool(silent_calls) or "HasAndAuditAsync" in body

            if "__ANON__" in attrs:
                problems.append(
                    f"{name}:{line_no}  [AllowAnonymous] روی «{route}» — "
                    f"کل کنترل دسترسی را دور می‌زند"
                )

            if not has_attr and not has_inline:
                if (name, route) in ALLOWLIST:
                    continue
                problems.append(
                    f"{name}:{line_no}  «{route}» هیچ کنترل دسترسی ندارد — "
                    f"[Pay2Authorize] اضافه کنید یا در ALLOWLIST با دلیل ثبتش کنید"
                )

            # حتی اگر اتریبیوت هم باشد، بررسی داخلیِ خاموش یعنی رد شدن
            # روی آن فرمِ دوم هیچ ردی در سابقه نمی‌گذارد.
            if silent_calls:
                problems.append(
                    f"{name}:{line_no}  «{route}» {len(silent_calls)} بار مجوز را با "
                    f"HasAsync بررسی می‌کند و رد شدن را در PAY2_SEC_AUDIT ثبت نمی‌کند — "
                    f"از HasAndAuditAsync استفاده کنید"
                )

            dupes = [a for a, c in Counter(a for a in attrs if a != "__ANON__").items() if c > 1]
            for d in dupes:
                problems.append(f"{name}:{line_no}  اتریبیوت تکراری روی «{route}»: {d}")

    if problems:
        print(f"\n✗ نگهبان دسترسی PAY2 — {len(problems)} مشکل در {checked} اکشن:\n", file=sys.stderr)
        for p in problems:
            print(f"   • {p}", file=sys.stderr)
        print("", file=sys.stderr)
        return 1

    print(f"✓ نگهبان دسترسی PAY2 — هر {checked} اکشن محافظت شده است.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
