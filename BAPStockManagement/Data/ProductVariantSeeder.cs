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

        var products = await context.Products
            .Include(p => p.ProductVariants)
                .ThenInclude(v => v.StockBalance)
            .ToListAsync();

        foreach (var product in products)
        {
            await EnsureDefaultColorsAsync(product, logger);
        }

        await context.SaveChangesAsync();
    }

    public static Task EnsureDefaultColorsAsync(Product product, ILogger? logger = null)
    {
        foreach (var standardVariant in product.ProductVariants
            .Where(v => v.VariantName == ProductVariantDefaults.StandardVariantName && v.IsActive))
        {
            standardVariant.IsActive = false;
            logger?.LogInformation(
                "ปิดใช้งานสี/รุ่น {VariantName} ของสินค้า {ProductName}",
                standardVariant.VariantName,
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
}
