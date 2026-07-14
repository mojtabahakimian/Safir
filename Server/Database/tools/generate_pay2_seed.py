#!/usr/bin/env python3
"""
Convert the PAY2 JSON export in Data.txt to an anonymized SQL Server seed script.

Usage:
    python3 generate_pay2_seed.py Data.txt pay2_seed.sql

The generated seed:
- preserves IDs, payroll amounts, dates, and table relationships;
- removes the non-portable audit column CRT;
- anonymizes employee names and identifiers;
- adds minimal parent rows for PAY2_WORKSHOP, PAY2_JOB, and
  PAY2_ITEM_TEMPLATE because the supplied export does not include them;
- refuses to run against a database whose name does not start with SafirTest.
"""

from __future__ import annotations

import json
import re
import sys
from decimal import Decimal
from pathlib import Path
from typing import Any, Iterable

EXPECTED_TABLES = [
    "PAY2_CONFIG",
    "PAY2_TAX_BRACKET",
    "PAY2_PERIOD",
    "PAY2_RUN",
    "PAY2_RUN_LINE",
    "PAY2_RUN_DETAIL",
    "PAY2_EMPLOYEE",
    "PAY2_ITEM_DEF",
    "PAY2_ATTENDANCE",
    "PAY2_DECREE",
    "PAY2_DECREE_LINE",
]

IDENTITY_COLUMNS = {
    "PAY2_WORKSHOP": "WS_ID",
    "PAY2_JOB": "JOB_ID",
    "PAY2_ITEM_DEF": "ITEM_ID",
    "PAY2_ITEM_TEMPLATE": "TMPL_ID",
    "PAY2_TAX_BRACKET": "BRK_ID",
    "PAY2_EMPLOYEE": "EMP_ID",
    "PAY2_PERIOD": "PER_ID",
    "PAY2_DECREE": "DEC_ID",
    "PAY2_RUN": "RUN_ID",
}

DATETIME_RE = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?$"
)


def extract_payload(raw_text: str) -> dict[str, list[dict[str, Any]]]:
    """Extract and repair the compact JSON payload copied from SSMS/chat."""
    fenced = re.search(r"```(?:json)?\s*(\{.*\})\s*```", raw_text, re.S | re.I)
    if fenced:
        payload = fenced.group(1)
    else:
        start = raw_text.find("{")
        end = raw_text.rfind("}")
        if start < 0 or end <= start:
            raise ValueError("No JSON object was found in the input file.")
        payload = raw_text[start : end + 1]

    # The supplied chat export introduced hard line breaks inside JSON strings
    # and even inside property names. The original SQL JSON was compact, so
    # removing CR/LF restores the valid payload.
    payload = payload.replace("\r", "").replace("\n", "")

    parsed = json.loads(payload, parse_float=Decimal)
    if not isinstance(parsed, dict):
        raise ValueError("The JSON root must be an object.")

    missing = [name for name in EXPECTED_TABLES if name not in parsed]
    if missing:
        raise ValueError(f"Missing expected tables: {', '.join(missing)}")

    for name in EXPECTED_TABLES:
        if not isinstance(parsed[name], list):
            raise ValueError(f"{name} must contain a JSON array.")
    return parsed


def make_valid_national_code(sequence: int) -> str:
    """Create a deterministic, synthetic 10-digit code with a valid checksum."""
    first_nine = f"{990000000 + sequence:09d}"
    weighted = sum(int(first_nine[i]) * (10 - i) for i in range(9))
    remainder = weighted % 11
    check = remainder if remainder < 2 else 11 - remainder
    return first_nine + str(check)


def anonymize_employee(row: dict[str, Any], ordinal: int) -> dict[str, Any]:
    result = dict(row)
    synthetic_code = str(9000 + ordinal)

    result["EMP_CODE"] = synthetic_code
    result["FIRST_NAME"] = "کارمند"
    result["LAST_NAME"] = f"آزمایشی {ordinal:02d}"
    result["FATHER_NAME"] = "نام پدر آزمایشی"
    result["NATIONAL_CODE"] = make_valid_national_code(ordinal)
    result["ID_NUMBER"] = f"T{ordinal:07d}"
    result["BIRTH_PLACE"] = "آزمایشی"
    result["BIRTH_DATE"] = 13700100 + min(ordinal, 28)
    result["INS_CODE"] = f"9000{ordinal:04d}"
    result["CARD_NO"] = synthetic_code
    result["MOBILE"] = f"0900000{ordinal:04d}"
    result["BANK_ACC"] = f"TEST{ordinal:026d}"[-30:]
    result["IBAN"] = f"{ordinal:024d}"
    result["ACC_T"] = f"213-1-{synthetic_code}"

    source_keys = set(row)
    optional_replacements = {"BANK_ACC", "IBAN", "MOBILE", "CARD_NO"}
    for key in optional_replacements:
        if key not in source_keys:
            result.pop(key, None)

    return result


