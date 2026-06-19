-- Migration 008: تغییر نوع ستون AMOUNT در PAY2_DECREE_LINE از BIGINT به DECIMAL(18,2)
-- هدف: پشتیبانی از مقادیر اعشاری برای فیلد حق شیفت (PCT) در حکم
-- این اسکریپت idempotent است: فقط اگر ستون هنوز BIGINT باشد اجرا می‌شود

IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    JOIN sys.objects o ON c.object_id = o.object_id
    WHERE o.name = 'PAY2_DECREE_LINE'
      AND c.name = 'AMOUNT'
      AND t.name = 'bigint'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_DL_AMT' AND parent_object_id = OBJECT_ID('dbo.PAY2_DECREE_LINE'))
        ALTER TABLE [dbo].[PAY2_DECREE_LINE] DROP CONSTRAINT [DF_DL_AMT];

    ALTER TABLE [dbo].[PAY2_DECREE_LINE]
        ALTER COLUMN [AMOUNT] DECIMAL(18,2) NOT NULL;

    ALTER TABLE [dbo].[PAY2_DECREE_LINE]
        ADD CONSTRAINT [DF_DL_AMT] DEFAULT(0) FOR [AMOUNT];
END
GO
