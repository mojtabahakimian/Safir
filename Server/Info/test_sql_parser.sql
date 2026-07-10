-- Test Script for SQL Account Functions

-- Test 1: Resolve Full Path
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT('213-1', '112-2-150') AS ShouldBe_213_1_150;
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT('213-1', '112-2-150-10') AS ShouldBe_213_1_150_10;

-- Test 2: Resolve Leaf (Legacy)
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT('213-1', '150') AS ShouldBe_213_1_150;

-- Test 3: Normalization
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT(' 213 - ۱ ', ' ۱۱۲ - ۲ - ۱۵۰ ') AS ShouldBe_213_1_150;
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT('213-1', '150-10-20-30-40') AS ShouldBeNull_TooManyLevels;

-- Test 4: Invalid inputs
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT('213-1', 'abc') AS ShouldBeNull_NonNumeric;
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT('213-1', '112--150') AS ShouldBeNull_EmptySegment;
SELECT dbo.FN_PAY2_RESOLVE_ACCOUNT('213', '150') AS ShouldBeNull_TargetTooShort;
