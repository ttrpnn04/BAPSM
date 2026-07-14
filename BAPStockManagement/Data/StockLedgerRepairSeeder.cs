using BAPStockManagement.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Data;

/// <summary>
/// ซ่อม StockBalances ที่ไม่ตรงกับผลรวม StockTransactions
/// (เช่น ลบประวัติธุรกรรมทิ้งแต่คงยอดไว้ พอขายออก trigger จะคำนวณใหม่จนติดลบ)
/// </summary>
public static class StockLedgerRepairSeeder
{
    public const string GapRepairRef = "LEDGER-REPAIR-GAP";
    public const string KnownOpeningRef = "LEDGER-REPAIR-KNOWN-OPENING";
    public const string NegativeZeroRef = "LEDGER-REPAIR-NEG-ZERO";

    /// <summary>
    /// ยอดเปิดก่อนบิล "ร้าน A" จากหน้าจอก่อนตัดสต็อก (BLB-A025 12")
    /// </summary>
    private static readonly (string Sku, string ProductNameContains, string VariantName, int OpeningPieces)[] KnownWipedOpenings =
    [
        ("BLB-A025", "12\"", "แดง", 66),
        ("BLB-A025", "12\"", "ฟ้า", 57),
        ("BLB-A025", "12\"", "เขียว", 55),
    ];

    public static async Task EnsureAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<BAPStockContext>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StockLedgerRepairSeeder");

