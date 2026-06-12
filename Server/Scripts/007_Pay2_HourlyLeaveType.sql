-- ============================================================================
-- پشتیبانی از نوع مرخصی «ساعتی» (مقدار 6) در جدول PAY2_LEAVE
--
-- انواع مرخصی:
--   1=استحقاقی  2=استعلاجی  3=بدون حقوق  4=زایمان  5=مأموریت  6=ساعتی (جدید)
--
-- اگر CHECK CONSTRAINT روی LEV_TYPE مقدار 6 را مجاز نمی‌داند، حذف می‌شود.
-- سقف مرخصی ساعتی (3 ساعت و 20 دقیقه) در سمت سرور (Pay2EmployeesController)
-- و سمت کلاینت اعتبارسنجی می‌شود.
-- ============================================================================

DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(cc.parent_object_id))
            + N'.' + QUOTENAME(OBJECT_NAME(cc.parent_object_id))
            + N' DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) = 'PAY2_LEAVE'
  AND cc.definition LIKE '%LEV_TYPE%'
  AND cc.definition NOT LIKE '%(6)%';

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO
