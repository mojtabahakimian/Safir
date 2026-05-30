IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[BugReports]') AND name = 'UserNote')
BEGIN
    ALTER TABLE [dbo].[BugReports] ADD [UserNote] NVARCHAR(MAX) NULL;
END
GO
