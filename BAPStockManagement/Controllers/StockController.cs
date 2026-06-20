using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Stock;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class StockController : Controller
{
    private readonly BAPStockContext _context;

    public StockController(BAPStockContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? category, string? search, int page = 1, int pageSize = 20)
    {
        var allowedPageSizes = new[] { 10, 20, 50 };
        if (!allowedPageSizes.Contains(pageSize))
        {
            pageSize = 20;
        }

        if (page < 1)
        {
            page = 1;
        }

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .Select(c => c.CategoryName)
            .ToListAsync();

        var productQuery = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
                .ThenInclude(v => v.StockBalance)
            .Where(p => p.IsActive && p.Category.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            productQuery = productQuery.Where(p => p.Category.CategoryName == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            productQuery = productQuery.Where(p =>
                p.Sku.Contains(term) ||
                p.ProductName.Contains(term) ||
                p.ProductVariants.Any(v => v.VariantName.Contains(term)));
        }

        var products = await productQuery
            .OrderBy(p => p.Category.SortOrder)
            .ThenBy(p => p.Category.CategoryName)
            .ThenBy(p => p.ProductName)
            .ToListAsync();

        var items = products
            .Select(p =>
            {
                var activeVariants = p.ProductVariants
                    .Where(v => v.IsActive)
                    .Select(v => new StockColorRowViewModel
                    {
                        VariantId = v.VariantId,
                        VariantName = v.VariantName,
                        QtyPieces = v.StockBalance?.QtyPieces ?? 0,
                        QtyCases = v.StockBalance?.QtyCases ?? 0
                    })
                    .OrderByDescending(v => v.QtyPieces)
                    .ThenBy(v => v.VariantName)
                    .ToList();

                return new StockProductRowViewModel
                {
                    ProductId = p.ProductId,
                    CategoryName = p.Category.CategoryName,
                    Sku = p.Sku,
                    ProductName = p.ProductName,
                    Unit = p.Unit,
                    QtyPieces = activeVariants.Sum(v => v.QtyPieces),
                    QtyCases = activeVariants.Sum(v => v.QtyCases),
                    LastUpdated = p.ProductVariants
                        .Where(v => v.IsActive && v.StockBalance != null)
                        .Select(v => (DateTime?)v.StockBalance!.LastUpdated)
                        .Max(),
                    PrimaryVariantId = activeVariants.FirstOrDefault()?.VariantId,
                    Colors = activeVariants
                };
            })
            .OrderBy(i => i.QtyPieces == 0 && i.QtyCases == 0 ? 1 : 0)
            .ThenBy(i => i.CategoryName)
            .ThenBy(i => i.ProductName)
            .ToList();

        var totalItems = items.Count;
        var totalVariants = items.Sum(i => i.Colors.Count);
        var totalPieces = items.Sum(i => i.QtyPieces);
        var totalCases = items.Sum(i => i.QtyCases);

        var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling((double)totalItems / pageSize);
        if (page > totalPages)
        {
            page = totalPages;
        }

        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return View(new StockIndexViewModel
        {
            CategoryFilter = category,
            Search = search,
            Categories = categories,
            Items = pagedItems,
            TotalVariants = totalVariants,
            TotalPieces = totalPieces,
            TotalCases = totalCases,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        });
    }

    public async Task<IActionResult> MonthlyReport(int? year, int? month, string? category)
    {
        var today = DateTime.Today;
        var reportYear = year ?? today.Year;
        var reportMonth = month ?? today.Month;

        if (reportMonth < 1)
        {
            reportMonth = 12;
            reportYear--;
        }
        else if (reportMonth > 12)
        {
            reportMonth = 1;
            reportYear++;
        }

        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .Select(c => c.CategoryName)
            .ToListAsync();

        var query = _context.VwMonthlySummaries
            .Where(s => s.TxnYear == reportYear && s.TxnMonth == reportMonth);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(s => s.CategoryName == category);
        }

        var items = await query
            .OrderBy(s => s.CategoryName)
            .ThenBy(s => s.ProductName)
            .ThenBy(s => s.VariantName)
            .ToListAsync();

        return View(new MonthlyReportViewModel
        {
            Year = reportYear,
            Month = reportMonth,
            CategoryFilter = category,
            Categories = categories,
            Items = items
        });
    }
}
