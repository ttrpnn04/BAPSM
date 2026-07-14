/*
  ซ่อม StockBalances ให้มี ledger ใน StockTransactions ตรงกัน
  รันครั้งเดียว — ข้ามถ้ามี RefNo ซ่อมแล้ว
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.StockTransactions WHERE RefNo IN (N'LEDGER-REPAIR-KNOWN-OPENING', N'LEDGER-REPAIR-NEG-ZERO', N'LEDGER-REPAIR-GAP'))
BEGIN
    PRINT N'ข้าม: พบรายการซ่อม ledger แล้ว';
    RETURN;
END

DECLARE @ReceiveTypeID INT = (
    SELECT TOP 1 TransactionTypeID
    FROM dbo.TransactionTypes
    WHERE IsActive = 1 AND Direction > 0 AND TypeName = N'ปรับยอดเพิ่ม'
);
IF @ReceiveTypeID IS NULL
    SET @ReceiveTypeID = (
        SELECT TOP 1 TransactionTypeID
        FROM dbo.TransactionTypes
        WHERE IsActive = 1 AND Direction > 0
        ORDER BY TransactionTypeID
    );

DECLARE @IssueTypeID INT = (
    SELECT TOP 1 TransactionTypeID
    FROM dbo.TransactionTypes
    WHERE IsActive = 1 AND Direction < 0 AND TypeName = N'ปรับยอดลด'
);
IF @IssueTypeID IS NULL
    SET @IssueTypeID = (
        SELECT TOP 1 TransactionTypeID
        FROM dbo.TransactionTypes
        WHERE IsActive = 1 AND Direction < 0
        ORDER BY TransactionTypeID
    );

DECLARE @CreatedBy NVARCHAR(100) = (
    SELECT TOP 1 Id FROM dbo.AspNetUsers ORDER BY CASE WHEN Email LIKE N'superadmin%' THEN 0 ELSE 1 END, Email
);

IF @ReceiveTypeID IS NULL OR @IssueTypeID IS NULL
    THROW 50001, N'ไม่พบประเภทธุรกรรมรับ/จ่าย', 1;

BEGIN TRAN;

/* 1) กู้ยอดเปิด BLB-A025 12" จากหน้าจอก่อนบิล ร้าน A */
DECLARE @KnownDocID BIGINT;
INSERT INTO dbo.StockDocuments (TransactionTypeID, TxnDate, RefNo, Note, CreatedBy)
VALUES (@ReceiveTypeID, '2026-07-13', N'LEDGER-REPAIR-KNOWN-OPENING',
        N'กู้ยอดเปิดก่อนบิล (จากยอดที่โชว์ก่อนตัดสต็อก)', @CreatedBy);
SET @KnownDocID = SCOPE_IDENTITY();

INSERT INTO dbo.StockTransactions (DocumentID, VariantID, TransactionTypeID, TxnDate, QtyPieces, QtyCases, RefNo, Note, CreatedBy)
SELECT @KnownDocID, v.VariantID, @ReceiveTypeID, '2026-07-13', x.OpeningPieces, 0,
       N'LEDGER-REPAIR-KNOWN-OPENING',
       N'ยอดเปิดก่อนบิล ' + p.SKU + N' ' + v.VariantName,
       @CreatedBy
FROM (VALUES
    (N'BLB-A025', N'12"', N'แดง', 66),
    (N'BLB-A025', N'12"', N'ฟ้า', 57),
    (N'BLB-A025', N'12"', N'เขียว', 55)
) AS x(Sku, NameHas, VariantName, OpeningPieces)
INNER JOIN dbo.Products p ON p.SKU = x.Sku AND p.ProductName LIKE N'%' + x.NameHas + N'%' AND p.IsActive = 1
INNER JOIN dbo.ProductVariants v ON v.ProductID = p.ProductID AND v.VariantName = x.VariantName AND v.IsActive = 1;

PRINT N'Known openings: ' + CAST(@@ROWCOUNT AS nvarchar(20));

/* 2) ล้างยอดติดลบที่เหลือ (เช่น BLB-A058 หลังบิล Test — ไม่ทราบยอดเปิดเดิม) ให้เป็น 0 */
DECLARE @NegDocID BIGINT;
INSERT INTO dbo.StockDocuments (TransactionTypeID, TxnDate, RefNo, Note, CreatedBy)
VALUES (@ReceiveTypeID, CONVERT(date, SYSDATETIME()), N'LEDGER-REPAIR-NEG-ZERO',
        N'ล้างยอดติดลบหลังประวัติธุรกรรมหาย (ยอดเปิดเดิมไม่ทราบ — ตั้งกลับเป็น 0)', @CreatedBy);
SET @NegDocID = SCOPE_IDENTITY();

