-- Migration 008: تغییر نوع ستون AMOUNT در PAY2_DECREE_LINE از BIGINT به DECIMAL(18,2)
-- هدف: پشتیبانی از مقادیر اعشاری برای فیلد حق شیفت (PCT) در حکم

-- ابتدا constraint پیش‌فرض را حذف می‌کنیم، سپس ستون را تغییر می‌دهیم، و constraint را دوباره اضافه می‌کنیم
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_DL_AMT')
    ALTER TABLE [dbo].[PAY2_DECREE_LINE] DROP CONSTRAINT [DF_DL_AMT];
GO

ALTER TABLE [dbo].[PAY2_DECREE_LINE]
    ALTER COLUMN [AMOUNT] DECIMAL(18,2) NOT NULL;
GO

ALTER TABLE [dbo].[PAY2_DECREE_LINE]
    ADD CONSTRAINT [DF_DL_AMT] DEFAULT(0) FOR [AMOUNT];
GO
