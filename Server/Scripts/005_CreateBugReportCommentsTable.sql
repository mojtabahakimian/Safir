IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BugReportComments' and xtype='U')
BEGIN
    CREATE TABLE BugReportComments (
        Id INT PRIMARY KEY IDENTITY(1,1),
        BugReportId INT NOT NULL,
        UserId NVARCHAR(100) NOT NULL,
        UserName NVARCHAR(200) NULL,
        IsAdmin BIT NOT NULL DEFAULT 0,
        CommentText NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME NOT NULL DEFAULT (GETDATE()),
        FOREIGN KEY (BugReportId) REFERENCES BugReports(Id) ON DELETE CASCADE
    );
END
GO
