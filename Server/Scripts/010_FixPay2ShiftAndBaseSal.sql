-- Migration 010: Fix configurations for Shift Allowance and Nominal Base Salary
-- Issue 1: Shift Allowance should not be subject to insurance.
UPDATE [dbo].[PAY2_ITEM_DEF]
SET [INS_SUBJECT] = 0
WHERE [ITEM_CODE] = 'SHIFT';

-- Issue 3/4: Nominal Base Salary (BASE_SAL) should evaluate based on Nominal Days (DAYS) instead of Official Days (DAYSB).
-- Issue 3/4: Nominal Base Salary (BASE_SAL) and general allowances should evaluate based on Nominal Days (DAYS) instead of Official Days (DAYSB).
-- PAY_BASE_DAYS = 1 means it uses @DAYS (Nominal).
UPDATE [dbo].[PAY2_ITEM_DEF]
SET [PAY_BASE_DAYS] = 1
WHERE [ITEM_CODE] IN ('BASE_SAL', 'HOME', 'CHILDREN', 'FAMILY_ALLOW', 'ATTRACT', 'GROCERY', 'HARD_COND', 'OTHER_FIX');

GO
