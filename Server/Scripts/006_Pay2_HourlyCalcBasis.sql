-- ============================================================================
-- پشتیبانی از مبنای محاسبه «ساعتی» (مقدار 3) در حقوق و دستمزد (Pay2)
--
-- مقادیر مبنای محاسبه:
--   1 = روزانه   2 = ماهیانه   3 = ساعتی (جدید)
--
-- این اسکریپت هر CHECK CONSTRAINT روی ستون‌های CALC_BASIS و BASIS_OV را که
-- مقدار 3 را مجاز نمی‌داند حذف می‌کند تا UI بتواند مبنای ساعتی را ذخیره کند.
--
-- ⚠️ توجه (اقدام دستی): رویه‌ی ذخیره‌شده SP_PAY2_CALC_RUN در دیتابیس باید
-- برای مبنای 3 (ساعتی) به‌روزرسانی شود تا مبلغ آیتم به ازای هر «ساعت» کارکرد
-- (به‌خصوص اضافه‌کار عادی OT_NORMAL و اضافه‌کار تعطیل OT_HOLIDAY از ستون‌های
-- OT_NORMAL_H و OT_HOLIDAY_H جدول PAY2_ATTENDANCE) ضرب شود:
--   مبلغ نهایی = AMOUNT × ساعت کارکرد مربوطه
-- ============================================================================

DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(cc.parent_object_id))
            + N'.' + QUOTENAME(OBJECT_NAME(cc.parent_object_id))
            + N' DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) IN ('PAY2_ITEM_DEF', 'PAY2_DECREE_LINE', 'PAY2_OVERRIDE', 'PAY2_ITEM_TMPL_LINE')
  AND (cc.definition LIKE '%CALC_BASIS%' OR cc.definition LIKE '%BASIS_OV%')
  AND cc.definition NOT LIKE '%(3)%';

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO
