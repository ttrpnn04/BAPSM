using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class ProductsController : Controller
{
    private readonly BAPStockContext _context;

    public ProductsController(BAPStockContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? category, string? search, bool showInactive = false)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
                .ThenInclude(v => v.StockBalance)
            .AsQueryable();

        if (!showInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category.CategoryName == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.Sku.Contains(term) ||
                p.ProductName.Contains(term));
        }

        var items = await query
            .OrderBy(p => p.Category.SortOrder)
            .ThenBy(p => p.Category.CategoryName)
            .ThenBy(p => p.ProductName)
            .Select(p => new ProductListItemViewModel
            {
                ProductId = p.ProductId,
                CategoryName = p.Category.CategoryName,
                Sku = p.Sku,
                ProductName = p.ProductName,
                Unit = p.Unit,
                VariantCount = p.ProductVariants.Count(v => v.IsActive),
                TotalPieces = p.ProductVariants
                    .Where(v => v.IsActive)
                    .Sum(v => v.StockBalance != null ? v.StockBalance.QtyPieces : 0),
                TotalCases = p.ProductVariants
                    .Where(v => v.IsActive)
                    .Sum(v => v.StockBalance != null ? v.StockBalance.QtyCases : 0),
                IsActive = p.IsActive
            })
            .ToListAsync();

        ViewBag.Categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.CategoryName)
            .ToListAsync();
        ViewBag.CategoryFilter = category;
        ViewBag.Search = search;
        ViewBag.ShowInactive = showInactive;

        return View(items);
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateCategoriesAsync();
        return View(new CreateProductViewModel());
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpGet]
    public async Task<IActionResult> CreateModal()
    {
        await PopulateCategoriesAsync();
        return PartialView("_CreateModal", new CreateProductViewModel());
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProductViewModel model)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        await PopulateCategoriesAsync();

        var variantNames = ParseVariantNames(model.VariantsText);
        if (variantNames.Count == 0)
        {
            ModelState.AddModelError(nameof(model.VariantsText), "กรุณาระบุสี/รุ่นอย่างน้อย 1 รายการ");
        }

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return PartialView("_CreateModal", model);
            }

            return View(model);
        }

        var skuExists = await _context.Products
            .AnyAsync(p => p.Sku == model.Sku.Trim() && p.ProductName == model.ProductName.Trim());

        if (skuExists)
        {
            ModelState.AddModelError(string.Empty, "SKU และชื่อสินค้านี้มีอยู่แล้ว");
            if (isAjax)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return PartialView("_CreateModal", model);
            }

            return View(model);
        }

        var product = new Product
        {
            CategoryId = model.CategoryId,
            Sku = model.Sku.Trim(),
            ProductName = model.ProductName.Trim(),
            Unit = ProductUnits.Normalize(model.Unit),
            Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(),
            IsActive = true
        };

        for (var i = 0; i < variantNames.Count; i++)
        {
            var variant = new ProductVariant
            {
                VariantName = variantNames[i],
                SortOrder = i + 1,
                IsActive = true
            };
            variant.StockBalance = new StockBalance { QtyPieces = 0, QtyCases = 0 };
            product.ProductVariants.Add(variant);
        }

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"สร้างสินค้า {product.ProductName} สำเร็จ ({variantNames.Count} สี/รุ่น)";
        if (isAjax)
        {
            return Ok(new { success = true, message = TempData["Success"] });
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await LoadProductForEditAsync(id);
        if (product == null)
        {
            return NotFound();
        }

        await PopulateCategoriesAsync(product.CategoryId);
        return View(product);
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditProductViewModel model)
    {
        await PopulateCategoriesAsync(model.CategoryId);

        if (!ModelState.IsValid)
        {
            model.Variants = await LoadVariantsAsync(model.ProductId);
            return View(model);
        }

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.ProductId == model.ProductId);

        if (product == null)
        {
            return NotFound();
        }

        var duplicate = await _context.Products
            .AnyAsync(p =>
                p.ProductId != model.ProductId &&
                p.Sku == model.Sku.Trim() &&
                p.ProductName == model.ProductName.Trim());

        if (duplicate)
        {
            ModelState.AddModelError(string.Empty, "SKU และชื่อสินค้านี้มีอยู่แล้ว");
            model.Variants = await LoadVariantsAsync(model.ProductId);
            return View(model);
        }

        product.CategoryId = model.CategoryId;
        product.Sku = model.Sku.Trim();
        product.ProductName = model.ProductName.Trim();
        product.Unit = ProductUnits.Normalize(model.Unit);
        product.Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
        product.IsActive = model.IsActive;
        product.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        TempData["Success"] = $"บันทึกสินค้า {product.ProductName} สำเร็จ";
        return RedirectToAction(nameof(Edit), new { id = product.ProductId });
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddVariant(int productId, string newVariantName)
    {
        var name = newVariantName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "กรุณาระบุชื่อสี/รุ่น";
            return RedirectToAction(nameof(Edit), new { id = productId });
        }

        var product = await _context.Products
            .Include(p => p.ProductVariants)
            .FirstOrDefaultAsync(p => p.ProductId == productId);

        if (product == null)
        {
            return NotFound();
        }

        if (product.ProductVariants.Any(v => v.VariantName == name))
        {
            TempData["Error"] = "สี/รุ่นนี้มีอยู่แล้ว";
            return RedirectToAction(nameof(Edit), new { id = productId });
        }

        var maxSort = product.ProductVariants.Any()
            ? product.ProductVariants.Max(v => v.SortOrder)
            : 0;

        var variant = new ProductVariant
        {
            ProductId = productId,
            VariantName = name,
            SortOrder = maxSort + 1,
            IsActive = true,
            StockBalance = new StockBalance { QtyPieces = 0, QtyCases = 0 }
        };

        _context.ProductVariants.Add(variant);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"เพิ่มสี/รุ่น {name} สำเร็จ";
        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVariant(int variantId)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.VariantId == variantId);

        if (variant == null)
        {
            return NotFound();
        }

        variant.IsActive = !variant.IsActive;
        await _context.SaveChangesAsync();

        TempData["Success"] = variant.IsActive
            ? $"เปิดใช้งาน {variant.VariantName} แล้ว"
            : $"ปิดใช้งาน {variant.VariantName} แล้ว";

        return RedirectToAction(nameof(Edit), new { id = variant.ProductId });
    }

    private async Task PopulateCategoriesAsync(int? selectedId = null)
    {
        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .ToListAsync();

        ViewBag.Categories = new SelectList(categories, "CategoryId", "CategoryName", selectedId);
    }

    private async Task<EditProductViewModel?> LoadProductForEditAsync(int id)
    {
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == id);

        if (product == null)
        {
            return null;
        }

        return new EditProductViewModel
        {
            ProductId = product.ProductId,
            CategoryId = product.CategoryId,
            Sku = product.Sku,
            ProductName = product.ProductName,
            Unit = product.Unit,
            Note = product.Note,
            IsActive = product.IsActive,
            Variants = await LoadVariantsAsync(id)
        };
    }

    private async Task<IReadOnlyList<ProductVariantItemViewModel>> LoadVariantsAsync(int productId)
    {
        return await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.StockBalance)
            .Where(v => v.ProductId == productId)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.VariantName)
            .Select(v => new ProductVariantItemViewModel
            {
                VariantId = v.VariantId,
                VariantName = v.VariantName,
                QtyPieces = v.StockBalance != null ? v.StockBalance.QtyPieces : 0,
                QtyCases = v.StockBalance != null ? v.StockBalance.QtyCases : 0,
                IsActive = v.IsActive
            })
            .ToListAsync();
    }

    private static List<string> ParseVariantNames(string text)
    {
        return text
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