def anonymize(data: dict[str, list[dict[str, Any]]]) -> None:
    employees = sorted(data["PAY2_EMPLOYEE"], key=lambda r: int(r["EMP_ID"]))
    data["PAY2_EMPLOYEE"] = [
        anonymize_employee(row, index)
        for index, row in enumerate(employees, start=1)
    ]


def validate_relationships(data: dict[str, list[dict[str, Any]]]) -> None:
    emp_ids = {row["EMP_ID"] for row in data["PAY2_EMPLOYEE"]}
    item_ids = {row["ITEM_ID"] for row in data["PAY2_ITEM_DEF"]}
    period_ids = {row["PER_ID"] for row in data["PAY2_PERIOD"]}
    run_ids = {row["RUN_ID"] for row in data["PAY2_RUN"]}
    run_line_keys = {
        (row["RUN_ID"], row["EMP_ID"]) for row in data["PAY2_RUN_LINE"]
    }
    decree_ids = {row["DEC_ID"] for row in data["PAY2_DECREE"]}

    errors: list[str] = []

    for row in data["PAY2_RUN"]:
        if row["PER_ID"] not in period_ids:
            errors.append(f"PAY2_RUN {row['RUN_ID']} references missing PER_ID.")
        prev = row.get("PREV_RUN_ID")
        if prev is not None and prev not in run_ids:
            errors.append(f"PAY2_RUN {row['RUN_ID']} references missing PREV_RUN_ID.")

    for row in data["PAY2_RUN_LINE"]:
        if row["RUN_ID"] not in run_ids or row["EMP_ID"] not in emp_ids:
            errors.append(
                f"PAY2_RUN_LINE ({row['RUN_ID']}, {row['EMP_ID']}) has a missing parent."
            )

    for row in data["PAY2_RUN_DETAIL"]:
        key = (row["RUN_ID"], row["EMP_ID"])
        if key not in run_line_keys:
            errors.append(f"PAY2_RUN_DETAIL {key} has no PAY2_RUN_LINE.")
        if row["ITEM_ID"] not in item_ids:
            errors.append(f"PAY2_RUN_DETAIL references missing ITEM_ID {row['ITEM_ID']}.")

    for row in data["PAY2_ATTENDANCE"]:
        if row["PER_ID"] not in period_ids or row["EMP_ID"] not in emp_ids:
            errors.append(
                f"PAY2_ATTENDANCE ({row['PER_ID']}, {row['EMP_ID']}) has a missing parent."
            )

    for row in data["PAY2_DECREE"]:
        if row["EMP_ID"] not in emp_ids:
            errors.append(f"PAY2_DECREE {row['DEC_ID']} references missing EMP_ID.")

    for row in data["PAY2_DECREE_LINE"]:
        if row["DEC_ID"] not in decree_ids or row["ITEM_ID"] not in item_ids:
            errors.append(
                f"PAY2_DECREE_LINE ({row['DEC_ID']}, {row['ITEM_ID']}) has a missing parent."
            )

    if errors:
        raise ValueError("\n".join(errors))


def sql_identifier(name: str) -> str:
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
        raise ValueError(f"Unsafe SQL identifier: {name}")
    return f"[{name}]"


def sql_literal(value: Any) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "1" if value else "0"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, Decimal):
        return format(value, "f")
    if isinstance(value, float):
        return format(Decimal(str(value)), "f")
    if isinstance(value, str):
        escaped = value.replace("'", "''")
        if DATETIME_RE.fullmatch(value):
            return f"CONVERT(datetime, '{escaped}', 126)"
        return f"N'{escaped}'"
    raise TypeError(f"Unsupported value type: {type(value).__name__}")


def ordered_columns(rows: Iterable[dict[str, Any]]) -> list[str]:
    result: list[str] = []
    seen: set[str] = set()
    for row in rows:
        for key in row:
            if key == "CRT":
                continue
            if key not in seen:
                result.append(key)
                seen.add(key)
    return result


def render_insert(table: str, row: dict[str, Any], columns: list[str]) -> str:
    column_sql = ", ".join(sql_identifier(name) for name in columns)
    values_sql = ", ".join(sql_literal(row.get(name)) for name in columns)
    return (
        f"INSERT INTO [dbo].{sql_identifier(table)} ({column_sql})\n"
        f"VALUES ({values_sql});"
    )


