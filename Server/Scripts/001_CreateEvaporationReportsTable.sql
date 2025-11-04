IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='EvaporationReports' and xtype='U')
BEGIN
    CREATE TABLE EvaporationReports (
        Id INT PRIMARY KEY IDENTITY(1,1),
        ReportDate DATETIME,
        Shift NVARCHAR(50),
        OperatorName NVARCHAR(100),
        StartTime TIME,
        EndTime TIME,
        DurationInMinutes INT,
        OutletDryMatterPercentage DECIMAL(5, 2),
        FillTime20LitreContainerInSeconds INT,
        BoilerSteamPressure DECIMAL(5, 2),
        TvrSteamPressure DECIMAL(5, 2),
        VacuumPressure DECIMAL(5, 2),
        Tower1Temperature DECIMAL(5, 2),
        Tower2Temperature DECIMAL(5, 2),
        Tower3Temperature DECIMAL(5, 2),
        CondenserInletTemperature DECIMAL(5, 2),
        CondenserOutletTemperature DECIMAL(5, 2),
        IsTower1PumpOn BIT,
        IsTower2PumpOn BIT,
        DistilledWaterTemperature DECIMAL(5, 2),
        CipStatus NVARCHAR(50),
        HoursSinceLastCip INT,
        CRT DATETIME NULL CONSTRAINT DF_EvaporationReports_CRT DEFAULT (GETDATE()) 
    );
END
GO