        var receiveType = await context.TransactionTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsActive && t.Direction > 0 && t.TypeName == "ปรับยอดเพิ่ม")
            ?? await context.TransactionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IsActive && t.Direction > 0);

        var issueType = await context.TransactionTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsActive && t.Direction < 0 && t.TypeName == "ปรับยอดลด")
            ?? await context.TransactionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IsActive && t.Direction < 0);

        if (receiveType == null || issueType == null)
        {
            logger.LogWarning("ข้ามซ่อม ledger: ไม่พบประเภทธุรกรรมรับ/จ่าย");
            return;
        }

        var repairUserId =
            (await userManager.FindByEmailAsync("superadmin@bap.local"))?.Id
            ?? await context.Users.Select(u => u.Id).FirstOrDefaultAsync();

        await using var tx = await context.Database.BeginTransactionAsync();
        try
        {
            var knownCount = await RestoreKnownWipedOpeningsAsync(context, receiveType.TransactionTypeId, repairUserId, logger);
            var negCount = await ZeroUnknownNegativesAsync(context, receiveType.TransactionTypeId, repairUserId, logger);
            var gapCount = await ReconcileBalanceGapsAsync(
                context,
                receiveType.TransactionTypeId,
                issueType.TransactionTypeId,
                repairUserId,
                logger);

            await tx.CommitAsync();
            logger.LogInformation(
                "ซ่อม ledger เสร็จ: knownOpening={Known}, negZero={Neg}, gapAdjust={Gap}",
                knownCount, negCount, gapCount);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static async Task<int> RestoreKnownWipedOpeningsAsync(
        BAPStockContext context,
        int receiveTypeId,
        string? createdBy,
        ILogger logger)
    {
        if (await context.StockTransactions.AnyAsync(t => t.RefNo == KnownOpeningRef))
        {
            return 0;
        }

        var openingDate = new DateOnly(2026, 7, 13);
        var document = new StockDocument
        {
            TransactionTypeId = receiveTypeId,
            TxnDate = openingDate,
            RefNo = KnownOpeningRef,
            Note = "กู้ยอดเปิดก่อนบิล (จากยอดที่โชว์ก่อนตัดสต็อก)",
            CreatedBy = createdBy
        };
        context.StockDocuments.Add(document);

        var count = 0;
        foreach (var item in KnownWipedOpenings)
        {
            var variant = await context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.StockBalance)
                .FirstOrDefaultAsync(v =>
                    v.IsActive &&
                    v.Product.IsActive &&
                    v.Product.Sku == item.Sku &&
                    v.Product.ProductName.Contains(item.ProductNameContains) &&
                    v.VariantName == item.VariantName);

            if (variant == null)
            {
                logger.LogWarning(
                    "ไม่พบ variant สำหรับกู้ยอด: {Sku} {Name} {Variant}",
                    item.Sku, item.ProductNameContains, item.VariantName);
                continue;
            }

            // กู้เฉพาะตอนที่ยอดยังติดลบจากบั๊ก wipe — กันพองยอดถ้าถูกรันซ้ำหลังซ่อมแล้ว
            if ((variant.StockBalance?.QtyPieces ?? 0) >= 0)
            {
                continue;
            }

            context.StockTransactions.Add(new StockTransaction
            {
                Document = document,
                VariantId = variant.VariantId,
                TransactionTypeId = receiveTypeId,
                TxnDate = openingDate,
                QtyPieces = item.OpeningPieces,
                QtyCases = 0,
                RefNo = KnownOpeningRef,
                Note = $"ยอดเปิดก่อนบิล {item.Sku} {item.VariantName} = {item.OpeningPieces}",
                CreatedBy = createdBy
            });
            count++;
        }

        if (count == 0)
        {
            context.StockDocuments.Remove(document);
            return 0;
        }

        await context.SaveChangesAsync();
        return count;
    }

    private static async Task<int> ZeroUnknownNegativesAsync(
        BAPStockContext context,
        int receiveTypeId,
        string? createdBy,
        ILogger logger)
    {
        if (await context.StockTransactions.AnyAsync(t => t.RefNo == NegativeZeroRef))
        {
            return 0;
        }

        var negatives = await context.StockBalances
            .AsNoTracking()
            .Where(b => b.QtyPieces < 0 || b.QtyCases < 0)
            .Select(b => new { b.VariantId, b.QtyPieces, b.QtyCases })
            .ToListAsync();

        if (negatives.Count == 0)
        {
            return 0;
        }

        var txnDate = DateOnly.FromDateTime(DateTime.Today);
        var document = new StockDocument
        {
            TransactionTypeId = receiveTypeId,
            TxnDate = txnDate,
            RefNo = NegativeZeroRef,
            Note = "ล้างยอดติดลบหลังประวัติธุรกรรมหาย (ยอดเปิดเดิมไม่ทราบ — ตั้งกลับเป็น 0)",
            CreatedBy = createdBy
        };
        context.StockDocuments.Add(document);

        var count = 0;
        foreach (var row in negatives)
        {
            var pieces = Math.Max(0, -row.QtyPieces);
            var cases = Math.Max(0, -row.QtyCases);
            if (pieces == 0 && cases == 0)
            {
                continue;
            }

            context.StockTransactions.Add(new StockTransaction
            {
                Document = document,
                VariantId = row.VariantId,
                TransactionTypeId = receiveTypeId,
                TxnDate = txnDate,
                QtyPieces = pieces,
                QtyCases = cases,
                RefNo = NegativeZeroRef,
                Note = "ล้างยอดติดลบ (ไม่ทราบยอดเปิดเดิม)",
                CreatedBy = createdBy
            });
            count++;
        }

        if (count == 0)
        {
            context.StockDocuments.Remove(document);
            return 0;
        }

        await context.SaveChangesAsync();
        logger.LogWarning(
            "ล้างยอดติดลบ {Count} สี เป็น 0 (ยอดเปิดเดิมไม่ทราบ — ควรปรับยอดเพิ่มถ้าทราบจำนวนจริง)",
            count);
        return count;
    }

    private static async Task<int> ReconcileBalanceGapsAsync(
        BAPStockContext context,
        int receiveTypeId,
        int issueTypeId,
        string? createdBy,
        ILogger logger)
    {
        var gaps = await context.Database
            .SqlQueryRaw<BalanceGapRow>("""
                SELECT
                    sb.VariantID AS VariantId,
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
                """)
            .ToListAsync();

        if (gaps.Count == 0)
        {
            return 0;
        }

        var txnDate = DateOnly.FromDateTime(DateTime.Today);
        var receiveDoc = new StockDocument
        {
            TransactionTypeId = receiveTypeId,
            TxnDate = txnDate,
            RefNo = GapRepairRef,
            Note = "ซ่อมยอดเปิดให้ตรง StockBalances (มียอดแต่ไม่มีประวัติธุรกรรม)",
            CreatedBy = createdBy
        };
        var issueDoc = new StockDocument
        {
            TransactionTypeId = issueTypeId,
            TxnDate = txnDate,
            RefNo = GapRepairRef,
            Note = "ซ่อมยอดให้ตรง StockBalances (ledger สูงกว่ายอด)",
            CreatedBy = createdBy
        };

        var receiveAdded = false;
        var issueAdded = false;
        var count = 0;

        foreach (var gap in gaps)
        {
            var receivePieces = Math.Max(0, gap.GapPieces);
            var receiveCases = Math.Max(0, gap.GapCases);
            if (receivePieces > 0 || receiveCases > 0)
            {
                if (!receiveAdded)
                {
                    context.StockDocuments.Add(receiveDoc);
                    receiveAdded = true;
                }

                context.StockTransactions.Add(new StockTransaction
                {
                    Document = receiveDoc,
                    VariantId = gap.VariantId,
                    TransactionTypeId = receiveTypeId,
                    TxnDate = txnDate,
                    QtyPieces = receivePieces,
                    QtyCases = receiveCases,
                    RefNo = GapRepairRef,
                    Note = "รับเข้าชดเชยยอดเปิดที่ไม่มี ledger",
                    CreatedBy = createdBy
                });
                count++;
            }

            var issuePieces = Math.Max(0, -gap.GapPieces);
            var issueCases = Math.Max(0, -gap.GapCases);
            if (issuePieces > 0 || issueCases > 0)
            {
                if (!issueAdded)
                {
                    context.StockDocuments.Add(issueDoc);
                    issueAdded = true;
                }

                context.StockTransactions.Add(new StockTransaction
                {
                    Document = issueDoc,
                    VariantId = gap.VariantId,
                    TransactionTypeId = issueTypeId,
                    TxnDate = txnDate,
                    QtyPieces = issuePieces,
                    QtyCases = issueCases,
                    RefNo = GapRepairRef,
                    Note = "จ่ายออกปรับให้ตรงยอดคงเหลือ",
                    CreatedBy = createdBy
                });
                count++;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("สร้างรายการปรับยอด gap {Count} รายการ", count);
        return count;
    }

    private sealed class BalanceGapRow
    {
        public int VariantId { get; set; }
        public int GapPieces { get; set; }
        public int GapCases { get; set; }
    }
}