def render_table(table: str, rows: list[dict[str, Any]]) -> str:
    if not rows:
        return f"-- {table}: no rows supplied.\n"

    columns = ordered_columns(rows)
    lines: list[str] = [f"-- {table}: {len(rows)} row(s)"]

    if table in IDENTITY_COLUMNS:
        lines.append(f"SET IDENTITY_INSERT [dbo].{sql_identifier(table)} ON;")

    for row in rows:
        lines.append(render_insert(table, row, columns))

    if table in IDENTITY_COLUMNS:
        lines.append(f"SET IDENTITY_INSERT [dbo].{sql_identifier(table)} OFF;")

    lines.append("GO")
    return "\n".join(lines) + "\n"


def build_support_rows(data: dict[str, list[dict[str, Any]]]) -> dict[str, list[dict[str, Any]]]:
    workshop_ids = sorted(
        {
            int(row["WS_ID"])
            for table in ("PAY2_EMPLOYEE", "PAY2_PERIOD", "PAY2_DECREE")
            for row in data[table]
            if row.get("WS_ID") is not None
        }
    )
    workshops = [
        {
            "WS_ID": ws_id,
            "WS_CODE": f"TEST-{ws_id}",
            "WS_NAME": f"کارگاه آزمایشی {ws_id}",
            "INS_MODE": 1,
            "IS_ACTIVE": True,
        }
        for ws_id in workshop_ids
    ]

    job_ids = sorted(
        {
            int(row["JOB_ID"])
            for row in data["PAY2_EMPLOYEE"]
            if row.get("JOB_ID") is not None
        }
    )
    jobs = [
        {
            "JOB_ID": job_id,
            "JOB_CODE": f"TEST-{job_id}",
            "JOB_NAME": f"شغل آزمایشی {index:02d}",
            "IS_ACTIVE": True,
        }
        for index, job_id in enumerate(job_ids, start=1)
    ]

    template_ids = sorted(
        {
            int(row["TMPL_ID"])
            for row in data["PAY2_DECREE"]
            if row.get("TMPL_ID") is not None
        }
    )
    templates = [
        {
            "TMPL_ID": template_id,
            "TMPL_CODE": f"TEST-TMPL-{template_id}",
            "TMPL_NAME": f"قالب حکم آزمایشی {template_id}",
            "WS_ID": workshop_ids[0] if workshop_ids else None,
            "IS_ACTIVE": True,
        }
        for template_id in template_ids
    ]

    return {
        "PAY2_WORKSHOP": workshops,
        "PAY2_JOB": jobs,
        "PAY2_ITEM_TEMPLATE": templates,
    }


