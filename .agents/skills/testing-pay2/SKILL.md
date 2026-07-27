---
name: testing-pay2
description: How to run and test the Safir Blazor app (PAY2 payroll module) end-to-end on Linux, including bootstrapping a throwaway SQL Server database, creating a login user, and reaching the payroll screens.
---

# Testing the Safir app (PAY2 payroll) locally

## Run the app
- .NET 8. `dotnet build Safir.sln -c Release` works on Linux; the server also runs on Linux
  (QuestPDF font registration logs an error about `Fonts/IRANYekanFN.ttf` — harmless for UI testing).
- Start with: `cd Server && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 dotnet run --no-launch-profile -c Release`
- Connection string goes in `Server/appsettings.Development.json` under `ConnectionStrings:DefaultConnection`
  (the committed `appsettings.json` points at a Windows-only host, so override it).

## Database
- No SQL Server is available by default. Spin one up:
  `docker run -d --name sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<pw>' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest`
  then `CREATE DATABASE NEWPOODR1405`.
- **Schema is NOT migrated by the app.** `Server/Info/ScriptSqly.cs` holds the whole PAY2 DDL + stored
  procedures as C# verbatim strings and is executed by a separate external updater tool. To bootstrap,
  extract every `@"..."` literal from the top ~3240 lines of `ScriptSqly.cs` (un-escape `""` → `"`),
  join them with `GO`, and run through `sqlcmd -i`. Expect a couple of non-fatal errors
  (a filtered index needing `SET QUOTED_IDENTIFIER ON`, and a syntax error in `SP_PAY2_GEN_DEED`);
  they do not block the attendance/employee screens. Incremental scripts also live in `Server/Scripts/*.sql`.
- Legacy tables (e.g. `SALA_DTL` for users) are not in that script — create the minimum yourself.

## Login
- `POST /api/Auth/login` accepts a **bypass password `442100200`** for any enabled user, so you only need a
  user row, not a real password.
- Usernames are stored obfuscated: `CL_METHODS.DECODEUN` adds 20 to each cp1256 byte, so store
  `bytes(username) - 20`. e.g. username `admin` → stored `SAL_NAME = 'MPYUZ'`.
  `PSAL_NAME` just needs ≥6 chars (DECODEPS strips 3 chars from each end).
- Minimal table:
  `CREATE TABLE SALA_DTL (IDD INT IDENTITY PRIMARY KEY, SAL_NAME NVARCHAR(50), PSAL_NAME NVARCHAR(50), GRSAL INT, HES NVARCHAR(50) NULL, PORID INT NULL, erjabe INT NULL, ENABL TINYINT, EMZA VARBINARY(MAX) NULL, menup INT NULL);`
  and insert with `ENABL = 0` (0 means enabled).

## Reaching the PAY2 screens
- Route `/salary/manage`; left sidebar tabs: داشبورد / مدیریت کارگاه‌ها / پرسنل و احکام / کارکرد پرسنل / …
- Seed at least one `PAY2_WORKSHOP` row and `PAY2_EMPLOYEE` rows (`WS_ID`, `EMP_CODE`, `FIRST_NAME`,
  `LAST_NAME`, `HIRE_DATE` are the required non-defaulted columns) or the grids are empty.
- Attendance grid: «کارکرد پرسنل» → «ایجاد دوره جدید» (period format `YYYYMM00`) → «ورود به لیست» →
  Excel-like grid → «ذخیره کارکرد» to save.
- The grid is horizontally scrollable and RTL: scroll **left** to reach the money columns
  (پاداش/راندمان، حق ایاب‌وذهاب، سایر کسورات) and the عملیات column.

## Input-guard quirks (Pay2NumericInput / pay2-input-guards.js)
- Money fields use live thousands separators; hours fields accept `H:MM` and store decimal hours (3:30 → 3.50).
- The `+`/`-` "add 3 zeros / 2 zeros" shortcut listens for `e.key === '+'|'-'`, `'Add'|'Subtract'`,
  or `code === 'NumpadAdd'|'NumpadSubtract'`. On the default X layout in test VMs the top-row `+`
  may be delivered as `code: IntlRo` with an empty key — **use xdotool `KP_Add` / `KP_Subtract`** instead,
  otherwise the shortcut looks broken when it is not.
- `pay2-input-guards.js` is imported with a `?v=` cache-buster; if guard behaviour looks stale, hard-reload.

## Devin Secrets Needed
- None. Everything above uses a locally created SQL Server container and the built-in login bypass password.
