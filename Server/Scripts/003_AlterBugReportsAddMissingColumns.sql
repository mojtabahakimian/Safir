IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BugReports]') AND name = 'IsBlocking')
BEGIN
    ALTER TABLE [dbo].[BugReports] ADD [IsBlocking] BIT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BugReports]') AND name = 'TestedOnAnotherDevice')
BEGIN
    ALTER TABLE [dbo].[BugReports] ADD [TestedOnAnotherDevice] BIT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BugReports]') AND name = 'HasRecentChanges')
BEGIN
    ALTER TABLE [dbo].[BugReports] ADD [HasRecentChanges] BIT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BugReports]') AND name = 'AdminNote')
BEGIN
    ALTER TABLE [dbo].[BugReports] ADD [AdminNote] NVARCHAR(MAX) NULL;
END
GO
