/*
StockCount 2026-11-23 — persistent idempotency journal for Sage X3 count inserts.
Run ONCE in a dedicated TEST clone with an authorized DBA. This is DDL, not
a regular business-query script. No schema or table is created automatically
by StockCount.Api. Journal and XCOUTD MUST use the same SQL Server database
and transaction; NEVER use SQLite for idempotency.

IMPORTANT:
  1. Back up the test database and verify XTEST.XCOUTD metadata/trigger semantics.
  2. Grant only SELECT, INSERT on dbo.StockCountSubmission to the API principal.
  3. Grant only required SELECT + INSERT on approved XTEST tables.
  4. Grant no production COB INSERT until independently reviewed.
*/
USE [sagex3];
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.StockCountSubmission', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[StockCountSubmission] (
        [Environment] nvarchar(10) NOT NULL,
        [CountNumber] nvarchar(30) NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [PayloadHash] binary(32) NOT NULL,
        [BoxNumber] nvarchar(20) NOT NULL,
        [DetailRowId] numeric(38,0) NOT NULL,
        [StockUnit] nvarchar(3) NOT NULL,
        [QuantityStu] numeric(28,13) NOT NULL,
        [QuantityPcu] numeric(28,13) NOT NULL,
        [CreatedAtUtc] datetime2(3) NOT NULL
            CONSTRAINT [DF_StockCountSubmission_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_StockCountSubmission]
            PRIMARY KEY CLUSTERED ([Environment],[CountNumber],[RequestId]),
        CONSTRAINT [CK_StockCountSubmission_Environment]
            CHECK ([Environment] IN (N'Test',N'Production')),
        CONSTRAINT [CK_StockCountSubmission_QuantitiesEqual]
            CHECK ([QuantityStu]=[QuantityPcu])
    );
    CREATE INDEX [IX_StockCountSubmission_CountBox]
      ON [dbo].[StockCountSubmission]([Environment],[CountNumber],[BoxNumber]);
END;
GO
-- Non-destructive verification of the installation.
SELECT name FROM sys.columns
WHERE object_id=OBJECT_ID(N'[dbo].[StockCountSubmission]')
ORDER BY column_id;
GO
