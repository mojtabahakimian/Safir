-- Migration 008: تغییر نوع ستون AMOUNT در PAY2_DECREE_LINE و DEF_AMOUNT در PAY2_ITEM_TMPL_LINE از BIGINT به DECIMAL(18,2)
-- هدف: پشتیبانی از مقادیر اعشاری برای فیلد حق شیفت (PCT) در حکم و قالب‌های حکم
-- این اسکریپت idempotent است: فقط اگر ستون هنوز BIGINT باشد اجرا می‌شود

-- بخش الف: PAY2_DECREE_LINE.AMOUNT
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

-- بخش ب: PAY2_ITEM_TMPL_LINE.DEF_AMOUNT
-- دلیل: INSERT INTO PAY2_DECREE_LINE ... SELECT DEF_AMOUNT FROM PAY2_ITEM_TMPL_LINE
-- اگر DEF_AMOUNT همچنان BIGINT باشد، مقادیر اعشاری (مثل 7.5 برای حق شیفت) هنگام کپی به حکم گرد می‌شوند
IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    JOIN sys.objects o ON c.object_id = o.object_id
    WHERE o.name = 'PAY2_ITEM_TMPL_LINE'
      AND c.name = 'DEF_AMOUNT'
      AND t.name = 'bigint'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_TL_AMT')
        ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] DROP CONSTRAINT [DF_TL_AMT];

    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE]
        ALTER COLUMN [DEF_AMOUNT] DECIMAL(18,2) NOT NULL;

    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE]
        ADD CONSTRAINT [DF_TL_AMT] DEFAULT(0) FOR [DEF_AMOUNT];
END
GO
