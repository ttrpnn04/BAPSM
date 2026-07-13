-- Sync product catalog to Excel master (สต็อกจักรยาน 0769.xlsx)
-- 1) Move history from wrong "เด็กหญิง" -> correct "จ้าหญิง"
-- 2) Delete products not in Excel / mis-imported headers
-- 3) Fix SKU notes like "BLB-A036 ตะกร้าขาด 9 ใบ"

SET XACT_ABORT ON;
BEGIN TRAN;

UPDATE t
SET t.VariantID = tgt.VariantID
FROM dbo.StockTransactions t
INNER JOIN dbo.ProductVariants src ON src.VariantID = t.VariantID AND src.ProductID = 2
INNER JOIN dbo.ProductVariants tgt ON tgt.ProductID = 7 AND tgt.VariantName = src.VariantName;

IF EXISTS (
    SELECT 1
    FROM dbo.StockTransactions t
    INNER JOIN dbo.ProductVariants v ON v.VariantID = t.VariantID
    WHERE v.ProductID IN (1, 2, 86, 99, 130, 117, 213, 201)
)
BEGIN
    ROLLBACK TRAN;
    THROW 50001, N'Still have transactions on products marked for delete', 1;
END;

DELETE sb
FROM dbo.StockBalances sb
INNER JOIN dbo.ProductVariants v ON v.VariantID = sb.VariantID
WHERE v.ProductID IN (1, 2, 86, 99, 130, 117, 213, 201);

DELETE FROM dbo.ProductVariants
WHERE ProductID IN (1, 2, 86, 99, 130, 117, 213, 201);

DELETE FROM dbo.Products
WHERE ProductID IN (1, 2, 86, 99, 130, 117, 213, 201);

UPDATE dbo.Products
SET SKU = N'BLB-A036', UpdatedAt = SYSDATETIME()
WHERE ProductID = 9 AND SKU LIKE N'BLB-A036%';

DELETE d
FROM dbo.StockDocuments d
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.StockTransactions t WHERE t.DocumentID = d.DocumentID
);

COMMIT TRAN;

SELECT ProductID, SKU, ProductName
FROM dbo.Products
WHERE SKU LIKE N'%025%'
   OR ProductName LIKE N'%เด็กหญิง%'
   OR ProductName LIKE N'%จ้าหญิง%'
   OR SKU LIKE N'%036%'
ORDER BY SKU, ProductName;
