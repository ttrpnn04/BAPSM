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

    public async Task<IActionResult> Index(string? category, string? search)
    {
        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .Select(c => c.CategoryName)
            .ToListAsync();

        var query = _context.VwCurrentStocks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(s => s.CategoryName == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                s.Sku.Contains(term) ||
                s.ProductName.Contains(term) ||
                s.VariantName.Contains(term));
        }

        var items = await query
            .OrderBy(s => s.CategoryName)
            .ThenBy(s => s.ProductName)
            .ThenBy(s => s.VariantName)
            .ToListAsync();

        return View(new StockIndexViewModel
        {
            CategoryFilter = category,
            Search = search,
            Categories = categories,
            Items = items
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
