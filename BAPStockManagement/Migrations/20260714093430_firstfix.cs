using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BAPStockManagement.Migrations
{
    /// <inheritdoc />
    public partial class firstfix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: company DBs may already have these objects from StockDocumentSeeder
            // or from running an earlier app build before this migration existed.
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.Products', N'SortOrder') IS NULL
                BEGIN
                    ALTER TABLE dbo.Products
                    ADD SortOrder INT NOT NULL
                        CONSTRAINT DF_Products_SortOrder DEFAULT (0);
                END
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.StockDocuments', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.StockDocuments
                    (
                        DocumentID        BIGINT IDENTITY(1,1) NOT NULL
                            CONSTRAINT PK_StockDocuments PRIMARY KEY,
                        TransactionTypeID INT NOT NULL,
                        TxnDate           DATE NOT NULL
                            CONSTRAINT DF_StockDocuments_TxnDate
                            DEFAULT (CONVERT(date, SYSDATETIME())),
                        RefNo             NVARCHAR(50) NULL,
                        Note              NVARCHAR(300) NULL,
                        CreatedAt         DATETIME2 NOT NULL
                            CONSTRAINT DF_StockDocuments_CreatedAt DEFAULT (SYSDATETIME()),
                        CreatedBy         NVARCHAR(100) NULL,
                        CONSTRAINT FK_StockDocuments_Types FOREIGN KEY (TransactionTypeID)
                            REFERENCES dbo.TransactionTypes (TransactionTypeID)
                    );

                    CREATE INDEX IX_StockDocuments_Date
                        ON dbo.StockDocuments (TxnDate);
                    CREATE INDEX IX_StockDocuments_RefNo
                        ON dbo.StockDocuments (RefNo);
                    CREATE INDEX IX_StockDocuments_TransactionTypeID
                        ON dbo.StockDocuments (TransactionTypeID);
                END
                ELSE
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE name = N'IX_StockDocuments_TransactionTypeID'
                          AND object_id = OBJECT_ID(N'dbo.StockDocuments')
                    )
                    BEGIN
                        CREATE INDEX IX_StockDocuments_TransactionTypeID
                            ON dbo.StockDocuments (TransactionTypeID);
                    END
                END
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.StockTransactions', N'DocumentID') IS NULL
                BEGIN
                    ALTER TABLE dbo.StockTransactions ADD DocumentID BIGINT NULL;
                END
                """);

            // Legacy rows: one document per transaction line that still lacks DocumentID
            migrationBuilder.Sql("""
                DECLARE @TransactionID BIGINT;
                DECLARE @DocumentID BIGINT;
                DECLARE @TransactionTypeID INT;
                DECLARE @TxnDate DATE;
                DECLARE @RefNo NVARCHAR(50);
                DECLARE @Note NVARCHAR(300);
                DECLARE @CreatedAt DATETIME2;
                DECLARE @CreatedBy NVARCHAR(100);

                DECLARE orphan_cursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT TransactionID, TransactionTypeID, TxnDate, RefNo, Note, CreatedAt, CreatedBy
                    FROM dbo.StockTransactions
                    WHERE DocumentID IS NULL;

                OPEN orphan_cursor;
                FETCH NEXT FROM orphan_cursor
                    INTO @TransactionID, @TransactionTypeID, @TxnDate, @RefNo, @Note, @CreatedAt, @CreatedBy;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    INSERT INTO dbo.StockDocuments (TransactionTypeID, TxnDate, RefNo, Note, CreatedAt, CreatedBy)
                    VALUES (@TransactionTypeID, @TxnDate, @RefNo, @Note, @CreatedAt, @CreatedBy);

                    SET @DocumentID = SCOPE_IDENTITY();

                    UPDATE dbo.StockTransactions
                    SET DocumentID = @DocumentID
                    WHERE TransactionID = @TransactionID;

                    FETCH NEXT FROM orphan_cursor
                        INTO @TransactionID, @TransactionTypeID, @TxnDate, @RefNo, @Note, @CreatedAt, @CreatedBy;
                END

                CLOSE orphan_cursor;
                DEALLOCATE orphan_cursor;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.StockTransactions', N'DocumentID') IS NOT NULL
                   AND EXISTS (
                        SELECT 1
                        FROM sys.columns
                        WHERE object_id = OBJECT_ID(N'dbo.StockTransactions')
                          AND name = N'DocumentID'
                          AND is_nullable = 1
                   )
                   AND NOT EXISTS (SELECT 1 FROM dbo.StockTransactions WHERE DocumentID IS NULL)
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE name = N'IX_StockTransactions_DocumentID'
                          AND object_id = OBJECT_ID(N'dbo.StockTransactions')
                    )
                    BEGIN
                        DROP INDEX IX_StockTransactions_DocumentID ON dbo.StockTransactions;
                    END

                    IF OBJECT_ID(N'dbo.FK_StockTransactions_Documents', N'F') IS NOT NULL
                    BEGIN
                        ALTER TABLE dbo.StockTransactions
                            DROP CONSTRAINT FK_StockTransactions_Documents;
                    END

                    ALTER TABLE dbo.StockTransactions ALTER COLUMN DocumentID BIGINT NOT NULL;
                END

                IF COL_LENGTH(N'dbo.StockTransactions', N'DocumentID') IS NOT NULL
                   AND OBJECT_ID(N'dbo.FK_StockTransactions_Documents', N'F') IS NULL
                BEGIN
                    ALTER TABLE dbo.StockTransactions WITH CHECK
                    ADD CONSTRAINT FK_StockTransactions_Documents
                        FOREIGN KEY (DocumentID) REFERENCES dbo.StockDocuments (DocumentID);
                END

                IF COL_LENGTH(N'dbo.StockTransactions', N'DocumentID') IS NOT NULL
                   AND NOT EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE name = N'IX_StockTransactions_DocumentID'
                          AND object_id = OBJECT_ID(N'dbo.StockTransactions')
                   )
                BEGIN
                    CREATE INDEX IX_StockTransactions_DocumentID
                        ON dbo.StockTransactions (DocumentID);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.FK_StockTransactions_Documents', N'F') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.StockTransactions
                        DROP CONSTRAINT FK_StockTransactions_Documents;
                END

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_StockTransactions_DocumentID'
                      AND object_id = OBJECT_ID(N'dbo.StockTransactions')
                )
                BEGIN
                    DROP INDEX IX_StockTransactions_DocumentID ON dbo.StockTransactions;
                END

                IF COL_LENGTH(N'dbo.StockTransactions', N'DocumentID') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.StockTransactions DROP COLUMN DocumentID;
                END

                IF OBJECT_ID(N'dbo.StockDocuments', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE dbo.StockDocuments;
                END

                IF COL_LENGTH(N'dbo.Products', N'SortOrder') IS NOT NULL
                BEGIN
                    DECLARE @df NVARCHAR(256);
                    SELECT @df = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Products')
                      AND c.name = N'SortOrder';

                    IF @df IS NOT NULL
                        EXEC(N'ALTER TABLE dbo.Products DROP CONSTRAINT [' + @df + N']');

                    ALTER TABLE dbo.Products DROP COLUMN SortOrder;
                END
                """);
        }
    }
}