def generate_sql(data: dict[str, list[dict[str, Any]]]) -> str:
    support = build_support_rows(data)

    counts = {name: len(rows) for name, rows in data.items()}
    counts.update({name: len(rows) for name, rows in support.items()})

    header = f'''-- =====================================================================
-- Safir PAY2 anonymized E2E seed
-- Generated from Data.txt. Do not use against production.
-- Source row counts: {json.dumps(counts, ensure_ascii=False, sort_keys=True)}
-- =====================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'SafirTest%'
    THROW 51000, 'Safety stop: PAY2 seed may run only in a SafirTest* database.', 1;
GO

-- All transactional target tables must be empty because this seed is
-- intended for a freshly recreated Jules database.
IF EXISTS (SELECT 1 FROM [dbo].[PAY2_EMPLOYEE])
   OR EXISTS (SELECT 1 FROM [dbo].[PAY2_PERIOD])
   OR EXISTS (SELECT 1 FROM [dbo].[PAY2_DECREE])
   OR EXISTS (SELECT 1 FROM [dbo].[PAY2_RUN])
   OR EXISTS (SELECT 1 FROM [dbo].[PAY2_ATTENDANCE])
    THROW 51001, 'Safety stop: transactional PAY2 tables are not empty.', 1;
GO

BEGIN TRANSACTION;

-- schema.sql already inserts default rows in these three tables.
DELETE FROM [dbo].[PAY2_CONFIG];
DELETE FROM [dbo].[PAY2_TAX_BRACKET];
DELETE FROM [dbo].[PAY2_ITEM_DEF];
GO

'''

    table_order = [
        ("PAY2_WORKSHOP", support["PAY2_WORKSHOP"]),
        ("PAY2_JOB", support["PAY2_JOB"]),
        ("PAY2_ITEM_DEF", sorted(data["PAY2_ITEM_DEF"], key=lambda r: int(r["ITEM_ID"]))),
        ("PAY2_ITEM_TEMPLATE", support["PAY2_ITEM_TEMPLATE"]),
        ("PAY2_CONFIG", sorted(data["PAY2_CONFIG"], key=lambda r: str(r["CFG_KEY"]))),
        ("PAY2_TAX_BRACKET", sorted(data["PAY2_TAX_BRACKET"], key=lambda r: int(r["BRK_ID"]))),
        ("PAY2_EMPLOYEE", sorted(data["PAY2_EMPLOYEE"], key=lambda r: int(r["EMP_ID"]))),
        ("PAY2_PERIOD", sorted(data["PAY2_PERIOD"], key=lambda r: int(r["PER_ID"]))),
        ("PAY2_ATTENDANCE", sorted(data["PAY2_ATTENDANCE"], key=lambda r: (int(r["PER_ID"]), int(r["EMP_ID"])))),
        ("PAY2_DECREE", sorted(data["PAY2_DECREE"], key=lambda r: int(r["DEC_ID"]))),
        ("PAY2_DECREE_LINE", sorted(data["PAY2_DECREE_LINE"], key=lambda r: (int(r["DEC_ID"]), int(r["ITEM_ID"])))),
        ("PAY2_RUN", sorted(data["PAY2_RUN"], key=lambda r: int(r["RUN_ID"]))),
        ("PAY2_RUN_LINE", sorted(data["PAY2_RUN_LINE"], key=lambda r: (int(r["RUN_ID"]), int(r["EMP_ID"])))),
        ("PAY2_RUN_DETAIL", sorted(data["PAY2_RUN_DETAIL"], key=lambda r: (int(r["RUN_ID"]), int(r["EMP_ID"]), int(r["ITEM_ID"])))),
    ]

    body = "\n".join(render_table(table, rows) for table, rows in table_order)

    expected = {
        "PAY2_CONFIG": len(data["PAY2_CONFIG"]),
        "PAY2_TAX_BRACKET": len(data["PAY2_TAX_BRACKET"]),
        "PAY2_PERIOD": len(data["PAY2_PERIOD"]),
        "PAY2_RUN": len(data["PAY2_RUN"]),
        "PAY2_RUN_LINE": len(data["PAY2_RUN_LINE"]),
        "PAY2_RUN_DETAIL": len(data["PAY2_RUN_DETAIL"]),
        "PAY2_EMPLOYEE": len(data["PAY2_EMPLOYEE"]),
        "PAY2_ITEM_DEF": len(data["PAY2_ITEM_DEF"]),
        "PAY2_ATTENDANCE": len(data["PAY2_ATTENDANCE"]),
        "PAY2_DECREE": len(data["PAY2_DECREE"]),
        "PAY2_DECREE_LINE": len(data["PAY2_DECREE_LINE"]),
    }

    checks = []
    for table, count in expected.items():
        checks.append(
            f"IF (SELECT COUNT_BIG(*) FROM [dbo].{sql_identifier(table)}) <> {count}\n"
            f"    THROW 51100, 'Seed verification failed for {table}.', 1;"
        )

    footer = "\n".join(checks) + r'''

DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;

SELECT N'PAY2_CONFIG' AS [TABLE_NAME], COUNT_BIG(*) AS [ROW_COUNT] FROM [dbo].[PAY2_CONFIG]
UNION ALL SELECT N'PAY2_TAX_BRACKET', COUNT_BIG(*) FROM [dbo].[PAY2_TAX_BRACKET]
UNION ALL SELECT N'PAY2_PERIOD', COUNT_BIG(*) FROM [dbo].[PAY2_PERIOD]
UNION ALL SELECT N'PAY2_RUN', COUNT_BIG(*) FROM [dbo].[PAY2_RUN]
UNION ALL SELECT N'PAY2_RUN_LINE', COUNT_BIG(*) FROM [dbo].[PAY2_RUN_LINE]
UNION ALL SELECT N'PAY2_RUN_DETAIL', COUNT_BIG(*) FROM [dbo].[PAY2_RUN_DETAIL]
UNION ALL SELECT N'PAY2_EMPLOYEE', COUNT_BIG(*) FROM [dbo].[PAY2_EMPLOYEE]
UNION ALL SELECT N'PAY2_ITEM_DEF', COUNT_BIG(*) FROM [dbo].[PAY2_ITEM_DEF]
UNION ALL SELECT N'PAY2_ATTENDANCE', COUNT_BIG(*) FROM [dbo].[PAY2_ATTENDANCE]
UNION ALL SELECT N'PAY2_DECREE', COUNT_BIG(*) FROM [dbo].[PAY2_DECREE]
UNION ALL SELECT N'PAY2_DECREE_LINE', COUNT_BIG(*) FROM [dbo].[PAY2_DECREE_LINE];

COMMIT TRANSACTION;
PRINT N'PAY2 anonymized E2E seed completed successfully.';
GO
'''
    return header + body + "\n" + footer


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: generate_pay2_seed.py <Data.txt> <pay2_seed.sql>", file=sys.stderr)
        return 2

    input_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    raw_text = input_path.read_text(encoding="utf-8-sig", errors="strict")
    data = extract_payload(raw_text)
    anonymize(data)
    validate_relationships(data)

    sql = generate_sql(data)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(sql, encoding="utf-8", newline="\n")

    print(f"Generated {output_path} ({output_path.stat().st_size:,} bytes).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