INSERT INTO dbo.StockTransactions (DocumentID, VariantID, TransactionTypeID, TxnDate, QtyPieces, QtyCases, RefNo, Note, CreatedBy)
SELECT @NegDocID, sb.VariantID, @ReceiveTypeID, CONVERT(date, SYSDATETIME()),
       CASE WHEN sb.QtyPieces < 0 THEN -sb.QtyPieces ELSE 0 END,
       CASE WHEN sb.QtyCases < 0 THEN -sb.QtyCases ELSE 0 END,
       N'LEDGER-REPAIR-NEG-ZERO',
       N'ล้างยอดติดลบ (ไม่ทราบยอดเปิดเดิม)',
       @CreatedBy
FROM dbo.StockBalances sb
WHERE sb.QtyPieces < 0 OR sb.QtyCases < 0;

DECLARE @NegRows INT = @@ROWCOUNT;
IF @NegRows = 0
BEGIN
    DELETE FROM dbo.StockDocuments WHERE DocumentID = @NegDocID;
END
PRINT N'Neg zeros: ' + CAST(@NegRows AS nvarchar(20));

/* 3) ชดเชย gap ทุกสีที่มียอดแต่ไม่มี ledger (หรือไม่ตรง) */
DECLARE @GapReceiveDocID BIGINT;
DECLARE @GapIssueDocID BIGINT;

INSERT INTO dbo.StockDocuments (TransactionTypeID, TxnDate, RefNo, Note, CreatedBy)
VALUES (@ReceiveTypeID, CONVERT(date, SYSDATETIME()), N'LEDGER-REPAIR-GAP',
        N'ซ่อมยอดเปิดให้ตรง StockBalances (มียอดแต่ไม่มีประวัติธุรกรรม)', @CreatedBy);
SET @GapReceiveDocID = SCOPE_IDENTITY();

INSERT INTO dbo.StockDocuments (TransactionTypeID, TxnDate, RefNo, Note, CreatedBy)
VALUES (@IssueTypeID, CONVERT(date, SYSDATETIME()), N'LEDGER-REPAIR-GAP',
        N'ซ่อมยอดให้ตรง StockBalances (ledger สูงกว่ายอด)', @CreatedBy);
SET @GapIssueDocID = SCOPE_IDENTITY();

;WITH Gaps AS (
    SELECT
        sb.VariantID,
        sb.QtyPieces - ISNULL(x.LedgerPieces, 0) AS GapPieces,
        sb.QtyCases - ISNULL(x.LedgerCases, 0) AS GapCases
    FROM dbo.StockBalances sb
    OUTER APPLY (
        SELECT
            SUM(st.QtyPieces * tt.Direction) AS LedgerPieces,
            SUM(st.QtyCases * tt.Direction) AS LedgerCases
        FROM dbo.StockTransactions st
        INNER JOIN dbo.TransactionTypes tt ON tt.TransactionTypeID = st.TransactionTypeID
        WHERE st.VariantID = sb.VariantID
    ) x
    WHERE sb.QtyPieces <> ISNULL(x.LedgerPieces, 0)
       OR sb.QtyCases <> ISNULL(x.LedgerCases, 0)
)
INSERT INTO dbo.StockTransactions (DocumentID, VariantID, TransactionTypeID, TxnDate, QtyPieces, QtyCases, RefNo, Note, CreatedBy)
SELECT
    CASE WHEN g.GapPieces > 0 OR g.GapCases > 0 THEN @GapReceiveDocID ELSE @GapIssueDocID END,
    g.VariantID,
    CASE WHEN g.GapPieces > 0 OR g.GapCases > 0 THEN @ReceiveTypeID ELSE @IssueTypeID END,
    CONVERT(date, SYSDATETIME()),
    CASE
        WHEN g.GapPieces > 0 OR g.GapCases > 0 THEN CASE WHEN g.GapPieces > 0 THEN g.GapPieces ELSE 0 END
        ELSE CASE WHEN g.GapPieces < 0 THEN -g.GapPieces ELSE 0 END
    END,
    CASE
        WHEN g.GapPieces > 0 OR g.GapCases > 0 THEN CASE WHEN g.GapCases > 0 THEN g.GapCases ELSE 0 END
        ELSE CASE WHEN g.GapCases < 0 THEN -g.GapCases ELSE 0 END
    END,
    N'LEDGER-REPAIR-GAP',
    N'รับเข้า/จ่ายออกชดเชยยอดเปิดที่ไม่มี ledger',
    @CreatedBy
FROM Gaps g
WHERE (g.GapPieces > 0 OR g.GapCases > 0 OR g.GapPieces < 0 OR g.GapCases < 0)
  AND NOT (
      -- ข้ามกรณีชิ้นกับลังคนละทิศ (แยกไม่ได้ในแถวเดียว) — จัดการด้านล่างถ้ามี
      (g.GapPieces > 0 AND g.GapCases < 0) OR (g.GapPieces < 0 AND g.GapCases > 0)
  );

