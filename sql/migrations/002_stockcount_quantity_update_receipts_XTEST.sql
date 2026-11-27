-- DBA-reviewed migration only. Run explicitly in sagex3 against XTEST first.
-- No production execution, no ERP table alteration, no data backfill.
-- Existing X3 tick and UPDATE triggers must be independently reviewed.
USE [sagex3];
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'XTEST.StockCountOperationReceipt',N'U') IS NULL
BEGIN
    CREATE TABLE [XTEST].[StockCountOperationReceipt] (
        ReceiptSequence bigint IDENTITY(1,1) NOT NULL,
        CountNumber nvarchar(30) NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        Operation nvarchar(6) NOT NULL,
        PayloadHash char(64) NULL,
        DetailId nvarchar(40) NOT NULL,
        BoxNumber nvarchar(20) NOT NULL,
        LocationCode nvarchar(10) NOT NULL,
        StockUnit nvarchar(3) NOT NULL,
        OldQtyStu numeric(28,13) NULL,
        OldQtyPcu numeric(28,13) NULL,
        SavedQtyStu numeric(28,13) NOT NULL,
        SavedQtyPcu numeric(28,13) NOT NULL,
        AuditUser nvarchar(5) NOT NULL,
        CommittedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_StockCountOperationReceipt PRIMARY KEY (CountNumber,RequestId),
        CONSTRAINT CK_StockCountOperationReceipt_Operation CHECK (
            (Operation=N'Insert' AND PayloadHash IS NULL AND OldQtyStu IS NULL AND OldQtyPcu IS NULL)
            OR (Operation=N'Update' AND PayloadHash IS NOT NULL AND OldQtyStu IS NOT NULL AND OldQtyPcu IS NOT NULL)),
        CONSTRAINT CK_StockCountOperationReceipt_Quantity CHECK (SavedQtyStu=SavedQtyPcu AND SavedQtyStu>=0)
    );
    CREATE INDEX IX_StockCountOperationReceipt_Detail
        ON [XTEST].[StockCountOperationReceipt](CountNumber,DetailId,Operation,ReceiptSequence DESC)
        INCLUDE(OldQtyStu,SavedQtyStu,CommittedAt);
END;
COMMIT TRANSACTION;
-- Grant only SELECT/INSERT on receipt and reviewed column-level UPDATE on XCOUTD
-- to the actual service principal separately; never grant receipt UPDATE/DELETE.
-- COB requires a separately reviewed deployment; do not blindly replace schema names.
