DECLARE @Username NVARCHAR(50) = N'QQKa_Q^'; -- Encoded 'e2e_user'
DECLARE @Password NVARCHAR(50) = N'NNN[([UfWiimehZNNN'; -- Encoded 'e2e_password'
DECLARE @UserId INT = 1;

-- Generate IDD by finding max + 1 if not exists
IF NOT EXISTS (SELECT 1 FROM dbo.SALA_DTL WHERE SAL_NAME = @Username)
BEGIN
    SELECT @UserId = ISNULL(MAX(IDD), 0) + 1 FROM dbo.SALA_DTL;

    INSERT INTO dbo.SALA_DTL (SAL_NAME, PSAL_NAME, GRSAL, ENABL, IDD, CRT, erjabe)
    VALUES (@Username, @Password, 1, 0, @UserId, GETDATE(), 1);
END
ELSE
BEGIN
    SELECT @UserId = IDD FROM dbo.SALA_DTL WHERE SAL_NAME = @Username;
END

-- Ensure SAL_CHEK has access
IF NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK WHERE USERCO = @UserId)
BEGIN
    -- Just give access to a few dummy objects so the login process doesn't fail on any initial auth checks
    INSERT INTO dbo.SAL_CHEK (USERCO, OBJECT, RUN, SEE, INP, UPD, DEL)
    VALUES (@UserId, 1, 1, 1, 1, 1, 1);
END
