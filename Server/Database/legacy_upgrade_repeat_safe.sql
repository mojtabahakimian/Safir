-- Repeat-safe legacy migration blocks; identical to ScriptSqly Main/Blazor.
INSERT INTO dbo.PRICE_PAYNO ([PPID], [PPAME], [TR_DATE], [USERNAME], [MODAT])
SELECT seed.[PPID], seed.[PPAME], seed.[TR_DATE], seed.[USERNAME], seed.[MODAT] FROM (VALUES
(0, N'آزاد', GETDATE(), N'System', 0)
) AS seed ([PPID], [PPAME], [TR_DATE], [USERNAME], [MODAT])
WHERE NOT EXISTS (SELECT 1 FROM dbo.PRICE_PAYNO AS target WHERE target.[PPID]=seed.[PPID]);
GO

IF OBJECT_ID(N'dbo.DEFAULTDEP',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[DEFAULTDEP](
	[TFSAZMAN] [int] NULL,
	[SHIFT] [int] NULL,
	[USERID] [int] NOT NULL,
	[CRT] [datetime] NULL,
	[UID] [int] NULL,
 CONSTRAINT [PK_DEFAULTDEP] PRIMARY KEY CLUSTERED 
(
	[USERID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DEFAULTDEP') AND name=N'CRT' AND default_object_id<>0)
ALTER TABLE [dbo].[DEFAULTDEP] ADD  DEFAULT (getdate()) FOR [CRT]
GO

INSERT INTO GSCADTL ([GSCADTCOD], [GSCANAME], [GSCAGRADE], [GSCAFROM], [GSCATO], [GSCACOD])
SELECT seed.[GSCADTCOD], seed.[GSCANAME], seed.[GSCAGRADE], seed.[GSCAFROM], seed.[GSCATO], seed.[GSCACOD] FROM (VALUES
( 1, N'عالی', 100, 0, 0, 1 ), 
									( 2, N'خیلی خوب', 83, 0, 0, 1 ), 
									( 3, N'خوب', 66, 0, 0, 1 ), 
									( 4, N'متوسط', 50, 0, 0, 1 ), 
									( 5, N'ضعیف', 33, 0, 0, 1 ), 
									( 6, N'خیلی ضعیف', 16, 0, 0, 1 ), 
									( 7, N'بد', 0, 0, 0, 1 ), 
									( 8, N'تا دیپلم', 20, 0, 0, 2 ), 
									( 9, N'فوق دیپلم', 40, 0, 0, 2 ), 
									( 10, N'لیسانس', 60, 0, 0, 2 ), 
									( 11, N'فوق لیسانس', 80, 0, 0, 2 ), 
									( 12, N'دکتری', 100, 0, 0, 2 ), 
									( 13, N'تا 30', 0, 0, 30, 3 ), 
									( 14, N'31', 5, 31, 31, 3 ), 
									( 15, N'32', 10, 32, 32, 3 ), 
									( 16, N'33', 15, 33, 33, 3 ), 
									( 17, N'34', 20, 34, 34, 3 ), 
									( 18, N'35', 25, 35, 35, 3 ), 
									( 19, N'36', 30, 36, 36, 3 ), 
									( 20, N'37', 35, 37, 37, 3 ), 
									( 21, N'38', 40, 38, 38, 3 ), 
									( 22, N'39', 45, 39, 39, 3 ), 
									( 23, N'40', 50, 40, 40, 3 ), 
									( 24, N'41', 55, 41, 41, 3 ), 
									( 25, N'42', 60, 42, 42, 3 ), 
									( 26, N'43', 65, 43, 43, 3 ), 
									( 27, N'44', 70, 44, 44, 3 ), 
									( 28, N'45', 75, 45, 45, 3 ), 
									( 29, N'46', 80, 46, 46, 3 ), 
									( 30, N'47', 85, 47, 47, 3 ), 
									( 31, N'48', 90, 48, 48, 3 ), 
									( 32, N'49', 95, 49, 49, 3 ), 
									( 33, N'50', 100, 50, 50, 3 ), 
									( 34, N'زیر 1 سال', 0, 1, 1, 4 ), 
									( 35, N'1 سال', 10, 1, 1, 4 ), 
									( 36, N'2 سال', 20, 2, 2, 4 ), 
									( 37, N'3 سال', 30, 3, 3, 4 ), 
									( 38, N'4 سال', 40, 4, 4, 4 ), 
									( 39, N'5 سال', 50, 5, 5, 4 ), 
									( 40, N'6 سال', 60, 6, 6, 4 ), 
									( 41, N'7 سال', 70, 7, 7, 4 ), 
									( 42, N'8 سال', 80, 8, 8, 4 ), 
									( 43, N'9 سال', 90, 9, 9, 4 ), 
									( 44, N'10 سال', 100, 10, 10, 4 ), 
									( 45, N'بیشتر 10 سال', 100, 11, 1000, 4 ), 
									( 46, N'بیشتر از 50', 100, 51, 1000, 3 ), 
									( 47, N'زیر 6 ماه', 0, 0, 0, 5 ), 
									( 48, N'6ماه', 10, 60, 1000, 5 ), 
									( 49, N'1 سال', 20, 0, 0, 5 ), 
									( 50, N'1.5 سال', 30, 0, 0, 5 ), 
									( 51, N'2 سال', 40, 0, 0, 5 ), 
									( 52, N'2.5 سال', 50, 0, 0, 5 ), 
									( 53, N'3 سال', 60, 0, 0, 5 ), 
									( 54, N'3.5 سال', 70, 0, 0, 5 ), 
									( 55, N'4 سال', 80, 0, 0, 5 ), 
									( 56, N'4.5 سال', 90, 0, 0, 5 ), 
									( 57, N'5 سال وبیشتر', 100, 0, 0, 5 ), 
									( 58, N'مجرد', 0, 0, 0, 6 ), 
									( 59, N'متاهل', 100, 0, 0, 6 ), 
									( 60, N'بله', 100, 0, 0, 7 ), 
									( 61, N'خیر', 0, 0, 0, 7 ), 
									( 62, N'زیر50 میلیون تومان', 0, 0, 0, 8 ), 
									( 63, N'از 50 تا 100 میلیون تومان', 10, 0, 0, 8 ), 
									( 64, N'از 100 تا 150 میلیون تومان', 20, 0, 0, 8 ), 
									( 65, N'از 150 تا 200 میلیون تومان', 30, 0, 0, 8 ), 
									( 66, N'از 200 تا 250 میلیون تومان', 40, 0, 0, 8 ), 
									( 67, N'از 250 تا 300 میلیون تومان', 50, 0, 0, 8 ), 
									( 68, N'از 300 تا 350 میلیون تومان', 60, 0, 0, 8 ), 
									( 69, N'از 350 تا 400 میلیون تومان', 70, 0, 0, 8 ), 
									( 70, N'از 400 تا 450 میلیون تومان', 80, 0, 0, 8 ), 
									( 71, N'از 450 تا 500 میلیون تومان', 90, 0, 0, 8 ), 
									( 72, N'از 500 میلیون تومان به بالا', 100, 0, 0, 8 ), 
									( 73, N'زیر 1 سال', 0, 0, 0, 9 ), 
									( 74, N'1 سال', 100, 0, 0, 9 ), 
									( 75, N'2 سال', 200, 0, 0, 9 ), 
									( 76, N'3 سال', 300, 0, 0, 9 ), 
									( 77, N'4 سال', 400, 0, 0, 9 ), 
									( 78, N'5 سال', 500, 0, 0, 9 ), 
									( 79, N'6 سال', 600, 0, 0, 9 ), 
									( 80, N'7 سال', 700, 0, 0, 9 ), 
									( 81, N'8 سال', 800, 0, 0, 9 ), 
									( 82, N'9 سال', 900, 0, 0, 9 ), 
									( 83, N'10 سال و بیشتر', 1000, 0, 0, 9 ), 
									( 84, N'زیر 200 میلیون تومان', 100, 0, 0, 10 ), 
									( 85, N'از 200 تا 400 میلیون تومان', 200, 0, 0, 10 ), 
									( 86, N'از 400 تا 600 میلیون تومان', 300, 0, 0, 10 ), 
									( 87, N'از 600 تا 800 میلیون تومان', 400, 0, 0, 10 ), 
									( 88, N'از 800 میلیون تا 1 میلیارد', 500, 0, 0, 10 ), 
									( 89, N'از 1 میلیارد تا 1.2 میلیارد', 600, 0, 0, 10 ), 
									( 90, N'از1.2  میلیارد تا 1.4 میلیارد', 700, 0, 0, 10 ), 
									( 91, N'از 1.4میلیارد تا 1.6 میلیارد', 800, 0, 0, 10 ), 
									( 92, N'از 1.6میلیارد تا 1.8 میلیارد', 900, 0, 0, 10 ), 
									( 93, N'از 1.8میلیارد تا 2 میلیارد', 1000, 0, 0, 10 ), 
									( 94, N'عالی', 1000, 0, 0, 11 ), 
									( 95, N'خیلی خوب', 830, 0, 0, 11 ), 
									( 96, N'خوب', 660, 0, 0, 11 ), 
									( 97, N'متوسط', 500, 0, 0, 11 ), 
									( 98, N'ضعیف', 330, 0, 0, 11 ), 
									( 99, N'خیلی ضعیف', 160, 0, 0, 11 ), 
									( 100, N'بد', 0, 0, 0, 11 ), 
									( 101, N'عالی', 1000, 0, 0, 12 ), 
									( 102, N'خیلی خوب', 830, 0, 0, 12 ), 
									( 103, N'خوب', 660, 0, 0, 12 ), 
									( 104, N'متوسط', 500, 0, 0, 12 ), 
									( 105, N'ضعیف', 330, 0, 0, 12 ), 
									( 106, N'خیلی ضعیف', 160, 0, 0, 12 ), 
									( 107, N'بد', 0, 0, 0, 12 )
) AS seed ([GSCADTCOD], [GSCANAME], [GSCAGRADE], [GSCAFROM], [GSCATO], [GSCACOD])
WHERE NOT EXISTS (SELECT 1 FROM GSCADTL AS target WHERE target.[GSCADTCOD]=seed.[GSCADTCOD]);
GO

IF OBJECT_ID(N'dbo.TR_PAY_GETD',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[TR_PAY_GETD]
									(
									[N_SERI] [float] NULL,
									[BANK] [int] NULL,
									[DATE_S] [bigint] NULL,
									[DATE] [bigint] NULL,
									[SHOBEH] [nvarchar] (40) COLLATE Arabic_CI_AS NULL,
									[MABL] [float] NULL,
									[NAME_TAH] [nvarchar] (120) COLLATE Arabic_CI_AS NULL,
									[N_HESAB] [nvarchar] (100) COLLATE Arabic_CI_AS NULL,
									[N_S] [float] NULL,
									[N_KOL] [int] NULL,
									[N_MOIN] [int] NULL,
									[N_TAF] [int] NULL,
									[N_KOL2] [int] NULL,
									[N_MOIN2] [int] NULL,
									[N_TAF2] [int] NULL,
									[N_KOL3] [int] NULL,
									[N_MOIN3] [int] NULL,
									[N_TAF3] [int] NULL,
									[NUMBER] [float] NULL,
									[TAG] [float] NULL,
									[ANBAR] [float] NULL,
									[RADIF] [float] NULL,
									[CUST_NO] [nvarchar] (40) COLLATE Arabic_CI_AS NULL,
									[VAZ] [float] NULL,
									[LIST_NO] [int] NULL,
									[KIND] [int] NULL,
									[SANDUGH] [int] NULL,
									[HES1] [nvarchar] (80) COLLATE Arabic_CI_AS NULL,
									[HES2] [nvarchar] (80) COLLATE Arabic_CI_AS NULL,
									[HES3] [nvarchar] (80) COLLATE Arabic_CI_AS NULL,
									[ESTELAM] [nvarchar] (510) COLLATE Arabic_CI_AS NULL,
									[CRT] [datetime] NULL,
									[UID] [int] NULL,
									[SAYADI] [nvarchar] (32) COLLATE Arabic_CI_AS NULL,
									[ID] [bigint] NULL,
									[UP_DATE] [bigint] NOT NULL,
									[UP_TIME] [float] NOT NULL,
									[UP_USER_NAME] [nvarchar] (40) COLLATE Arabic_CI_AS NULL,
									[PC_NAME] [nvarchar] (50) COLLATE Arabic_CI_AS NULL,
									[IPADD] [nvarchar] (50) COLLATE Arabic_CI_AS NULL,
									[TRIDD] [int] NOT NULL IDENTITY(1, 1)
									) ON [PRIMARY]
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.TR_PAY_GETD') AND type='PK')
ALTER TABLE [dbo].[TR_PAY_GETD] ADD CONSTRAINT [PK__TR_PAY_G__9FFE4EA46E02EDDB] PRIMARY KEY CLUSTERED ([TRIDD]) ON [PRIMARY]
GO

INSERT INTO dbo.TCOD_ARZ ([Code], [Title], [ISOCode], [CountryName])
SELECT seed.[Code], seed.[Title], seed.[ISOCode], seed.[CountryName] FROM (VALUES
(965, N'ADB Unit of Account', N'XUA', N'MEMBER COUNTRIES OF THE AFRICAN DEVELOPMENT BANK'),
(971, N'Afghani', N'AFN', N'AFGHANISTAN'),
(8,   N'Lek', N'ALL', N'ALBANIA'),
(12,  N'Algerian Dinar', N'DZD', N'ALGERIA'),
(973, N'Kwanza', N'AOA', N'ANGOLA'),
(32,  N'Argentine Peso', N'ARS', N'ARGENTINA'),
(51,  N'Armenian Dram', N'AMD', N'ARMENIA'),
(533, N'Aruban Florin', N'AWG', N'ARUBA'),
(36,  N'Australian Dollar', N'AUD', N'AUSTRALIA'),
(944, N'Azerbaijan Manat', N'AZN', N'AZERBAIJAN'),
(44,  N'Bahamian Dollar', N'BSD', N'BAHAMAS (THE)'),
(48,  N'Bahraini Dinar', N'BHD', N'BAHRAIN'),
(50,  N'Taka', N'BDT', N'BANGLADESH'),
(52,  N'Barbados Dollar', N'BBD', N'BARBADOS'),
(933, N'Belarusian Ruble', N'BYN', N'BELARUS'),
(84,  N'Belize Dollar', N'BZD', N'BELIZE'),
(60,  N'Bermudian Dollar', N'BMD', N'BERMUDA'),
(64,  N'Ngultrum', N'BTN', N'BHUTAN'),
(68,  N'Boliviano', N'BOB', N'BOLIVIA (PLURINATIONAL STATE OF)'),
(984, N'Mvdol', N'BOV', N'BOLIVIA (PLURINATIONAL STATE OF)'),
(977, N'Convertible Mark', N'BAM', N'BOSNIA AND HERZEGOVINA'),
(72,  N'Pula', N'BWP', N'BOTSWANA'),
(986, N'Brazilian Real', N'BRL', N'BRAZIL'),
(96,  N'Brunei Dollar', N'BND', N'BRUNEI DARUSSALAM'),
(975, N'Bulgarian Lev', N'BGN', N'BULGARIA'),
(108, N'Burundi Franc', N'BIF', N'BURUNDI'),
(132, N'Cabo Verde Escudo', N'CVE', N'CABO VERDE'),
(116, N'Riel', N'KHR', N'CAMBODIA'),
(124, N'Canadian Dollar', N'CAD', N'CANADA'),
(136, N'Cayman Islands Dollar', N'KYD', N'CAYMAN ISLANDS (THE)'),
(950, N'CFA Franc BEAC', N'XAF', N'CAMEROON'),
(952, N'CFA Franc BCEAO', N'XOF', N'BURKINA FASO'),
(953, N'CFP Franc', N'XPF', N'FRENCH POLYNESIA'),
(152, N'Chilean Peso', N'CLP', N'CHILE'),
(990, N'Unidad de Fomento', N'CLF', N'CHILE'),
(156, N'Yuan Renminbi', N'CNY', N'CHINA'),
(170, N'Colombian Peso', N'COP', N'COLOMBIA'),
(970, N'Unidad de Valor Real', N'COU', N'COLOMBIA'),
(174, N'Comorian Franc', N'KMF', N'COMOROS (THE)'),
(976, N'Congolese Franc', N'CDF', N'CONGO (THE DEMOCRATIC REPUBLIC OF THE)'),
(188, N'Costa Rican Colon', N'CRC', N'COSTA RICA'),
(192, N'Cuban Peso', N'CUP', N'CUBA'),
(931, N'Peso Convertible', N'CUC', N'CUBA'),
(203, N'Czech Koruna', N'CZK', N'CZECHIA'),
(208, N'Danish Krone', N'DKK', N'DENMARK'),
(262, N'Djibouti Franc', N'DJF', N'DJIBOUTI'),
(214, N'Dominican Peso', N'DOP', N'DOMINICAN REPUBLIC (THE)'),
(818, N'Egyptian Pound', N'EGP', N'EGYPT'),
(222, N'El Salvador Colon', N'SVC', N'EL SALVADOR'),
(232, N'Nakfa', N'ERN', N'ERITREA'),
(230, N'Ethiopian Birr', N'ETB', N'ETHIOPIA'),
(978, N'Euro', N'EUR', N'EUROPEAN UNION'),
(238, N'Falkland Islands Pound', N'FKP', N'FALKLAND ISLANDS (THE) [MALVINAS]'),
(242, N'Fiji Dollar', N'FJD', N'FIJI'),
(270, N'Dalasi', N'GMD', N'GAMBIA (THE)'),
(981, N'Lari', N'GEL', N'GEORGIA'),
(936, N'Ghana Cedi', N'GHS', N'GHANA'),
(292, N'Gibraltar Pound', N'GIP', N'GIBRALTAR'),
(320, N'Quetzal', N'GTQ', N'GUATEMALA'),
(324, N'Guinean Franc', N'GNF', N'GUINEA'),
(328, N'Guyana Dollar', N'GYD', N'GUYANA'),
(332, N'Gourde', N'HTG', N'HAITI'),
(340, N'Lempira', N'HNL', N'HONDURAS'),
(344, N'Hong Kong Dollar', N'HKD', N'HONG KONG'),
(348, N'Forint', N'HUF', N'HUNGARY'),
(352, N'Iceland Krona', N'ISK', N'ICELAND'),
(356, N'Indian Rupee', N'INR', N'INDIA'),
(360, N'Rupiah', N'IDR', N'INDONESIA'),
(364, N'Iranian Rial', N'IRR', N'IRAN (ISLAMIC REPUBLIC OF)'),
(368, N'Iraqi Dinar', N'IQD', N'IRAQ'),
(376, N'New Israeli Sheqel', N'ILS', N'ISRAEL'),
(388, N'Jamaican Dollar', N'JMD', N'JAMAICA'),
(392, N'Yen', N'JPY', N'JAPAN'),
(400, N'Jordanian Dinar', N'JOD', N'JORDAN'),
(398, N'Tenge', N'KZT', N'KAZAKHSTAN'),
(404, N'Kenyan Shilling', N'KES', N'KENYA'),
(408, N'North Korean Won', N'KPW', N'KOREA (THE DEMOCRATIC PEOPLE’S REPUBLIC OF)'),
(410, N'Won', N'KRW', N'KOREA (THE REPUBLIC OF)'),
(414, N'Kuwaiti Dinar', N'KWD', N'KUWAIT'),
(417, N'Som', N'KGS', N'KYRGYZSTAN'),
(418, N'Lao Kip', N'LAK', N'LAO PEOPLE’S DEMOCRATIC REPUBLIC (THE)'),
(422, N'Lebanese Pound', N'LBP', N'LEBANON'),
(426, N'Loti', N'LSL', N'LESOTHO'),
(430, N'Liberian Dollar', N'LRD', N'LIBERIA'),
(434, N'Libyan Dinar', N'LYD', N'LIBYA'),
(440, N'Lithuanian Litas', N'LTL', N'LITHUANIA'), -- تاریخی (اختیاری)
(446, N'Pataca', N'MOP', N'MACAO'),
(454, N'Malawi Kwacha', N'MWK', N'MALAWI'),
(458, N'Malaysian Ringgit', N'MYR', N'MALAYSIA'),
(462, N'Rufiyaa', N'MVR', N'MALDIVES'),
(478, N'Ouguiya', N'MRO', N'MAURITANIA'), -- تاریخی
(929, N'Ouguiya', N'MRU', N'MAURITANIA'),
(480, N'Mauritius Rupee', N'MUR', N'MAURITIUS'),
(484, N'Mexican Peso', N'MXN', N'MEXICO'),
(979, N'Mexican Unidad de Inversion (UDI)', N'MXV', N'MEXICO'),
(498, N'Moldovan Leu', N'MDL', N'MOLDOVA (THE REPUBLIC OF)'),
(496, N'Tugrik', N'MNT', N'MONGOLIA'),
(504, N'Moroccan Dirham', N'MAD', N'MOROCCO'),
(943, N'Mozambique Metical', N'MZN', N'MOZAMBIQUE'),
(104, N'Kyat', N'MMK', N'MYANMAR'),
(516, N'Namibia Dollar', N'NAD', N'NAMIBIA'),
(524, N'Nepalese Rupee', N'NPR', N'NEPAL'),
(532, N'Netherlands Antillean Guilder', N'ANG', N'CURAÇAO'),
(558, N'Cordoba Oro', N'NIO', N'NICARAGUA'),
(566, N'Naira', N'NGN', N'NIGERIA'),
(578, N'Norwegian Krone', N'NOK', N'NORWAY'),
(512, N'Rial Omani', N'OMR', N'OMAN'),
(586, N'Pakistan Rupee', N'PKR', N'PAKISTAN'),
(590, N'Balboa', N'PAB', N'PANAMA'),
(598, N'Kina', N'PGK', N'PAPUA NEW GUINEA'),
(600, N'Guarani', N'PYG', N'PARAGUAY'),
(604, N'Sol', N'PEN', N'PERU'),
(608, N'Philippine Peso', N'PHP', N'PHILIPPINES (THE)'),
(985, N'Zloty', N'PLN', N'POLAND'),
(634, N'Qatari Rial', N'QAR', N'QATAR'),
(946, N'Romanian Leu', N'RON', N'ROMANIA'),
(643, N'Russian Ruble', N'RUB', N'RUSSIAN FEDERATION (THE)'),
(646, N'Rwanda Franc', N'RWF', N'RWANDA'),
(654, N'Saint Helena Pound', N'SHP', N'SAINT HELENA, ASCENSION AND TRISTAN DA CUNHA'),
(682, N'Saudi Riyal', N'SAR', N'SAUDI ARABIA'),
(941, N'Serbian Dinar', N'RSD', N'SERBIA'),
(690, N'Seychelles Rupee', N'SCR', N'SEYCHELLES'),
(694, N'Leone', N'SLL', N'SIERRA LEONE'),
(925, N'Leone', N'SLE', N'SIERRA LEONE'),
(702, N'Singapore Dollar', N'SGD', N'SINGAPORE'),
(994, N'Sucre', N'XSU', N'SISTEMA UNITARIO DE COMPENSACION REGIONAL'),
(90,  N'Solomon Islands Dollar', N'SBD', N'SOLOMON ISLANDS'),
(706, N'Somali Shilling', N'SOS', N'SOMALIA'),
(710, N'Rand', N'ZAR', N'SOUTH AFRICA'),
(728, N'South Sudanese Pound', N'SSP', N'SOUTH SUDAN'),
(144, N'Sri Lanka Rupee', N'LKR', N'SRI LANKA'),
(938, N'Sudanese Pound', N'SDG', N'SUDAN (THE)'),
(968, N'Surinam Dollar', N'SRD', N'SURINAME'),
(748, N'Lilangeni', N'SZL', N'ESWATINI'),
(752, N'Swedish Krona', N'SEK', N'SWEDEN'),
(756, N'Swiss Franc', N'CHF', N'SWITZERLAND'),
(947, N'WIR Euro', N'CHE', N'SWITZERLAND'),
(948, N'WIR Franc', N'CHW', N'SWITZERLAND'),
(760, N'Syrian Pound', N'SYP', N'SYRIAN ARAB REPUBLIC'),
(901, N'New Taiwan Dollar', N'TWD', N'TAIWAN (PROVINCE OF CHINA)'),
(972, N'Somoni', N'TJS', N'TAJIKISTAN'),
(834, N'Tanzanian Shilling', N'TZS', N'TANZANIA, UNITED REPUBLIC OF'),
(764, N'Baht', N'THB', N'THAILAND'),
(776, N'Pa’anga', N'TOP', N'TONGA'),
(780, N'Trinidad and Tobago Dollar', N'TTD', N'TRINIDAD AND TOBAGO'),
(788, N'Tunisian Dinar', N'TND', N'TUNISIA'),
(949, N'Turkish Lira', N'TRY', N'TÜRKİYE'),
(934, N'Turkmenistan New Manat', N'TMT', N'TURKMENISTAN'),
(800, N'Uganda Shilling', N'UGX', N'UGANDA'),
(980, N'Hryvnia', N'UAH', N'UKRAINE'),
(784, N'UAE Dirham', N'AED', N'UNITED ARAB EMIRATES (THE)'),
(826, N'Pound Sterling', N'GBP', N'UNITED KINGDOM OF GREAT BRITAIN AND N. IRELAND'),
(840, N'US Dollar', N'USD', N'UNITED STATES OF AMERICA (THE)'),
(997, N'US Dollar (Next day)', N'USN', N'UNITED STATES OF AMERICA (THE)'),
(858, N'Peso Uruguayo', N'UYU', N'URUGUAY'),
(940, N'Uruguay Peso en Unidades Indexadas (UI)', N'UYI', N'URUGUAY'),
(927, N'Unidad Previsional', N'UYW', N'URUGUAY'),
(860, N'Uzbekistan Sum', N'UZS', N'UZBEKISTAN'),
(548, N'Vatu', N'VUV', N'VANUATU'),
(928, N'Bolívar Soberano', N'VES', N'VENEZUELA (BOLIVARIAN REPUBLIC OF)'),
(926, N'Bolívar Soberano', N'VED', N'VENEZUELA (BOLIVARIAN REPUBLIC OF)'),
(704, N'Dong', N'VND', N'VIET NAM'),
(886, N'Yemeni Rial', N'YER', N'YEMEN'),
(967, N'Zambian Kwacha', N'ZMW', N'ZAMBIA'),
(932, N'Zimbabwe Dollar', N'ZWL', N'ZIMBABWE'),
-- کدهای ویژه و صندوق‌ها
(955, N'Bond Markets Unit European Composite Unit (EURCO)', N'XBA', N'ZZ01_Bond Markets Unit European_EURCO'),
(956, N'Bond Markets Unit European Monetary Unit (EMU-6)', N'XBB', N'ZZ02_Bond Markets Unit European_EMU-6'),
(957, N'Bond Markets Unit European Unit of Account 9', N'XBC', N'ZZ03_Bond Markets Unit European_EUA-9'),
(958, N'Bond Markets Unit European Unit of Account 17', N'XBD', N'ZZ04_Bond Markets Unit European_EUA-17'),
(959, N'Gold', N'XAU', N'ZZ08_Gold'),
(961, N'Silver', N'XAG', N'ZZ11_Silver'),
(962, N'Platinum', N'XPT', N'ZZ10_Platinum'),
(964, N'Palladium', N'XPD', N'ZZ09_Palladium'),
(960, N'SDR (Special Drawing Right)', N'XDR', N'INTERNATIONAL MONETARY FUND (IMF)'),
(963, N'Codes specifically reserved for testing purposes', N'XTS', N'ZZ06_Testing_Code'),
(999, N'Codes for transactions with no currency involved', N'XXX', N'ZZ07_No_Currency'),
(951, N'East Caribbean Dollar', N'XCD', N'ANGUILLA')
) AS seed ([Code], [Title], [ISOCode], [CountryName])
WHERE NOT EXISTS (SELECT 1 FROM dbo.TCOD_ARZ AS target WHERE target.[Code]=seed.[Code]);
GO

IF OBJECT_ID(N'dbo.USER_AUDIT_LOG',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[USER_AUDIT_LOG](
										[ID] [BIGINT] IDENTITY(1,1) NOT NULL,
										[UserName] [NVARCHAR](100) NOT NULL,
										[WindowsUserName] [NVARCHAR](100) NULL,
										[ActionType] [NVARCHAR](50) NOT NULL,
										[TableName] [NVARCHAR](100) NOT NULL,
										[RecordID] [NVARCHAR](100) NULL,
										[OldValue] [NVARCHAR](MAX) NULL,
										[NewValue] [NVARCHAR](MAX) NULL,
										[IPAddress] [NVARCHAR](50) NULL,
										[MachineName] [NVARCHAR](100) NULL,
										[ApplicationVersion] [NVARCHAR](50) NULL,
										[WindowsVersion] [NVARCHAR](100) NULL,
										[ActionDateTime] [DATETIME2](7) NOT NULL,
										[AdditionalInfo] [NVARCHAR](MAX) NULL,
										[SessionID] [UNIQUEIDENTIFIER] NULL,
										[ProcessID] [INT] NULL,
										[ThreadID] [INT] NULL,
										[StackTrace] [NVARCHAR](MAX) NULL,
										[IsSuccess] [BIT] NOT NULL,
										[ErrorMessage] [NVARCHAR](MAX) NULL,
									PRIMARY KEY CLUSTERED 
									(
										[ID] ASC
									)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
									) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.USER_AUDIT_LOG') AND name=N'IsSuccess' AND default_object_id<>0)
ALTER TABLE [dbo].[USER_AUDIT_LOG] ADD  DEFAULT ((1)) FOR [IsSuccess]
GO

IF OBJECT_ID(N'dbo.CustomerComplaints',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[CustomerComplaints](
								    [ComplaintID] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
								    [CustomerFirstName] [nvarchar](100) NOT NULL,
								    [CustomerLastName] [nvarchar](100) NOT NULL,
								    [CustomerMobile] [nvarchar](20) NOT NULL,
								    [CustomerEmail] [nvarchar](100) NULL,
								    [CustomerAddress] [nvarchar](500) NULL,
								    [ProductTypeComplaint] [nvarchar](100) NULL,
								    [PizzaType] [nvarchar](100) NULL,
								    [ProductWeight] [nvarchar](50) NULL,
								    [ProductionDate] [date] NULL,
								    [ExpiryDate] [date] NULL,
								    [ProductCode] [nvarchar](50) NULL,
								    [OtherDairyProductName] [nvarchar](100) NULL,
								    [PurchaseLocation] [nvarchar](200) NULL,
								    [PurchaseDate] [date] NULL,
								    [BatchNumber] [nvarchar](100) NULL,
								    [ComplaintRegisteredDate] [date] NULL,
								    [IsComplaintType_TasteSmell] [bit] NOT NULL DEFAULT 0,
								    [IsComplaintType_Packaging] [bit] NOT NULL DEFAULT 0,
								    [IsComplaintType_WrongExpiryDate] [bit] NOT NULL DEFAULT 0,
								    [IsComplaintType_NonConformity] [bit] NOT NULL DEFAULT 0,
								    [IsComplaintType_ForeignObject] [bit] NOT NULL DEFAULT 0,
								    [IsComplaintType_AbnormalTexture] [bit] NOT NULL DEFAULT 0,
								    [IsComplaintType_Mold] [bit] NOT NULL DEFAULT 0,
								    [IsComplaintType_Other] [bit] NOT NULL DEFAULT 0,
								    [ComplaintType_OtherDescription] [nvarchar](500) NULL,
								    [ComplaintDescription] [nvarchar](max) NOT NULL,
								    [CustomerActionTaken] [bit] NOT NULL DEFAULT 0,
								    [CustomerActionDescription] [nvarchar](max) NULL,
								    [RequestedResolution_Refund] [bit] NOT NULL DEFAULT 0,
								    [RequestedResolution_Replacement] [bit] NOT NULL DEFAULT 0,
								    [RequestedResolution_FurtherInvestigation] [bit] NOT NULL DEFAULT 0,
								    [RequestedResolution_Explanation] [nvarchar](max) NULL,
								    [InformationConfirmed] [bit] NOT NULL DEFAULT 0,
								    [SubmissionTimestamp] [datetime2](7) NOT NULL DEFAULT GETDATE(),
								    [ComplaintStatus] [nvarchar](50) NOT NULL DEFAULT N'جدید' -- e.g., جدید، در حال بررسی، بررسی شده، بسته شده
								   ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];
END;
GO

IF OBJECT_ID(N'dbo.InvoiceRewards',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[InvoiceRewards](
											[InvoiceRewardID] [bigint] IDENTITY(1,1) NOT NULL,
											[InvoiceNumber] [float] NOT NULL,
											[InvoiceTag] [float] NOT NULL,
											[CustomerID] [nvarchar](40) NULL,
											[RewardRuleID] [int] NOT NULL,
											[ProductCode_Earned] [nvarchar](15) NOT NULL,
											[Quantity_Earned] [int] NOT NULL,
											[Reward_Given_Type] [nvarchar](50) NOT NULL,
											[Reward_Given_ProductCode] [nvarchar](15) NULL,
											[Reward_Given_Quantity] [int] NULL,
											[Reward_Given_Discount_Amount] [float] NULL,
											[RewardDate] [bigint] NULL,
											[RecordedBy_UserID] [int] NULL,
											[CRT] [datetime] NULL,
											[UID] [int] NULL,
										 CONSTRAINT [PK__InvoiceR__80A1268F23AE5E36] PRIMARY KEY CLUSTERED 
										(
											[InvoiceRewardID] ASC
										)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
										) ON [PRIMARY]
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.InvoiceRewards') AND name=N'FK_InvoiceRewards_HEAD_LST')
ALTER TABLE [dbo].[InvoiceRewards]  WITH CHECK ADD  CONSTRAINT [FK_InvoiceRewards_HEAD_LST] FOREIGN KEY([InvoiceNumber], [InvoiceTag])
										REFERENCES [dbo].[HEAD_LST] ([NUMBER], [TAG])
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.InvoiceRewards') AND name=N'FK_InvoiceRewards_RewardRule')
ALTER TABLE [dbo].[InvoiceRewards]  WITH CHECK ADD  CONSTRAINT [FK_InvoiceRewards_RewardRule] FOREIGN KEY([RewardRuleID])
										REFERENCES [dbo].[RewardRules] ([RuleID])
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.InvoiceRewards') AND name=N'CRT' AND default_object_id<>0)
ALTER TABLE [dbo].[InvoiceRewards] ADD  CONSTRAINT [DF__InvoiceRewa__CRT__268ACAE1]  DEFAULT (getdate()) FOR [CRT]
GO

IF OBJECT_ID(N'dbo.PRICE_ELAMIETF_EXCEPTION',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[PRICE_ELAMIETF_EXCEPTION](
									[EXCEPTION_ID] [int] IDENTITY(1,1) NOT NULL,
									[PETID] [int] NOT NULL,
									[CODE] [nvarchar](15) NOT NULL,
									[EXCEPTION_TF1] [real] NOT NULL,
									[EXCEPTION_TF2] [real] NOT NULL,
									[TR_DATE] [datetime] NOT NULL,
									[USERNAME] [nvarchar](50) NOT NULL,
									[CRT] [datetime] NULL,
									[UID] [int] NULL,
								 CONSTRAINT [PK_PRICE_ELAMIETF_EXCEPTION] PRIMARY KEY CLUSTERED 
								(
									[EXCEPTION_ID] ASC
								)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY],
								 CONSTRAINT [UK_PRICE_ELAMIETF_EXCEPTION_RuleItem] UNIQUE NONCLUSTERED 
								(
									[PETID] ASC,
									[CODE] ASC
								)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
								) ON [PRIMARY]
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.PRICE_ELAMIETF_EXCEPTION') AND name=N'FK_PRICE_ELAMIETF_EXCEPTION_DTL')
ALTER TABLE [dbo].[PRICE_ELAMIETF_EXCEPTION]  WITH CHECK ADD  CONSTRAINT [FK_PRICE_ELAMIETF_EXCEPTION_DTL] FOREIGN KEY([PETID])
								REFERENCES [dbo].[PRICE_ELAMIETF_DTL] ([PETID])
								ON UPDATE CASCADE
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.PRICE_ELAMIETF_EXCEPTION') AND name=N'FK_PRICE_ELAMIETF_EXCEPTION_STUF')
ALTER TABLE [dbo].[PRICE_ELAMIETF_EXCEPTION]  WITH CHECK ADD  CONSTRAINT [FK_PRICE_ELAMIETF_EXCEPTION_STUF] FOREIGN KEY([CODE])
								REFERENCES [dbo].[STUF_DEF] ([CODE])
								ON UPDATE CASCADE
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PRICE_ELAMIETF_EXCEPTION') AND name=N'EXCEPTION_TF1' AND default_object_id<>0)
ALTER TABLE [dbo].[PRICE_ELAMIETF_EXCEPTION] ADD  CONSTRAINT [DF_PRICE_ELAMIETF_EXCEPTION_TF1]  DEFAULT ((0)) FOR [EXCEPTION_TF1]
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PRICE_ELAMIETF_EXCEPTION') AND name=N'EXCEPTION_TF2' AND default_object_id<>0)
ALTER TABLE [dbo].[PRICE_ELAMIETF_EXCEPTION] ADD  CONSTRAINT [DF_PRICE_ELAMIETF_EXCEPTION_TF2]  DEFAULT ((0)) FOR [EXCEPTION_TF2]
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PRICE_ELAMIETF_EXCEPTION') AND name=N'TR_DATE' AND default_object_id<>0)
ALTER TABLE [dbo].[PRICE_ELAMIETF_EXCEPTION] ADD  CONSTRAINT [DF_PRICE_ELAMIETF_EXCEPTION_TR_DATE]  DEFAULT (getdate()) FOR [TR_DATE]
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PRICE_ELAMIETF_EXCEPTION') AND name=N'CRT' AND default_object_id<>0)
ALTER TABLE [dbo].[PRICE_ELAMIETF_EXCEPTION] ADD  CONSTRAINT [DF_PRICE_ELAMIETF_EXCEPTION_CRT]  DEFAULT (getdate()) FOR [CRT]
GO

IF OBJECT_ID(N'dbo.RewardRules',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[RewardRules](
									  	[RuleID] [int] IDENTITY(1,1) NOT NULL,
									  	[ProductID_Target] [nvarchar](15) NOT NULL,
									  	[Quantity_Threshold] [int] NOT NULL,
									  	[Reward_Type] [nvarchar](50) NOT NULL,
									  	[Reward_ProductID] [nvarchar](15) NOT NULL,
									  	[Reward_Quantity] [int] NULL,
									  	[Reward_Discount_Percentage] [decimal](5, 2) NULL,
									  	[IsActive] [bit] NOT NULL,
									  	[StartDate] [bigint] NULL,
									  	[EndDate] [bigint] NULL,
									  	[Description] [nvarchar](200) NULL,
									  	[CRT] [datetime] NULL,
									  	[UID] [int] NULL,
									   CONSTRAINT [PK__RewardRu__110458C21C0D3C6E] PRIMARY KEY CLUSTERED 
									  (
									  	[RuleID] ASC
									  )WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON) ON [PRIMARY]
									  ) ON [PRIMARY]
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.RewardRules') AND name=N'FK_RewardRules_ProductID_Target')
ALTER TABLE [dbo].[RewardRules]  WITH CHECK ADD  CONSTRAINT [FK_RewardRules_ProductID_Target] FOREIGN KEY([ProductID_Target])
									  REFERENCES [dbo].[STUF_DEF] ([CODE])
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.RewardRules') AND name=N'FK_RewardRules_Reward_ProductID')
ALTER TABLE [dbo].[RewardRules]  WITH CHECK ADD  CONSTRAINT [FK_RewardRules_Reward_ProductID] FOREIGN KEY([Reward_ProductID])
									  REFERENCES [dbo].[STUF_DEF] ([CODE])
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.RewardRules') AND name=N'Reward_Type' AND default_object_id<>0)
ALTER TABLE [dbo].[RewardRules] ADD  CONSTRAINT [DF_RewardRules_Reward_Type]  DEFAULT (N'محصول') FOR [Reward_Type]
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.RewardRules') AND name=N'IsActive' AND default_object_id<>0)
ALTER TABLE [dbo].[RewardRules] ADD  CONSTRAINT [DF__RewardRul__IsAct__1DF584E0]  DEFAULT ((1)) FOR [IsActive]
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.RewardRules') AND name=N'CRT' AND default_object_id<>0)
ALTER TABLE [dbo].[RewardRules] ADD  CONSTRAINT [DF__RewardRules__CRT__1EE9A919]  DEFAULT (getdate()) FOR [CRT]
GO

IF OBJECT_ID(N'dbo.USER_PERSONEL_ORDER',N'U') IS NULL
BEGIN
CREATE TABLE USER_PERSONEL_ORDER (
									USER_ID      INT        NOT NULL,
									PERSONEL_ID  INT        NOT NULL,
									SORT_ORDER   INT        NOT NULL,
									PRIMARY KEY (USER_ID, PERSONEL_ID))
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TASKS') AND name=N'IX_TASKS_Status1')
CREATE NONCLUSTERED INDEX IX_TASKS_Status1
									ON dbo.TASKS (STATUS, IDNUM)          -- برای فیلتر و ORDER BY
									INCLUDE (GR, PERSONEL, TASK, PERIORITY, STDATE, STTIME,
									         ENDATE, ENTIME, USERNAME, COMP_COD, SUMTIME,
									          ss, skid, num, tg, CTIM, USERCO, SEE)
GO

IF OBJECT_ID(N'dbo.GENERAL_OPTIONS',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[GENERAL_OPTIONS] (
								       [OptionName]  NVARCHAR(100) PRIMARY KEY NOT NULL,
								       [OptionValue] NVARCHAR(500) NULL,
								       [Description] NVARCHAR(1000) NULL,
								       [LastUpdated] DATETIME DEFAULT GETDATE()
				
								   );
END;
GO

IF COL_LENGTH(N'dbo.MESAGEP',N'SNOOZE_COUNT') IS NULL
ALTER TABLE dbo.MESAGEP ADD SNOOZE_COUNT INT DEFAULT 0;
IF COL_LENGTH(N'dbo.MESAGEP',N'LAST_NOTIFY_TIME') IS NULL
ALTER TABLE dbo.MESAGEP ADD LAST_NOTIFY_TIME DATETIME NULL;
GO

INSERT INTO TCOD_Countries ([Code], [CountriesName], [CodeIcon], [THREE_LETTER_CODE])
SELECT seed.[Code], seed.[CountriesName], seed.[CodeIcon], seed.[THREE_LETTER_CODE] FROM (VALUES
( 100001, N'آرژانتین', 64, N'ARG' ), 
						                ( 100002, N'آروبا', 75, N'ABW' ), 
						                ( 100003, N'آفریقای جنوبی', 66, N'ZAF' ), 
						                ( 100004, N'آفریقای مرکزی', 65, N'CAF' ), 
						                ( 100005, N'آلبانی', 67, N'ALB' ), 
						                ( 100006, N'آلمان', 68, N'D' ), 
						                ( 100007, N'آنتیل هلند', 205, N'ANT' ), 
						                ( 100008, N'آندورا', 205, N'AND' ), 
						                ( 100009, N'آنگوئیلا', 205, N'AIA' ), 
						                ( 100010, N'آنگولا', 70, N'AGO' ), 
						                ( 100011, N'اتریش', 72, N'AUT' ), 
						                ( 100012, N'اتیوپی', 73, N'ETH' ), 
						                ( 100013, N'اردن', 205, N'JOR' ), 
						                ( 100014, N'ارمنستان', 74, N'ARM' ), 
						                ( 100015, N'اروگوئه', 205, NULL ), 
						                ( 100016, N'اریتره', 205, N'ERI' ), 
						                ( 100017, N'ازبکستان', 76, N'UZB' ), 
						                ( 100018, N'اسانسیون', 205, NULL ), 
						                ( 100019, N'اسپانیا', 77, N'ESP' ), 
						                ( 100020, N'استرالیا', 78, N'AUS' ), 
						                ( 100021, N'استونی', 79, N'EST' ), 
						                ( 100022, N'اسلواکی', 205, N'SVK' ), 
						                ( 100023, N'افغانستان', 81, N'AFG' ), 
						                ( 100028, N'اوکراین', 88, N'UKR' ), 
						                ( 100029, N'اکوادور', 83, N'ECU' ), 
						                ( 100030, N'الجزایر', 205, N'DZA' ), 
						                ( 100031, N'السالوادور', 84, N'SLV' ), 
						                ( 100032, N'امارات متحده عربی', 85, N'ARE' ), 
						                ( 100033, N'اندونزی', 205, N'IDN' ), 
						                ( 100034, N'انگلستان', 87, N'GBR' ), 
						                ( 100035, N'اوگاندا', 208, N'UGA' ), 
						                ( 100036, N'آمریکا', 69, N'USA' ), 
						                ( 100037, N'ایتالیا', 90, N'ITA' ), 
						                ( 100038, N'ایران', 91, N'IRN' ), 
						                ( 100039, N'ایرلند', 92, N'IRL' ), 
						                ( 100040, N'ایسلند', 93, N'ISL' ), 
						                ( 100041, N'باهاما', 95, NULL ), 
						                ( 100042, N'بحرین', 96, N'BHR' ), 
						                ( 100043, N'برزیل', 97, N'BRA' ), 
						                ( 100044, N'برمودا', 205, N'BMU' ), 
						                ( 100045, N'برمه', 98, N'MMR' ), 
						                ( 100046, N'برونئی', 99, N'BRN' ), 
						                ( 100047, N'بروندی', 205, N'BDI' ), 
						                ( 100048, N'بلیز', 100, N'BLZ' ), 
						                ( 100049, N'بلژیک', 101, N'BEL' ), 
						                ( 100050, N'بلغارستان', 102, N'BGR' ), 
						                ( 100051, N'بنگلادش', 103, N'BGD' ), 
						                ( 100052, N'بوتان', 205, N'BTN' ), 
						                ( 100053, N'بوتسوانا', 105, N'BWA' ), 
						                ( 100054, N'بورکینافاسو', 205, N'BFA' ), 
						                ( 100055, N'بوسنی وهرزگوین', 106, N'BIH' ), 
						                ( 100056, N'بولیوی', 107, N'BOL' ), 
						                ( 100057, N'بلاروس', 205, N'BLR' ), 
						                ( 100058, N'پاراگوئه', 108, N'PRY' ), 
						                ( 100059, N'پاکستان', 109, N'PAK' ), 
						                ( 100060, N'پاناما', 110, N'PAN' ), 
						                ( 100061, N'پرتغال', 111, N'PRT' ), 
						                ( 100062, N'پرتوریکو', 169, N'PRI' ), 
						                ( 100063, N'پرو', 112, N'PER' ), 
						                ( 100064, N'پلی‌نزیا', 205, N'PYF' ), 
						                ( 100065, N'تاجیکستان', 113, N'TJK' ), 
						                ( 100066, N'تانزانیا', 114, N'TZA' ), 
						                ( 100067, N'تایلند', 115, N'THA' ), 
						                ( 100068, N'تایوان', 116, N'TWN' ), 
						                ( 100069, N'ترکمنستان', 117, N'TKM' ), 
						                ( 100070, N'ترکیه', 118, N'TUR' ), 
						                ( 100071, N'ترینیداد و توباگو', 205, N'TTO' ), 
						                ( 100072, N'توگو', 119, N'TGO' ), 
						                ( 100073, N'تونس', 120, N'TUN' ), 
						                ( 100074, N'تونگا', 121, NULL ), 
						                ( 100075, N'جامائیکا', 122, N'JAM' ), 
						                ( 100077, N'جزایر سلیمان', 205, N'CYM' ), 
						                ( 100083, N'جزایر ویرجین انگلیس', 205, N'IOT' ), 
						                ( 100084, N'آذربایجان', 63, N'AZE' ), 
						                ( 100085, N'جیبوتی', 205, N'DJI' ), 
						                ( 100086, N'چاد', 125, N'TCD' ), 
						                ( 100087, N'جمهوری چک', 126, N'CZE' ), 
						                ( 100088, N'چین', 127, N'CHN' ), 
						                ( 100089, N'دانمارک', 128, N'DNK' ), 
						                ( 100090, N'دومینیکا', 205, N'DMA' ), 
						                ( 100091, N'دومینیکن', 124, N'DMA' ), 
						                ( 100092, N'رئونیون', 129, N'REU' ), 
						                ( 100093, N'رواندا', 130, N'RWA' ), 
						                ( 100094, N'روسیه', 131, N'RUS' ), 
						                ( 100095, N'رومانی', 132, N'ROU' ), 
						                ( 100096, N'زئیر', 133, NULL ), 
						                ( 100097, N'زامبیا', 134, N'ZMB' ), 
						                ( 100098, N'زلاندنو', 205, N'NZL' ), 
						                ( 100099, N'زیمباوه', 135, N'ZMB' ), 
						                ( 100100, N'ژاپن', 136, N'JPN' ), 
						                ( 100101, N'ساحل عاج', 205, NULL ), 
						                ( 100102, N'ساموای غربی', 205, N'WSM' ), 
						                ( 100103, N'ساموای آمریکا', 69, N'ASM' ), 
						                ( 100104, N'سریلانکا', 209, N'LKA' ), 
						                ( 100105, N'سن‌مارینو', 138, NULL ), 
						                ( 100106, N'سنت پیئرو', 205, N'SPM' ), 
						                ( 100107, N'سنت تام پرنسیب', 205, N'KNA' ), 
						                ( 100108, N'سنت کیتس', 205, N'KNA' ), 
						                ( 100109, N'سنت لوسیا', 205, N'LCA' ), 
						                ( 100110, N'سنگاپور', 139, N'SGP' ), 
						                ( 100111, N'سنگال', 140, N'SEN' ), 
						                ( 100112, N'سوئد', 141, N'SWE' ), 
						                ( 100113, N'سوئیس', 143, N'CHE' ), 
						                ( 100114, N'سوازیلند', 142, N'SWZ' ), 
						                ( 100115, N'سودان', 144, N'SDN' ), 
						                ( 100116, N'سورینام', 145, N'SUR' ), 
						                ( 100117, N'سوریه', 146, N'SYR' ), 
						                ( 100118, N'سومالی', 147, N'SOM' ), 
						                ( 100119, N'سیرالئون', 148, N'SLE' ), 
						                ( 100120, N'سیشل', 149, N'SYC' ), 
						                ( 100121, N'شیلی', 205, N'CHL' ), 
						                ( 100122, N'صربستان', 150, NULL ), 
						                ( 100123, N'عراق', 151, N'IRQ' ), 
						                ( 100124, N'عربستان سعودی', 152, N'SAU' ), 
						                ( 100125, N'عمان', 153, N'OMN' ), 
						                ( 100126, N'غنا', 155, N'GHA' ), 
						                ( 100127, N'فرانسه', 154, N'FRA' ), 
						                ( 100128, N'فنلاند', 157, N'FIN' ), 
						                ( 100129, N'فیجی', 158, N'FJI' ), 
						                ( 100130, N'فیلیپین', 156, N'PHL' ), 
						                ( 100131, N'قبرس', 205, N'CYP' ), 
						                ( 100132, N'قرقیزستان', 159, N'KGZ' ), 
						                ( 100133, N'قزاقستان', 160, N'KAZ' ), 
						                ( 100134, N'قطر', 205, N'QAT' ), 
						                ( 100135, N'کاستاریکا', 161, N'CRI' ), 
						                ( 100136, N'کالدونیای جدید', 205, N'NCL' ), 
						                ( 100137, N'کامبوج', 205, N'KHM' ), 
						                ( 100138, N'کامرون', 162, N'CMR' ), 
						                ( 100139, N'کانادا', 163, N'CAN' ), 
						                ( 100140, N'کرواسی', 210, N'HRV' ), 
						                ( 100141, N'کره جنوبی', 164, N'KOR' ), 
						                ( 100142, N'کره شمالی', 165, N'PRK' ), 
						                ( 100143, N'کلمبیا', 166, N'COL' ), 
						                ( 100144, N'کنگو', 167, N'COG' ), 
						                ( 100145, N'کنیا', 168, N'KEN' ), 
						                ( 100146, N'کوبا', 169, N'CUB' ), 
						                ( 100147, N'کومور', 205, N'COM' ), 
						                ( 100148, N'کویت', 170, N'KWT' ), 
						                ( 100149, N'کیپ ورد', 171, N'CPV' ), 
						                ( 100150, N'گابون', 172, N'GAB' ), 
						                ( 100151, N'گامبیا', 173, N'GMB' ), 
						                ( 100152, N'گرانادا', 205, N'GRD' ), 
						                ( 100153, N'گرجستان', 205, N'GEO' ), 
						                ( 100154, N'گرینلند', 205, N'GRL' ), 
						                ( 100155, N'گواتمالا', 174, N'GTM' ), 
						                ( 100156, N'گویان فرانسه', 205, N'GUF' ), 
						                ( 100157, N'گویان جرج تاون', 205, N'GUY' ), 
						                ( 100158, N'گینه استوائی', 176, N'GNQ' ), 
						                ( 100159, N'گینه بیسائو', 176, N'GNB' ), 
						                ( 100160, N'گینه جمهوری', 176, NULL ), 
						                ( 100161, N'گینه نو', 176, N'GIN' ), 
						                ( 100162, N'لائوس', 205, N'LAO' ), 
						                ( 100163, N'لبنان', 177, N'LBN' ), 
						                ( 100164, N'لتونی', 205, N'LVA' ), 
						                ( 100165, N'لسوتو', 178, N'LSO' ), 
						                ( 100166, N'لوگزامبورگ', 205, N'LUX' ), 
						                ( 100167, N'لهستان', 199, N'POL' ), 
						                ( 100168, N'لیبریا', 179, N'LBR' ), 
						                ( 100169, N'لیبی', 180, N'LBY' ), 
						                ( 100170, N'لیتوانی', 205, N'LTU' ), 
						                ( 100171, N'لیختن اشتاین', 205, N'LIE' ), 
						                ( 100172, N'ماداگاسکار', 181, N'MDG' ), 
						                ( 100173, N'ماکائو', 182, N'MAC' ), 
						                ( 100174, N'مالاوی', 183, N'MWI' ), 
						                ( 100175, N'مالت', 184, N'MLT' ), 
						                ( 100176, N'مالدیو', 185, N'MDV' ), 
						                ( 100177, N'مالزی', 186, N'MYS' ), 
						                ( 100178, N'مالی', 187, N'MLI' ), 
						                ( 100179, N'مجارستان', 205, N'HUN' ), 
						                ( 100180, N'مراکش', 205, N'MAR' ), 
						                ( 100181, N'مصر', 188, N'EGY' ), 
						                ( 100182, N'مغولستان', 205, NULL ), 
						                ( 100183, N'مقدونیه', 205, N'MKD' ), 
						                ( 100184, N'مکزیک', 189, N'MEX' ), 
						                ( 100185, N'موریتانی', 205, N'MRT' ), 
						                ( 100186, N'موریس', 205, N'MUS' ), 
						                ( 100187, N'موزامبیک', 190, N'MOZ' ), 
						                ( 100188, N'موناکو', 205, N'MCO' ), 
						                ( 100189, N'میانمار', 205, N'MMR' ), 
						                ( 100190, N'نامبیا', 192, N'NAM' ), 
						                ( 100191, N'نپال', 193, N'NPL' ), 
						                ( 100192, N'نروژ', 194, N'NOR' ), 
						                ( 100193, N'نیجر', 195, N'NER' ), 
						                ( 100194, N'نیجریه', 196, N'NGA' ), 
						                ( 100195, N'نیکاراگوئه', 197, NULL ), 
						                ( 100196, N'واتیکان', 205, N'VAT' ), 
						                ( 100197, N'ونزوئلا', 202, N'VEN' ), 
						                ( 100198, N'ویتنام', 203, N'VNM' ), 
						                ( 100199, N'هائیتی', 198, N'HTI' ), 
						                ( 100200, N'هلند', 206, N'NLD' ), 
						                ( 100201, N'هندوراس', 200, N'HND' ), 
						                ( 100202, N'هندوستان', 201, N'IND' ), 
						                ( 100203, N'هنگ کنگ', 205, N'HKG' ), 
						                ( 100204, N'یمن (صنعا)', 204, N'YEM' ), 
						                ( 100205, N'یمن (عدن)', 204, N'YEM' ), 
						                ( 100206, N'یونان', 205, N'GRC' ), 
						                ( 100207, N'فلسطین', 205, N'PSE' ), 
						                ( 100208, N'رژیم اشغالگر قدس', 205, N'ISR' ), 
						                ( 100209, N'مولداوی', 191, N'MDA' ), 
						                ( 100210, N'اسکاتلند', 205, NULL ), 
						                ( 100211, N'اسلونی', 80, N'SVN' ), 
						                ( 100212, N'کوزوو', 205, N'UNK' ), 
						                ( 100213, N'بنین', 104, NULL ), 
						                ( 100214, N'یوگسلاوی', 205, N'YUG' ), 
						                ( 100215, N'سازمان ملل متحد', 205, N'UNO' ), 
						                ( 100216, N'سنت وینسنت', 205, N'VCT' ), 
						                ( 100217, N'تیمور شرقی', 205, NULL )
) AS seed ([Code], [CountriesName], [CodeIcon], [THREE_LETTER_CODE])
WHERE NOT EXISTS (SELECT 1 FROM TCOD_Countries AS target WHERE target.[Code]=seed.[Code]);
GO

IF OBJECT_ID(N'dbo.Travelreason',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[Travelreason]
(
[Code] [int] NULL,
[TravelreasonName] [nvarchar] (25) COLLATE Arabic_CI_AS NULL,
[CRT] [datetime] NULL CONSTRAINT [DF__Travelreaso__CRT__5E7FE7D2] DEFAULT (getdate()),
[UID] [int] NULL
) ON [PRIMARY]
END;
GO

IF OBJECT_ID(N'dbo.UserState',N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[UserState](
								       [UserId]   INT            NOT NULL PRIMARY KEY,
								       [StateJson] NVARCHAR(MAX) NOT NULL
								   );
END;
GO
