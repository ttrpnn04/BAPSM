using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Data;

public static class ProductVariantSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<BAPStockContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ProductVariantSeeder");

        await NormalizeVariantAliasesAsync(context, logger);
        await NormalizeProductUnitsAsync(context, logger);

        var products = await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
                .ThenInclude(v => v.StockBalance)
            .ToListAsync();

        foreach (var product in products)
        {
            await EnsureDefaultColorsAsync(product, logger);
        }

        await context.SaveChangesAsync();
    }

    public static async Task NormalizeProductUnitsAsync(
        BAPStockContext context,
        ILogger? logger = null)
    {
        var products = await context.Products.ToListAsync();
        var changed = 0;

        foreach (var product in products)
        {
            var normalized = ProductUnits.Normalize(product.Unit);
            if (string.Equals(product.Unit, normalized, StringComparison.Ordinal))
            {
                continue;
            }

            product.Unit = normalized;
            changed++;
        }

        if (changed > 0)
        {
            await context.SaveChangesAsync();
            logger?.LogInformation("Normalized product units for {Count} products", changed);
        }
    }

    public static async Task NormalizeVariantAliasesAsync(
        BAPStockContext context,
        ILogger? logger = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            foreach (var (alias, canonicalName) in ProductVariantDefaults.VariantAliases)
            {
                var sourceVariants = await context.ProductVariants
                    .Include(v => v.StockBalance)
                    .Include(v => v.StockTransactions)
                    .Where(v => v.VariantName == alias)
                    .ToListAsync();
                var variantsToRemove = new List<ProductVariant>();

                foreach (var source in sourceVariants)
                {
                    var target = await context.ProductVariants
                        .FirstOrDefaultAsync(v =>
                            v.ProductId == source.ProductId &&
                            v.VariantName == canonicalName);

                    if (target == null)
                    {
                        source.VariantName = canonicalName;
                        continue;
                    }

                    foreach (var stockTransaction in source.StockTransactions.ToList())
                    {
                        stockTransaction.Variant = target;
                        stockTransaction.VariantId = target.VariantId;
                    }

                    variantsToRemove.Add(source);
                }

                // Trigger จะคำนวณ StockBalance ของสีต้นทางและปลายทางใหม่เมื่อย้าย transaction
                await context.SaveChangesAsync();

                foreach (var source in variantsToRemove)
                {
                    if (source.StockBalance != null)
                    {
                        context.StockBalances.Remove(source.StockBalance);
                    }

                    context.ProductVariants.Remove(source);
                }

                await context.SaveChangesAsync();

                if (sourceVariants.Count > 0)
                {
                    logger?.LogInformation(
                        "รวมสีซ้ำ {Alias} เป็น {CanonicalName} จำนวน {Count} รายการ",
                        alias,
                        canonicalName,
                        sourceVariants.Count);
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public static Task EnsureDefaultColorsAsync(Product product, ILogger? logger = null)
    {
        if (UsesStandardVariant(product.Category?.CategoryName))
        {
            foreach (var colorVariant in product.ProductVariants
                .Where(v => v.IsActive &&
                            v.VariantName != ProductVariantDefaults.StandardVariantName))
            {
                colorVariant.IsActive = false;
            }

            var standardVariant = product.ProductVariants
                .FirstOrDefault(v => v.VariantName == ProductVariantDefaults.StandardVariantName);
            if (standardVariant == null)
            {
                standardVariant = new ProductVariant
                {
                    ProductId = product.ProductId,
                    VariantName = ProductVariantDefaults.StandardVariantName,
                    SortOrder = product.ProductVariants.Any()
                        ? product.ProductVariants.Max(v => v.SortOrder) + 1
                        : 1,
                    IsActive = true,
                    StockBalance = new StockBalance
                    {
                        QtyPieces = 0,
                        QtyCases = 0
                    }
                };
                product.ProductVariants.Add(standardVariant);
            }
            else
            {
                standardVariant.IsActive = true;
                standardVariant.StockBalance ??= new StockBalance
                {
                    QtyPieces = 0,
                    QtyCases = 0
                };
            }

            return Task.CompletedTask;
        }

        foreach (var standardVariant in product.ProductVariants
            .Where(v => v.VariantName == ProductVariantDefaults.StandardVariantName && v.IsActive))
        {
            standardVariant.IsActive = false;
            logger?.LogInformation(
                "ปิดใช้งานสี/รุ่น {VariantName} ของสินค้า {ProductName}",
                standardVariant.VariantName,
                product.ProductName);
        }

        // ปิดสีที่ไม่อยู่ในรายการ Excel 14 สี เพื่อไม่ให้โผล่ซ้ำ
        foreach (var unknownVariant in product.ProductVariants
            .Where(v => v.IsActive &&
                        v.VariantName != ProductVariantDefaults.StandardVariantName &&
                        !ProductVariantDefaults.IsAllowedColorName(v.VariantName)))
        {
            unknownVariant.IsActive = false;
            logger?.LogInformation(
                "ปิดใช้งานสีนอก Excel {VariantName} ของสินค้า {ProductName}",
                unknownVariant.VariantName,
                product.ProductName);
        }

        var maxSortOrder = product.ProductVariants.Any()
            ? product.ProductVariants.Max(v => v.SortOrder)
            : 0;

        foreach (var colorName in ProductVariantDefaults.ColorNames)
        {
            var existingVariant = product.ProductVariants
                .FirstOrDefault(v => v.VariantName == colorName);

            if (existingVariant != null)
            {
                existingVariant.IsActive = true;
                existingVariant.StockBalance ??= new StockBalance
                {
                    QtyPieces = 0,
                    QtyCases = 0
                };
                continue;
            }

            maxSortOrder++;
            product.ProductVariants.Add(new ProductVariant
            {
                ProductId = product.ProductId,
                VariantName = colorName,
                SortOrder = maxSortOrder,
                IsActive = true,
                StockBalance = new StockBalance
                {
                    QtyPieces = 0,
                    QtyCases = 0
                }
            });
        }

        return Task.CompletedTask;
    }

    private static bool UsesStandardVariant(string? categoryName)
    {
        var normalized = categoryName?.Trim() ?? string.Empty;
        return normalized.Equals("ยางในจักรยาน-COLUN", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ยางในมอเตอร์ไซค์ BLUE", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("อะไหล่", StringComparison.OrdinalIgnoreCase);
    }
}
