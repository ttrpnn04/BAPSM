using System.Diagnostics;
using BAPStockManagement.ViewModels.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BAPStockManagement.Models;

namespace BAPStockManagement.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly BAPStockContext _context;

    public HomeController(BAPStockContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
                .ThenInclude(v => v.StockBalance)
            .Where(p => p.IsActive && p.Category.IsActive)
            .ToListAsync();

        var productStockRows = products
            .Select(p =>
            {
                var activeVariants = p.ProductVariants.Where(v => v.IsActive).ToList();

                return new
                {
                    CategoryName = p.Category.CategoryName,
                    VariantCount = activeVariants.Count,
                    QtyPieces = activeVariants.Sum(v => v.StockBalance?.QtyPieces ?? 0),
                    QtyCases = activeVariants.Sum(v => v.StockBalance?.QtyCases ?? 0)
                };
            })
            .ToList();

        var categoryStockChartItems = productStockRows
            .GroupBy(r => r.CategoryName)
            .Select(g => new CategoryStockChartItem
            {
                CategoryName = g.Key,
                QtyPieces = g.Sum(r => r.QtyPieces)
            })
            .OrderByDescending(i => i.QtyPieces)
            .ThenBy(i => i.CategoryName)
            .Take(6)
            .ToList();

        var dailyMovements = await _context.StockTransactions
            .AsNoTracking()
            .Include(t => t.TransactionType)
            .Where(t => t.TxnDate >= monthStart && t.TxnDate <= monthEnd)
            .GroupBy(t => t.TxnDate)
            .Select(g => new
            {
                TxnDate = g.Key,
                InPieces = g
                    .Where(t => t.TransactionType.Direction > 0)
                    .Sum(t => (int?)t.QtyPieces) ?? 0,
                OutPieces = g
                    .Where(t => t.TransactionType.Direction < 0)
                    .Sum(t => (int?)t.QtyPieces) ?? 0
            })
            .ToListAsync();

        var movementByDate = dailyMovements.ToDictionary(i => i.TxnDate);
        var dailyMovementChartItems = Enumerable.Range(0, today.Day)
            .Select(offset =>
            {
                var date = monthStart.AddDays(offset);
                movementByDate.TryGetValue(date, out var movement);

                return new DailyMovementChartItem
                {
                    Label = date.Day.ToString("00"),
                    InPieces = movement?.InPieces ?? 0,
                    OutPieces = movement?.OutPieces ?? 0
                };
            })
            .ToList();

        var monthTransactions = await _context.StockTransactions
            .AsNoTracking()
            .Include(t => t.TransactionType)
            .Where(t => t.TxnDate >= monthStart && t.TxnDate <= monthEnd)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                InPieces = g
                    .Where(t => t.TransactionType.Direction > 0)
                    .Sum(t => (int?)t.QtyPieces) ?? 0,
                OutPieces = g
                    .Where(t => t.TransactionType.Direction < 0)
                    .Sum(t => (int?)t.QtyPieces) ?? 0
            })
            .FirstOrDefaultAsync();

        var lastTransactionAt = await _context.StockTransactions
            .AsNoTracking()
            .Select(t => (DateTime?)t.CreatedAt)
            .MaxAsync();

        return View(new HomeDashboardViewModel
        {
            ActiveProductCount = products.Count,
            ActiveCategoryCount = await _context.Categories.CountAsync(c => c.IsActive),
            ActiveVariantCount = productStockRows.Sum(r => r.VariantCount),
            TotalPieces = productStockRows.Sum(r => r.QtyPieces),
            TotalCases = productStockRows.Sum(r => r.QtyCases),
            OutOfStockProductCount = productStockRows.Count(r => r.QtyPieces == 0 && r.QtyCases == 0),
            LowStockProductCount = productStockRows.Count(r => r.QtyPieces > 0 && r.QtyPieces <= 10),
            MonthInPieces = monthTransactions?.InPieces ?? 0,
            MonthOutPieces = monthTransactions?.OutPieces ?? 0,
            MonthTransactionCount = monthTransactions?.Count ?? 0,
            LastTransactionAt = lastTransactionAt,
            CategoryStockChartItems = categoryStockChartItems,
            DailyMovementChartItems = dailyMovementChartItems
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
