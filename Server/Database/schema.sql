USE [SafirTestDb]
GO
/****** Object:  UserDefinedFunction [dbo].[fn_JalaliIntToGregorianDate]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE FUNCTION [dbo].[fn_JalaliIntToGregorianDate] (@JalaliInt BIGINT)
RETURNS DATETIME
AS
BEGIN
    DECLARE
        @jy INT, @jm INT, @jd INT,
        @gy INT, @gm INT, @gd INT,
        @j_day_no INT, @g_day_no INT,
        @leap INT,
        @i INT,
        @tmp INT;

    IF @JalaliInt IS NULL OR @JalaliInt = 0
        RETURN NULL;

    -- Parse yyyymmdd
    SET @jy = CAST(@JalaliInt / 10000 AS INT);
    SET @jm = CAST((@JalaliInt / 100) % 100 AS INT);
    SET @jd = CAST(@JalaliInt % 100 AS INT);

    -- Basic validation
    IF @jy < 1200 OR @jy > 1600 OR @jm < 1 OR @jm > 12 OR @jd < 1 OR @jd > 31
        RETURN NULL;

    -- Convert Jalali to day number
    SET @jy = @jy - 979;
    SET @jm = @jm - 1;
    SET @jd = @jd - 1;

    SET @j_day_no = 365 * @jy + (@jy / 33) * 8 + ((@jy % 33 + 3) / 4);

    SET @i = 0;
    WHILE @i < @jm
    BEGIN
        SET @j_day_no = @j_day_no +
            CASE
                WHEN @i < 6 THEN 31
                WHEN @i < 11 THEN 30
                ELSE 29
            END;
        SET @i = @i + 1;
    END

    SET @j_day_no = @j_day_no + @jd;

    -- Jalali day number to Gregorian day number
    SET @g_day_no = @j_day_no + 79;

    SET @gy = 1600 + 400 * (@g_day_no / 146097);
    SET @g_day_no = @g_day_no % 146097;

    SET @leap = 1;
    IF @g_day_no >= 36525
    BEGIN
        SET @g_day_no = @g_day_no - 1;
        SET @gy = @gy + 100 * (@g_day_no / 36524);
        SET @g_day_no = @g_day_no % 36524;

        IF @g_day_no >= 365
            SET @g_day_no = @g_day_no + 1;
        ELSE
            SET @leap = 0;
    END

    SET @gy = @gy + 4 * (@g_day_no / 1461);
    SET @g_day_no = @g_day_no % 1461;

    IF @g_day_no >= 366
    BEGIN
        SET @leap = 0;
        SET @g_day_no = @g_day_no - 1;
        SET @gy = @gy + (@g_day_no / 365);
        SET @g_day_no = @g_day_no % 365;
    END

    -- Compute Gregorian month/day
    DECLARE @g_days_in_month TABLE (m INT PRIMARY KEY, d INT);
    INSERT INTO @g_days_in_month (m, d)
    VALUES
      (1,31),(2,28),(3,31),(4,30),(5,31),(6,30),
      (7,31),(8,31),(9,30),(10,31),(11,30),(12,31);

    IF @leap = 1
        UPDATE @g_days_in_month SET d = 29 WHERE m = 2;

    SET @gm = 1;
    WHILE @gm <= 12
    BEGIN
        SELECT @tmp = d FROM @g_days_in_month WHERE m = @gm;
        IF @g_day_no < @tmp BREAK;
        SET @g_day_no = @g_day_no - @tmp;
        SET @gm = @gm + 1;
    END

    SET @gd = @g_day_no + 1;

    -- Return as datetime (SQL 2008: no DATEFROMPARTS)
    RETURN CONVERT(DATETIME,
        CAST(@gy AS VARCHAR(4)) + '-' +
        RIGHT('00' + CAST(@gm AS VARCHAR(2)), 2) + '-' +
        RIGHT('00' + CAST(@gd AS VARCHAR(2)), 2),
        120
    );
END
GO
/****** Object:  UserDefinedFunction [dbo].[FN_PAY2_CALC_TAX]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- تابع محاسبه مالیات پلکانی
CREATE   FUNCTION [dbo].[FN_PAY2_CALC_TAX]
    (@ANNUAL_BASE BIGINT, @TAX_YEAR SMALLINT)
RETURNS BIGINT
AS
BEGIN
    DECLARE @TAX        BIGINT      = 0;
    DECLARE @PREV_LIMIT BIGINT      = 0;
    DECLARE @RATE       DECIMAL(5,2);
    DECLARE @LIMIT      BIGINT;
    DECLARE @FIXED      BIGINT;

    DECLARE cur CURSOR FOR
        SELECT UPPER_LIMIT, RATE_PCT, FIXED_TAX
        FROM PAY2_TAX_BRACKET
        WHERE TAX_YEAR = @TAX_YEAR
        ORDER BY SORT_ORDER;

    OPEN cur;
    FETCH NEXT FROM cur INTO @LIMIT, @RATE, @FIXED;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @ANNUAL_BASE <= @LIMIT
        BEGIN
            SET @TAX = @FIXED + CAST((@ANNUAL_BASE - @PREV_LIMIT) * @RATE / 100 AS BIGINT);
            BREAK;
        END;
        SET @PREV_LIMIT = @LIMIT;
        FETCH NEXT FROM cur INTO @LIMIT, @RATE, @FIXED;
    END;

    -- اگر از همه پله‌ها بیشتر بود: پله آخر اعمال شود
    IF @@FETCH_STATUS <> 0 AND @TAX = 0
        SET @TAX = @FIXED + CAST((@ANNUAL_BASE - @PREV_LIMIT) * @RATE / 100 AS BIGINT);

    CLOSE cur;
    DEALLOCATE cur;

    RETURN @TAX;  -- مالیات سالانه — موتور ÷12 می‌کند
END;
GO
/****** Object:  UserDefinedFunction [dbo].[FN_PAY2_MONTH]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── تابع کمکی: تبدیل تاریخ شمسی به ماه (مشابه Umonth سیستم قدیم) ─

CREATE   FUNCTION [dbo].[FN_PAY2_MONTH](@DATE BIGINT)
RETURNS INT
AS
BEGIN
    RETURN @DATE / 100  -- YYYYMM
END;
GO
/****** Object:  Table [dbo].[TDETA_HES2]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TDETA_HES2](
	[N_KOL] [int] NOT NULL,
	[NUMBER] [int] NOT NULL,
	[TNUMBER] [int] NOT NULL,
	[TNUMBER2] [int] NOT NULL,
	[NAME] [nvarchar](100) NULL,
	[TOZIH] [nvarchar](255) NULL,
	[BED_BES] [float] NULL,
	[ADDRESS] [nvarchar](100) NULL,
	[TEL] [nvarchar](50) NULL,
	[CODE_E] [nvarchar](20) NULL,
	[IDD] [int] IDENTITY(1,1) NOT NULL,
	[ECODE] [nvarchar](20) NULL,
	[PCODE] [nvarchar](10) NULL,
	[IYALAT] [nvarchar](20) NULL,
	[CITY] [nvarchar](20) NULL,
	[MCODEM] [nvarchar](20) NULL,
	[CUST_COD] [int] NULL,
	[MOBILE] [nvarchar](55) NULL,
	[ROUTE_NAME] [nvarchar](50) NULL,
	[Longitude] [float] NULL,
	[Latitude] [float] NULL,
	[OSTANID] [int] NULL,
	[SHAHRID] [int] NULL,
	[USERCO] [int] NULL,
	[USER_NAME] [nvarchar](50) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[tob] [int] NULL,
 CONSTRAINT [PK__TDETA_HES2__51A6D819] PRIMARY KEY CLUSTERED 
(
	[N_KOL] ASC,
	[NUMBER] ASC,
	[TNUMBER] ASC,
	[TNUMBER2] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TDETA_HES3]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TDETA_HES3](
	[N_KOL] [int] NOT NULL,
	[NUMBER] [int] NOT NULL,
	[TNUMBER] [int] NOT NULL,
	[TNUMBER2] [int] NOT NULL,
	[TNUMBER3] [int] NOT NULL,
	[NAME] [nvarchar](100) NULL,
	[TOZIH] [nvarchar](255) NULL,
	[BED_BES] [float] NULL,
	[ADDRESS] [nvarchar](100) NULL,
	[TEL] [nvarchar](20) NULL,
	[CODE_E] [nvarchar](20) NULL,
	[IDD] [int] IDENTITY(1,1) NOT NULL,
	[ECODE] [nvarchar](20) NULL,
	[PCODE] [nvarchar](10) NULL,
	[IYALAT] [nvarchar](20) NULL,
	[CITY] [nvarchar](20) NULL,
	[MCODEM] [nvarchar](20) NULL,
	[CUST_COD] [int] NULL,
	[MOBILE] [nvarchar](55) NULL,
	[ROUTE_NAME] [nvarchar](50) NULL,
	[Longitude] [float] NULL,
	[Latitude] [float] NULL,
	[OSTANID] [int] NULL,
	[SHAHRID] [int] NULL,
	[USERCO] [int] NULL,
	[USER_NAME] [nvarchar](50) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[tob] [int] NULL,
 CONSTRAINT [PK__TDETA_HES3__51A6D819] PRIMARY KEY CLUSTERED 
(
	[N_KOL] ASC,
	[NUMBER] ASC,
	[TNUMBER] ASC,
	[TNUMBER2] ASC,
	[TNUMBER3] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TDETA_HES4]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TDETA_HES4](
	[N_KOL] [int] NOT NULL,
	[NUMBER] [int] NOT NULL,
	[TNUMBER] [int] NOT NULL,
	[TNUMBER2] [int] NOT NULL,
	[TNUMBER3] [int] NOT NULL,
	[TNUMBER4] [int] NOT NULL,
	[NAME] [nvarchar](100) NULL,
	[TOZIH] [nvarchar](255) NULL,
	[BED_BES] [float] NULL,
	[ADDRESS] [nvarchar](100) NULL,
	[TEL] [nvarchar](20) NULL,
	[CODE_E] [nvarchar](20) NULL,
	[IDD] [int] IDENTITY(1,1) NOT NULL,
	[ECODE] [nvarchar](20) NULL,
	[PCODE] [nvarchar](10) NULL,
	[IYALAT] [nvarchar](20) NULL,
	[CITY] [nvarchar](20) NULL,
	[MCODEM] [nvarchar](20) NULL,
	[CUST_COD] [int] NULL,
	[MOBILE] [nvarchar](55) NULL,
	[ROUTE_NAME] [nvarchar](50) NULL,
	[Longitude] [float] NULL,
	[Latitude] [float] NULL,
	[OSTANID] [int] NULL,
	[SHAHRID] [int] NULL,
	[USERCO] [int] NULL,
	[USER_NAME] [nvarchar](50) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[tob] [int] NULL,
 CONSTRAINT [PK__TDETA_HES4__51A6] PRIMARY KEY CLUSTERED 
(
	[N_KOL] ASC,
	[NUMBER] ASC,
	[TNUMBER] ASC,
	[TNUMBER2] ASC,
	[TNUMBER3] ASC,
	[TNUMBER4] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TDETA_HES]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TDETA_HES](
	[N_KOL] [int] NOT NULL,
	[NUMBER] [int] NOT NULL,
	[TNUMBER] [int] NOT NULL,
	[NAME] [nvarchar](100) NULL,
	[TOZIH] [nvarchar](255) NULL,
	[BED_BES] [float] NULL,
	[ADDRESS] [nvarchar](100) NULL,
	[TEL] [nvarchar](50) NULL,
	[CODE_E] [nvarchar](20) NULL,
	[IDD] [int] IDENTITY(1,1) NOT NULL,
	[ECODE] [nvarchar](20) NULL,
	[PCODE] [nvarchar](10) NULL,
	[IYALAT] [nvarchar](20) NULL,
	[CITY] [nvarchar](20) NULL,
	[MCODEM] [nvarchar](20) NULL,
	[CUST_COD] [int] NULL,
	[MOBILE] [nvarchar](55) NULL,
	[ROUTE_NAME] [nvarchar](50) NULL,
	[Longitude] [float] NULL,
	[Latitude] [float] NULL,
	[OSTANID] [int] NULL,
	[SHAHRID] [int] NULL,
	[USERCO] [int] NULL,
	[USER_NAME] [nvarchar](50) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[tob] [int] NULL,
 CONSTRAINT [PK_TDETA_HES] PRIMARY KEY CLUSTERED 
(
	[N_KOL] ASC,
	[NUMBER] ASC,
	[TNUMBER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [IX_TDETA_HES_NAME] UNIQUE NONCLUSTERED 
(
	[N_KOL] ASC,
	[NUMBER] ASC,
	[NAME] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[CUST_HESAB]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE VIEW [dbo].[CUST_HESAB] AS SELECT     RTRIM(CAST(N_KOL AS NVARCHAR)) + '-' + RTRIM(CAST(NUMBER AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER AS NVARCHAR)) AS hes, NAME, ADDRESS, TEL,                      CODE_E , ECODE, PCODE, IYALAT, CITY, MCODEM, TOZIH, CUST_COD, MOBILE, Longitude, Latitude, ROUTE_NAME, OSTANID, SHAHRID, tob FROM dbo.TDETA_HES UNION SELECT     RTRIM(CAST(N_KOL AS NVARCHAR)) + '-' + RTRIM(CAST(NUMBER AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER AS NVARCHAR))                       + '-' + RTRIM(CAST(TNUMBER2 AS NVARCHAR)) AS hes, NAME, ADDRESS, TEL, CODE_E, ECODE, PCODE, IYALAT, CITY, MCODEM, TOZIH, CUST_COD, MOBILE,                      Longitude , Latitude, ROUTE_NAME, OSTANID, SHAHRID, tob  FROM dbo.TDETA_HES2  UNION  SELECT     RTRIM(CAST(N_KOL AS NVARCHAR)) + '-' + RTRIM(CAST(NUMBER AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER2 AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER3 AS NVARCHAR)) AS hes, NAME, ADDRESS, TEL, CODE_E, ECODE, PCODE, IYALAT,                       CITY , MCODEM, TOZIH, CUST_COD, MOBILE, Longitude, Latitude, ROUTE_NAME, OSTANID, SHAHRID, tob FROM dbo.TDETA_HES3 UNION SELECT     RTRIM(CAST(N_KOL AS NVARCHAR)) + '-' + RTRIM(CAST(NUMBER AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER AS NVARCHAR))                      + '-' + RTRIM(CAST(TNUMBER2 AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER3 AS NVARCHAR)) + '-' + RTRIM(CAST(TNUMBER4 AS NVARCHAR)) AS hes, NAME,                       ADDRESS , TEL, CODE_E, ECODE, PCODE, IYALAT, CITY, MCODEM, TOZIH, CUST_COD, MOBILE, Longitude, Latitude, ROUTE_NAME, OSTANID, SHAHRID, tob FROM         dbo.TDETA_HES4 
GO
/****** Object:  Table [dbo].[CUSTKIND]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[CUSTKIND](
	[CUST_COD] [int] NOT NULL,
	[CUSTKNAME] [nvarchar](50) NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [aaaaaCUSTKIND_PK] PRIMARY KEY NONCLUSTERED 
(
	[CUST_COD] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[DEED_DTL]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DEED_DTL](
	[N_S] [float] NOT NULL,
	[RADIF] [float] NULL,
	[HES_K] [int] NOT NULL,
	[HES_M] [int] NOT NULL,
	[HES_T] [int] NOT NULL,
	[SHARH] [nvarchar](255) NULL,
	[BED] [float] NOT NULL,
	[BES] [float] NOT NULL,
	[N_SERI] [float] NULL,
	[BANK] [int] NULL,
	[NUMBER] [float] NULL,
	[TAG] [float] NULL,
	[HES] [nvarchar](40) NOT NULL,
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[ARZD] [float] NULL,
	[MHAZ_NO] [int] NULL,
	[HES_T2] [int] NULL,
	[HES_T3] [int] NULL,
	[HES_T4] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_DEED_DTL] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[DEED_HED]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DEED_HED](
	[N_S] [float] NOT NULL,
	[DATE_S] [bigint] NOT NULL,
	[SHARH_S] [nvarchar](255) NULL,
	[NO_S] [float] NOT NULL,
	[ANBAR] [float] NULL,
	[N_FACTOR] [float] NULL,
	[GHATEI] [bit] NOT NULL,
	[USER_NAME] [nvarchar](40) NULL,
	[base] [int] IDENTITY(1,1) NOT NULL,
	[SGN1] [bit] NULL,
	[SGN2] [bit] NULL,
	[SGN3] [bit] NULL,
	[SGN4] [bit] NULL,
	[OKF] [bit] NULL,
	[sgn1usid] [int] NULL,
	[sgn2usid] [int] NULL,
	[sgn3usid] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[BAYEG] [int] NULL,
 CONSTRAINT [aaaaaDEED_HED_PK] PRIMARY KEY NONCLUSTERED 
(
	[N_S] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[DEPART]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DEPART](
	[DEPATMAN] [int] NOT NULL,
	[DEPNAME] [nvarchar](100) NOT NULL,
	[IDD] [int] IDENTITY(1,1) NOT NULL,
	[DEPART] [nvarchar](200) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[PCODE] [nvarchar](10) NULL,
	[BBC] [nvarchar](50) NULL,
 CONSTRAINT [aaaaaDEPART_PK] PRIMARY KEY NONCLUSTERED 
(
	[DEPATMAN] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[DETA_HES]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[DETA_HES](
	[N_KOL] [int] NOT NULL,
	[NUMBER] [int] NOT NULL,
	[NAME] [nvarchar](100) NULL,
	[TOZIH] [nvarchar](40) NULL,
	[BED_BES] [float] NULL,
	[ADDRESS] [nvarchar](100) NULL,
	[TEL] [nvarchar](50) NULL,
	[CODE_E] [nvarchar](20) NULL,
	[USERCO] [int] NULL,
	[USER_NAME] [nvarchar](50) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
 CONSTRAINT [aaaaaDETA_HES_PK] PRIMARY KEY NONCLUSTERED 
(
	[N_KOL] ASC,
	[NUMBER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[EVENTS]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[EVENTS](
	[IDNUM] [int] NULL,
	[IDD] [int] IDENTITY(1,1) NOT NULL,
	[EVENTS] [nvarchar](4000) NULL,
	[STDATE] [int] NULL,
	[STTIME] [int] NULL,
	[USERNAME] [nvarchar](50) NULL,
	[COMPANY] [int] NULL,
	[SUMTIME] [int] NULL,
	[pic] [image] NULL,
	[skid] [int] NULL,
	[num] [bigint] NULL,
	[tg] [bigint] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[FXTYPE] [nvarchar](10) NULL,
 CONSTRAINT [PK_EVENTS] PRIMARY KEY CLUSTERED 
(
	[IDD] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[HEAD_LST]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[HEAD_LST](
	[NUMBER] [float] NOT NULL,
	[TAG] [float] NOT NULL,
	[ANBAR] [int] NULL,
	[NUMBER1] [float] NULL,
	[DATE_N] [bigint] NOT NULL,
	[TAH] [nvarchar](100) NULL,
	[MAS] [float] NOT NULL,
	[VAS] [float] NOT NULL,
	[N_S] [float] NULL,
	[CUST_NO] [nvarchar](40) NULL,
	[MOLAH] [nvarchar](200) NULL,
	[M_NAGHD] [float] NOT NULL,
	[MABL_VAR] [float] NOT NULL,
	[MOIN_VAR] [nvarchar](40) NULL,
	[MABL_HAV] [float] NOT NULL,
	[MOIN_HAV] [nvarchar](40) NULL,
	[MABL_HAZ] [float] NOT NULL,
	[MOIN_HAZ] [nvarchar](40) NULL,
	[TAKHFIF] [float] NOT NULL,
	[MOIN_KHF] [nvarchar](40) NULL,
	[ANBARF] [int] NULL,
	[FNUMCO] [float] NULL,
	[DEPATMAN] [int] NULL,
	[SHIFT] [int] NULL,
	[CUST_KIND] [int] NULL,
	[USER_NAME] [nvarchar](40) NULL,
	[SHARAYET] [nvarchar](max) NULL,
	[SGN1] [bit] NULL,
	[SGN2] [bit] NULL,
	[SGN3] [bit] NULL,
	[SGN4] [bit] NULL,
	[MBAA] [float] NULL,
	[HMBAA] [nvarchar](40) NULL,
	[TAMIR] [float] NULL,
	[TICMBAA] [bit] NULL,
	[TKHF] [bit] NULL,
	[OKF] [bit] NULL,
	[SADER] [tinyint] NULL,
	[ARZD] [float] NULL,
	[ARZKIND] [tinyint] NULL,
	[CDDATE] [bigint] NULL,
	[CDTIME] [int] NULL,
	[OKDATE] [bigint] NULL,
	[OKTIME] [int] NULL,
	[JAY] [bit] NULL,
	[MODAT_PPID] [int] NULL,
	[PEPID] [int] NULL,
	[PEID] [int] NULL,
	[sgn1usid] [int] NULL,
	[sgn2usid] [int] NULL,
	[sgn3usid] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[ARZKIND2] [bigint] NULL,
	[ARZCODING] [nvarchar](100) NULL,
 CONSTRAINT [aaaaaHEAD_LST_PK] PRIMARY KEY NONCLUSTERED 
(
	[NUMBER] ASC,
	[TAG] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[INVO_LST]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[INVO_LST](
	[NUMBER] [float] NOT NULL,
	[TAG] [float] NOT NULL,
	[ANBAR] [int] NOT NULL,
	[RADIF] [float] NULL,
	[CODE] [nvarchar](15) NOT NULL,
	[MEGH] [float] NOT NULL,
	[MEGHk] [float] NOT NULL,
	[MEGH_MAR] [float] NOT NULL,
	[MANDAH] [nvarchar](50) NULL,
	[MABL] [float] NOT NULL,
	[MABL_K] [float] NOT NULL,
	[FROM_A] [bit] NOT NULL,
	[N_RASID] [nvarchar](40) NULL,
	[MEGH_R] [float] NOT NULL,
	[RADAH] [float] NULL,
	[SANAD_NO] [float] NULL,
	[CUST_NO] [float] NULL,
	[ANBARF] [float] NULL,
	[VAHED_K] [int] NULL,
	[N_KOL] [float] NULL,
	[N_MOIN] [float] NULL,
	[N_TAF] [float] NULL,
	[AVRAGE] [float] NULL,
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[AVRAGE2] [float] NULL,
	[IMBAA] [float] NULL,
	[TOTALARZ] [float] NULL,
	[VISITOR] [nvarchar](40) NULL,
	[TKHN] [float] NULL,
	[JAY] [bigint] NULL,
	[JAYO] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_INVO_LST] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_ADVANCE_EXCL]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_ADVANCE_EXCL](
	[EXCL_ID] [int] IDENTITY(1,1) NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[PERIOD_DATE] [bigint] NOT NULL,
	[EXCL_AMOUNT] [bigint] NOT NULL,
	[REASON] [nvarchar](300) NOT NULL,
	[DEED_N_S] [float] NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[APPROVED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_ADV_EXCL] PRIMARY KEY CLUSTERED 
(
	[EXCL_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_ATT_VALUE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_ATT_VALUE](
	[PER_ID] [int] NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[ITEM_ID] [int] NOT NULL,
	[VALUE] [bigint] NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_ATT_VAL] PRIMARY KEY CLUSTERED 
(
	[PER_ID] ASC,
	[EMP_ID] ASC,
	[ITEM_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_ATTENDANCE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_ATTENDANCE](
	[PER_ID] [int] NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[WORK_DAYS] [decimal](5, 2) NOT NULL,
	[DAYS_TOLID] [decimal](5, 2) NOT NULL,
	[DAYS_EDARI] [decimal](5, 2) NOT NULL,
	[DAYS_KHADAMAT] [decimal](5, 2) NOT NULL,
	[DAYS_FOROSH] [decimal](5, 2) NOT NULL,
	[OT_NORMAL_H] [decimal](6, 2) NOT NULL,
	[OT_HOLIDAY_H] [decimal](6, 2) NOT NULL,
	[OT_ADMIN_H] [decimal](6, 2) NOT NULL,
	[LEAVE_DAYS] [decimal](5, 2) NOT NULL,
	[ABSENT_DAYS] [decimal](5, 2) NOT NULL,
	[MISSION_DAYS] [decimal](5, 2) NOT NULL,
	[DAYS] [decimal](5, 2) NOT NULL,
	[DAYSB] [decimal](5, 2) NOT NULL,
	[FRID_COUNT] [tinyint] NOT NULL,
	[TDAYS] [decimal](5, 2) NOT NULL,
	[PERF_AMOUNT] [bigint] NOT NULL,
	[TRANSP_AMOUNT] [bigint] NOT NULL,
	[KASR_OTHER] [bigint] NOT NULL,
	[SOURCE] [tinyint] NOT NULL,
	[LOCKED] [bit] NOT NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_ATT] PRIMARY KEY CLUSTERED 
(
	[PER_ID] ASC,
	[EMP_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_CONFIG]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_CONFIG](
	[CFG_KEY] [nvarchar](80) NOT NULL,
	[CFG_VALUE] [nvarchar](500) NOT NULL,
	[CFG_OPTIONS] [nvarchar](500) NULL,
	[CFG_DEFAULT] [nvarchar](500) NOT NULL,
	[CFG_SECTION] [nvarchar](60) NOT NULL,
	[LABEL_FA] [nvarchar](200) NOT NULL,
	[DESC_FA] [nvarchar](1000) NULL,
	[OPT_LABELS] [nvarchar](500) NULL,
	[DATA_TYPE] [nvarchar](20) NOT NULL,
	[ACCESS_LEVEL] [tinyint] NOT NULL,
	[CHANGED_AT] [datetime] NULL,
	[CHANGED_BY] [int] NULL,
	[CHANGE_NOTE] [nvarchar](300) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_CONFIG] PRIMARY KEY CLUSTERED 
(
	[CFG_KEY] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_CONFIG_LOG]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_CONFIG_LOG](
	[LOG_ID] [int] IDENTITY(1,1) NOT NULL,
	[CFG_KEY] [nvarchar](80) NOT NULL,
	[OLD_VALUE] [nvarchar](500) NULL,
	[NEW_VALUE] [nvarchar](500) NOT NULL,
	[CHANGED_BY] [int] NOT NULL,
	[CHANGED_AT] [datetime] NOT NULL,
	[REASON] [nvarchar](300) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_CONFIG_LOG] PRIMARY KEY CLUSTERED 
(
	[LOG_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_CONTRACT]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_CONTRACT](
	[CON_ID] [int] IDENTITY(1,1) NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[CON_TYPE] [tinyint] NOT NULL,
	[START_DATE] [bigint] NOT NULL,
	[END_DATE] [bigint] NULL,
	[TRIAL_END] [bigint] NULL,
	[WEEKLY_HOURS] [decimal](5, 2) NOT NULL,
	[NOTES] [nvarchar](200) NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_CONTRACT] PRIMARY KEY CLUSTERED 
(
	[CON_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_DECREE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_DECREE](
	[DEC_ID] [int] IDENTITY(1,1) NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[WS_ID] [int] NOT NULL,
	[ISSUED_DATE] [bigint] NOT NULL,
	[EFF_FROM] [bigint] NOT NULL,
	[EFF_TO] [bigint] NULL,
	[EDU_LEVEL] [tinyint] NULL,
	[MARITAL] [tinyint] NULL,
	[IS_MANAGER] [bit] NULL,
	[TMPL_ID] [int] NULL,
	[IS_CONFIRMED] [bit] NOT NULL,
	[CONFIRMED_BY] [int] NULL,
	[CONFIRMED_AT] [datetime] NULL,
	[NOTES] [nvarchar](300) NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[SHIFT_MODE] [nvarchar](10) NULL,
 CONSTRAINT [PK_PAY2_DECREE] PRIMARY KEY CLUSTERED 
(
	[DEC_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_DECREE_LINE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_DECREE_LINE](
	[DEC_ID] [int] NOT NULL,
	[ITEM_ID] [int] NOT NULL,
	[AMOUNT] [decimal](18, 2) NOT NULL,
	[INS_OV] [bit] NULL,
	[TAX_OV] [bit] NULL,
	[BASIS_OV] [tinyint] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[SHIFT_MODE_OV] [nvarchar](10) NULL,
 CONSTRAINT [PK_PAY2_DECREE_LINE] PRIMARY KEY CLUSTERED 
(
	[DEC_ID] ASC,
	[ITEM_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_EMPLOYEE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_EMPLOYEE](
	[EMP_ID] [int] IDENTITY(1,1) NOT NULL,
	[EMP_CODE] [nvarchar](20) NOT NULL,
	[WS_ID] [int] NOT NULL,
	[FIRST_NAME] [nvarchar](50) NOT NULL,
	[LAST_NAME] [nvarchar](50) NOT NULL,
	[FATHER_NAME] [nvarchar](50) NULL,
	[NATIONAL_CODE] [nvarchar](10) NULL,
	[ID_NUMBER] [nvarchar](20) NULL,
	[BIRTH_PLACE] [nvarchar](50) NULL,
	[BIRTH_DATE] [bigint] NULL,
	[GENDER] [tinyint] NOT NULL,
	[NATIONALITY] [tinyint] NOT NULL,
	[IS_JANBAZ] [bit] NOT NULL,
	[HIRE_DATE] [bigint] NOT NULL,
	[FIRE_DATE] [bigint] NULL,
	[JOB_ID] [int] NULL,
	[UNIT] [tinyint] NULL,
	[EDU_LEVEL] [tinyint] NULL,
	[MARITAL] [tinyint] NOT NULL,
	[IS_MANAGER] [bit] NOT NULL,
	[INS_CODE] [nvarchar](15) NULL,
	[INS_TYPE] [tinyint] NOT NULL,
	[TAX_EXEMPT] [bit] NOT NULL,
	[REGION_DEPRIVATION] [tinyint] NOT NULL,
	[ACC_T] [nvarchar](50) NULL,
	[CARD_NO] [nvarchar](20) NULL,
	[MOBILE] [nvarchar](15) NULL,
	[BANK_ACC] [nvarchar](30) NULL,
	[IBAN] [nvarchar](26) NULL,
	[IS_ACTIVE] [bit] NOT NULL,
	[NOTES] [nvarchar](300) NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_EMPLOYEE] PRIMARY KEY CLUSTERED 
(
	[EMP_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_EMP_CODE] UNIQUE NONCLUSTERED 
(
	[EMP_CODE] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_ITEM_DEF]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_ITEM_DEF](
	[ITEM_ID] [int] IDENTITY(1,1) NOT NULL,
	[ITEM_CODE] [nvarchar](30) NOT NULL,
	[ITEM_NAME] [nvarchar](100) NOT NULL,
	[ITEM_TYPE] [tinyint] NOT NULL,
	[CALC_BASIS] [tinyint] NOT NULL,
	[INS_SUBJECT] [bit] NOT NULL,
	[TAX_SUBJECT] [bit] NOT NULL,
	[INS_BASE_DAYS] [tinyint] NOT NULL,
	[PAY_BASE_DAYS] [tinyint] NOT NULL,
	[IS_SYSTEM] [bit] NOT NULL,
	[SHOW_IN_SLIP] [bit] NOT NULL,
	[SORT_ORDER] [smallint] NOT NULL,
	[IS_ACTIVE] [bit] NOT NULL,
	[NOTES] [nvarchar](200) NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_ITEM_DEF] PRIMARY KEY CLUSTERED 
(
	[ITEM_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_ITEM_CODE] UNIQUE NONCLUSTERED 
(
	[ITEM_CODE] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_ITEM_TEMPLATE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_ITEM_TEMPLATE](
	[TMPL_ID] [int] IDENTITY(1,1) NOT NULL,
	[TMPL_CODE] [nvarchar](30) NOT NULL,
	[TMPL_NAME] [nvarchar](100) NOT NULL,
	[WS_ID] [int] NULL,
	[IS_ACTIVE] [bit] NOT NULL,
	[NOTES] [nvarchar](200) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_TMPL] PRIMARY KEY CLUSTERED 
(
	[TMPL_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_TMPL_CODE] UNIQUE NONCLUSTERED 
(
	[TMPL_CODE] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_ITEM_TMPL_LINE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_ITEM_TMPL_LINE](
	[TMPL_ID] [int] NOT NULL,
	[ITEM_ID] [int] NOT NULL,
	[DEF_AMOUNT] [decimal](18, 2) NOT NULL,
	[INS_OV] [bit] NULL,
	[TAX_OV] [bit] NULL,
	[BASIS_OV] [tinyint] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[SHIFT_MODE_OV] [nvarchar](10) NULL,
 CONSTRAINT [PK_PAY2_TMPL_LINE] PRIMARY KEY CLUSTERED 
(
	[TMPL_ID] ASC,
	[ITEM_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_JOB]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_JOB](
	[JOB_ID] [int] IDENTITY(1,1) NOT NULL,
	[JOB_CODE] [nvarchar](20) NOT NULL,
	[JOB_NAME] [nvarchar](100) NOT NULL,
	[JOB_GROUP] [nvarchar](50) NULL,
	[IS_ACTIVE] [bit] NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_JOB] PRIMARY KEY CLUSTERED 
(
	[JOB_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_JOB_CODE] UNIQUE NONCLUSTERED 
(
	[JOB_CODE] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_LEAVE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_LEAVE](
	[LEV_ID] [int] IDENTITY(1,1) NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[LEV_TYPE] [tinyint] NOT NULL,
	[REQUEST_DATE] [bigint] NOT NULL,
	[START_DATE] [bigint] NOT NULL,
	[END_DATE] [bigint] NOT NULL,
	[REQ_DAYS] [smallint] NOT NULL,
	[REQ_HOURS] [tinyint] NOT NULL,
	[REQ_MINUTES] [tinyint] NOT NULL,
	[TOTAL_MINUTES]  AS (([REQ_DAYS]*(440)+[REQ_HOURS]*(60))+[REQ_MINUTES]),
	[BAL_BEFORE] [int] NULL,
	[DESCRIPTION] [nvarchar](300) NULL,
	[REFER_TO] [int] NULL,
	[STATUS] [tinyint] NOT NULL,
	[APV1_BY] [int] NULL,
	[APV1_AT] [datetime] NULL,
	[APV2_BY] [int] NULL,
	[APV2_AT] [datetime] NULL,
	[APV3_BY] [int] NULL,
	[APV3_AT] [datetime] NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_LEAVE] PRIMARY KEY CLUSTERED 
(
	[LEV_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_LEAVE_BAL]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_LEAVE_BAL](
	[EMP_ID] [int] NOT NULL,
	[YEAR] [smallint] NOT NULL,
	[ENTITLEMENT_MIN] [int] NOT NULL,
	[USED_MIN] [int] NOT NULL,
	[CARRIED_IN_MIN] [int] NOT NULL,
	[CARRIED_OUT_MIN] [int] NOT NULL,
	[BALANCE_MIN]  AS (([ENTITLEMENT_MIN]+[CARRIED_IN_MIN])-[USED_MIN]),
	[BALANCE_DAYS]  AS ((([ENTITLEMENT_MIN]+[CARRIED_IN_MIN])-[USED_MIN])/(440)),
	[UPDATED_AT] [datetime] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_LEAVE_BAL] PRIMARY KEY CLUSTERED 
(
	[EMP_ID] ASC,
	[YEAR] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_LOAN]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_LOAN](
	[LOAN_ID] [int] IDENTITY(1,1) NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[WS_ID] [int] NOT NULL,
	[LOAN_TYPE] [tinyint] NOT NULL,
	[LOAN_DATE] [bigint] NOT NULL,
	[AMOUNT] [bigint] NOT NULL,
	[INSTALLMENT] [bigint] NOT NULL,
	[TOTAL_INST] [smallint] NOT NULL,
	[PAID_INST] [smallint] NOT NULL,
	[FIRST_PAY] [bigint] NOT NULL,
	[PURPOSE] [nvarchar](200) NULL,
	[IS_ACTIVE] [bit] NOT NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_LOAN] PRIMARY KEY CLUSTERED 
(
	[LOAN_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_LOAN_SCHED]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_LOAN_SCHED](
	[SCHED_ID] [int] IDENTITY(1,1) NOT NULL,
	[LOAN_ID] [int] NOT NULL,
	[INST_NUM] [smallint] NOT NULL,
	[DUE_PERIOD] [bigint] NOT NULL,
	[AMOUNT] [bigint] NOT NULL,
	[RUN_ID] [int] NULL,
	[PAID_AT] [datetime] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_LOAN_SCHED] PRIMARY KEY CLUSTERED 
(
	[SCHED_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_LOAN_INST] UNIQUE NONCLUSTERED 
(
	[LOAN_ID] ASC,
	[INST_NUM] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_OVERRIDE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_OVERRIDE](
	[EMP_ID] [int] NOT NULL,
	[ITEM_ID] [int] NOT NULL,
	[INS_OV] [bit] NULL,
	[TAX_OV] [bit] NULL,
	[BASIS_OV] [tinyint] NULL,
	[VALID_FROM] [bigint] NOT NULL,
	[VALID_TO] [bigint] NULL,
	[REASON] [nvarchar](200) NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_OVERRIDE] PRIMARY KEY CLUSTERED 
(
	[EMP_ID] ASC,
	[ITEM_ID] ASC,
	[VALID_FROM] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_PERIOD]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_PERIOD](
	[PER_ID] [int] IDENTITY(1,1) NOT NULL,
	[WS_ID] [int] NOT NULL,
	[PERIOD_DATE] [bigint] NOT NULL,
	[HOLIDAY_DAYS] [tinyint] NOT NULL,
	[TENDAR_APPLY] [bit] NOT NULL,
	[DEED_N_S_PAY] [float] NULL,
	[STATUS] [tinyint] NOT NULL,
	[OPENED_AT] [datetime] NOT NULL,
	[CLOSED_AT] [datetime] NULL,
	[NOTES] [nvarchar](200) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_PERIOD] PRIMARY KEY CLUSTERED 
(
	[PER_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_PERIOD] UNIQUE NONCLUSTERED 
(
	[WS_ID] ASC,
	[PERIOD_DATE] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_RUN]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_RUN](
	[RUN_ID] [int] IDENTITY(1,1) NOT NULL,
	[PER_ID] [int] NOT NULL,
	[RUN_NO] [smallint] NOT NULL,
	[IS_LATEST] [bit] NOT NULL,
	[CALC_AT] [datetime] NOT NULL,
	[CALC_BY] [int] NULL,
	[STATUS] [tinyint] NOT NULL,
	[PREV_RUN_ID] [int] NULL,
	[DEED_ID_SAL] [int] NULL,
	[DEED_ID_INS] [int] NULL,
	[NOTES] [nvarchar](300) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[DEED_MODE] [tinyint] NULL,
	[DEED_GENERATOR_VERSION] [smallint] NULL,
 CONSTRAINT [PK_PAY2_RUN] PRIMARY KEY CLUSTERED 
(
	[RUN_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_RUN_PERIOD_NO] UNIQUE NONCLUSTERED 
(
	[PER_ID] ASC,
	[RUN_NO] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_RUN_DETAIL]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_RUN_DETAIL](
	[RUN_ID] [int] NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[ITEM_ID] [int] NOT NULL,
	[AMOUNT] [bigint] NOT NULL,
	[INS_SUBJECT] [bit] NOT NULL,
	[TAX_SUBJECT] [bit] NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_RUN_DETAIL] PRIMARY KEY CLUSTERED 
(
	[RUN_ID] ASC,
	[EMP_ID] ASC,
	[ITEM_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_RUN_LINE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_RUN_LINE](
	[RUN_ID] [int] NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[DEC_ID] [int] NULL,
	[WORK_DAYS] [decimal](5, 2) NOT NULL,
	[GROSS_PAY] [bigint] NOT NULL,
	[INS_BASE] [bigint] NOT NULL,
	[INS_WORKER] [bigint] NOT NULL,
	[INS_EMPLOYER] [bigint] NOT NULL,
	[TAX_BASE] [bigint] NOT NULL,
	[TAX_AMOUNT] [bigint] NOT NULL,
	[LOAN_DED] [bigint] NOT NULL,
	[ADVANCE_DED] [bigint] NOT NULL,
	[OTHER_DED] [bigint] NOT NULL,
	[TOTAL_DED] [bigint] NOT NULL,
	[NET_PAY] [bigint] NOT NULL,
	[LEAVE_BAL_DAYS] [decimal](5, 2) NULL,
	[LOAN_BALANCE] [bigint] NULL,
	[ADVANCE_BALANCE_SNAP] [bigint] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_RUN_LINE] PRIMARY KEY CLUSTERED 
(
	[RUN_ID] ASC,
	[EMP_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_SETTLEMENT]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_SETTLEMENT](
	[SET_ID] [int] IDENTITY(1,1) NOT NULL,
	[EMP_ID] [int] NOT NULL,
	[WS_ID] [int] NOT NULL,
	[SETTLE_DATE] [bigint] NOT NULL,
	[HIRE_DATE] [bigint] NOT NULL,
	[END_DATE] [bigint] NOT NULL,
	[SENIORITY_DAYS] [int] NOT NULL,
	[SENIORITY_YEARS] [decimal](6, 2) NOT NULL,
	[LAST_SALARY] [bigint] NOT NULL,
	[LAST_DAILY] [bigint] NOT NULL,
	[PREV_SET_ID] [int] NULL,
	[PREV_SENIORITY_DAYS] [int] NOT NULL,
	[LEAVE_BAL_MIN] [int] NOT NULL,
	[LEAVE_BAL_DAYS] [decimal](5, 2) NOT NULL,
	[EIDI] [bigint] NOT NULL,
	[BON] [bigint] NOT NULL,
	[LEAVE_PAY] [bigint] NOT NULL,
	[SANAVAT] [bigint] NOT NULL,
	[PREV_CREDIT] [bigint] NOT NULL,
	[OTHER_INCOME] [bigint] NOT NULL,
	[TOTAL_INCOME]  AS ((((([EIDI]+[BON])+[LEAVE_PAY])+[SANAVAT])+[PREV_CREDIT])+[OTHER_INCOME]),
	[PREV_DEBIT] [bigint] NOT NULL,
	[EIDI_TAX] [bigint] NOT NULL,
	[LOAN_BALANCE] [bigint] NOT NULL,
	[OTHER_DED] [bigint] NOT NULL,
	[TOTAL_DED]  AS ((([PREV_DEBIT]+[EIDI_TAX])+[LOAN_BALANCE])+[OTHER_DED]),
	[NET_SETTLE]  AS ((((((((([EIDI]+[BON])+[LEAVE_PAY])+[SANAVAT])+[PREV_CREDIT])+[OTHER_INCOME])-[PREV_DEBIT])-[EIDI_TAX])-[LOAN_BALANCE])-[OTHER_DED]),
	[STATUS] [tinyint] NOT NULL,
	[DEED_N_S] [float] NULL,
	[CALC_METHOD] [nvarchar](200) NULL,
	[NOTES] [nvarchar](300) NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[APPROVED_BY] [int] NULL,
	[APPROVED_AT] [datetime] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_SETTLEMENT] PRIMARY KEY CLUSTERED 
(
	[SET_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_TAX_BRACKET]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_TAX_BRACKET](
	[BRK_ID] [int] IDENTITY(1,1) NOT NULL,
	[TAX_YEAR] [smallint] NOT NULL,
	[UPPER_LIMIT] [bigint] NOT NULL,
	[RATE_PCT] [decimal](5, 2) NOT NULL,
	[FIXED_TAX] [bigint] NOT NULL,
	[SORT_ORDER] [smallint] NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_TAX_BRACKET] PRIMARY KEY CLUSTERED 
(
	[BRK_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_BRK] UNIQUE NONCLUSTERED 
(
	[TAX_YEAR] ASC,
	[SORT_ORDER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_WORKSHOP]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_WORKSHOP](
	[WS_ID] [int] IDENTITY(1,1) NOT NULL,
	[WS_CODE] [nvarchar](20) NOT NULL,
	[WS_NAME] [nvarchar](100) NOT NULL,
	[NATIONAL_ID] [nvarchar](11) NULL,
	[SOCIAL_INS_CODE] [nvarchar](20) NULL,
	[TAX_CODE] [nvarchar](20) NULL,
	[ADDRESS] [nvarchar](300) NULL,
	[PHONE] [nvarchar](30) NULL,
	[INS_MODE] [tinyint] NOT NULL,
	[IS_ACTIVE] [bit] NOT NULL,
	[CREATED_AT] [datetime] NOT NULL,
	[CREATED_BY] [int] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[POSTAL_CODE] [nvarchar](20) NULL,
	[EMPLOYER_NAME] [nvarchar](100) NULL,
	[PROVINCE] [nvarchar](50) NULL,
	[CITY] [nvarchar](50) NULL,
	[REGISTRATION_NUMBER] [nvarchar](20) NULL,
	[SSO_BRANCH] [nvarchar](50) NULL,
	[FINANCIAL_MANAGER] [nvarchar](100) NULL,
	[ADMIN_MANAGER] [nvarchar](100) NULL,
	[SHIFT_MODE] [nvarchar](10) NULL,
	[DEFAULT_DEED_MODE] [tinyint] NOT NULL,
 CONSTRAINT [PK_PAY2_WORKSHOP] PRIMARY KEY CLUSTERED 
(
	[WS_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_WS_CODE] UNIQUE NONCLUSTERED 
(
	[WS_CODE] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[PAY2_WORKSHOP_ACC]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[PAY2_WORKSHOP_ACC](
	[WS_ID] [int] NOT NULL,
	[ACC_KEY] [nvarchar](50) NOT NULL,
	[ACC_CODE] [nvarchar](20) NOT NULL,
	[ACC_DESC] [nvarchar](100) NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_PAY2_WS_ACC] PRIMARY KEY CLUSTERED 
(
	[WS_ID] ASC,
	[ACC_KEY] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TFORMS]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TFORMS](
	[FORMNAME] [nvarchar](50) NULL,
	[CAPTION] [nvarchar](100) NULL,
	[kind] [smallint] NULL,
	[GRP] [smallint] NULL,
	[IDH] [int] NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_TFORMS] PRIMARY KEY CLUSTERED 
(
	[IDH] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TOTA_HES]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TOTA_HES](
	[NUMBER] [int] NOT NULL,
	[NAME] [nvarchar](50) NOT NULL,
	[NO_HES] [float] NULL,
	[M_D] [float] NULL,
	[GROUP] [float] NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
 CONSTRAINT [aaaaaTOTA_HES_PK] PRIMARY KEY NONCLUSTERED 
(
	[NUMBER] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
ALTER TABLE [dbo].[CUSTKIND] ADD  CONSTRAINT [DF__CUSTKIND__CRT__0B1D841F]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[DEED_DTL] ADD  CONSTRAINT [DF__DEED_DTL__BED__43D61337]  DEFAULT ((0)) FOR [BED]
GO
ALTER TABLE [dbo].[DEED_DTL] ADD  CONSTRAINT [DF__DEED_DTL__BES__44CA3770]  DEFAULT ((0)) FOR [BES]
GO
ALTER TABLE [dbo].[DEED_DTL] ADD  CONSTRAINT [DF__DEED_DTL__ARZD__3528CC84]  DEFAULT ((1)) FOR [ARZD]
GO
ALTER TABLE [dbo].[DEED_DTL] ADD  CONSTRAINT [DF__DEED_DTL__CRT__096A45D7]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[DEED_HED] ADD  CONSTRAINT [DF__DEED_HED__GHATEI__1DB06A4F]  DEFAULT ((0)) FOR [GHATEI]
GO
ALTER TABLE [dbo].[DEED_HED] ADD  CONSTRAINT [DF__DEED_HED__SGN1__3BB699D9]  DEFAULT ((0)) FOR [SGN1]
GO
ALTER TABLE [dbo].[DEED_HED] ADD  CONSTRAINT [DF__DEED_HED__SGN2__3CAABE12]  DEFAULT ((0)) FOR [SGN2]
GO
ALTER TABLE [dbo].[DEED_HED] ADD  CONSTRAINT [DF__DEED_HED__SGN3__3D9EE24B]  DEFAULT ((0)) FOR [SGN3]
GO
ALTER TABLE [dbo].[DEED_HED] ADD  CONSTRAINT [DF__DEED_HED__SGN4__3E930684]  DEFAULT ((0)) FOR [SGN4]
GO
ALTER TABLE [dbo].[DEPART] ADD  CONSTRAINT [DF__DEPART__CRT__5B6E70FD]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[DETA_HES] ADD  CONSTRAINT [DF__DETA_HES__N_KOL__15DA3E5D]  DEFAULT ((0)) FOR [N_KOL]
GO
ALTER TABLE [dbo].[DETA_HES] ADD  CONSTRAINT [DF__DETA_HES__NUMBER__16CE6296]  DEFAULT ((0)) FOR [NUMBER]
GO
ALTER TABLE [dbo].[DETA_HES] ADD  CONSTRAINT [DF__DETA_HES__CRT__67152DD3]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[EVENTS] ADD  CONSTRAINT [DF_EVENTS_SUMTIME]  DEFAULT ((0)) FOR [SUMTIME]
GO
ALTER TABLE [dbo].[EVENTS] ADD  CONSTRAINT [DF__EVENTS__CRT__6630800A]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF_HEAD_LST_MAS]  DEFAULT ((0)) FOR [MAS]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF_HEAD_LST_VAS]  DEFAULT ((0)) FOR [VAS]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__M_NAGH__014935CB]  DEFAULT ((0)) FOR [M_NAGHD]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__MABL_V__023D5A04]  DEFAULT ((0)) FOR [MABL_VAR]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__MABL_H__03317E3D]  DEFAULT ((0)) FOR [MABL_HAV]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__MABL_H__0425A276]  DEFAULT ((0)) FOR [MABL_HAZ]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__TAKHFI__0519C6AF]  DEFAULT ((0)) FOR [TAKHFIF]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF_HEAD_LST_SGN1]  DEFAULT ((0)) FOR [SGN1]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF_HEAD_LST_SGN2]  DEFAULT ((0)) FOR [SGN2]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF_HEAD_LST_SGN3]  DEFAULT ((0)) FOR [SGN3]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF_HEAD_LST_SGN4]  DEFAULT ((0)) FOR [SGN4]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__MBAA__2B155265]  DEFAULT ((0)) FOR [MBAA]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__TICMBA__2C09769E]  DEFAULT ((0)) FOR [TICMBAA]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__TKHF__2CFD9AD7]  DEFAULT ((1)) FOR [TKHF]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__SADER__2DF1BF10]  DEFAULT ((0)) FOR [SADER]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__ARZD__2EE5E349]  DEFAULT ((1)) FOR [ARZD]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__ARZKIN__2FDA0782]  DEFAULT ((1)) FOR [ARZKIND]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__JAY__30CE2BBB]  DEFAULT ((0)) FOR [JAY]
GO
ALTER TABLE [dbo].[HEAD_LST] ADD  CONSTRAINT [DF__HEAD_LST__CRT__5F3F01E1]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__MEGH__117F9D94]  DEFAULT ((0)) FOR [MEGH]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__MEGHk__1273C1CD]  DEFAULT ((0)) FOR [MEGHk]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__MEGH_M__1367E606]  DEFAULT ((0)) FOR [MEGH_MAR]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__MABL__145C0A3F]  DEFAULT ((0)) FOR [MABL]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__MABL_K__15502E78]  DEFAULT ((0)) FOR [MABL_K]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__FROM_A__164452B1]  DEFAULT ((0)) FOR [FROM_A]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__MEGH_R__173876EA]  DEFAULT ((0)) FOR [MEGH_R]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF_INVO_LST_N_KOL]  DEFAULT ((0)) FOR [N_KOL]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF_INVO_LST_N_MOIN]  DEFAULT ((0)) FOR [N_MOIN]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF_INVO_LST_AVRAGE]  DEFAULT ((0)) FOR [AVRAGE]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF_INVO_LST_AVRAGE1]  DEFAULT ((0)) FOR [AVRAGE2]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__IMBAA__24F264BB]  DEFAULT ((0)) FOR [IMBAA]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__TOTALA__25E688F4]  DEFAULT ((0)) FOR [TOTALARZ]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__TKHN__26DAAD2D]  DEFAULT ((0)) FOR [TKHN]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__JAY__27CED166]  DEFAULT ((0)) FOR [JAY]
GO
ALTER TABLE [dbo].[INVO_LST] ADD  CONSTRAINT [DF__INVO_LST__CRT__5C629536]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_ADVANCE_EXCL] ADD  CONSTRAINT [DF_AE_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_ADVANCE_EXCL] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_ATT_VALUE] ADD  CONSTRAINT [DF_AV_VAL]  DEFAULT ((0)) FOR [VALUE]
GO
ALTER TABLE [dbo].[PAY2_ATT_VALUE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_WD]  DEFAULT ((0)) FOR [WORK_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_DTL]  DEFAULT ((0)) FOR [DAYS_TOLID]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_DED]  DEFAULT ((0)) FOR [DAYS_EDARI]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_DKH]  DEFAULT ((0)) FOR [DAYS_KHADAMAT]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_DFR]  DEFAULT ((0)) FOR [DAYS_FOROSH]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_OTN]  DEFAULT ((0)) FOR [OT_NORMAL_H]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_OTH]  DEFAULT ((0)) FOR [OT_HOLIDAY_H]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_OTA]  DEFAULT ((0)) FOR [OT_ADMIN_H]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_LD]  DEFAULT ((0)) FOR [LEAVE_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_AD]  DEFAULT ((0)) FOR [ABSENT_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_MD]  DEFAULT ((0)) FOR [MISSION_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_DAYS]  DEFAULT ((0)) FOR [DAYS]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_DAYSB]  DEFAULT ((0)) FOR [DAYSB]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_FRID]  DEFAULT ((0)) FOR [FRID_COUNT]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_TDAYS]  DEFAULT ((0)) FOR [TDAYS]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_PF]  DEFAULT ((0)) FOR [PERF_AMOUNT]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_TR]  DEFAULT ((0)) FOR [TRANSP_AMOUNT]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_KO]  DEFAULT ((0)) FOR [KASR_OTHER]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_SRC]  DEFAULT ((1)) FOR [SOURCE]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_LCK]  DEFAULT ((0)) FOR [LOCKED]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  CONSTRAINT [DF_ATT_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_CONFIG] ADD  CONSTRAINT [DF_CFG_DT]  DEFAULT ('TEXT') FOR [DATA_TYPE]
GO
ALTER TABLE [dbo].[PAY2_CONFIG] ADD  CONSTRAINT [DF_CFG_AL]  DEFAULT ((2)) FOR [ACCESS_LEVEL]
GO
ALTER TABLE [dbo].[PAY2_CONFIG] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_CONFIG_LOG] ADD  CONSTRAINT [DF_CFL_DT]  DEFAULT (getdate()) FOR [CHANGED_AT]
GO
ALTER TABLE [dbo].[PAY2_CONFIG_LOG] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_CONTRACT] ADD  CONSTRAINT [DF_CON_WH]  DEFAULT ((44)) FOR [WEEKLY_HOURS]
GO
ALTER TABLE [dbo].[PAY2_CONTRACT] ADD  CONSTRAINT [DF_CON_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_CONTRACT] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_DECREE] ADD  CONSTRAINT [DF_DEC_CON]  DEFAULT ((0)) FOR [IS_CONFIRMED]
GO
ALTER TABLE [dbo].[PAY2_DECREE] ADD  CONSTRAINT [DF_DEC_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_DECREE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE] ADD  CONSTRAINT [DF_DL_AMT]  DEFAULT ((0)) FOR [AMOUNT]
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_GND]  DEFAULT ((1)) FOR [GENDER]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_NAT]  DEFAULT ((1)) FOR [NATIONALITY]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_JAN]  DEFAULT ((0)) FOR [IS_JANBAZ]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_MAR]  DEFAULT ((2)) FOR [MARITAL]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_MGR]  DEFAULT ((0)) FOR [IS_MANAGER]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_INS]  DEFAULT ((1)) FOR [INS_TYPE]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_TEX]  DEFAULT ((0)) FOR [TAX_EXEMPT]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_DEP]  DEFAULT ((0)) FOR [REGION_DEPRIVATION]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_ACT]  DEFAULT ((1)) FOR [IS_ACTIVE]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  CONSTRAINT [DF_EMP_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_CB]  DEFAULT ((2)) FOR [CALC_BASIS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_INS]  DEFAULT ((1)) FOR [INS_SUBJECT]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_TAX]  DEFAULT ((1)) FOR [TAX_SUBJECT]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_IBD]  DEFAULT ((1)) FOR [INS_BASE_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_PBD]  DEFAULT ((2)) FOR [PAY_BASE_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_SYS]  DEFAULT ((0)) FOR [IS_SYSTEM]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_SLP]  DEFAULT ((1)) FOR [SHOW_IN_SLIP]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_SRT]  DEFAULT ((100)) FOR [SORT_ORDER]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_ACT]  DEFAULT ((1)) FOR [IS_ACTIVE]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  CONSTRAINT [DF_ID_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TEMPLATE] ADD  CONSTRAINT [DF_TMPL_ACT]  DEFAULT ((1)) FOR [IS_ACTIVE]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TEMPLATE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] ADD  CONSTRAINT [DF_TL_AMT]  DEFAULT ((0)) FOR [DEF_AMOUNT]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_JOB] ADD  CONSTRAINT [DF_JOB_ACT]  DEFAULT ((1)) FOR [IS_ACTIVE]
GO
ALTER TABLE [dbo].[PAY2_JOB] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_LEAVE] ADD  CONSTRAINT [DF_LEV_RD]  DEFAULT ((0)) FOR [REQ_DAYS]
GO
ALTER TABLE [dbo].[PAY2_LEAVE] ADD  CONSTRAINT [DF_LEV_RH]  DEFAULT ((0)) FOR [REQ_HOURS]
GO
ALTER TABLE [dbo].[PAY2_LEAVE] ADD  CONSTRAINT [DF_LEV_RM]  DEFAULT ((0)) FOR [REQ_MINUTES]
GO
ALTER TABLE [dbo].[PAY2_LEAVE] ADD  CONSTRAINT [DF_LEV_ST]  DEFAULT ((1)) FOR [STATUS]
GO
ALTER TABLE [dbo].[PAY2_LEAVE] ADD  CONSTRAINT [DF_LEV_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_LEAVE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_LEAVE_BAL] ADD  CONSTRAINT [DF_LB_ENT]  DEFAULT ((11440)) FOR [ENTITLEMENT_MIN]
GO
ALTER TABLE [dbo].[PAY2_LEAVE_BAL] ADD  CONSTRAINT [DF_LB_USD]  DEFAULT ((0)) FOR [USED_MIN]
GO
ALTER TABLE [dbo].[PAY2_LEAVE_BAL] ADD  CONSTRAINT [DF_LB_CIN]  DEFAULT ((0)) FOR [CARRIED_IN_MIN]
GO
ALTER TABLE [dbo].[PAY2_LEAVE_BAL] ADD  CONSTRAINT [DF_LB_COU]  DEFAULT ((0)) FOR [CARRIED_OUT_MIN]
GO
ALTER TABLE [dbo].[PAY2_LEAVE_BAL] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_LOAN] ADD  CONSTRAINT [DF_LN_TYP]  DEFAULT ((1)) FOR [LOAN_TYPE]
GO
ALTER TABLE [dbo].[PAY2_LOAN] ADD  CONSTRAINT [DF_LN_PI]  DEFAULT ((0)) FOR [PAID_INST]
GO
ALTER TABLE [dbo].[PAY2_LOAN] ADD  CONSTRAINT [DF_LN_ACT]  DEFAULT ((1)) FOR [IS_ACTIVE]
GO
ALTER TABLE [dbo].[PAY2_LOAN] ADD  CONSTRAINT [DF_LN_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_LOAN] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_LOAN_SCHED] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_OVERRIDE] ADD  CONSTRAINT [DF_OV_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_OVERRIDE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_PERIOD] ADD  CONSTRAINT [DF_PER_HD]  DEFAULT ((0)) FOR [HOLIDAY_DAYS]
GO
ALTER TABLE [dbo].[PAY2_PERIOD] ADD  CONSTRAINT [DF_PER_TEN]  DEFAULT ((0)) FOR [TENDAR_APPLY]
GO
ALTER TABLE [dbo].[PAY2_PERIOD] ADD  CONSTRAINT [DF_PER_ST]  DEFAULT ((1)) FOR [STATUS]
GO
ALTER TABLE [dbo].[PAY2_PERIOD] ADD  CONSTRAINT [DF_PER_OA]  DEFAULT (getdate()) FOR [OPENED_AT]
GO
ALTER TABLE [dbo].[PAY2_PERIOD] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_RUN] ADD  CONSTRAINT [DF_RUN_NO]  DEFAULT ((1)) FOR [RUN_NO]
GO
ALTER TABLE [dbo].[PAY2_RUN] ADD  CONSTRAINT [DF_RUN_IL]  DEFAULT ((1)) FOR [IS_LATEST]
GO
ALTER TABLE [dbo].[PAY2_RUN] ADD  CONSTRAINT [DF_RUN_CA]  DEFAULT (getdate()) FOR [CALC_AT]
GO
ALTER TABLE [dbo].[PAY2_RUN] ADD  CONSTRAINT [DF_RUN_ST]  DEFAULT ((1)) FOR [STATUS]
GO
ALTER TABLE [dbo].[PAY2_RUN] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_RUN_DETAIL] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE] ADD  CONSTRAINT [DF_RL_LD]  DEFAULT ((0)) FOR [LOAN_DED]
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE] ADD  CONSTRAINT [DF_RL_AD]  DEFAULT ((0)) FOR [ADVANCE_DED]
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE] ADD  CONSTRAINT [DF_RL_OD]  DEFAULT ((0)) FOR [OTHER_DED]
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_PSD]  DEFAULT ((0)) FOR [PREV_SENIORITY_DAYS]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_LBM]  DEFAULT ((0)) FOR [LEAVE_BAL_MIN]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_LBD]  DEFAULT ((0)) FOR [LEAVE_BAL_DAYS]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_EID]  DEFAULT ((0)) FOR [EIDI]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_BON]  DEFAULT ((0)) FOR [BON]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_LPY]  DEFAULT ((0)) FOR [LEAVE_PAY]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_SAN]  DEFAULT ((0)) FOR [SANAVAT]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_PCR]  DEFAULT ((0)) FOR [PREV_CREDIT]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_OIN]  DEFAULT ((0)) FOR [OTHER_INCOME]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_PDB]  DEFAULT ((0)) FOR [PREV_DEBIT]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_ETX]  DEFAULT ((0)) FOR [EIDI_TAX]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_LBL]  DEFAULT ((0)) FOR [LOAN_BALANCE]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_ODE]  DEFAULT ((0)) FOR [OTHER_DED]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_ST]  DEFAULT ((1)) FOR [STATUS]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  CONSTRAINT [DF_SET_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_TAX_BRACKET] ADD  CONSTRAINT [DF_BRK_FT]  DEFAULT ((0)) FOR [FIXED_TAX]
GO
ALTER TABLE [dbo].[PAY2_TAX_BRACKET] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD  CONSTRAINT [DF_WS_INS]  DEFAULT ((1)) FOR [INS_MODE]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD  CONSTRAINT [DF_WS_ACT]  DEFAULT ((1)) FOR [IS_ACTIVE]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD  CONSTRAINT [DF_WS_CRT]  DEFAULT (getdate()) FOR [CREATED_AT]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD  CONSTRAINT [DF_WS_DEED_MODE]  DEFAULT ((1)) FOR [DEFAULT_DEED_MODE]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP_ACC] ADD  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[TDETA_HES] ADD  CONSTRAINT [DF__TDETA_HES__CODE___7DCDAAA2]  DEFAULT ('0') FOR [CODE_E]
GO
ALTER TABLE [dbo].[TDETA_HES] ADD  CONSTRAINT [DF__TDETA_HES__CRT__4A43E4FB]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[TDETA_HES] ADD  CONSTRAINT [DF__TDETA_HES__tob__20389C96]  DEFAULT ((2)) FOR [tob]
GO
ALTER TABLE [dbo].[TDETA_HES2] ADD  CONSTRAINT [DF__TDETA_HES2__CRT__110B679F]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[TDETA_HES2] ADD  CONSTRAINT [DF__TDETA_HES2__tob__70547F4A]  DEFAULT ((2)) FOR [tob]
GO
ALTER TABLE [dbo].[TDETA_HES3] ADD  CONSTRAINT [DF__TDETA_HES3__CRT__7CCF64C8]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[TDETA_HES3] ADD  CONSTRAINT [DF__TDETA_HES3__tob__7148A383]  DEFAULT ((2)) FOR [tob]
GO
ALTER TABLE [dbo].[TDETA_HES4] ADD  CONSTRAINT [DF__TDETA_HES4__CRT__78FED3E4]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[TDETA_HES4] ADD  CONSTRAINT [DF__TDETA_HES4__tob__723CC7BC]  DEFAULT ((2)) FOR [tob]
GO
ALTER TABLE [dbo].[TFORMS] ADD  CONSTRAINT [DF__TFORMS__kind__02925FBF]  DEFAULT ((3)) FOR [kind]
GO
ALTER TABLE [dbo].[TFORMS] ADD  CONSTRAINT [DF__TFORMS__GRP__038683F8]  DEFAULT ((0)) FOR [GRP]
GO
ALTER TABLE [dbo].[TFORMS] ADD  CONSTRAINT [DF__TFORMS__CRT__4D555BD0]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[TOTA_HES] ADD  CONSTRAINT [DF__TOTA_HES__NUMBER__075714DC]  DEFAULT ((0)) FOR [NUMBER]
GO
ALTER TABLE [dbo].[TOTA_HES] ADD  CONSTRAINT [DF__TOTA_HES__NO_HES__084B3915]  DEFAULT ((0)) FOR [NO_HES]
GO
ALTER TABLE [dbo].[TOTA_HES] ADD  CONSTRAINT [DF__TOTA_HES__M_D__093F5D4E]  DEFAULT ((0)) FOR [M_D]
GO
ALTER TABLE [dbo].[TOTA_HES] ADD  CONSTRAINT [DF__TOTA_HES__GROUP__0A338187]  DEFAULT ((0)) FOR [GROUP]
GO
ALTER TABLE [dbo].[TOTA_HES] ADD  CONSTRAINT [DF__TOTA_HES__CRT__40BA7AC1]  DEFAULT (getdate()) FOR [CRT]
GO
ALTER TABLE [dbo].[DEED_DTL]  WITH NOCHECK ADD  CONSTRAINT [FK_DEED_DTL_DEED_HED] FOREIGN KEY([N_S])
REFERENCES [dbo].[DEED_HED] ([N_S])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[DEED_DTL] CHECK CONSTRAINT [FK_DEED_DTL_DEED_HED]
GO
ALTER TABLE [dbo].[DEED_DTL]  WITH NOCHECK ADD  CONSTRAINT [FK_DEED_DTL_HEAD_LST] FOREIGN KEY([NUMBER], [TAG])
REFERENCES [dbo].[HEAD_LST] ([NUMBER], [TAG])
GO
ALTER TABLE [dbo].[DEED_DTL] CHECK CONSTRAINT [FK_DEED_DTL_HEAD_LST]
GO
ALTER TABLE [dbo].[DEED_DTL]  WITH NOCHECK ADD  CONSTRAINT [FK_DEED_DTL_TCOD_BANKS] FOREIGN KEY([BANK])
REFERENCES [dbo].[TCOD_BANKS] ([CODE])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[DEED_DTL] CHECK CONSTRAINT [FK_DEED_DTL_TCOD_BANKS]
GO
ALTER TABLE [dbo].[DEED_DTL]  WITH NOCHECK ADD  CONSTRAINT [FK_DEED_DTL_TDETA_HES] FOREIGN KEY([HES_K], [HES_M], [HES_T])
REFERENCES [dbo].[TDETA_HES] ([N_KOL], [NUMBER], [TNUMBER])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[DEED_DTL] CHECK CONSTRAINT [FK_DEED_DTL_TDETA_HES]
GO
ALTER TABLE [dbo].[DETA_HES]  WITH NOCHECK ADD  CONSTRAINT [DETA_HES_FK00] FOREIGN KEY([N_KOL])
REFERENCES [dbo].[TOTA_HES] ([NUMBER])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[DETA_HES] CHECK CONSTRAINT [DETA_HES_FK00]
GO
ALTER TABLE [dbo].[EVENTS]  WITH NOCHECK ADD  CONSTRAINT [FK_EVENTS_TASKS] FOREIGN KEY([IDNUM])
REFERENCES [dbo].[TASKS] ([IDNUM])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[EVENTS] CHECK CONSTRAINT [FK_EVENTS_TASKS]
GO
ALTER TABLE [dbo].[HEAD_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_HEAD_LST_CUSTKIND] FOREIGN KEY([CUST_KIND])
REFERENCES [dbo].[CUSTKIND] ([CUST_COD])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[HEAD_LST] CHECK CONSTRAINT [FK_HEAD_LST_CUSTKIND]
GO
ALTER TABLE [dbo].[HEAD_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_HEAD_LST_DEED_HED] FOREIGN KEY([N_S])
REFERENCES [dbo].[DEED_HED] ([N_S])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[HEAD_LST] CHECK CONSTRAINT [FK_HEAD_LST_DEED_HED]
GO
ALTER TABLE [dbo].[HEAD_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_HEAD_LST_DEPART] FOREIGN KEY([DEPATMAN])
REFERENCES [dbo].[DEPART] ([DEPATMAN])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[HEAD_LST] CHECK CONSTRAINT [FK_HEAD_LST_DEPART]
GO
ALTER TABLE [dbo].[HEAD_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_HEAD_LST_PRICE_ELAMIE] FOREIGN KEY([PEPID])
REFERENCES [dbo].[PRICE_ELAMIE] ([PEPID])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[HEAD_LST] CHECK CONSTRAINT [FK_HEAD_LST_PRICE_ELAMIE]
GO
ALTER TABLE [dbo].[HEAD_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_HEAD_LST_PRICE_ELAMIETF] FOREIGN KEY([PEID])
REFERENCES [dbo].[PRICE_ELAMIETF] ([PEID])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[HEAD_LST] CHECK CONSTRAINT [FK_HEAD_LST_PRICE_ELAMIETF]
GO
ALTER TABLE [dbo].[HEAD_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_HEAD_LST_PRICE_PAYNO] FOREIGN KEY([MODAT_PPID])
REFERENCES [dbo].[PRICE_PAYNO] ([PPID])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[HEAD_LST] CHECK CONSTRAINT [FK_HEAD_LST_PRICE_PAYNO]
GO
ALTER TABLE [dbo].[HEAD_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_HEAD_LST_SHIFT] FOREIGN KEY([SHIFT])
REFERENCES [dbo].[SHIFT] ([SHIFT_ID])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[HEAD_LST] CHECK CONSTRAINT [FK_HEAD_LST_SHIFT]
GO
ALTER TABLE [dbo].[INVO_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_INVO_LST_HEAD_LST] FOREIGN KEY([NUMBER], [TAG])
REFERENCES [dbo].[HEAD_LST] ([NUMBER], [TAG])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[INVO_LST] CHECK CONSTRAINT [FK_INVO_LST_HEAD_LST]
GO
ALTER TABLE [dbo].[INVO_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_INVO_LST_STUF_FSK] FOREIGN KEY([CODE], [ANBAR])
REFERENCES [dbo].[STUF_FSK] ([CODE], [ANBAR])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[INVO_LST] CHECK CONSTRAINT [FK_INVO_LST_STUF_FSK]
GO
ALTER TABLE [dbo].[INVO_LST]  WITH NOCHECK ADD  CONSTRAINT [FK_INVO_LST_TCOD_VAHEDS] FOREIGN KEY([VAHED_K])
REFERENCES [dbo].[TCOD_VAHEDS] ([CODE])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[INVO_LST] CHECK CONSTRAINT [FK_INVO_LST_TCOD_VAHEDS]
GO
ALTER TABLE [dbo].[PAY2_ADVANCE_EXCL]  WITH CHECK ADD  CONSTRAINT [FK_AE_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_ADVANCE_EXCL] CHECK CONSTRAINT [FK_AE_EMP]
GO
ALTER TABLE [dbo].[PAY2_ATT_VALUE]  WITH CHECK ADD  CONSTRAINT [FK_AV_ATT] FOREIGN KEY([PER_ID], [EMP_ID])
REFERENCES [dbo].[PAY2_ATTENDANCE] ([PER_ID], [EMP_ID])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[PAY2_ATT_VALUE] CHECK CONSTRAINT [FK_AV_ATT]
GO
ALTER TABLE [dbo].[PAY2_ATT_VALUE]  WITH CHECK ADD  CONSTRAINT [FK_AV_ITEM] FOREIGN KEY([ITEM_ID])
REFERENCES [dbo].[PAY2_ITEM_DEF] ([ITEM_ID])
GO
ALTER TABLE [dbo].[PAY2_ATT_VALUE] CHECK CONSTRAINT [FK_AV_ITEM]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE]  WITH CHECK ADD  CONSTRAINT [FK_ATT_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] CHECK CONSTRAINT [FK_ATT_EMP]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE]  WITH CHECK ADD  CONSTRAINT [FK_ATT_PER] FOREIGN KEY([PER_ID])
REFERENCES [dbo].[PAY2_PERIOD] ([PER_ID])
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] CHECK CONSTRAINT [FK_ATT_PER]
GO
ALTER TABLE [dbo].[PAY2_CONTRACT]  WITH CHECK ADD  CONSTRAINT [FK_CON_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_CONTRACT] CHECK CONSTRAINT [FK_CON_EMP]
GO
ALTER TABLE [dbo].[PAY2_DECREE]  WITH CHECK ADD  CONSTRAINT [FK_DEC_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_DECREE] CHECK CONSTRAINT [FK_DEC_EMP]
GO
ALTER TABLE [dbo].[PAY2_DECREE]  WITH CHECK ADD  CONSTRAINT [FK_DEC_TMPL] FOREIGN KEY([TMPL_ID])
REFERENCES [dbo].[PAY2_ITEM_TEMPLATE] ([TMPL_ID])
GO
ALTER TABLE [dbo].[PAY2_DECREE] CHECK CONSTRAINT [FK_DEC_TMPL]
GO
ALTER TABLE [dbo].[PAY2_DECREE]  WITH CHECK ADD  CONSTRAINT [FK_DEC_WS] FOREIGN KEY([WS_ID])
REFERENCES [dbo].[PAY2_WORKSHOP] ([WS_ID])
GO
ALTER TABLE [dbo].[PAY2_DECREE] CHECK CONSTRAINT [FK_DEC_WS]
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE]  WITH CHECK ADD  CONSTRAINT [FK_DL_DEC] FOREIGN KEY([DEC_ID])
REFERENCES [dbo].[PAY2_DECREE] ([DEC_ID])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE] CHECK CONSTRAINT [FK_DL_DEC]
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE]  WITH CHECK ADD  CONSTRAINT [FK_DL_ITEM] FOREIGN KEY([ITEM_ID])
REFERENCES [dbo].[PAY2_ITEM_DEF] ([ITEM_ID])
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE] CHECK CONSTRAINT [FK_DL_ITEM]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE]  WITH CHECK ADD  CONSTRAINT [FK_EMP_JOB] FOREIGN KEY([JOB_ID])
REFERENCES [dbo].[PAY2_JOB] ([JOB_ID])
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] CHECK CONSTRAINT [FK_EMP_JOB]
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE]  WITH CHECK ADD  CONSTRAINT [FK_EMP_WS] FOREIGN KEY([WS_ID])
REFERENCES [dbo].[PAY2_WORKSHOP] ([WS_ID])
GO
ALTER TABLE [dbo].[PAY2_EMPLOYEE] CHECK CONSTRAINT [FK_EMP_WS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TEMPLATE]  WITH CHECK ADD  CONSTRAINT [FK_TMPL_WS] FOREIGN KEY([WS_ID])
REFERENCES [dbo].[PAY2_WORKSHOP] ([WS_ID])
GO
ALTER TABLE [dbo].[PAY2_ITEM_TEMPLATE] CHECK CONSTRAINT [FK_TMPL_WS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE]  WITH CHECK ADD  CONSTRAINT [FK_TL_ITEM] FOREIGN KEY([ITEM_ID])
REFERENCES [dbo].[PAY2_ITEM_DEF] ([ITEM_ID])
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] CHECK CONSTRAINT [FK_TL_ITEM]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE]  WITH CHECK ADD  CONSTRAINT [FK_TL_TMPL] FOREIGN KEY([TMPL_ID])
REFERENCES [dbo].[PAY2_ITEM_TEMPLATE] ([TMPL_ID])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] CHECK CONSTRAINT [FK_TL_TMPL]
GO
ALTER TABLE [dbo].[PAY2_LEAVE]  WITH CHECK ADD  CONSTRAINT [FK_LEV_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_LEAVE] CHECK CONSTRAINT [FK_LEV_EMP]
GO
ALTER TABLE [dbo].[PAY2_LEAVE]  WITH CHECK ADD  CONSTRAINT [FK_LEV_REFER] FOREIGN KEY([REFER_TO])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_LEAVE] CHECK CONSTRAINT [FK_LEV_REFER]
GO
ALTER TABLE [dbo].[PAY2_LEAVE_BAL]  WITH CHECK ADD  CONSTRAINT [FK_LB_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_LEAVE_BAL] CHECK CONSTRAINT [FK_LB_EMP]
GO
ALTER TABLE [dbo].[PAY2_LOAN]  WITH CHECK ADD  CONSTRAINT [FK_LN_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_LOAN] CHECK CONSTRAINT [FK_LN_EMP]
GO
ALTER TABLE [dbo].[PAY2_LOAN]  WITH CHECK ADD  CONSTRAINT [FK_LN_WS] FOREIGN KEY([WS_ID])
REFERENCES [dbo].[PAY2_WORKSHOP] ([WS_ID])
GO
ALTER TABLE [dbo].[PAY2_LOAN] CHECK CONSTRAINT [FK_LN_WS]
GO
ALTER TABLE [dbo].[PAY2_LOAN_SCHED]  WITH CHECK ADD  CONSTRAINT [FK_LS_LOAN] FOREIGN KEY([LOAN_ID])
REFERENCES [dbo].[PAY2_LOAN] ([LOAN_ID])
GO
ALTER TABLE [dbo].[PAY2_LOAN_SCHED] CHECK CONSTRAINT [FK_LS_LOAN]
GO
ALTER TABLE [dbo].[PAY2_OVERRIDE]  WITH CHECK ADD  CONSTRAINT [FK_OV_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_OVERRIDE] CHECK CONSTRAINT [FK_OV_EMP]
GO
ALTER TABLE [dbo].[PAY2_OVERRIDE]  WITH CHECK ADD  CONSTRAINT [FK_OV_ITEM] FOREIGN KEY([ITEM_ID])
REFERENCES [dbo].[PAY2_ITEM_DEF] ([ITEM_ID])
GO
ALTER TABLE [dbo].[PAY2_OVERRIDE] CHECK CONSTRAINT [FK_OV_ITEM]
GO
ALTER TABLE [dbo].[PAY2_PERIOD]  WITH CHECK ADD  CONSTRAINT [FK_PER_WS] FOREIGN KEY([WS_ID])
REFERENCES [dbo].[PAY2_WORKSHOP] ([WS_ID])
GO
ALTER TABLE [dbo].[PAY2_PERIOD] CHECK CONSTRAINT [FK_PER_WS]
GO
ALTER TABLE [dbo].[PAY2_RUN]  WITH CHECK ADD  CONSTRAINT [FK_RUN_PER] FOREIGN KEY([PER_ID])
REFERENCES [dbo].[PAY2_PERIOD] ([PER_ID])
GO
ALTER TABLE [dbo].[PAY2_RUN] CHECK CONSTRAINT [FK_RUN_PER]
GO
ALTER TABLE [dbo].[PAY2_RUN]  WITH CHECK ADD  CONSTRAINT [FK_RUN_PREV] FOREIGN KEY([PREV_RUN_ID])
REFERENCES [dbo].[PAY2_RUN] ([RUN_ID])
GO
ALTER TABLE [dbo].[PAY2_RUN] CHECK CONSTRAINT [FK_RUN_PREV]
GO
ALTER TABLE [dbo].[PAY2_RUN_DETAIL]  WITH CHECK ADD  CONSTRAINT [FK_RD_LINE] FOREIGN KEY([RUN_ID], [EMP_ID])
REFERENCES [dbo].[PAY2_RUN_LINE] ([RUN_ID], [EMP_ID])
ON DELETE CASCADE
GO
ALTER TABLE [dbo].[PAY2_RUN_DETAIL] CHECK CONSTRAINT [FK_RD_LINE]
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE]  WITH CHECK ADD  CONSTRAINT [FK_RL_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE] CHECK CONSTRAINT [FK_RL_EMP]
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE]  WITH CHECK ADD  CONSTRAINT [FK_RL_RUN] FOREIGN KEY([RUN_ID])
REFERENCES [dbo].[PAY2_RUN] ([RUN_ID])
GO
ALTER TABLE [dbo].[PAY2_RUN_LINE] CHECK CONSTRAINT [FK_RL_RUN]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT]  WITH CHECK ADD  CONSTRAINT [FK_SET_EMP] FOREIGN KEY([EMP_ID])
REFERENCES [dbo].[PAY2_EMPLOYEE] ([EMP_ID])
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] CHECK CONSTRAINT [FK_SET_EMP]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT]  WITH CHECK ADD  CONSTRAINT [FK_SET_PREV] FOREIGN KEY([PREV_SET_ID])
REFERENCES [dbo].[PAY2_SETTLEMENT] ([SET_ID])
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] CHECK CONSTRAINT [FK_SET_PREV]
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT]  WITH CHECK ADD  CONSTRAINT [FK_SET_WS] FOREIGN KEY([WS_ID])
REFERENCES [dbo].[PAY2_WORKSHOP] ([WS_ID])
GO
ALTER TABLE [dbo].[PAY2_SETTLEMENT] CHECK CONSTRAINT [FK_SET_WS]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP_ACC]  WITH CHECK ADD  CONSTRAINT [FK_WS_ACC] FOREIGN KEY([WS_ID])
REFERENCES [dbo].[PAY2_WORKSHOP] ([WS_ID])
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP_ACC] CHECK CONSTRAINT [FK_WS_ACC]
GO
ALTER TABLE [dbo].[TDETA_HES]  WITH NOCHECK ADD  CONSTRAINT [FK_TDETA_HES_DETA_HES] FOREIGN KEY([N_KOL], [NUMBER])
REFERENCES [dbo].[DETA_HES] ([N_KOL], [NUMBER])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[TDETA_HES] CHECK CONSTRAINT [FK_TDETA_HES_DETA_HES]
GO
ALTER TABLE [dbo].[TDETA_HES2]  WITH NOCHECK ADD  CONSTRAINT [FK_TDETA_HES2_TDETA_HES] FOREIGN KEY([N_KOL], [NUMBER], [TNUMBER])
REFERENCES [dbo].[TDETA_HES] ([N_KOL], [NUMBER], [TNUMBER])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[TDETA_HES2] CHECK CONSTRAINT [FK_TDETA_HES2_TDETA_HES]
GO
ALTER TABLE [dbo].[TDETA_HES3]  WITH NOCHECK ADD  CONSTRAINT [FK_TDETA_HES3_TDETA_HES2] FOREIGN KEY([N_KOL], [NUMBER], [TNUMBER], [TNUMBER2])
REFERENCES [dbo].[TDETA_HES2] ([N_KOL], [NUMBER], [TNUMBER], [TNUMBER2])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[TDETA_HES3] CHECK CONSTRAINT [FK_TDETA_HES3_TDETA_HES2]
GO
ALTER TABLE [dbo].[TDETA_HES4]  WITH NOCHECK ADD  CONSTRAINT [FK_TDETA_HES4_TDETA_HES3] FOREIGN KEY([N_KOL], [NUMBER], [TNUMBER], [TNUMBER2], [TNUMBER3])
REFERENCES [dbo].[TDETA_HES3] ([N_KOL], [NUMBER], [TNUMBER], [TNUMBER2], [TNUMBER3])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[TDETA_HES4] CHECK CONSTRAINT [FK_TDETA_HES4_TDETA_HES3]
GO
ALTER TABLE [dbo].[TOTA_HES]  WITH NOCHECK ADD  CONSTRAINT [TOTA_HES_FK00] FOREIGN KEY([NO_HES])
REFERENCES [dbo].[TCOD_HESKIND] ([CODE])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[TOTA_HES] CHECK CONSTRAINT [TOTA_HES_FK00]
GO
ALTER TABLE [dbo].[TOTA_HES]  WITH NOCHECK ADD  CONSTRAINT [TOTA_HES_FK01] FOREIGN KEY([M_D])
REFERENCES [dbo].[TCOD_HESVAZ] ([CODE])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[TOTA_HES] CHECK CONSTRAINT [TOTA_HES_FK01]
GO
ALTER TABLE [dbo].[TOTA_HES]  WITH NOCHECK ADD  CONSTRAINT [TOTA_HES_FK02] FOREIGN KEY([GROUP])
REFERENCES [dbo].[TCOD_HESGROUP] ([CODE])
ON UPDATE CASCADE
GO
ALTER TABLE [dbo].[TOTA_HES] CHECK CONSTRAINT [TOTA_HES_FK02]
GO
ALTER TABLE [dbo].[DEED_DTL]  WITH NOCHECK ADD  CONSTRAINT [CK DEED_DTL BED] CHECK  ((NOT [BED] IS NULL))
GO
ALTER TABLE [dbo].[DEED_DTL] CHECK CONSTRAINT [CK DEED_DTL BED]
GO
ALTER TABLE [dbo].[DEED_DTL]  WITH NOCHECK ADD  CONSTRAINT [CK DEED_DTL BES] CHECK  ((NOT [BES] IS NULL))
GO
ALTER TABLE [dbo].[DEED_DTL] CHECK CONSTRAINT [CK DEED_DTL BES]
GO
ALTER TABLE [dbo].[DEED_HED]  WITH NOCHECK ADD  CONSTRAINT [CK_DEED_HED] CHECK  (([date_s]>=(10101)))
GO
ALTER TABLE [dbo].[DEED_HED] CHECK CONSTRAINT [CK_DEED_HED]
GO
ALTER TABLE [dbo].[INVO_LST]  WITH NOCHECK ADD  CONSTRAINT [CK INVO_LST MABL] CHECK  ((NOT [MABL] IS NULL))
GO
ALTER TABLE [dbo].[INVO_LST] CHECK CONSTRAINT [CK INVO_LST MABL]
GO
ALTER TABLE [dbo].[INVO_LST]  WITH NOCHECK ADD  CONSTRAINT [CK INVO_LST MABL_K] CHECK  ((NOT [MABL_K] IS NULL))
GO
ALTER TABLE [dbo].[INVO_LST] CHECK CONSTRAINT [CK INVO_LST MABL_K]
GO
ALTER TABLE [dbo].[INVO_LST]  WITH NOCHECK ADD  CONSTRAINT [CK INVO_LST MEGH] CHECK  ((NOT [MEGH] IS NULL))
GO
ALTER TABLE [dbo].[INVO_LST] CHECK CONSTRAINT [CK INVO_LST MEGH]
GO
ALTER TABLE [dbo].[INVO_LST]  WITH NOCHECK ADD  CONSTRAINT [CK INVO_LST MEGHk] CHECK  ((NOT [MEGHk] IS NULL))
GO
ALTER TABLE [dbo].[INVO_LST] CHECK CONSTRAINT [CK INVO_LST MEGHk]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE]  WITH CHECK ADD  CONSTRAINT [CK_ATT_DAYS] CHECK  ((((([DAYS_TOLID]+[DAYS_EDARI])+[DAYS_KHADAMAT])+[DAYS_FOROSH])<=([WORK_DAYS]+(0.01))))
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] CHECK CONSTRAINT [CK_ATT_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE]  WITH CHECK ADD  CONSTRAINT [CK_ATT_DAYSB] CHECK  (([DAYSB]<=([WORK_DAYS]+(0.01))))
GO
ALTER TABLE [dbo].[PAY2_ATTENDANCE] CHECK CONSTRAINT [CK_ATT_DAYSB]
GO
ALTER TABLE [dbo].[PAY2_DECREE]  WITH CHECK ADD  CONSTRAINT [CK_DEC_SHIFT_MODE] CHECK  (([SHIFT_MODE]='FIXED' OR [SHIFT_MODE]='PCT'))
GO
ALTER TABLE [dbo].[PAY2_DECREE] CHECK CONSTRAINT [CK_DEC_SHIFT_MODE]
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE]  WITH CHECK ADD  CONSTRAINT [CK_DL_SHIFT_MODE_OV] CHECK  (([SHIFT_MODE_OV]='FIXED' OR [SHIFT_MODE_OV]='PCT'))
GO
ALTER TABLE [dbo].[PAY2_DECREE_LINE] CHECK CONSTRAINT [CK_DL_SHIFT_MODE_OV]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF]  WITH CHECK ADD  CONSTRAINT [CK_CALC_BASIS] CHECK  (([CALC_BASIS]=(3) OR [CALC_BASIS]=(2) OR [CALC_BASIS]=(1)))
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] CHECK CONSTRAINT [CK_CALC_BASIS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF]  WITH CHECK ADD  CONSTRAINT [CK_INS_BASE_DAYS] CHECK  (([INS_BASE_DAYS]=(2) OR [INS_BASE_DAYS]=(1)))
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] CHECK CONSTRAINT [CK_INS_BASE_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF]  WITH CHECK ADD  CONSTRAINT [CK_ITEM_TYPE] CHECK  (([ITEM_TYPE]>=(1) AND [ITEM_TYPE]<=(5)))
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] CHECK CONSTRAINT [CK_ITEM_TYPE]
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF]  WITH CHECK ADD  CONSTRAINT [CK_PAY_BASE_DAYS] CHECK  (([PAY_BASE_DAYS]=(2) OR [PAY_BASE_DAYS]=(1)))
GO
ALTER TABLE [dbo].[PAY2_ITEM_DEF] CHECK CONSTRAINT [CK_PAY_BASE_DAYS]
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE]  WITH CHECK ADD  CONSTRAINT [CK_TL_SHIFT_MODE_OV] CHECK  (([SHIFT_MODE_OV]='FIXED' OR [SHIFT_MODE_OV]='PCT'))
GO
ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] CHECK CONSTRAINT [CK_TL_SHIFT_MODE_OV]
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP]  WITH CHECK ADD  CONSTRAINT [CK_WS_SHIFT_MODE] CHECK  (([SHIFT_MODE]='FIXED' OR [SHIFT_MODE]='PCT'))
GO
ALTER TABLE [dbo].[PAY2_WORKSHOP] CHECK CONSTRAINT [CK_WS_SHIFT_MODE]
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_CALC_RUN]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- پارامترها:
--   @WS_ID        : شناسه کارگاه
--   @PER_ID       : شناسه دوره (از PAY2_PERIOD)
--   @PAYROLL_N_S  : شماره سند حقوق در DEED_HED (برای مساعده)
--   @CALC_BY      : کد کاربر محاسبه‌گر
--   @IS_RERUN     : 0=اول بار | 1=بازمحاسبه (RUN_NO جدید ایجاد می‌کند)
-- خروجی:
--   @NEW_RUN_ID   OUTPUT — شناسه PAY2_RUN ایجادشده
-- ================================================================
-- ================================================================
-- ۱. SP_PAY2_CALC_RUN — موتور محاسبه حقوق ماهیانه 
-- (نسخه نهایی: موتور قطعی دو دفتره / Dual-Track Engine + فیکس باگ Fallback)
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_CALC_RUN]
    @WS_ID       INT,
    @PER_ID      INT,
    @PAYROLL_N_S FLOAT,
    @CALC_BY     INT          = NULL,
    @IS_RERUN    BIT          = 0,
    @NEW_RUN_ID  INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET ANSI_WARNINGS OFF;

    -- 🚀 گام صفر: اعلان (DECLARE) تمامی متغیرها در سطح Batch برای جلوگیری از نشت اسکوپ در T-SQL
    DECLARE
        @MONTH_DAYS_MODE NVARCHAR(10), @MONTH_DAYS TINYINT,
        @OT_NORMAL_MULT DECIMAL(6,4), @OT_HOLIDAY_MULT DECIMAL(6,4),
        @OT_HOUR_BASE DECIMAL(6,4), @SHIFT_MODE NVARCHAR(10),
        @ROUND_MODE INT, @INS_WORKER_RATE DECIMAL(6,4),
        @INS_EMPLOYER_RATE DECIMAL(6,4), @INS_UNEMP_RATE DECIMAL(6,4),
        @INS_CEILING_APPLY BIT, @INS_CEILING BIGINT,
        @TAX_YEAR SMALLINT, @TAX_EXEMPT BIGINT,
        @TAX_DEDUCT_INS BIT, @TAX_DEP_APPLY BIT,
        @ADV_ENABLED BIT, @PERIOD_DATE BIGINT,
        @PERIOD_MONTH INT, @PERIOD_YEAR INT,
        @MONTHLY_PRORATE BIT;

    DECLARE @INS_DED_ID INT, @TAX_DED_ID INT, @LOAN_DED_ID INT, @ADV_DED_ID INT;
    DECLARE @PREV_RUN_ID INT, @PREV_STATUS TINYINT, @NEXT_RUN_NO SMALLINT = 1;
    DECLARE @IS_LEAP_YEAR BIT;

    -- متغیرهای حلقه پرسنل
    DECLARE @PER_START BIGINT, @PER_END BIGINT;
    DECLARE @WS_SHIFT_MODE NVARCHAR(10);
    DECLARE @EMP_ID INT, @IS_MANAGER BIT, @INS_TYPE TINYINT, @TAX_EXEMPT_FLAG BIT, @REGION_DEP TINYINT, @ACC_T NVARCHAR(50);
    
    DECLARE @WORK_DAYS DECIMAL(5,2), @DAYS DECIMAL(5,2), @DAYSB DECIMAL(5,2),
            @FRID_COUNT TINYINT, @TDAYS DECIMAL(5,2), @OT_NORMAL_H DECIMAL(6,2),
            @OT_HOLIDAY_H DECIMAL(6,2), @OT_ADMIN_H DECIMAL(6,2), @LEAVE_DAYS DECIMAL(5,2),
            @PERF_AMOUNT BIGINT, @TRANSP_AMOUNT BIGINT, @KASR_OTHER BIGINT;

    -- متغیرهای حلقه‌های احکام و اقلام
    DECLARE @DEC_ID INT, @DEC_FROM BIGINT, @DEC_TO BIGINT, @DEC_SHIFT_MODE NVARCHAR(10);
    DECLARE @DEC_ACTUAL_START BIGINT, @DEC_ACTUAL_END BIGINT, @DEC_ACTIVE_DAYS INT, @PRORATE_FACTOR DECIMAL(18,6);
    
    DECLARE @HAS_BOTH_SAL BIT, @DAILY_NOMINAL DECIMAL(18,2), @DAILY_OFFICIAL DECIMAL(18,2);
    DECLARE @INS_OFFICIAL_VALID BIT, @TAX_OFFICIAL_VALID BIT, @INS_DROP_SAL NVARCHAR(30), @TAX_DROP_SAL NVARCHAR(30);

    DECLARE @ITEM_ID INT, @ITEM_CODE NVARCHAR(30), @ITEM_TYPE TINYINT, @ITEM_AMOUNT DECIMAL(18,2),
            @ITEM_BASIS TINYINT, @ITEM_INS BIT, @ITEM_TAX BIT, @ITEM_PBD TINYINT, @ITEM_IBD TINYINT, @DL_SHIFT_MODE_OV NVARCHAR(10);
    DECLARE @OV_INS BIT, @OV_TAX BIT, @OV_BASIS TINYINT;
    DECLARE @CALC_AMOUNT BIGINT, @INS_CALC_AMOUNT BIGINT;
    DECLARE @PAY_DAYS DECIMAL(18,6), @BASE_DAYS_RAW DECIMAL(5,2), @INS_DAYS DECIMAL(18,6), @INS_DAYS_RAW DECIMAL(5,2);
    DECLARE @FULL_MONTH BIGINT, @FULL_MONTH_INS BIGINT, @NAHAR_DAYS DECIMAL(18,6), @EFF_SHIFT_MODE NVARCHAR(10);

    -- متغیرهای محاسباتی نهایی
    DECLARE @TOTAL_NOMINAL_BASE BIGINT, @TOTAL_OFFICIAL_BASE BIGINT;
    DECLARE @EFFECTIVE_HOURLY DECIMAL(18,2), @OFFICIAL_HOURLY DECIMAL(18,2);
    DECLARE @FB_NOMINAL BIGINT, @FB_OFFICIAL BIGINT;

    DECLARE @GROSS_PAY BIGINT, @INS_BASE BIGINT, @INS_WORKER BIGINT, @INS_EMPLOYER BIGINT;
    DECLARE @EFFECTIVE_INS_CEILING BIGINT, @EMP_IS_JANBAZ BIT, @JANBAZ_RATE DECIMAL(6,4);
    DECLARE @TAX_BASE BIGINT, @TAX_AMOUNT BIGINT;
    DECLARE @ADVANCE_DED BIGINT, @LOAN_DED BIGINT, @OTHER_DED BIGINT, @TOTAL_DED BIGINT, @NET_PAY BIGINT;
    DECLARE @LEAVE_BAL_DAYS DECIMAL(5,2), @LOAN_BAL BIGINT, @LEAVE_MIN_USED INT;

    DECLARE @ItemCalc TABLE (
        ITEM_ID INT, ITEM_CODE NVARCHAR(30), ITEM_TYPE TINYINT,
        AMOUNT BIGINT, INS_AMOUNT BIGINT, INS_SUBJECT BIT, TAX_SUBJECT BIT
    );

    -- گام ۱ — بارگذاری تنظیمات
    SELECT
        @MONTH_DAYS_MODE   = ISNULL(MAX(CASE WHEN CFG_KEY='MONTH_DAYS_MODE'    THEN CFG_VALUE END), '30'),
        @OT_NORMAL_MULT    = ISNULL(MAX(CASE WHEN CFG_KEY='OT_NORMAL_MULT'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOLIDAY_MULT   = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOLIDAY_MULT'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOUR_BASE      = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOUR_BASE'       THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 7.33),
        @SHIFT_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='SHIFT_MODE'         THEN CFG_VALUE END), 'PCT'),
        @ROUND_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='ROUND_MODE'         THEN CAST(CFG_VALUE AS INT) END), 1),
        @INS_WORKER_RATE   = ISNULL(MAX(CASE WHEN CFG_KEY='INS_WORKER_RATE'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.07),
        @INS_EMPLOYER_RATE = ISNULL(MAX(CASE WHEN CFG_KEY='INS_EMPLOYER_RATE'  THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.20),
        @INS_UNEMP_RATE    = ISNULL(MAX(CASE WHEN CFG_KEY='INS_UNEMP_RATE'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.03),
        @INS_CEILING       = ISNULL(MAX(CASE WHEN CFG_KEY='INS_CEILING_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 999999999),
        @TAX_YEAR          = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_YEAR'           THEN CAST(CFG_VALUE AS SMALLINT) END), 1403),
        @TAX_EXEMPT        = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_EXEMPT_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 0),
        @INS_CEILING_APPLY = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='INS_CEILING_APPLY'  THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEDUCT_INS    = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEDUCT_INS'     THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEP_APPLY     = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEPRIVATION_APPLY' THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @ADV_ENABLED       = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='ADV_ENABLED'        THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @MONTHLY_PRORATE   = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='MONTHLY_ITEM_PRORATE' THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0)
    FROM PAY2_CONFIG;

    SELECT @PERIOD_DATE = PERIOD_DATE FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @PER_ID;
    IF @PERIOD_DATE IS NULL
    BEGIN
        RAISERROR(N'دوره %d یافت نشد.', 16, 1, @PER_ID);
        RETURN;
    END;

    SET @PERIOD_MONTH = (@PERIOD_DATE / 100) % 100;
    SET @PERIOD_YEAR  = @PERIOD_DATE / 10000;
    SET @IS_LEAP_YEAR = CASE WHEN ((25 * @PERIOD_YEAR + 11) % 33) < 8 THEN 1 ELSE 0 END; 

    SET @MONTH_DAYS = CASE
        WHEN @MONTH_DAYS_MODE = '30' THEN 30
        WHEN @PERIOD_MONTH <= 6 THEN 31
        WHEN @PERIOD_MONTH BETWEEN 7 AND 11 THEN 30
        WHEN @PERIOD_MONTH = 12 AND @IS_LEAP_YEAR = 1 THEN 30
        ELSE 29
    END;

    SET @INS_DED_ID  = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='INS_DED');
    SET @TAX_DED_ID  = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='TAX_DED');
    SET @LOAN_DED_ID = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='LOAN_DED');
    SET @ADV_DED_ID  = (SELECT ITEM_ID FROM PAY2_ITEM_DEF WHERE ITEM_CODE='ADVANCE_DED');

    -- گام ۲ — ایجاد هدر PAY2_RUN
    IF @IS_RERUN = 1
    BEGIN
        SELECT TOP 1 @PREV_RUN_ID = RUN_ID, @NEXT_RUN_NO = RUN_NO + 1, @PREV_STATUS = STATUS
        FROM PAY2_RUN WHERE PER_ID = @PER_ID AND IS_LATEST = 1 ORDER BY RUN_NO DESC;

        IF @PREV_STATUS >= 2
        BEGIN
            RAISERROR(N'اجرای قبلی تأیید نهایی شده است. دیتابیس اجازه بازمحاسبه را نمی‌دهد.', 16, 1);
            RETURN;
        END

        IF @PREV_RUN_ID IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM PAY2_RUN WHERE RUN_ID = @PREV_RUN_ID AND STATUS = 1)
               AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @PREV_RUN_ID)
            BEGIN
                EXEC SP_PAY2_REVERT_RUN @RUN_ID = @PREV_RUN_ID, @REVERT_BY = @CALC_BY;
            END
        END

        UPDATE PAY2_RUN SET IS_LATEST = 0 WHERE PER_ID = @PER_ID;
    END;

    INSERT INTO PAY2_RUN (PER_ID, RUN_NO, IS_LATEST, CALC_AT, CALC_BY, STATUS, PREV_RUN_ID)
    VALUES (@PER_ID, @NEXT_RUN_NO, 1, GETDATE(), @CALC_BY, 1, @PREV_RUN_ID);

    SET @NEW_RUN_ID = SCOPE_IDENTITY();

    CREATE TABLE #AdvResult (EMP_ID INT, PCODE NVARCHAR(50), FULL_NAME NVARCHAR(150), RAW_BALANCE BIGINT, MANUAL_EXCL BIGINT, ADVANCE_DEDUCTION BIGINT);
    IF @ADV_ENABLED = 1
    BEGIN
        INSERT INTO #AdvResult (EMP_ID, PCODE, FULL_NAME, RAW_BALANCE, MANUAL_EXCL, ADVANCE_DEDUCTION)
        EXEC SP_PAY2_GET_ADVANCES @PERIOD_DATE = @PERIOD_DATE, @PAYROLL_N_S = @PAYROLL_N_S, @WS_ID = @WS_ID;
    END;

    SELECT @WS_SHIFT_MODE = NULLIF(SHIFT_MODE, N'') FROM PAY2_WORKSHOP WHERE WS_ID = @WS_ID;

    DECLARE cur_emp CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
        SELECT E.EMP_ID, E.IS_MANAGER, E.INS_TYPE, E.TAX_EXEMPT, E.REGION_DEPRIVATION, E.ACC_T
        FROM PAY2_EMPLOYEE E
        WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
          AND EXISTS (SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID = @PER_ID AND A.EMP_ID = E.EMP_ID);

    OPEN cur_emp;
    FETCH NEXT FROM cur_emp INTO @EMP_ID, @IS_MANAGER, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;

    -- گام ۳ — حلقه روی پرسنل فعال کارگاه
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DELETE FROM @ItemCalc;
        
        -- 🚀 ریست صریح مقادیر در هر چرخش حلقه پرسنل
        SET @HAS_BOTH_SAL = 0;
        SET @DAILY_NOMINAL = 0; SET @DAILY_OFFICIAL = 0;
        SET @TOTAL_NOMINAL_BASE = 0; SET @TOTAL_OFFICIAL_BASE = 0;
        SET @EFFECTIVE_HOURLY = 0; SET @OFFICIAL_HOURLY = 0;

        SELECT
            @WORK_DAYS = ISNULL(WORK_DAYS,0), @DAYS = ISNULL(DAYS,0), @DAYSB = ISNULL(DAYSB,0),
            @FRID_COUNT = ISNULL(FRID_COUNT,0), @TDAYS = ISNULL(TDAYS,0), @OT_NORMAL_H = ISNULL(OT_NORMAL_H,0),
            @OT_HOLIDAY_H = ISNULL(OT_HOLIDAY_H,0), @OT_ADMIN_H = ISNULL(OT_ADMIN_H,0), @LEAVE_DAYS = ISNULL(LEAVE_DAYS,0),
            @PERF_AMOUNT = ISNULL(PERF_AMOUNT,0), @TRANSP_AMOUNT = ISNULL(TRANSP_AMOUNT,0), @KASR_OTHER = ISNULL(KASR_OTHER,0)
        FROM PAY2_ATTENDANCE WHERE PER_ID = @PER_ID AND EMP_ID = @EMP_ID;

        SET @PER_START = @PERIOD_DATE + 1;
        SET @PER_END   = @PERIOD_DATE + @MONTH_DAYS;

        DECLARE cur_dec CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
            SELECT DEC_ID, EFF_FROM, ISNULL(EFF_TO, 99991231), NULLIF(SHIFT_MODE, N'')
            FROM PAY2_DECREE
            WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1
              AND EFF_FROM <= @PER_END
              AND (EFF_TO IS NULL OR EFF_TO >= @PER_START)
            ORDER BY EFF_FROM;

        OPEN cur_dec;
        FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @DEC_ACTUAL_START = CASE WHEN @DEC_FROM > @PER_START THEN @DEC_FROM ELSE @PER_START END;
            SET @DEC_ACTUAL_END   = CASE WHEN @DEC_TO < @PER_END THEN @DEC_TO ELSE @PER_END END;
            SET @DEC_ACTIVE_DAYS = 0;

            IF @DEC_ACTUAL_START <= @DEC_ACTUAL_END
                SET @DEC_ACTIVE_DAYS = (@DEC_ACTUAL_END % 100) - (@DEC_ACTUAL_START % 100) + 1;

            IF @DEC_ACTIVE_DAYS > 0
            BEGIN
                SET @PRORATE_FACTOR = CAST(@DEC_ACTIVE_DAYS AS DECIMAL(18,6)) / CAST(@MONTH_DAYS AS DECIMAL(18,6));

                SELECT 
                    @DAILY_NOMINAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE = 'BASE_SAL' THEN DL.AMOUNT END), @DAILY_NOMINAL),
                    @DAILY_OFFICIAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE = 'BASE_SAL_B' THEN DL.AMOUNT END), @DAILY_OFFICIAL)
                FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID
                WHERE DL.DEC_ID = @DEC_ID;

                -- 🚨 [CRITICAL FIX]: Fallback دقیقاً در جای درست، قبل از اعمال فرمول‌های درصدی
                IF @DAILY_NOMINAL = 0 SET @DAILY_NOMINAL = @DAILY_OFFICIAL;
                IF @DAILY_OFFICIAL = 0 SET @DAILY_OFFICIAL = @DAILY_NOMINAL;

                DECLARE cur_line CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
                    SELECT DL.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, ISNULL(DL.AMOUNT, 0),
                        DL.SHIFT_MODE_OV,
                        ISNULL(DL.BASIS_OV, ID.CALC_BASIS), ISNULL(DL.INS_OV, ID.INS_SUBJECT), ISNULL(DL.TAX_OV, ID.TAX_SUBJECT), ID.PAY_BASE_DAYS, ID.INS_BASE_DAYS
                    FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID
                    WHERE DL.DEC_ID = @DEC_ID AND ID.IS_ACTIVE = 1 AND ID.ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
                    ORDER BY ID.SORT_ORDER;

                OPEN cur_line;
                FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @OV_INS = NULL; SET @OV_TAX = NULL; SET @OV_BASIS = NULL;
                    SELECT TOP 1 @OV_INS = INS_OV, @OV_TAX = TAX_OV, @OV_BASIS = BASIS_OV
                    FROM PAY2_OVERRIDE WHERE EMP_ID = @EMP_ID AND ITEM_ID = @ITEM_ID AND VALID_FROM <= @PERIOD_DATE AND (VALID_TO IS NULL OR VALID_TO >= @PERIOD_DATE) ORDER BY VALID_FROM DESC;

                    IF @OV_INS IS NOT NULL SET @ITEM_INS = @OV_INS;
                    IF @OV_TAX IS NOT NULL SET @ITEM_TAX = @OV_TAX;
                    IF @OV_BASIS IS NOT NULL SET @ITEM_BASIS = @OV_BASIS;

                    SET @PAY_DAYS      = (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @PRORATE_FACTOR;
                    SET @BASE_DAYS_RAW = (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END);
                    SET @INS_DAYS      = (CASE @ITEM_IBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @PRORATE_FACTOR;
                    SET @INS_DAYS_RAW  = (CASE @ITEM_IBD WHEN 1 THEN @DAYS ELSE @DAYSB END);

                    IF @ITEM_CODE IN ('BASE_SAL', 'BASE_SAL_B')
                    BEGIN
                        SET @CALC_AMOUNT     = CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * @INS_DAYS AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE IN ('HOME','CHILDREN','GROCERY')
                    BEGIN
                        SET @FULL_MONTH     = CASE WHEN @BASE_DAYS_RAW >= 28 THEN CAST(@ITEM_AMOUNT AS BIGINT) ELSE CAST(@ITEM_AMOUNT * (@BASE_DAYS_RAW / 30.0) AS BIGINT) END;
                        SET @FULL_MONTH_INS = CASE WHEN @INS_DAYS_RAW  >= 28 THEN CAST(@ITEM_AMOUNT AS BIGINT) ELSE CAST(@ITEM_AMOUNT * (@INS_DAYS_RAW  / 30.0) AS BIGINT) END;
                        SET @CALC_AMOUNT     = CAST(@FULL_MONTH     * @PRORATE_FACTOR AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@FULL_MONTH_INS * @PRORATE_FACTOR AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE = 'NAHAR'
                    BEGIN
                        SET @NAHAR_DAYS = (@DAYSB - @FRID_COUNT - @LEAVE_DAYS + @TDAYS) * @PRORATE_FACTOR;
                        SET @CALC_AMOUNT = CASE WHEN @NAHAR_DAYS > 0 THEN CAST(@ITEM_AMOUNT * @NAHAR_DAYS AS BIGINT) ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT) END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_CODE = 'SHIFT'
                    BEGIN
                        SET @EFF_SHIFT_MODE = COALESCE(NULLIF(@DL_SHIFT_MODE_OV, N''), @DEC_SHIFT_MODE, @WS_SHIFT_MODE, @SHIFT_MODE, 'PCT');
                        IF @EFF_SHIFT_MODE = 'FIXED'
                        BEGIN
                            SET @CALC_AMOUNT = CAST(@ITEM_AMOUNT * (@PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT);
                            SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * (@INS_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT);
                        END
                        ELSE
                        BEGIN
                            -- حق شیفت پرداختی از اسمی، حق شیفت بیمه از رسمی
                            SET @CALC_AMOUNT = CAST(ROUND((@DAILY_NOMINAL * @PAY_DAYS * @ITEM_AMOUNT / 100.0), 0) AS BIGINT);
                            SET @INS_CALC_AMOUNT = CAST(ROUND((@DAILY_OFFICIAL * @INS_DAYS * @ITEM_AMOUNT / 100.0), 0) AS BIGINT);
                        END
                    END
                    ELSE IF @ITEM_BASIS = 3
                    BEGIN
                        SET @CALC_AMOUNT =
                            CASE @ITEM_CODE
                                WHEN 'OT_NORMAL'  THEN CAST(@ITEM_AMOUNT * @OT_NORMAL_H  AS BIGINT)
                                WHEN 'OT_HOLIDAY' THEN CAST(@ITEM_AMOUNT * @OT_HOLIDAY_H AS BIGINT)
                                WHEN 'OT_ADMIN'   THEN CAST(@ITEM_AMOUNT * @OT_ADMIN_H   AS BIGINT)
                                ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS * @OT_HOUR_BASE AS BIGINT)
                            END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_BASIS = 2
                    BEGIN
                        SET @CALC_AMOUNT = CASE
                            WHEN @MONTHLY_PRORATE = 1
                                THEN CAST(@ITEM_AMOUNT * (@PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT)
                            ELSE CAST(@ITEM_AMOUNT * @PRORATE_FACTOR AS BIGINT)
                        END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_BASIS = 1
                    BEGIN
                        SET @CALC_AMOUNT     = CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * @INS_DAYS AS BIGINT);
                    END
                    ELSE
                    BEGIN
                        SET @CALC_AMOUNT = ISNULL(@ITEM_AMOUNT, 0);
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END

                    INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
                    VALUES (@ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @CALC_AMOUNT, @INS_CALC_AMOUNT, @ITEM_INS, @ITEM_TAX);

                    FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;
                END;
                CLOSE cur_line; DEALLOCATE cur_line;
            END;

            FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;
        END;
        CLOSE cur_dec; DEALLOCATE cur_dec;

        -- 🚨 باگ‌فیکس: خطوط Fallback از اینجا به طور کامل حذف شدند، زیرا در بالای حلقه اقلام انجام شده است.

        IF EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL') AND EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B')
            SET @HAS_BOTH_SAL = 1;

        -- گام ۶ — افزودن آیتم‌های متغیر
        SET @TOTAL_NOMINAL_BASE = ISNULL((
            SELECT SUM(AMOUNT) FROM @ItemCalc 
            WHERE ITEM_CODE = 'BASE_SAL' OR (@HAS_BOTH_SAL = 0 AND ITEM_CODE = 'BASE_SAL_B')
        ), 0);

        SET @TOTAL_OFFICIAL_BASE = ISNULL((
            SELECT SUM(AMOUNT) FROM @ItemCalc 
            WHERE ITEM_CODE = 'BASE_SAL_B' OR (@HAS_BOTH_SAL = 0 AND ITEM_CODE = 'BASE_SAL')
        ), 0);

        -- ریل اسمی (برای پرداختی اضافه‌کار)
        IF @DAYSB > 0 AND @OT_HOUR_BASE > 0
        BEGIN
            SET @EFFECTIVE_HOURLY = ISNULL((CAST(@TOTAL_NOMINAL_BASE AS DECIMAL(18,2)) / @DAYSB) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END
        ELSE IF @OT_HOUR_BASE > 0
        BEGIN
            SET @FB_NOMINAL = ISNULL((SELECT TOP 1 CAST(DL.AMOUNT AS BIGINT) FROM PAY2_DECREE D INNER JOIN PAY2_DECREE_LINE DL ON D.DEC_ID = DL.DEC_ID INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE D.EMP_ID = @EMP_ID AND D.IS_CONFIRMED = 1 AND ID.ITEM_CODE = 'BASE_SAL' ORDER BY D.EFF_FROM DESC), 0);
            IF @FB_NOMINAL = 0
                SELECT TOP 1 @FB_NOMINAL = CAST(DL.AMOUNT AS BIGINT) FROM PAY2_DECREE D INNER JOIN PAY2_DECREE_LINE DL ON D.DEC_ID = DL.DEC_ID INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE D.EMP_ID = @EMP_ID AND D.IS_CONFIRMED = 1 AND ID.ITEM_CODE = 'BASE_SAL_B' ORDER BY D.EFF_FROM DESC;
            
            SET @EFFECTIVE_HOURLY = ISNULL(CAST(@FB_NOMINAL AS DECIMAL(18,2)) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END

        -- ریل رسمی (برای بیمه و مالیات اضافه‌کار)
        IF @DAYS > 0 AND @OT_HOUR_BASE > 0
        BEGIN
            SET @OFFICIAL_HOURLY = ISNULL((CAST(@TOTAL_OFFICIAL_BASE AS DECIMAL(18,2)) / @DAYS) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END
        ELSE IF @OT_HOUR_BASE > 0
        BEGIN
            SET @FB_OFFICIAL = ISNULL((SELECT TOP 1 CAST(DL.AMOUNT AS BIGINT) FROM PAY2_DECREE D INNER JOIN PAY2_DECREE_LINE DL ON D.DEC_ID = DL.DEC_ID INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE D.EMP_ID = @EMP_ID AND D.IS_CONFIRMED = 1 AND ID.ITEM_CODE = 'BASE_SAL_B' ORDER BY D.EFF_FROM DESC), 0);
            IF @FB_OFFICIAL = 0
                SELECT TOP 1 @FB_OFFICIAL = CAST(DL.AMOUNT AS BIGINT) FROM PAY2_DECREE D INNER JOIN PAY2_DECREE_LINE DL ON D.DEC_ID = DL.DEC_ID INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE D.EMP_ID = @EMP_ID AND D.IS_CONFIRMED = 1 AND ID.ITEM_CODE = 'BASE_SAL' ORDER BY D.EFF_FROM DESC;
                
            SET @OFFICIAL_HOURLY = ISNULL(CAST(@FB_OFFICIAL AS DECIMAL(18,2)) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END

        IF @OT_NORMAL_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_NORMAL')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_NORMAL', 2, CAST(@EFFECTIVE_HOURLY * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_NORMAL';

        IF @OT_HOLIDAY_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_HOLIDAY')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_HOLIDAY', 2, CAST(@EFFECTIVE_HOURLY * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_HOLIDAY';

        IF @OT_ADMIN_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_ADMIN')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_ADMIN', 2, CAST(@EFFECTIVE_HOURLY * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'OT_ADMIN';

        IF @PERF_AMOUNT > 0
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'PERF_BONUS', 2, @PERF_AMOUNT, @PERF_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'PERF_BONUS';

        IF @TRANSP_AMOUNT > 0
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'TRANSP', 2, @TRANSP_AMOUNT, @TRANSP_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'TRANSP';

        INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT AV.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, AV.VALUE, AV.VALUE, ID.INS_SUBJECT, ID.TAX_SUBJECT
        FROM PAY2_ATT_VALUE AV INNER JOIN PAY2_ITEM_DEF ID ON AV.ITEM_ID = ID.ITEM_ID
        WHERE AV.PER_ID = @PER_ID AND AV.EMP_ID = @EMP_ID AND AV.VALUE <> 0
          AND NOT EXISTS (SELECT 1 FROM @ItemCalc X WHERE X.ITEM_ID = AV.ITEM_ID);

        -- گام ۷ — محاسبه بیمه
        SET @GROSS_PAY = 0; SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0;

        -- ناخالص پرداختی بر اساس حقوق اسمی (با جلوگیری از دوبارشماری)
        SELECT @GROSS_PAY = ISNULL(SUM(AMOUNT), 0) 
        FROM @ItemCalc 
        WHERE ITEM_TYPE IN (1, 2) AND (@HAS_BOTH_SAL = 0 OR ITEM_CODE <> 'BASE_SAL_B');

        -- 🚀 مبنای بیمه بر اساس حقوق رسمی (با استفاده از INS_AMOUNT)
        SET @INS_OFFICIAL_VALID = 0; SET @INS_DROP_SAL = NULL;
        IF @HAS_BOTH_SAL = 1
        BEGIN
            IF EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B' AND INS_SUBJECT = 1 AND INS_AMOUNT <> 0)
                SET @INS_OFFICIAL_VALID = 1;
            SET @INS_DROP_SAL = CASE WHEN @INS_OFFICIAL_VALID = 1 THEN 'BASE_SAL' ELSE 'BASE_SAL_B' END;
        END;

        SELECT @INS_BASE = ISNULL(SUM(INS_AMOUNT), 0)
        FROM @ItemCalc
        WHERE INS_SUBJECT = 1 AND ITEM_TYPE IN (1, 2) AND (@INS_DROP_SAL IS NULL OR ITEM_CODE <> @INS_DROP_SAL);

        SET @EFFECTIVE_INS_CEILING = CAST((@INS_CEILING / 30.0) * (CASE WHEN @DAYSB > 0 THEN @DAYSB ELSE @DAYS END) AS BIGINT);
        IF @INS_CEILING_APPLY = 1 AND @INS_TYPE <> 3
            SET @INS_BASE = CASE WHEN @INS_BASE > @EFFECTIVE_INS_CEILING THEN @EFFECTIVE_INS_CEILING ELSE @INS_BASE END;

        IF @INS_TYPE = 3
        BEGIN
            SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0;
        END;
        ELSE
        BEGIN
            SET @INS_WORKER = ISNULL(CAST(@INS_BASE * @INS_WORKER_RATE AS BIGINT), 0);
            SET @EMP_IS_JANBAZ = ISNULL((SELECT IS_JANBAZ FROM PAY2_EMPLOYEE WHERE EMP_ID = @EMP_ID), 0);
            SET @JANBAZ_RATE = ISNULL(CAST((SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY='INS_JANBAZ_RATE') AS DECIMAL(6,4)), 0.18);

            IF @EMP_IS_JANBAZ = 1
                SET @INS_EMPLOYER = ISNULL(CAST(@INS_BASE * @JANBAZ_RATE AS BIGINT), 0);
            ELSE
                SET @INS_EMPLOYER = ISNULL(CAST(@INS_BASE * (@INS_EMPLOYER_RATE + CASE WHEN ISNULL(@IS_MANAGER,0)=0 THEN @INS_UNEMP_RATE ELSE 0 END) AS BIGINT), 0);
        END;

        -- گام ۸ — محاسبه مالیات
        SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        IF @TAX_EXEMPT_FLAG = 1
        BEGIN
            SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        END;
        ELSE
        BEGIN
            -- 🚀 مالیات کاملاً بر اساس حقوق رسمی و مقادیر INS_AMOUNT محاسبه می‌شود
            SET @TAX_OFFICIAL_VALID = 0; SET @TAX_DROP_SAL = NULL;
            IF @HAS_BOTH_SAL = 1
            BEGIN
                IF EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B' AND TAX_SUBJECT = 1 AND INS_AMOUNT <> 0)
                    SET @TAX_OFFICIAL_VALID = 1;
                SET @TAX_DROP_SAL = CASE WHEN @TAX_OFFICIAL_VALID = 1 THEN 'BASE_SAL' ELSE 'BASE_SAL_B' END;
            END;

            SELECT @TAX_BASE = ISNULL(SUM(INS_AMOUNT), 0)
            FROM @ItemCalc
            WHERE TAX_SUBJECT = 1 AND ITEM_TYPE IN (1, 2) AND (@TAX_DROP_SAL IS NULL OR ITEM_CODE <> @TAX_DROP_SAL);
            
            IF @TAX_DEDUCT_INS = 1 SET @TAX_BASE = @TAX_BASE - @INS_WORKER;
            SET @TAX_BASE = CASE WHEN @TAX_BASE > @TAX_EXEMPT THEN @TAX_BASE - @TAX_EXEMPT ELSE 0 END;
            IF @TAX_DEP_APPLY = 1 AND @REGION_DEP > 0 SET @TAX_BASE = CAST(@TAX_BASE * (1.0 - @REGION_DEP / 100.0) AS BIGINT);
            SET @TAX_AMOUNT = ISNULL([dbo].[FN_PAY2_CALC_TAX](@TAX_BASE * 12, @TAX_YEAR) / 12, 0);
            IF @TAX_AMOUNT < 0 SET @TAX_AMOUNT = 0;
        END;

        SET @ADVANCE_DED = 0;
        IF @ADV_ENABLED = 1 SELECT @ADVANCE_DED = ISNULL(ADVANCE_DEDUCTION, 0) FROM #AdvResult WHERE EMP_ID = @EMP_ID;

        SET @LOAN_DED = 0;
        SELECT @LOAN_DED = ISNULL(SUM(LS.AMOUNT), 0) FROM PAY2_LOAN_SCHED LS INNER JOIN PAY2_LOAN L ON LS.LOAN_ID = L.LOAN_ID
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1 AND LS.DUE_PERIOD = @PERIOD_DATE AND LS.RUN_ID IS NULL;

        SET @OTHER_DED = ISNULL(@KASR_OTHER, 0);
        SET @TOTAL_DED = @INS_WORKER + @TAX_AMOUNT + @LOAN_DED + @ADVANCE_DED + @OTHER_DED;
        
        -- فرمول تراز: پیدا کردن اختلاف گرد کردن و اعمال آن روی ناخالص پرداختی
        DECLARE @RAW_NET BIGINT = @GROSS_PAY - @TOTAL_DED;
        SET @NET_PAY = @RAW_NET;

        IF @ROUND_MODE > 1 
            SET @NET_PAY = ISNULL(ROUND(CAST(@RAW_NET AS FLOAT) / @ROUND_MODE, 0) * @ROUND_MODE, 0);

        -- اختلافی که بخاطر گرد کردن ایجاد شده را به ناخالص اضافه/کم میکنیم تا معادله تراز بماند
        DECLARE @ROUNDING_DIFF BIGINT = @NET_PAY - @RAW_NET;
        SET @GROSS_PAY = @GROSS_PAY + @ROUNDING_DIFF; 

        SET @LEAVE_BAL_DAYS = NULL;
        SELECT @LEAVE_BAL_DAYS = CAST(BALANCE_MIN AS DECIMAL(10,2)) / 440.0 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;
        
        SET @LOAN_BAL = NULL;
        SELECT @LOAN_BAL = ISNULL(SUM(BALANCE), 0) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID;

        INSERT INTO PAY2_RUN_LINE (
            RUN_ID, EMP_ID, WORK_DAYS, GROSS_PAY, INS_BASE, INS_WORKER, INS_EMPLOYER, TAX_BASE, TAX_AMOUNT,
            LOAN_DED, ADVANCE_DED, OTHER_DED, TOTAL_DED, NET_PAY, LEAVE_BAL_DAYS, LOAN_BALANCE, ADVANCE_BALANCE_SNAP
        ) VALUES (
            @NEW_RUN_ID, @EMP_ID, @DAYSB, @GROSS_PAY, @INS_BASE, @INS_WORKER, @INS_EMPLOYER, @TAX_BASE, @TAX_AMOUNT,
            @LOAN_DED, @ADVANCE_DED, @OTHER_DED, @TOTAL_DED, @NET_PAY, @LEAVE_BAL_DAYS, @LOAN_BAL, @ADVANCE_DED
        );

        INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT @NEW_RUN_ID, @EMP_ID, ITEM_ID, SUM(AMOUNT), MAX(CAST(INS_SUBJECT AS INT)), MAX(CAST(TAX_SUBJECT AS INT))
        FROM @ItemCalc GROUP BY ITEM_ID HAVING SUM(AMOUNT) <> 0;

        IF @INS_WORKER  > 0 AND @INS_DED_ID IS NOT NULL INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@INS_DED_ID, @INS_WORKER, 0,0);
        IF @TAX_AMOUNT  > 0 AND @TAX_DED_ID IS NOT NULL INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@TAX_DED_ID, @TAX_AMOUNT, 0,0);
        IF @LOAN_DED    > 0 AND @LOAN_DED_ID IS NOT NULL INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@LOAN_DED_ID,@LOAN_DED,   0,0);
        IF @ADVANCE_DED > 0 AND @ADV_DED_ID IS NOT NULL INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, INS_SUBJECT, TAX_SUBJECT) VALUES (@NEW_RUN_ID,@EMP_ID,@ADV_DED_ID, @ADVANCE_DED,0,0);

        UPDATE PAY2_LOAN_SCHED SET RUN_ID = @NEW_RUN_ID, PAID_AT = GETDATE()
        WHERE DUE_PERIOD = @PERIOD_DATE AND RUN_ID IS NULL AND LOAN_ID IN (SELECT LOAN_ID FROM PAY2_LOAN WHERE EMP_ID=@EMP_ID AND IS_ACTIVE=1);

        UPDATE L
        SET L.PAID_INST = L.PAID_INST + (
            SELECT COUNT(1) FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @NEW_RUN_ID
        )
        FROM PAY2_LOAN L
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1
          AND EXISTS (SELECT 1 FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @NEW_RUN_ID);

        SET @LEAVE_MIN_USED = CAST(@LEAVE_DAYS * 440 AS INT);
        IF @LEAVE_MIN_USED > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000)
            BEGIN
                UPDATE PAY2_LEAVE_BAL SET USED_MIN = USED_MIN + @LEAVE_MIN_USED, UPDATED_AT = GETDATE()
                WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;
            END
            ELSE
            BEGIN
                INSERT INTO PAY2_LEAVE_BAL (EMP_ID, YEAR, ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN, CARRIED_OUT_MIN, UPDATED_AT)
                VALUES (@EMP_ID, @PERIOD_DATE / 10000, 11440, @LEAVE_MIN_USED, 0, 0, GETDATE());
            END
        END;

        FETCH NEXT FROM cur_emp INTO @EMP_ID, @IS_MANAGER, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;
    END;

    CLOSE cur_emp; DEALLOCATE cur_emp;
    DROP TABLE #AdvResult;

    UPDATE PAY2_PERIOD SET STATUS = 3 WHERE PER_ID = @PER_ID;

END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_CALC_SETTLE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۳. SP_PAY2_CALC_SETTLE — محاسبه تسویه حساب پرسنل
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_CALC_SETTLE]
    @EMP_ID        INT,
    @WS_ID         INT,
    @SETTLE_DATE   BIGINT,
    @END_DATE      BIGINT,
    @PREV_CREDIT   BIGINT = 0,
    @OTHER_INCOME  BIGINT = 0,
    @OTHER_DED     BIGINT = 0,
    @CALC_BY       INT    = NULL,
    @NEW_SET_ID    INT    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @BONUS_MODE          NVARCHAR(20),
        @BONUS_CUSTOM_DAYS   INT,
        @MIN_WAGE_DAILY      BIGINT,
        @MIN_WAGE_MONTHLY    BIGINT,
        @EIDI_MIN_DAYS       INT,
        @EIDI_MAX_DAYS       INT,
        @SENIORITY_MODE      NVARCHAR(20),
        @SENIORITY_FIXED_AMT BIGINT,
        @TAX_YEAR            SMALLINT,
        @TAX_EXEMPT_MONTHLY  BIGINT,
        @LEAVE_MINS_PER_DAY  INT;

    SELECT
        @BONUS_MODE          = ISNULL(MAX(CASE WHEN CFG_KEY='BONUS_MODE'          THEN CFG_VALUE END), 'MIN_WAGE'),
        @BONUS_CUSTOM_DAYS   = ISNULL(MAX(CASE WHEN CFG_KEY='BONUS_CUSTOM_DAYS'   THEN CAST(CFG_VALUE AS INT) END), 60),
        @MIN_WAGE_DAILY      = ISNULL(MAX(CASE WHEN CFG_KEY='MIN_WAGE_DAILY'      THEN CAST(CFG_VALUE AS BIGINT) END), 73200),
        @MIN_WAGE_MONTHLY    = ISNULL(MAX(CASE WHEN CFG_KEY='MIN_WAGE_MONTHLY'    THEN CAST(CFG_VALUE AS BIGINT) END), 2196000),
        @EIDI_MIN_DAYS       = ISNULL(MAX(CASE WHEN CFG_KEY='EIDI_MIN_DAYS'       THEN CAST(CFG_VALUE AS INT) END), 60),
        @EIDI_MAX_DAYS       = ISNULL(MAX(CASE WHEN CFG_KEY='EIDI_MAX_DAYS'       THEN CAST(CFG_VALUE AS INT) END), 90),
        @SENIORITY_MODE      = ISNULL(MAX(CASE WHEN CFG_KEY='SENIORITY_MODE'      THEN CFG_VALUE END), 'LAST_SAL'),
        @SENIORITY_FIXED_AMT = ISNULL(MAX(CASE WHEN CFG_KEY='SENIORITY_FIXED_AMT' THEN CAST(CFG_VALUE AS BIGINT) END), 0),
        @TAX_YEAR            = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_YEAR'            THEN CAST(CFG_VALUE AS SMALLINT) END), 1403),
        @TAX_EXEMPT_MONTHLY  = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_EXEMPT_MONTHLY'  THEN CAST(CFG_VALUE AS BIGINT) END), 84000000),
        @LEAVE_MINS_PER_DAY  = ISNULL(MAX(CASE WHEN CFG_KEY='LEAVE_MINS_PER_DAY'  THEN CAST(CFG_VALUE AS INT) END), 440)
    FROM PAY2_CONFIG
    WHERE CFG_KEY IN ('BONUS_MODE','BONUS_CUSTOM_DAYS','MIN_WAGE_DAILY','MIN_WAGE_MONTHLY','EIDI_MIN_DAYS','EIDI_MAX_DAYS','SENIORITY_MODE','SENIORITY_FIXED_AMT','TAX_YEAR','TAX_EXEMPT_MONTHLY','LEAVE_MINS_PER_DAY');

    DECLARE @HIRE_DATE BIGINT, @EMP_FIRST_NAME NVARCHAR(50), @EMP_LAST_NAME NVARCHAR(50);
    SELECT @HIRE_DATE = HIRE_DATE, @EMP_FIRST_NAME = FIRST_NAME, @EMP_LAST_NAME = LAST_NAME FROM PAY2_EMPLOYEE WHERE EMP_ID = @EMP_ID;

    IF @HIRE_DATE IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_CALC_SETTLE: پرسنل %d یافت نشد.', 16, 1, @EMP_ID);
        RETURN;
    END;

    DECLARE @PREV_SET_ID INT = NULL, @PREV_SEN_DAYS INT = 0, @PREV_SETTLE_DATE BIGINT = NULL;
    SELECT TOP 1 @PREV_SET_ID = SET_ID, @PREV_SEN_DAYS = SENIORITY_DAYS + PREV_SENIORITY_DAYS, @PREV_SETTLE_DATE = SETTLE_DATE
    FROM PAY2_SETTLEMENT WHERE EMP_ID = @EMP_ID AND STATUS >= 2 ORDER BY SETTLE_DATE DESC;

    -- سابقه کل بر اساس استاندارد ۳۶۵ روزه
    DECLARE @SENIORITY_DAYS INT = (@END_DATE / 10000 * 365) + (((@END_DATE % 10000) / 100) * 30) + (@END_DATE % 100) - (@HIRE_DATE / 10000 * 365) - (((@HIRE_DATE % 10000) / 100) * 30) - (@HIRE_DATE % 100) - @PREV_SEN_DAYS;
    IF @SENIORITY_DAYS < 0 SET @SENIORITY_DAYS = 0;

    DECLARE @SENIORITY_YEARS  DECIMAL(6,2) = CAST(@SENIORITY_DAYS AS DECIMAL(10,2)) / 365.0;
    DECLARE @SENIORITY_FULL   INT           = @SENIORITY_DAYS / 365;
    DECLARE @SENIORITY_REMAIN INT           = @SENIORITY_DAYS % 365;

    DECLARE @LAST_DEC_ID  INT;
    SELECT TOP 1 @LAST_DEC_ID = DEC_ID FROM PAY2_DECREE WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1 AND EFF_FROM <= @SETTLE_DATE AND (EFF_TO IS NULL OR EFF_TO >= @SETTLE_DATE) ORDER BY EFF_FROM DESC;

    DECLARE @LAST_DAILY_ONLY BIGINT = ISNULL((SELECT SUM(DL.AMOUNT) FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_TYPE = 1 AND ID.INS_SUBJECT = 1 AND ID.CALC_BASIS = 1), 0);
    DECLARE @LAST_MONTHLY_ONLY BIGINT = ISNULL((SELECT SUM(DL.AMOUNT) FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_TYPE = 1 AND ID.INS_SUBJECT = 1 AND ID.CALC_BASIS = 2), 0);
    DECLARE @LAST_DAILY BIGINT = @LAST_DAILY_ONLY + CAST(@LAST_MONTHLY_ONLY / 30.0 AS BIGINT);
    IF @LAST_DAILY < @MIN_WAGE_DAILY SET @LAST_DAILY = @MIN_WAGE_DAILY;
    DECLARE @LAST_SALARY BIGINT = @LAST_DAILY * 30;

    -- محاسبه روزهای عیدی محدود به سال جاری تقویمی / پس از آخرین تسویه
    DECLARE @EIDI BIGINT = 0;

    DECLARE @START_OF_YEAR BIGINT = (@END_DATE / 10000) * 10000 + 101;
    DECLARE @EIDI_START_DATE BIGINT = @HIRE_DATE;

    IF @START_OF_YEAR > @EIDI_START_DATE SET @EIDI_START_DATE = @START_OF_YEAR;
    IF @PREV_SETTLE_DATE IS NOT NULL AND @PREV_SETTLE_DATE > @EIDI_START_DATE SET @EIDI_START_DATE = @PREV_SETTLE_DATE;

    DECLARE @END_M INT = (@END_DATE / 100) % 100;
    DECLARE @END_D INT = @END_DATE % 100;

    DECLARE @START_M INT = (@EIDI_START_DATE / 100) % 100;
    DECLARE @START_D INT = @EIDI_START_DATE % 100;

    DECLARE @DAYS_SINCE_YEAR_START_END INT =
        CASE
            WHEN @END_M <= 6 THEN (@END_M - 1) * 31 + @END_D
            ELSE (6 * 31) + (@END_M - 7) * 30 + @END_D
        END;

    DECLARE @DAYS_SINCE_YEAR_START_START INT =
        CASE
            WHEN @START_M <= 6 THEN (@START_M - 1) * 31 + @START_D
            ELSE (6 * 31) + (@START_M - 7) * 30 + @START_D
        END;

    DECLARE @WORKED_DAYS_FOR_EIDI INT = @DAYS_SINCE_YEAR_START_END - @DAYS_SINCE_YEAR_START_START + 1;

    IF @WORKED_DAYS_FOR_EIDI < 0 SET @WORKED_DAYS_FOR_EIDI = 0;
    IF @WORKED_DAYS_FOR_EIDI > 365 SET @WORKED_DAYS_FOR_EIDI = 365;

    IF @WORKED_DAYS_FOR_EIDI > 0
    BEGIN
        DECLARE @EIDI_BASE_DAILY BIGINT = @LAST_DAILY;

        IF @BONUS_MODE = 'MIN_WAGE'
            SET @EIDI_BASE_DAILY = CASE WHEN @LAST_SALARY < @MIN_WAGE_MONTHLY THEN @LAST_DAILY ELSE (@MIN_WAGE_MONTHLY / 30) END;

        IF @BONUS_MODE = 'CUSTOM'
        BEGIN
            SET @EIDI = @LAST_DAILY * ISNULL(@BONUS_CUSTOM_DAYS, 60);
        END
        ELSE
        BEGIN
            DECLARE @CALC_EIDI BIGINT = CAST((@EIDI_BASE_DAILY * @EIDI_MIN_DAYS * CAST(@WORKED_DAYS_FOR_EIDI AS FLOAT)) / 365.0 AS BIGINT);
            DECLARE @MAX_EIDI BIGINT  = CAST((@EIDI_BASE_DAILY * @EIDI_MAX_DAYS * CAST(@WORKED_DAYS_FOR_EIDI AS FLOAT)) / 365.0 AS BIGINT);

            IF @CALC_EIDI > @MAX_EIDI SET @EIDI = @MAX_EIDI;
            ELSE SET @EIDI = @CALC_EIDI;
        END
    END;

    -- معافیت مالیات عیدی طبق قانون: معادل «یک ماه» معافیت کامل، بدون پروریت بر حسب روزهای کارکرد
    DECLARE @EIDI_TAX BIGINT = 0;
    IF @EIDI > @TAX_EXEMPT_MONTHLY
    BEGIN
        SET @EIDI_TAX = [dbo].[FN_PAY2_CALC_TAX]((@EIDI - @TAX_EXEMPT_MONTHLY) * 12, @TAX_YEAR) / 12;
    END

    DECLARE @SANAVAT BIGINT = CASE
        WHEN @SENIORITY_MODE = 'LAST_SAL' THEN @LAST_SALARY * @SENIORITY_FULL + CAST(@LAST_SALARY * @SENIORITY_REMAIN / 365.0 AS BIGINT)
        WHEN @SENIORITY_MODE = 'DAILY' THEN @LAST_DAILY * 30 * @SENIORITY_FULL + CAST(@LAST_DAILY * @SENIORITY_REMAIN AS BIGINT)
        ELSE ISNULL(@SENIORITY_FIXED_AMT, 0) * @SENIORITY_FULL END;

    DECLARE @LEAVE_BAL_MIN  INT = ISNULL((SELECT SUM(BALANCE_MIN) FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID), 0);
    IF @LEAVE_BAL_MIN < 0 SET @LEAVE_BAL_MIN = 0;

    DECLARE @LEAVE_BAL_DAYS_CALC DECIMAL(5,2) = CAST(@LEAVE_BAL_MIN AS DECIMAL(10,2)) / ISNULL(NULLIF(@LEAVE_MINS_PER_DAY, 0), 440);
    DECLARE @LEAVE_PAY BIGINT = CAST(@LEAVE_BAL_DAYS_CALC * @LAST_DAILY AS BIGINT);

    DECLARE @BON_SETTLE BIGINT = ISNULL((SELECT TOP 1 DL.AMOUNT * @SENIORITY_FULL FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_CODE = 'GROCERY'), 0);
    DECLARE @LOAN_BALANCE_TOT BIGINT = ISNULL((SELECT SUM(BALANCE) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID), 0);

    INSERT INTO PAY2_SETTLEMENT (EMP_ID, WS_ID, SETTLE_DATE, HIRE_DATE, END_DATE, SENIORITY_DAYS, SENIORITY_YEARS, LAST_SALARY, LAST_DAILY, PREV_SET_ID, PREV_SENIORITY_DAYS, LEAVE_BAL_MIN, LEAVE_BAL_DAYS, EIDI, BON, LEAVE_PAY, SANAVAT, PREV_CREDIT, OTHER_INCOME, PREV_DEBIT, EIDI_TAX, LOAN_BALANCE, OTHER_DED, STATUS, CALC_METHOD, CREATED_BY)
    VALUES (@EMP_ID, @WS_ID, @SETTLE_DATE, @HIRE_DATE, @END_DATE, @SENIORITY_DAYS, @SENIORITY_YEARS, @LAST_SALARY, @LAST_DAILY, @PREV_SET_ID, @PREV_SEN_DAYS, @LEAVE_BAL_MIN, @LEAVE_BAL_DAYS_CALC, @EIDI, @BON_SETTLE, @LEAVE_PAY, @SANAVAT, @PREV_CREDIT, @OTHER_INCOME, 0, @EIDI_TAX, @LOAN_BALANCE_TOT, @OTHER_DED, 1,
        N'{"bonus_mode":"' + @BONUS_MODE + N'","seniority_mode":"' + @SENIORITY_MODE + N'","tax_year":' + CAST(@TAX_YEAR AS NVARCHAR) + N'}', @CALC_BY);

    SET @NEW_SET_ID = SCOPE_IDENTITY();

END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_CARRYOVER_LEAVE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۱۰. SP_PAY2_CARRYOVER_LEAVE — انتقال مانده مرخصی به سال بعد
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_CARRYOVER_LEAVE]
    @FROM_YEAR INT,
    @TO_YEAR   INT,
    @WS_ID     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CARRYOVER_MAX INT;
    SELECT @CARRYOVER_MAX = CAST(CFG_VALUE AS INT)
    FROM PAY2_CONFIG WHERE CFG_KEY = 'LEAVE_CARRYOVER_MAX';

    DECLARE @LEAVE_MINS_PER_DAY INT;
    SELECT @LEAVE_MINS_PER_DAY = CAST(CFG_VALUE AS INT)
    FROM PAY2_CONFIG WHERE CFG_KEY = 'LEAVE_MINS_PER_DAY';

    DECLARE @MAX_CARRY_MIN INT = @CARRYOVER_MAX * @LEAVE_MINS_PER_DAY;

    UPDATE PAY2_LEAVE_BAL
    SET CARRIED_OUT_MIN = CASE
        WHEN BALANCE_MIN > @MAX_CARRY_MIN THEN @MAX_CARRY_MIN
        WHEN BALANCE_MIN < 0 THEN 0
        ELSE BALANCE_MIN
    END,
    UPDATED_AT = GETDATE()
    WHERE YEAR = @FROM_YEAR
      AND (@WS_ID IS NULL OR EMP_ID IN (
          SELECT EMP_ID FROM PAY2_EMPLOYEE WHERE WS_ID = @WS_ID
      ));

    DECLARE @ANNUAL_DAYS INT;
    SELECT @ANNUAL_DAYS = CAST(CFG_VALUE AS INT) FROM PAY2_CONFIG WHERE CFG_KEY='LEAVE_ANNUAL_DAYS';
    DECLARE @ENTITLEMENT INT = @ANNUAL_DAYS * @LEAVE_MINS_PER_DAY;

    INSERT INTO PAY2_LEAVE_BAL (EMP_ID, YEAR, ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN)
    SELECT
        LB.EMP_ID, @TO_YEAR, @ENTITLEMENT, 0, LB.CARRIED_OUT_MIN
    FROM PAY2_LEAVE_BAL LB
    WHERE LB.YEAR = @FROM_YEAR
      AND (@WS_ID IS NULL OR LB.EMP_ID IN (SELECT EMP_ID FROM PAY2_EMPLOYEE WHERE WS_ID = @WS_ID))
      AND NOT EXISTS (SELECT 1 FROM PAY2_LEAVE_BAL X WHERE X.EMP_ID = LB.EMP_ID AND X.YEAR = @TO_YEAR);

    PRINT N'SP_PAY2_CARRYOVER_LEAVE — انتقال از ' + CAST(@FROM_YEAR AS NVARCHAR) + N' به ' + CAST(@TO_YEAR AS NVARCHAR) + N' انجام شد.';
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_CLOSE_PERIOD]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۵. SP_PAY2_CLOSE_PERIOD — بستن دوره و کنترل نهایی
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_CLOSE_PERIOD]
    @PER_ID  INT,
    @CLOSE_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WS_ID INT;
    DECLARE @STATUS TINYINT;
    DECLARE @PERIOD_DATE BIGINT;

    SELECT @WS_ID = WS_ID, @STATUS = STATUS, @PERIOD_DATE = PERIOD_DATE
    FROM PAY2_PERIOD WHERE PER_ID = @PER_ID;

    IF @STATUS <> 1
    BEGIN
        RAISERROR(N'SP_PAY2_CLOSE_PERIOD: دوره %d در وضعیت %d است. فقط دوره باز (1) قابل بستن است.', 16, 1, @PER_ID, @STATUS);
        RETURN;
    END;

    DECLARE @EMP_NO_ATT INT;
    SELECT @EMP_NO_ATT = COUNT(*)
    FROM PAY2_EMPLOYEE E
    WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
      AND NOT EXISTS (
          SELECT 1 FROM PAY2_ATTENDANCE A
          WHERE A.PER_ID = @PER_ID AND A.EMP_ID = E.EMP_ID
      );

    IF @EMP_NO_ATT > 0
        PRINT N'هشدار: ' + CAST(@EMP_NO_ATT AS NVARCHAR) + N' پرسنل فاقد ورودی کارکرد در این دوره هستند.';

    UPDATE PAY2_PERIOD SET STATUS = 2, CLOSED_AT = GETDATE() WHERE PER_ID = @PER_ID;

    PRINT N'دوره ' + CAST(@PER_ID AS NVARCHAR) + N' (ماه ' + CAST(@PERIOD_DATE AS NVARCHAR) + N') بسته شد.';
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_FINALIZE_RUN]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۷. SP_PAY2_FINALIZE_RUN — نهایی‌کردن محاسبه (STATUS 1→2)
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_FINALIZE_RUN]
    @RUN_ID   INT,
    @FINAL_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STATUS TINYINT;
    SELECT @STATUS = STATUS FROM PAY2_RUN WHERE RUN_ID = @RUN_ID;

    IF @STATUS <> 1
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: اجرا %d باید در وضعیت پیش‌نویس (1) باشد.', 16, 1, @RUN_ID);
        RETURN;
    END;

    DECLARE @PER_ID INT;
    DECLARE @WS_ID  INT;
    SELECT @PER_ID = R.PER_ID, @WS_ID = P.WS_ID
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID=P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    DECLARE @MISSING INT;
    SELECT @MISSING = COUNT(*)
    FROM PAY2_EMPLOYEE E
    WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
      AND EXISTS (SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID=@PER_ID AND A.EMP_ID=E.EMP_ID)
      AND NOT EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL WHERE RL.RUN_ID=@RUN_ID AND RL.EMP_ID=E.EMP_ID);

    IF @MISSING > 0
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: %d پرسنل هنوز محاسبه نشده‌اند.', 16, 1, @MISSING);
        RETURN;
    END;

    UPDATE PAY2_RUN
    SET STATUS = 2, NOTES = ISNULL(NOTES,'') + N' | Finalized by ' + CAST(ISNULL(@FINAL_BY,0) AS NVARCHAR)
    WHERE RUN_ID = @RUN_ID;

    PRINT N'SP_PAY2_FINALIZE_RUN — RUN_ID ' + CAST(@RUN_ID AS NVARCHAR) + N' نهایی شد.';
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_FINALIZE_SETTLE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۸. SP_PAY2_FINALIZE_SETTLE — نهایی‌کردن تسویه (STATUS 1→2)
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_FINALIZE_SETTLE]
    @SET_ID     INT,
    @APPROVED_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @STATUS TINYINT;
        DECLARE @EMP_ID INT;
        DECLARE @END_DATE BIGINT;
        DECLARE @LOAN_BALANCE BIGINT;

        -- قفلِ واقعی (UPDLOCK) تا زمان COMMIT/ROLLBACK روی این سطر باقی می‌ماند
        SELECT @STATUS = STATUS, @EMP_ID = EMP_ID, @END_DATE = END_DATE, @LOAN_BALANCE = LOAN_BALANCE
        FROM PAY2_SETTLEMENT WITH (UPDLOCK)
        WHERE SET_ID = @SET_ID;

        IF @STATUS IS NULL
            RAISERROR(N'تسویه حسابی با این شناسه یافت نشد.', 16, 1);

        IF @STATUS <> 1
            RAISERROR(N'تسویه در وضعیت پیش‌نویس (1) نیست یا قبلاً تأیید شده است.', 16, 1);

        UPDATE PAY2_SETTLEMENT
        SET STATUS = 2, APPROVED_BY = @APPROVED_BY, APPROVED_AT = GETDATE()
        WHERE SET_ID = @SET_ID;

        -- پایان همکاری و غیرفعال شدن پرسنل
        UPDATE PAY2_EMPLOYEE
        SET FIRE_DATE = @END_DATE, IS_ACTIVE = 0
        WHERE EMP_ID = @EMP_ID AND IS_ACTIVE = 1;

        -- بستن قطعی وام‌های فعالِ تسویه‌شده
        IF @LOAN_BALANCE > 0
        BEGIN
            UPDATE PAY2_LOAN
            SET IS_ACTIVE = 0,
                PURPOSE = SUBSTRING(ISNULL(PURPOSE, '') + N' (بسته‌شده در تسویه)', 1, 200)
            WHERE EMP_ID = @EMP_ID AND IS_ACTIVE = 1 AND PAID_INST < TOTAL_INST;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        DECLARE @ERR_MSG NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ERR_MSG, 16, 1);
    END CATCH;
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_GEN_DEED]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[SP_PAY2_GEN_DEED]
    @RUN_ID  INT,
    @CALC_BY INT = NULL,
    @DEED_MODE TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#SalarySplit') IS NOT NULL DROP TABLE #SalarySplit;
    IF OBJECT_ID('tempdb..#FinalArticles') IS NOT NULL DROP TABLE #FinalArticles;
    IF OBJECT_ID('tempdb..#UniqueAccounts') IS NOT NULL DROP TABLE #UniqueAccounts;

    DECLARE @PER_ID INT, @WS_ID INT, @PER_DATE BIGINT;

    SELECT @PER_ID = R.PER_ID, @WS_ID = P.WS_ID, @PER_DATE = P.PERIOD_DATE
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    DECLARE @MonthNum INT = (@PER_DATE / 100) % 100;
    DECLARE @MonthName NVARCHAR(10) = CASE @MonthNum
        WHEN 1 THEN N'فروردین' WHEN 2 THEN N'اردیبهشت' WHEN 3 THEN N'خرداد'
        WHEN 4 THEN N'تیر'     WHEN 5 THEN N'مرداد'    WHEN 6 THEN N'شهریور'
        WHEN 7 THEN N'مهر'     WHEN 8 THEN N'آبان'     WHEN 9 THEN N'آذر'
        WHEN 10 THEN N'دی'     WHEN 11 THEN N'بهمن'    WHEN 12 THEN N'اسفند' ELSE N'نامشخص' END;
    DECLARE @ML NVARCHAR(20) = RIGHT('0' + CAST(@MonthNum AS NVARCHAR(2)), 2) + N'-' + @MonthName;

    DECLARE 
        @ACC_SALARY_TOLID NVARCHAR(50), @ACC_SALARY_EDARI NVARCHAR(50), 
        @ACC_SALARY_FOROSH NVARCHAR(50), @ACC_SALARY_KHADAMAT NVARCHAR(50),
        @ACC_SALARY_PAY NVARCHAR(50), @ACC_INS_PAYABLE NVARCHAR(50),
        @ACC_TAX_PAYABLE NVARCHAR(50), @ACC_INS_EXP NVARCHAR(50),
        @ACC_ADV_HES NVARCHAR(50), @ACC_LOAN_HES NVARCHAR(50),
        @ACC_OTHER_DED_HES NVARCHAR(50);

    SELECT
        @ACC_SALARY_TOLID   = MAX(CASE WHEN ACC_KEY='SALARY_EXP_TOLID'    THEN ACC_CODE END),
        @ACC_SALARY_EDARI   = MAX(CASE WHEN ACC_KEY='SALARY_EXP_EDARI'    THEN ACC_CODE END),
        @ACC_SALARY_FOROSH  = MAX(CASE WHEN ACC_KEY='SALARY_EXP_FOROSH'   THEN ACC_CODE END),
        @ACC_SALARY_KHADAMAT= MAX(CASE WHEN ACC_KEY='SALARY_EXP_KHADAMAT' THEN ACC_CODE END),
        @ACC_SALARY_PAY     = MAX(CASE WHEN ACC_KEY='SALARY_PAYABLE'      THEN ACC_CODE END),
        @ACC_INS_PAYABLE    = MAX(CASE WHEN ACC_KEY='INS_PAYABLE'         THEN ACC_CODE END),
        @ACC_TAX_PAYABLE    = MAX(CASE WHEN ACC_KEY='TAX_PAYABLE'         THEN ACC_CODE END),
        @ACC_INS_EXP        = MAX(CASE WHEN ACC_KEY='INS_EXP'             THEN ACC_CODE END),
        @ACC_ADV_HES        = MAX(CASE WHEN ACC_KEY='ADV_HES'             THEN ACC_CODE END),
        @ACC_LOAN_HES       = MAX(CASE WHEN ACC_KEY='LOAN_HES'            THEN ACC_CODE END),
        @ACC_OTHER_DED_HES  = MAX(CASE WHEN ACC_KEY='OTHER_DED_HES'       THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    IF @DEED_MODE IS NULL
    BEGIN
        SELECT @DEED_MODE = CASE 
            WHEN R.DEED_MODE IS NOT NULL THEN R.DEED_MODE
            WHEN R.STATUS >= 2 THEN 1 
            ELSE W.DEFAULT_DEED_MODE
        END
        FROM PAY2_RUN R
        INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
        INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
        WHERE R.RUN_ID = @RUN_ID;
    END

    -- ─────────────────────────────────────────────────────────────────
    -- گاردهای امنیتی (جلوگیری از کمبود حساب‌ها)
    -- ─────────────────────────────────────────────────────────────────
    IF @ACC_SALARY_PAY IS NULL
    BEGIN
        RAISERROR(N'حساب پرداختنی حقوق (SALARY_PAYABLE) برای کارگاه تنظیم نشده است.', 16, 1);
        RETURN;
    END

    DECLARE @MissingAcc NVARCHAR(MAX) = N'';
    IF @ACC_INS_EXP IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND INS_EMPLOYER > 0) SET @MissingAcc += N'هزینه بیمه کارفرما، ';
    IF @ACC_INS_PAYABLE IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND (INS_WORKER + INS_EMPLOYER) > 0) SET @MissingAcc += N'اداره بیمه، ';
    IF @ACC_TAX_PAYABLE IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND TAX_AMOUNT > 0) SET @MissingAcc += N'اداره مالیات، ';
    IF @ACC_LOAN_HES IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND LOAN_DED > 0) SET @MissingAcc += N'صندوق وام، ';
    IF @ACC_ADV_HES IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND ADVANCE_DED > 0) SET @MissingAcc += N'حساب مساعده، ';
    IF @ACC_OTHER_DED_HES IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND OTHER_DED > 0) SET @MissingAcc += N'سایر کسورات، ';
    
    IF @ACC_SALARY_TOLID IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_TOLID > 0) SET @MissingAcc += N'هزینه تولید، ';
    IF @ACC_SALARY_EDARI IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_EDARI > 0) SET @MissingAcc += N'هزینه اداری، ';
    IF @ACC_SALARY_FOROSH IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_FOROSH > 0) SET @MissingAcc += N'هزینه فروش، ';
    IF @ACC_SALARY_KHADAMAT IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_KHADAMAT > 0) SET @MissingAcc += N'هزینه خدمات، ';

    IF LEN(@MissingAcc) > 0
    BEGIN
        DECLARE @Err2 NVARCHAR(MAX) = N'صدور سند متوقف شد: حساب‌های زیر در تنظیمات کارگاه خالی هستند: ' + SUBSTRING(@MissingAcc, 1, LEN(@MissingAcc)-2);
        RAISERROR(@Err2, 16, 1);
        RETURN;
    END

    DECLARE @BadEmpName NVARCHAR(100), @BadAccT NVARCHAR(50);
    SELECT TOP 1 @BadEmpName = E.LAST_NAME + N' ' + E.FIRST_NAME, @BadAccT = ISNULL(E.ACC_T, N'خالی')
    FROM PAY2_RUN_LINE RL
    INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
    WHERE RL.RUN_ID = @RUN_ID
      AND (
           (@DEED_MODE = 2)
           OR 
           (@DEED_MODE = 1 AND (RL.LOAN_DED > 0 OR RL.ADVANCE_DED > 0 OR RL.OTHER_DED > 0))
      )
      AND (
           NULLIF(TRIM(E.ACC_T), '') IS NULL 
      );

    IF @BadEmpName IS NOT NULL
    BEGIN
        DECLARE @Err4 NVARCHAR(500) = N'صدور سند متوقف شد: کد تفصیلی (ACC_T) برای پرسنل نامعتبر است. حساب پرسنل نمی‌تواند خالی باشد. نام پرسنل: ' + @BadEmpName + N' (' + @BadAccT + N')';
        RAISERROR(@Err4, 16, 1);
        RETURN;
    END

    -- ─────────────────────────────────────────────────────────────────
    -- جدول موقت محاسبات و ایجاد ردیف‌های خام
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #SalarySplit (
        EMP_ID INT PRIMARY KEY,
        FULL_NAME NVARCHAR(150),
        ACC_T NVARCHAR(50),
        EXP_TOLID BIGINT,
        EXP_EDARI BIGINT,
        EXP_FOROSH BIGINT,
        EXP_KHADAMAT BIGINT,
        NET_PAY BIGINT,
        INS_WORKER BIGINT,
        INS_EMPLOYER BIGINT,
        TAX_AMOUNT BIGINT,
        LOAN_DED BIGINT,
        ADVANCE_DED BIGINT,
        OTHER_DED BIGINT
    );

    ;WITH SplitBase AS (
        SELECT 
            RL.EMP_ID, RL.GROSS_PAY, A.DAYS_TOLID, A.DAYS_EDARI, A.DAYS_FOROSH, A.DAYS_KHADAMAT,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((RL.GROSS_PAY * A.DAYS_TOLID) / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_T,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((RL.GROSS_PAY * A.DAYS_EDARI) / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_E,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((RL.GROSS_PAY * A.DAYS_FOROSH) / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_F,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((RL.GROSS_PAY * A.DAYS_KHADAMAT) / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_K,
            RL.NET_PAY, RL.INS_WORKER, RL.INS_EMPLOYER, RL.TAX_AMOUNT, RL.LOAN_DED, RL.ADVANCE_DED, RL.OTHER_DED
        FROM PAY2_RUN_LINE RL
        INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID
        WHERE RL.RUN_ID = @RUN_ID
    )
    INSERT INTO #SalarySplit (
        EMP_ID, FULL_NAME, ACC_T, EXP_TOLID, EXP_EDARI, EXP_FOROSH, EXP_KHADAMAT, 
        NET_PAY, INS_WORKER, INS_EMPLOYER, TAX_AMOUNT, LOAN_DED, ADVANCE_DED, OTHER_DED
    )
    SELECT 
        B.EMP_ID, E.LAST_NAME + N' ' + E.FIRST_NAME, NULLIF(TRIM(E.ACC_T), ''),
        CASE WHEN B.DAYS_TOLID > 0 THEN B.R_T + (B.GROSS_PAY - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_T END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI > 0 THEN B.R_E + (B.GROSS_PAY - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_E END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI = 0 AND B.DAYS_FOROSH > 0 THEN B.R_F + (B.GROSS_PAY - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_F END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI = 0 AND B.DAYS_FOROSH = 0 THEN B.R_K + (B.GROSS_PAY - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_K END,
        B.NET_PAY, B.INS_WORKER, B.INS_EMPLOYER, B.TAX_AMOUNT, B.LOAN_DED, B.ADVANCE_DED, B.OTHER_DED
    FROM SplitBase B
    INNER JOIN PAY2_EMPLOYEE E ON B.EMP_ID = E.EMP_ID;

    -- ─────────────────────────────────────────────────────────────────
    -- جمع‌آوری مقادیر نهایی در جدول
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #FinalArticles (
        HES_CODE NVARCHAR(100) COLLATE database_default,
        SHARH NVARCHAR(500),
        BED BIGINT,
        BES BIGINT,
        ACC_KEY NVARCHAR(50),
        EMP_ID INT NULL,
        EmployeeName NVARCHAR(150),
        SortOrder INT
    );

    IF @DEED_MODE = 1
    BEGIN
        INSERT INTO #FinalArticles
        SELECT CAST(@ACC_SALARY_TOLID AS NVARCHAR(100)), CAST(N'هزینه حقوق تولید ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_TOLID) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_TOLID' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 1
        FROM #SalarySplit HAVING SUM(EXP_TOLID) > 0
        UNION ALL 
        SELECT CAST(@ACC_SALARY_EDARI AS NVARCHAR(100)), CAST(N'هزینه حقوق اداری ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_EDARI) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_EDARI' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 2
        FROM #SalarySplit HAVING SUM(EXP_EDARI) > 0
        UNION ALL 
        SELECT CAST(@ACC_SALARY_FOROSH AS NVARCHAR(100)), CAST(N'هزینه حقوق فروش ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_FOROSH) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_FOROSH' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 3
        FROM #SalarySplit HAVING SUM(EXP_FOROSH) > 0
        UNION ALL 
        SELECT CAST(@ACC_SALARY_KHADAMAT AS NVARCHAR(100)), CAST(N'هزینه حقوق خدمات ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_KHADAMAT) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_KHADAMAT' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 4
        FROM #SalarySplit HAVING SUM(EXP_KHADAMAT) > 0
        UNION ALL 
        SELECT CAST(@ACC_INS_EXP AS NVARCHAR(100)), CAST(N'هزینه بیمه کارفرما ' + @ML AS NVARCHAR(500)), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST(0 AS BIGINT), CAST('INS_EXP' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        
        -- 🚀 فیکس تراز در حالت کلی: افزودن امکان بدهکار شدنِ حقوق در صورت خالص منفی
        UNION ALL 
        SELECT CAST(@ACC_SALARY_PAY AS NVARCHAR(100)), CAST(N'حقوق پرداختنی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(NET_PAY) AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 6
        FROM #SalarySplit HAVING SUM(NET_PAY) > 0
        UNION ALL 
        SELECT CAST(@ACC_SALARY_PAY AS NVARCHAR(100)), CAST(N'بدهی حقوق (خالص منفی) ' + @ML AS NVARCHAR(500)), CAST(ABS(SUM(NET_PAY)) AS BIGINT), CAST(0 AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 6
        FROM #SalarySplit HAVING SUM(NET_PAY) < 0
        
        UNION ALL 
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه تأمین اجتماعی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_WORKER + INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 7
        FROM #SalarySplit HAVING SUM(INS_WORKER + INS_EMPLOYER) > 0
        UNION ALL 
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(TAX_AMOUNT) AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 8
        FROM #SalarySplit HAVING SUM(TAX_AMOUNT) > 0
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 9
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 10
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(OTHER_DED AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 11
        FROM #SalarySplit WHERE OTHER_DED > 0;
    END
    ELSE IF @DEED_MODE = 2
    BEGIN
        INSERT INTO #FinalArticles
        SELECT CAST(@ACC_SALARY_TOLID AS NVARCHAR(100)), CAST(N'هزینه حقوق تولید ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_TOLID AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_TOLID' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 1
        FROM #SalarySplit WHERE EXP_TOLID > 0
        UNION ALL 
        SELECT CAST(@ACC_SALARY_EDARI AS NVARCHAR(100)), CAST(N'هزینه حقوق اداری ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_EDARI AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_EDARI' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 2
        FROM #SalarySplit WHERE EXP_EDARI > 0
        UNION ALL 
        SELECT CAST(@ACC_SALARY_FOROSH AS NVARCHAR(100)), CAST(N'هزینه حقوق فروش ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_FOROSH AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_FOROSH' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 3
        FROM #SalarySplit WHERE EXP_FOROSH > 0
        UNION ALL 
        SELECT CAST(@ACC_SALARY_KHADAMAT AS NVARCHAR(100)), CAST(N'هزینه حقوق خدمات ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_KHADAMAT AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_KHADAMAT' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 4
        FROM #SalarySplit WHERE EXP_KHADAMAT > 0
        UNION ALL 
        SELECT CAST(@ACC_INS_EXP AS NVARCHAR(100)), CAST(N'هزینه بیمه کارفرما ' + @ML AS NVARCHAR(500)), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST(0 AS BIGINT), CAST('INS_EXP' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        
        -- 🚀 فیکس تراز در حالت تفصیلی: اگر خالص پرداختی منفی شود، شخص به شرکت بدهکار است (بدهکار ثبت می‌شود)
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'حقوق پرداختنی: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(NET_PAY AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE NET_PAY > 0
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'بدهی حقوق (خالص منفی): ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(ABS(NET_PAY) AS BIGINT), CAST(0 AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE NET_PAY < 0
        
        UNION ALL 
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه سهم کارگر ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(INS_WORKER AS BIGINT), CAST('INS_PAYABLE_W' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 7
        FROM #SalarySplit WHERE INS_WORKER > 0
        UNION ALL 
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه سهم کارفرما ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE_E' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 8
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        UNION ALL 
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(TAX_AMOUNT AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 9
        FROM #SalarySplit WHERE TAX_AMOUNT > 0
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 10
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 11
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL 
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(OTHER_DED AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 12
        FROM #SalarySplit WHERE OTHER_DED > 0;
    END

    -- ─────────────────────────────────────────────────────────────────
    -- 🚨 اعتبارسنجی Set-Based سطح دیتابیس
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #UniqueAccounts (
        HES_CODE NVARCHAR(100) COLLATE database_default
    );

    INSERT INTO #UniqueAccounts (HES_CODE)
    SELECT DISTINCT HES_CODE FROM #FinalArticles;

    DECLARE @MissingAccounts NVARCHAR(MAX) = N'';

    ;WITH Parsed AS (
        SELECT 
            HES_CODE,
            -- 🚀 اصلاح کلیدی: جایگزین کردن "" با " جهت ساخت JSON معتبر در T-SQL
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[0]') AS INT) AS K,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[1]') AS INT) AS M,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[2]') AS INT) AS T1,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[3]') AS INT) AS T2,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[4]') AS INT) AS T3,
            TRY_CAST(JSON_VALUE('["' + REPLACE(HES_CODE, '-', '","') + '"]', '$[5]') AS INT) AS T4
        FROM #UniqueAccounts
    ),
    Leveled AS (
        SELECT *,
            CASE 
                WHEN T4 IS NOT NULL THEN 6
                WHEN T3 IS NOT NULL THEN 5
                WHEN T2 IS NOT NULL THEN 4
                WHEN T1 IS NOT NULL THEN 3
                WHEN M IS NOT NULL THEN 2
                ELSE 1
            END AS Lvl
        FROM Parsed
    )
    SELECT @MissingAccounts = @MissingAccounts + U.HES_CODE + N', '
    FROM Leveled U
    LEFT JOIN TOTA_HES K ON U.K = K.NUMBER AND U.Lvl = 1
    LEFT JOIN DETA_HES M ON U.K = M.N_KOL AND U.M = M.NUMBER AND U.Lvl = 2
    LEFT JOIN TDETA_HES T1 ON U.K = T1.N_KOL AND U.M = T1.NUMBER AND U.T1 = T1.TNUMBER AND U.Lvl = 3
    LEFT JOIN TDETA_HES2 T2 ON U.K = T2.N_KOL AND U.M = T2.NUMBER AND U.T1 = T2.TNUMBER AND U.T2 = T2.TNUMBER2 AND U.Lvl = 4
    LEFT JOIN TDETA_HES3 T3 ON U.K = T3.N_KOL AND U.M = T3.NUMBER AND U.T1 = T3.TNUMBER AND U.T2 = T3.TNUMBER2 AND U.T3 = T3.TNUMBER3 AND U.Lvl = 5
    LEFT JOIN TDETA_HES4 T4 ON U.K = T4.N_KOL AND U.M = T4.NUMBER AND U.T1 = T4.TNUMBER AND U.T2 = T4.TNUMBER2 AND U.T3 = T4.TNUMBER3 AND U.T4 = T4.TNUMBER4 AND U.Lvl = 6
    WHERE 
        (U.Lvl = 1 AND K.NUMBER IS NULL) OR
        (U.Lvl = 2 AND M.NUMBER IS NULL) OR
        (U.Lvl = 3 AND T1.TNUMBER IS NULL) OR
        (U.Lvl = 4 AND T2.TNUMBER2 IS NULL) OR
        (U.Lvl = 5 AND T3.TNUMBER3 IS NULL) OR
        (U.Lvl = 6 AND T4.TNUMBER4 IS NULL) OR
        U.Lvl > 6 OR 
        U.T1 IS NULL;

    IF LEN(@MissingAccounts) > 0
    BEGIN
        DECLARE @ErrAcc NVARCHAR(MAX) = N'صدور سند متوقف شد. حساب‌های زیر در سیستم حسابداری نامعتبرند یا فاقد حداقل ۳ سطح (کل-معین-تفصیلی) می‌باشند: ' + SUBSTRING(@MissingAccounts, 1, LEN(@MissingAccounts)-2);
        RAISERROR(@ErrAcc, 16, 1);
        RETURN;
    END

    SELECT HES_CODE, SHARH, BED, BES, ACC_KEY, EMP_ID, EmployeeName
    FROM #FinalArticles
    ORDER BY SortOrder, EmployeeName;

    DROP TABLE #SalarySplit;
    DROP TABLE #FinalArticles;
    DROP TABLE #UniqueAccounts;
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_GEN_DEED_SETTLE]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۴. SP_PAY2_GEN_DEED_SETTLE — تولید آرتیکل‌های سند تسویه
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_GEN_DEED_SETTLE]
    @SET_ID  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STATUS TINYINT;
    DECLARE @WS_ID  INT;
    DECLARE @EMP_ID INT;
    DECLARE @EMP_NAME NVARCHAR(100);

    SELECT @STATUS = S.STATUS, @WS_ID = S.WS_ID, @EMP_ID = S.EMP_ID, 
           @EMP_NAME = E.LAST_NAME + N' ' + E.FIRST_NAME
    FROM PAY2_SETTLEMENT S
    INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID
    WHERE S.SET_ID = @SET_ID;

    IF @STATUS <> 2
    BEGIN
        RAISERROR(N'SP_PAY2_GEN_DEED_SETTLE: تسویه %d باید نهایی (STATUS=2) شود.', 16, 1, @SET_ID);
        RETURN;
    END;

    DECLARE @ACC_SALARY_PAY  NVARCHAR(50), @ACC_INS_PAYABLE NVARCHAR(50), @ACC_TAX_PAYABLE NVARCHAR(50);
    DECLARE @ACC_COST_EIDI   NVARCHAR(50), @ACC_COST_SANAVAT NVARCHAR(50), @ACC_COST_LEAVE NVARCHAR(50);
    DECLARE @ACC_LOAN_HES    NVARCHAR(50), @ACC_ADV_HES NVARCHAR(50);

    SELECT
        @ACC_SALARY_PAY   = MAX(CASE WHEN ACC_KEY='SALARY_PAYABLE' THEN ACC_CODE END),
        @ACC_INS_PAYABLE  = MAX(CASE WHEN ACC_KEY='INS_PAYABLE'    THEN ACC_CODE END),
        @ACC_TAX_PAYABLE  = MAX(CASE WHEN ACC_KEY='TAX_PAYABLE'    THEN ACC_CODE END),
        @ACC_COST_EIDI    = MAX(CASE WHEN ACC_KEY='COST_EIDI'      THEN ACC_CODE END),
        @ACC_COST_SANAVAT = MAX(CASE WHEN ACC_KEY='COST_SANAVAT'   THEN ACC_CODE END),
        @ACC_COST_LEAVE   = MAX(CASE WHEN ACC_KEY='COST_LEAVE'     THEN ACC_CODE END),
        @ACC_LOAN_HES     = MAX(CASE WHEN ACC_KEY='LOAN_HES'       THEN ACC_CODE END),
        @ACC_ADV_HES      = MAX(CASE WHEN ACC_KEY='ADV_HES'        THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    SELECT @ACC_COST_EIDI AS HES_CODE, N'هزینه عیدی' AS SHARH, EIDI AS BED, 0 AS BES, 'COST_EIDI' AS ACC_KEY, NULL AS EMP_ID 
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI > 0
    UNION ALL
    SELECT @ACC_COST_SANAVAT, N'هزینه حق سنوات', SANAVAT, 0, 'COST_SANAVAT', NULL 
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND SANAVAT > 0
    UNION ALL
    SELECT @ACC_COST_LEAVE, N'هزینه بازخرید مرخصی', LEAVE_PAY, 0, 'COST_LEAVE', NULL 
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND LEAVE_PAY > 0
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_SALARY_PAY), N'پرداختنی تسویه حساب: ' + @EMP_NAME, 0, CAST(EIDI+BON+LEAVE_PAY+SANAVAT+PREV_CREDIT+OTHER_INCOME-PREV_DEBIT-EIDI_TAX-LOAN_BALANCE-OTHER_DED AS BIGINT), 'SETTLE_PAYABLE', S.EMP_ID
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_LOAN_HES), N'وصول مانده وام از تسویه: ' + @EMP_NAME, 0, LOAN_BALANCE, 'LOAN_COLLECT', @EMP_ID 
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID AND LOAN_BALANCE > 0
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_ADV_HES), N'وصول بدهکاری (مساعده): ' + @EMP_NAME, 0, PREV_DEBIT, 'ADV_COLLECT', @EMP_ID 
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID AND PREV_DEBIT > 0
    UNION ALL
    SELECT @ACC_TAX_PAYABLE, N'مالیات عیدی', 0, EIDI_TAX, 'TAX_PAYABLE', NULL 
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI_TAX > 0;
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_GET_ADVANCES]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۲. SP_PAY2_GET_ADVANCES — محاسبه مساعده هوشمند (نسخه نهایی — JSON_VALUE)
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_GET_ADVANCES]
    @PERIOD_DATE  BIGINT,
    @PAYROLL_N_S  FLOAT,
    @WS_ID        INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. خواندن کد کامل حساب مساعده
    DECLARE @FULL_HES NVARCHAR(100);
    SELECT @FULL_HES = ACC_CODE 
    FROM PAY2_WORKSHOP_ACC WITH (NOLOCK)
    WHERE WS_ID = @WS_ID AND ACC_KEY = 'ADV_HES';

    IF @FULL_HES IS NULL
    BEGIN
        RAISERROR(N'حساب مساعده (ADV_HES) برای این کارگاه تنظیم نشده است.', 16, 1);
        RETURN;
    END;

    -- 2. پارس کردن کد ترکیبی با استفاده از JSON_VALUE 
    DECLARE @JsonArr NVARCHAR(250) = N'["' + REPLACE(@FULL_HES, '-', '","') + N'"]';
    
    DECLARE @HES_K  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[0]'), '') AS INT);
    DECLARE @HES_M  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[1]'), '') AS INT);
    DECLARE @HES_T  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[2]'), '') AS INT);
    DECLARE @HES_T2 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[3]'), '') AS INT);
    DECLARE @HES_T3 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[4]'), '') AS INT);
    DECLARE @HES_T4 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[5]'), '') AS INT);

    -- بررسی امنیتی حساب
    IF @HES_K IS NULL OR @HES_M IS NULL
    BEGIN
        RAISERROR(N'فرمت حساب مساعده نادرست است. باید حداقل شامل کل و معین باشد (مثال: 112-1).', 16, 1);
        RETURN;
    END;

    -- 3. تعیین سطح اعمال فیلتر کد پرسنل (ACC_T)
    DECLARE @EMP_FILTER_LEVEL TINYINT =
        CASE
            WHEN @HES_T  IS NULL THEN 3   
            WHEN @HES_T2 IS NULL THEN 4   
            WHEN @HES_T3 IS NULL THEN 5   
            ELSE                     6    
        END;

    -- 4. خواندن تنظیمات اضافی به صورت ایمن
    DECLARE @USE_T BIT = 1, @MIN_POS BIT = 1, @ADV_SCOPE NVARCHAR(20) = 'CURRENT_MONTH';
    
    SELECT 
        @USE_T     = ISNULL(CAST(MAX(CASE WHEN CFG_KEY = 'ADV_USE_HES_T_FILTER' THEN TRY_CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @MIN_POS   = ISNULL(CAST(MAX(CASE WHEN CFG_KEY = 'ADV_MIN_POSITIVE'   THEN TRY_CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @ADV_SCOPE = ISNULL(MAX(CASE WHEN CFG_KEY = 'ADV_SCOPE' THEN CFG_VALUE END), 'CURRENT_MONTH')
    FROM PAY2_CONFIG WITH (NOLOCK)
    WHERE CFG_KEY IN ('ADV_USE_HES_T_FILTER', 'ADV_MIN_POSITIVE', 'ADV_SCOPE');

    -- 5. محاسبه بازه تاریخ به صورت امن و بدون تقسیم خطرناک
    -- تبدیل 14030700 به بازه 14030700 تا 14030799
    DECLARE @MONTH_START BIGINT = (@PERIOD_DATE / 100) * 100;       
    DECLARE @MONTH_END   BIGINT = @MONTH_START + 99;  

    -- 6. اجرای کوئری نهایی مالی
    ;WITH AdvBase AS
    (
        SELECT
            E.EMP_ID,
            E.ACC_T                            AS PCODE,
            E.LAST_NAME + N' ' + E.FIRST_NAME  AS FULL_NAME,

            -- مانده خام از حسابداری
            ISNULL((
                SELECT CAST(SUM(D.BED - D.BES) AS BIGINT)
                FROM DEED_HED H
                INNER JOIN DEED_DTL D ON H.N_S = D.N_S
                WHERE
                    D.HES_K = @HES_K
                    AND D.HES_M = @HES_M
                    -- 🚀 فیلتر دقیق سطوح بالادستی (باید دقیقاً برابر با مقدار کانفیگ باشند)
                    AND (@EMP_FILTER_LEVEL <= 3 OR D.HES_T  = @HES_T)
                    AND (@EMP_FILTER_LEVEL <= 4 OR D.HES_T2 = @HES_T2)
                    AND (@EMP_FILTER_LEVEL <= 5 OR D.HES_T3 = @HES_T3)
                    AND (@EMP_FILTER_LEVEL <= 6 OR D.HES_T4 = @HES_T4)
                    
                    -- 🚀 فیلتر سطح پرسنل (یا فعال نیست، یا باید دقیقاً برابر با کد پرسنل باشد)
                    AND (
                        @USE_T = 0
                        OR TRY_CAST(NULLIF(TRIM(E.ACC_T), '') AS INT) = 
                           CASE @EMP_FILTER_LEVEL 
                                WHEN 3 THEN D.HES_T 
                                WHEN 4 THEN D.HES_T2 
                                WHEN 5 THEN D.HES_T3 
                                WHEN 6 THEN D.HES_T4 
                           END
                    )

                    -- 🚀 جلوگیری از نشت داده (سطوح پایین‌تر از پرسنل باید خالی یا صفر باشند)
                    AND (@EMP_FILTER_LEVEL >= 4 OR ISNULL(D.HES_T2, 0) = 0)
                    AND (@EMP_FILTER_LEVEL >= 5 OR ISNULL(D.HES_T3, 0) = 0)
                    AND (@EMP_FILTER_LEVEL >= 6 OR ISNULL(D.HES_T4, 0) = 0)

                    AND H.N_S < ISNULL(@PAYROLL_N_S, 999999999)
                    AND H.OKF = 1
                    AND (
                        @ADV_SCOPE = 'OPEN_BALANCE'
                        OR (H.DATE_S BETWEEN @MONTH_START AND @MONTH_END) 
                    )
            ), 0) AS RAW_BALANCE,

            -- استثناهای دستی مساعده
            ISNULL((
                SELECT SUM(EXCL_AMOUNT)
                FROM PAY2_ADVANCE_EXCL WITH (NOLOCK)
                WHERE EMP_ID = E.EMP_ID
                  AND PERIOD_DATE BETWEEN @MONTH_START AND @MONTH_END
            ), 0) AS MANUAL_EXCL

        FROM PAY2_EMPLOYEE E WITH (NOLOCK)
        INNER JOIN PAY2_PERIOD P WITH (NOLOCK)
            ON P.WS_ID = E.WS_ID
            AND P.PERIOD_DATE = @PERIOD_DATE 
        WHERE E.WS_ID     = @WS_ID
          AND E.IS_ACTIVE = 1
          AND E.ACC_T IS NOT NULL
    )
    SELECT
        EMP_ID,
        PCODE,
        FULL_NAME,
        RAW_BALANCE,
        MANUAL_EXCL,
        CASE
            WHEN @MIN_POS = 1 AND (RAW_BALANCE - MANUAL_EXCL) <= 0
                THEN 0
            ELSE CASE
                    WHEN (RAW_BALANCE - MANUAL_EXCL) < 0 THEN 0
                    ELSE RAW_BALANCE - MANUAL_EXCL
                 END
        END AS ADVANCE_DEDUCTION
    FROM AdvBase
    OPTION (RECOMPILE); 

END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_LOAN_GEN_SCHED]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۹. SP_PAY2_LOAN_GEN_SCHED — تولید خودکار جدول اقساط وام
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_LOAN_GEN_SCHED]
    @LOAN_ID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @TOTAL_INST  SMALLINT,
        @INSTALLMENT BIGINT,
        @FIRST_PAY   BIGINT,
        @EMP_ID      INT;

    SELECT
        @TOTAL_INST  = TOTAL_INST,
        @INSTALLMENT = INSTALLMENT,
        @FIRST_PAY   = FIRST_PAY,
        @EMP_ID      = EMP_ID
    FROM PAY2_LOAN WHERE LOAN_ID = @LOAN_ID;

    IF @TOTAL_INST IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_LOAN_GEN_SCHED: وام %d یافت نشد.', 16, 1, @LOAN_ID);
        RETURN;
    END;

    DELETE FROM PAY2_LOAN_SCHED WHERE LOAN_ID = @LOAN_ID AND PAID_AT IS NULL;

    DECLARE @I SMALLINT = 1;
    DECLARE @DUE BIGINT = @FIRST_PAY;

    DECLARE @DUE_YEAR  INT = @FIRST_PAY / 10000;
    DECLARE @DUE_MONTH INT = (@FIRST_PAY % 10000) / 100;

    WHILE @I <= @TOTAL_INST
    BEGIN
        DECLARE @THIS_AMT BIGINT =
            CASE WHEN @I = @TOTAL_INST
                 THEN (
                    SELECT CASE
                             WHEN AMOUNT - (@INSTALLMENT * (@TOTAL_INST - 1)) < 0 THEN 0
                             ELSE AMOUNT - (@INSTALLMENT * (@TOTAL_INST - 1))
                           END
                    FROM PAY2_LOAN WHERE LOAN_ID = @LOAN_ID
                 )
                 ELSE @INSTALLMENT
            END;

        INSERT INTO PAY2_LOAN_SCHED (LOAN_ID, INST_NUM, DUE_PERIOD, AMOUNT)
        VALUES (@LOAN_ID, @I, @DUE_YEAR * 10000 + @DUE_MONTH * 100, @THIS_AMT);

        SET @DUE_MONTH = @DUE_MONTH + 1;
        IF @DUE_MONTH > 12
        BEGIN
            SET @DUE_MONTH = 1;
            SET @DUE_YEAR  = @DUE_YEAR + 1;
        END;

        SET @I = @I + 1;
    END;

    PRINT N'SP_PAY2_LOAN_GEN_SCHED — ' + CAST(@TOTAL_INST AS NVARCHAR) + N' قسط برای وام ' + CAST(@LOAN_ID AS NVARCHAR) + N' ایجاد شد.';
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_NEW_PERIOD]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۱۱. SP_PAY2_NEW_PERIOD — ایجاد دوره ماهیانه جدید
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_NEW_PERIOD]
    @WS_ID        INT,
    @PERIOD_DATE  BIGINT,
    @HOLIDAY_DAYS TINYINT = 0,
    @OPENED_BY    INT     = NULL,
    @NEW_PER_ID   INT     OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM PAY2_PERIOD WHERE WS_ID=@WS_ID AND PERIOD_DATE=@PERIOD_DATE)
    BEGIN
        RAISERROR(N'SP_PAY2_NEW_PERIOD: دوره %I64d برای کارگاه %d قبلاً ایجاد شده است.', 16, 1, @PERIOD_DATE, @WS_ID);
        RETURN;
    END;

    INSERT INTO PAY2_PERIOD (WS_ID, PERIOD_DATE, HOLIDAY_DAYS, STATUS, OPENED_AT)
    VALUES (@WS_ID, @PERIOD_DATE, @HOLIDAY_DAYS, 1, GETDATE());

    SET @NEW_PER_ID = SCOPE_IDENTITY();

    PRINT N'SP_PAY2_NEW_PERIOD — دوره ' + CAST(@PERIOD_DATE AS NVARCHAR) + N' با PER_ID=' + CAST(@NEW_PER_ID AS NVARCHAR) + N' ایجاد شد.';
END;
GO
/****** Object:  StoredProcedure [dbo].[SP_PAY2_REVERT_RUN]    Script Date: 24/04/1405 01:56:54 ق.ظ ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ================================================================
-- ۶. SP_PAY2_REVERT_RUN — برگشت محاسبه (بازگشت به حالت قابل ویرایش)
-- ================================================================
CREATE   PROCEDURE [dbo].[SP_PAY2_REVERT_RUN]
    @RUN_ID   INT,
    @REVERT_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- این پروسیجر همیشه داخل تراکنشِ لایه‌ی C# (ExecuteInTransactionAsync) اجرا می‌شود،
    -- چه مستقیم و چه از طریق SP_PAY2_CALC_RUN. پس تراکنش داخلی نمی‌گذاریم تا تداخلِ
    -- تراکنش تو‌در‌تو (ROLLBACK داخلی که تراکنش بیرونی را می‌کشد) رخ ندهد. XACT_ABORT
    -- تضمین می‌کند در صورت خطا، تراکنش بیرونی doomed و توسط C# رول‌بک شود.
    SET XACT_ABORT ON;

    DECLARE @STATUS TINYINT;
    DECLARE @PER_ID INT;
    DECLARE @IS_LATEST BIT;
    DECLARE @PERIOD_DATE BIGINT;

    SELECT @STATUS = R.STATUS, @PER_ID = R.PER_ID, @IS_LATEST = R.IS_LATEST, @PERIOD_DATE = P.PERIOD_DATE
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE R.RUN_ID = @RUN_ID;

    IF @PER_ID IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: محاسبه‌ای با این شناسه یافت نشد.', 16, 1);
        RETURN;
    END;

    IF @STATUS >= 3
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: سند حسابداری صادر شده — برگشت ممکن نیست.', 16, 1);
        RETURN;
    END;

    IF @IS_LATEST = 0
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: فقط آخرین نسخه (IS_LATEST=1) قابل برگشت است.', 16, 1);
        RETURN;
    END;

    -- گارد Idempotency: اگر خروجی‌های RUN قبلاً پاک شده‌اند، برگشت دوباره نباید
    -- مرخصی یا اقساط را مجدداً دستکاری کند.
    IF NOT EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID)
    BEGIN
        RETURN;
    END;

    -- 1. بازگرداندن دقیق تعداد اقساط کسر شده در این RUN (فقط وام‌های درگیر همین RUN)
    UPDATE L SET L.PAID_INST = L.PAID_INST - (
        SELECT COUNT(1) FROM PAY2_LOAN_SCHED LS
        WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @RUN_ID
    )
    FROM PAY2_LOAN L
    WHERE EXISTS (SELECT 1 FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @RUN_ID);

    UPDATE PAY2_LOAN_SCHED
    SET RUN_ID = NULL, PAID_AT = NULL
    WHERE RUN_ID = @RUN_ID;

    -- 2. بازگرداندن دقیقه‌های مرخصی کسر شده (محافظت در برابر اعداد منفی)
    UPDATE LB
    SET LB.USED_MIN = CASE
                        WHEN LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT) < 0 THEN 0
                        ELSE LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT)
                      END,
        LB.UPDATED_AT = GETDATE()
    FROM PAY2_LEAVE_BAL LB
    INNER JOIN PAY2_ATTENDANCE A ON LB.EMP_ID = A.EMP_ID
    WHERE A.PER_ID = @PER_ID AND LB.YEAR = (@PERIOD_DATE / 10000)
      AND A.LEAVE_DAYS > 0;

    -- 3. حذف فیش‌ها
    DELETE FROM PAY2_RUN_DETAIL WHERE RUN_ID = @RUN_ID;
    DELETE FROM PAY2_RUN_LINE    WHERE RUN_ID = @RUN_ID;

    -- 4. باز کردن دوره و ثبت لاگ
    UPDATE PAY2_RUN
    SET STATUS = 1,
        NOTES = SUBSTRING(ISNULL(NOTES,'') + N' | Reverted by ' + CAST(ISNULL(@REVERT_BY,0) AS NVARCHAR), 1, 300)
    WHERE RUN_ID = @RUN_ID;

    UPDATE PAY2_PERIOD SET STATUS = 2 WHERE PER_ID = @PER_ID;

END;
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'كد نوع' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CUST_COD' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CUST_COD' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'CUSTKIND' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUST_COD'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نوع مشتري' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CUSTKNAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'50' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CUSTKNAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'CUSTKIND' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND', @level2type=N'COLUMN',@level2name=N'CUSTKNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CUSTKIND' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'CUSTKIND' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'CUSTKIND'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'رديف' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'RADIF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'RADIF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'حساب كل' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'HES_K' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'HES_K' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_K'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'حساب معين' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'HES_M' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'HES_M' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_M'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'معين تفضيلي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'HES_T' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'5' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'HES_T' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES_T'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'4650' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شرح' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=4650 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'SHARH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'6' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'60' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'SHARH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'SHARH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'بدهكار' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'BED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'BED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'ValidationRule', @value=N'Not Is Null' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BED'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'بستانكار' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'BES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'BES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ValidationRule', @value=N'Not Is Null' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BES'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره سري' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_SERI' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'9' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_SERI' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'N_SERI'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'بانك' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'BANK' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'BANK' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'BANK'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره فاكتور' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'11' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'برچسب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TAG' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'12' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TAG' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'حساب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'13' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'20' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'id'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'id'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'COLUMN',@level2name=N'id'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DefaultView', @value=0x02 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'رديف هاي سند حسابداري' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Filter', @value=N'((DEED_DTL.HES="111-1-1"))' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderBy', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=0x01 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_TableMaxRecords', @value=10000 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The value entered is prohibited by the validation rule set for field ''BED''.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'CONSTRAINT',@level2name=N'CK DEED_DTL BED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The value entered is prohibited by the validation rule set for field ''BES''.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_DTL', @level2type=N'CONSTRAINT',@level2name=N'CK DEED_DTL BES'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'تاريخ سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DATE_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'DATE_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'DATE_S'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شرح  سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'SHARH_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'70' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'SHARH_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'SHARH_S'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نوع سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NO_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NO_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'NO_S'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'انبار' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'ANBAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'5' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'ANBAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره فاكتور' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_FACTOR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'6' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_FACTOR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'N_FACTOR'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'وضعيت سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'GHATEI' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'GHATEI' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'GHATEI'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'USER_NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'USER_NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'base'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'base'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED', @level2type=N'COLUMN',@level2name=N'base'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DefaultView', @value=0x02 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'شماره و تاريخ سند حسابداري' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Filter', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_LinkChildFields', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_LinkMasterFields', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderBy', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=0x01 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_SubdatasheetName', @value=N'dbo.DEED_DTL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_TableMaxRecords', @value=10000 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'DEED_HED' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEED_HED'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'كد دپارتمان' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DEPATMAN' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'DEPATMAN' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEPART' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نام' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DEPNAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'50' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'DEPNAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DEPART' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART', @level2type=N'COLUMN',@level2name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DEPART' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'DEPART' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DEPART'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'حساب كل' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_KOL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_KOL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره حساب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نام حساب معين' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'100' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'توضيحات' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TOZIH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TOZIH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'بدهكار بستانكار' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'BED_BES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'5' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'BED_BES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'آدرس' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'ADDRESS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'6' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'100' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'ADDRESS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'تلفن' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TEL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'20' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TEL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'كداقتصادي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CODE_E' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'20' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CODE_E' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'تعريف حسابهاي معين' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Filter', @value=N'((Not DETA_HES.NUMBER=1))' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'DETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The record cannot be deleted because the table ''DETA_HES'' includes related records.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'DETA_HES', @level2type=N'CONSTRAINT',@level2name=N'DETA_HES_FK00'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره فاكتور' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'برچسب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TAG' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TAG' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'انبار' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'ANBAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'ANBAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'1545' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره فاكتور برگشت' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NUMBER1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NUMBER1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'NUMBER1'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'تاريخ فاكتور' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DATE_N' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'5' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'DATE_N' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DATE_N'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'تحويل گيردنده' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'6' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'25' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MAS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MAS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MAS'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'VAS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'VAS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'VAS'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'9' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_S' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'N_S'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره مشتري' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CUST_NO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CUST_NO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'ملاحظات' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MOLAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'11' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'60' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MOLAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOLAH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مبلغ نقد' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'M_NAGHD' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'12' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'M_NAGHD' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'M_NAGHD'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مبلغ واريزي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MABL_VAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'13' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MABL_VAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'معين واريزي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MOIN_VAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'14' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MOIN_VAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_VAR'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مبلغ حواله' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MABL_HAV' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'15' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MABL_HAV' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'معين حواله' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MOIN_HAV' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'16' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MOIN_HAV' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAV'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مبلغ هزينه' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MABL_HAZ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'17' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MABL_HAZ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MABL_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'معين هزينه' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MOIN_HAZ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'18' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MOIN_HAZ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_HAZ'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'تخفيف' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TAKHFIF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'19' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TAKHFIF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'TAKHFIF'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'معين تخفيف' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MOIN_KHF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'20' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MOIN_KHF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'MOIN_KHF'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'انبار فرعي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'ANBARF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'21' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'ANBARF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'1620' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره فاكتور فروشنده' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'FNUMCO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'22' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'FNUMCO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'FNUMCO'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'دپاتمان' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'DEPATMAN' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'23' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'DEPATMAN' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'DEPATMAN'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شيفت' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'SHIFT' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'24' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'SHIFT' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHIFT'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نوع مشتري' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CUST_KIND' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'25' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CUST_KIND' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'CUST_KIND'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'USER_NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'26' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'USER_NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'USER_NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHARAYET'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHARAYET'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SHARAYET'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN1'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN1'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN1'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN2'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN2'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN2'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN3'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN3'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN3'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN4'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN4'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST', @level2type=N'COLUMN',@level2name=N'SGN4'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'سر برگ فاكتورها' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'HEAD_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'HEAD_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره فاكتور' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'برچسب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TAG' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TAG' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'TAG'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'انبار' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'ANBAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'ANBAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBAR'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'رديف' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'RADIF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'RADIF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADIF'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'كد كالا' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CODE' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'5' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'15' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CODE' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CODE'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مقداركالا' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MEGH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'6' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MEGH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'ValidationRule', @value=N'Not Is Null' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مقدار كل كالا' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MEGHk' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MEGHk' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'ValidationRule', @value=N'Not Is Null' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مقدار مرجوعي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MEGH_MAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MEGH_MAR' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_MAR'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'ملاحظات' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MANDAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'9' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'50' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MANDAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MANDAH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مبلغ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MABL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MABL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'ValidationRule', @value=N'Not Is Null' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مبلغ كل' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MABL_K' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'11' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MABL_K' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ValidationRule', @value=N'Not Is Null' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'FROM_A' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'12' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'FROM_A' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'FROM_A'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره رسيد' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_RASID' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'13' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_RASID' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_RASID'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'مقدار رسيد' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'MEGH_R' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'14' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'MEGH_R' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'MEGH_R'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'رده' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'RADAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'15' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'RADAH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'RADAH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره سند' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'SANAD_NO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'16' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'SANAD_NO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'SANAD_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره مشتري' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CUST_NO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'17' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CUST_NO' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'CUST_NO'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'انبار فرعي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'ANBARF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'18' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'ANBARF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'ANBARF'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'واحدكالا' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'VAHED_K' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'19' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'VAHED_K' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'VAHED_K'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'حساب كل' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_KOL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'20' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_KOL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره حساب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_MOIN' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'21' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_MOIN' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_MOIN'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'معين تفضيلي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'N_TAF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'22' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'N_TAF' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'N_TAF'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'AVRAGE' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'23' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'AVRAGE' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'id'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'id'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'id'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE2'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'COLUMN',@level2name=N'AVRAGE2'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'فاكتورها' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'INVO_LST' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The value entered is prohibited by the validation rule set for field ''MABL''.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'CONSTRAINT',@level2name=N'CK INVO_LST MABL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The value entered is prohibited by the validation rule set for field ''MABL_K''.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'CONSTRAINT',@level2name=N'CK INVO_LST MABL_K'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The value entered is prohibited by the validation rule set for field ''MEGH''.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'CONSTRAINT',@level2name=N'CK INVO_LST MEGH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The value entered is prohibited by the validation rule set for field ''MEGHk''.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'INVO_LST', @level2type=N'CONSTRAINT',@level2name=N'CK INVO_LST MEGHk'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'N_KOL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TNUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TNUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TNUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TNUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TNUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TNUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نام حساب معين' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=2865 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'100' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'توضيحات' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TOZIH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'5' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'40' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TOZIH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TOZIH'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'بدهكار بستانكار' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'BED_BES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'6' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'BED_BES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'BED_BES'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'آدرس' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'ADDRESS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'100' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'ADDRESS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'ADDRESS'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'تلفن' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TEL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'20' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'TEL' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'TEL'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'كداقتصادي' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CODE_E' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'9' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'20' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CODE_E' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'CODE_E'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES', @level2type=N'COLUMN',@level2name=N'IDD'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DefaultView', @value=0x02 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'تعريف حسابهاي معين' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Filter', @value=N'((TDETA_HES.N_KOL=721))' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderBy', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=0x01 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_TableMaxRecords', @value=10000 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'TDETA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TDETA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1033' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'2835' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=2835 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'FORMNAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'50' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'FORMNAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TFORMS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'FORMNAME'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1033' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'3870' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=3870 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'CAPTION' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'100' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'CAPTION' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TFORMS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'CAPTION'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'690' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=690 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'kind' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'kind' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TFORMS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'kind'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'GRP' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'GRP' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TFORMS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'GRP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'IDH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'IDH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'IDH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'IDH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Format', @value=N'' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'IDH'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_IMEMode', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS', @level2type=N'COLUMN',@level2name=N'IDH'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/12 10:09:55 ق.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/12 10:09:56 ق.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DefaultView', @value=0x02 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Filter', @value=N'((TFORMS.CAPTION ALike "پذيرش%"))' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderBy', @value=N'TFORMS.IDH' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=0x01 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_TableMaxRecords', @value=10000 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TFORMS' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'111' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'True' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TFORMS'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'شماره حساب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NUMBER' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TOTA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NUMBER'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نام حساب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'2' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'50' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NAME' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TOTA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'10' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NAME'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'نوع حساب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'NO_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'3' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'NO_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TOTA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'NO_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'وضعيت' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'M_D' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'4' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'M_D' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TOTA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'M_D'
GO
EXEC sys.sp_addextendedproperty @name=N'AllowZeroLength', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'CollatingOrder', @value=N'1025' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnHidden', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnOrder', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'ColumnWidth', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'DataUpdatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'DefaultValue', @value=N'0' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Caption', @value=N'گروه حساب' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnHidden', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnOrder', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ColumnWidth', @value=-1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DecimalPlaces', @value=N'255' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DisplayControl', @value=N'109' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'GROUP' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'OrdinalPosition', @value=N'5' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'Required', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'Size', @value=N'8' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceField', @value=N'GROUP' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTable', @value=N'TOTA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'Type', @value=N'7' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'COLUMN',@level2name=N'GROUP'
GO
EXEC sys.sp_addextendedproperty @name=N'Attributes', @value=N'1073741824' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Connect', @value=N';DATABASE=D:\software\negin98\negin83\NEGIN_DB.mdb' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'DateCreated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'LastUpdated', @value=N'2004/07/16 01:19:18 ب.ظ' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DefaultView', @value=0x02 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'حسابهاي كل' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Filter', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderBy', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=0x01 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_TableMaxRecords', @value=10000 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Name', @value=N'TOTA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'RecordCount', @value=N'-1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'SourceTableName', @value=N'TOTA_HES' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'Updatable', @value=N'False' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_ConstraintText', @value=N'The record cannot be deleted because the table ''TOTA_HES'' includes related records.' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TOTA_HES', @level2type=N'CONSTRAINT',@level2name=N'TOTA_HES_FK02'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DefaultView', @value=0x02 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DiagramPane1', @value=N'[0E232FF0-B466-11cf-A24F-00AA00A3EFFF, 1.00]
Begin DesignProperties = 
   Begin PaneConfigurations = 
      Begin PaneConfiguration = 0
         NumPanes = 4
         Configuration = "(H (1[40] 4[20] 2[20] 3) )"
      End
      Begin PaneConfiguration = 1
         NumPanes = 3
         Configuration = "(H (1[50] 4[25] 3) )"
      End
      Begin PaneConfiguration = 2
         NumPanes = 3
         Configuration = "(H (1[50] 2[25] 3) )"
      End
      Begin PaneConfiguration = 3
         NumPanes = 3
         Configuration = "(H (4 [30] 2 [40] 3))"
      End
      Begin PaneConfiguration = 4
         NumPanes = 2
         Configuration = "(H (1 [56] 3))"
      End
      Begin PaneConfiguration = 5
         NumPanes = 2
         Configuration = "(H (2 [66] 3))"
      End
      Begin PaneConfiguration = 6
         NumPanes = 2
         Configuration = "(H (4 [50] 3))"
      End
      Begin PaneConfiguration = 7
         NumPanes = 1
         Configuration = "(V (3))"
      End
      Begin PaneConfiguration = 8
         NumPanes = 3
         Configuration = "(H (1 [56] 4 [18] 2))"
      End
      Begin PaneConfiguration = 9
         NumPanes = 2
         Configuration = "(H (1[75] 4) )"
      End
      Begin PaneConfiguration = 10
         NumPanes = 2
         Configuration = "(H (1[66] 2) )"
      End
      Begin PaneConfiguration = 11
         NumPanes = 2
         Configuration = "(H (4 [60] 2))"
      End
      Begin PaneConfiguration = 12
         NumPanes = 1
         Configuration = "(H (1) )"
      End
      Begin PaneConfiguration = 13
         NumPanes = 1
         Configuration = "(V (4))"
      End
      Begin PaneConfiguration = 14
         NumPanes = 1
         Configuration = "(V (2))"
      End
      ActivePaneConfig = 9
   End
   Begin DiagramPane = 
      Begin Origin = 
         Top = 0
         Left = 0
      End
      Begin Tables = 
         Begin Table = "TDETA_HES"
            Begin Extent = 
               Top = 6
               Left = 38
               Bottom = 114
               Right = 189
            End
            DisplayFlags = 280
            TopColumn = 0
         End
      End
   End
   Begin SQLPane = 
      PaneHidden = 
   End
   Begin DataPane = 
      PaneHidden = 
      Begin ParameterDefaults = ""
      End
      RowHeights = 220
   End
   Begin CriteriaPane = 
      Begin ColumnWidths = 11
         Column = 8040
         Alias = 900
         Table = 1170
         Output = 720
         Append = 1400
         NewValue = 1170
         SortType = 1350
         SortOrder = 1410
         GroupBy = 1350
         Filter = 1350
         Or = 1350
         Or = 1350
         Or = 1350
      End
   End
End
' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_DiagramPaneCount', @value=1 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Filter', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderBy', @value=NULL , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_OrderByOn', @value=0 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_Orientation', @value=0x00 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
EXEC sys.sp_addextendedproperty @name=N'MS_TableMaxRecords', @value=10000 , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'CUST_HESAB'
GO