PRINT N'Gap adjustments: ' + CAST(@@ROWCOUNT AS nvarchar(20));

-- กรณีชิ้น/ลังคนละทิศ: แยก receive + issue
;WITH Mixed AS (
    SELECT
        sb.VariantID,
        sb.QtyPieces - ISNULL(x.LedgerPieces, 0) AS GapPieces,
        sb.QtyCases - ISNULL(x.LedgerCases, 0) AS GapCases
    FROM dbo.StockBalances sb
    OUTER APPLY (
        SELECT
            SUM(st.QtyPieces * tt.Direction) AS LedgerPieces,
            SUM(st.QtyCases * tt.Direction) AS LedgerCases
        FROM dbo.StockTransactions st
        INNER JOIN dbo.TransactionTypes tt ON tt.TransactionTypeID = st.TransactionTypeID
        WHERE st.VariantID = sb.VariantID
    ) x
    WHERE (sb.QtyPieces - ISNULL(x.LedgerPieces, 0) > 0 AND sb.QtyCases - ISNULL(x.LedgerCases, 0) < 0)
       OR (sb.QtyPieces - ISNULL(x.LedgerPieces, 0) < 0 AND sb.QtyCases - ISNULL(x.LedgerCases, 0) > 0)
)
INSERT INTO dbo.StockTransactions (DocumentID, VariantID, TransactionTypeID, TxnDate, QtyPieces, QtyCases, RefNo, Note, CreatedBy)
SELECT @GapReceiveDocID, m.VariantID, @ReceiveTypeID, CONVERT(date, SYSDATETIME()),
       CASE WHEN m.GapPieces > 0 THEN m.GapPieces ELSE 0 END,
       CASE WHEN m.GapCases > 0 THEN m.GapCases ELSE 0 END,
       N'LEDGER-REPAIR-GAP', N'ชดเชยทิศบวก', @CreatedBy
FROM Mixed m
WHERE m.GapPieces > 0 OR m.GapCases > 0
UNION ALL
SELECT @GapIssueDocID, m.VariantID, @IssueTypeID, CONVERT(date, SYSDATETIME()),
       CASE WHEN m.GapPieces < 0 THEN -m.GapPieces ELSE 0 END,
       CASE WHEN m.GapCases < 0 THEN -m.GapCases ELSE 0 END,
       N'LEDGER-REPAIR-GAP', N'ชดเชยทิศลบ', @CreatedBy
FROM Mixed m
WHERE m.GapPieces < 0 OR m.GapCases < 0;

IF NOT EXISTS (SELECT 1 FROM dbo.StockTransactions WHERE DocumentID = @GapReceiveDocID)
    DELETE FROM dbo.StockDocuments WHERE DocumentID = @GapReceiveDocID;
IF NOT EXISTS (SELECT 1 FROM dbo.StockTransactions WHERE DocumentID = @GapIssueDocID)
    DELETE FROM dbo.StockDocuments WHERE DocumentID = @GapIssueDocID;

COMMIT;

-- สรุปผล
SELECT
    SUM(CASE WHEN sb.QtyPieces <> ISNULL(x.LP, 0) OR sb.QtyCases <> ISNULL(x.LC, 0) THEN 1 ELSE 0 END) AS RemainingGaps,
    SUM(CASE WHEN sb.QtyPieces < 0 OR sb.QtyCases < 0 THEN 1 ELSE 0 END) AS RemainingNegatives
FROM dbo.StockBalances sb
OUTER APPLY (
    SELECT SUM(st.QtyPieces * tt.Direction) LP, SUM(st.QtyCases * tt.Direction) LC
    FROM dbo.StockTransactions st
    JOIN dbo.TransactionTypes tt ON tt.TransactionTypeID = st.TransactionTypeID
    WHERE st.VariantID = sb.VariantID
) x;

SELECT p.SKU, v.VariantName, sb.QtyPieces AS Bal,
       ISNULL(x.LP, 0) AS Ledger
FROM dbo.StockBalances sb
JOIN dbo.ProductVariants v ON v.VariantID = sb.VariantID
JOIN dbo.Products p ON p.ProductID = v.ProductID
OUTER APPLY (
    SELECT SUM(st.QtyPieces * tt.Direction) LP
    FROM dbo.StockTransactions st
    JOIN dbo.TransactionTypes tt ON tt.TransactionTypeID = st.TransactionTypeID
    WHERE st.VariantID = sb.VariantID
) x
WHERE p.SKU IN (N'BLB-A025', N'BLB-A058')
  AND v.VariantName IN (N'แดง', N'ฟ้า', N'เขียว', N'ดำ/ธงฟ้า', N'ส้ม')
  AND p.ProductName LIKE N'12"%'
ORDER BY p.SKU, v.VariantName;
