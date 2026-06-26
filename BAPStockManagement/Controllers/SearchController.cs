using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class SearchController : Controller
{
    private readonly BAPStockContext _context;

    public SearchController(BAPStockContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Suggestions(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Json(Array.Empty<string>());
        }

        var query = term.Trim();
        var suggestions = new List<string>();

        suggestions.AddRange(await _context.Products
            .AsNoTracking()
            .Where(p =>
                p.IsActive &&
                p.Category.IsActive &&
                (p.Sku.Contains(query) || p.ProductName.Contains(query)))
            .OrderBy(p => p.Sku)
            .Select(p => p.Sku)
            .Take(6)
            .ToListAsync());

        suggestions.AddRange(await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.Category.IsActive && p.ProductName.Contains(query))
            .OrderBy(p => p.ProductName)
            .Select(p => p.ProductName)
            .Take(6)
            .ToListAsync());

        suggestions.AddRange(await _context.ProductVariants
            .AsNoTracking()
            .Where(v =>
                v.IsActive &&
                v.Product.IsActive &&
                v.Product.Category.IsActive &&
                v.VariantName.Contains(query))
            .OrderBy(v => v.VariantName)
            .Select(v => v.VariantName)
            .Take(6)
            .ToListAsync());

        suggestions.AddRange(await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.CategoryName.Contains(query))
            .OrderBy(c => c.CategoryName)
            .Select(c => c.CategoryName)
            .Take(6)
            .ToListAsync());

        suggestions.AddRange(await _context.StockTransactions
            .AsNoTracking()
            .Where(t =>
                t.RefNo != null &&
                t.RefNo.Contains(query) &&
                t.Variant.IsActive &&
                t.Variant.Product.IsActive &&
                t.Variant.Product.Category.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.RefNo!)
            .Take(6)
            .ToListAsync());

        return Json(suggestions
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList());
    }
}
