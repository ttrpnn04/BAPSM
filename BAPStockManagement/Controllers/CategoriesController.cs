using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.AdminManage)]
public class CategoriesController : Controller
{
    private readonly BAPStockContext _context;

    public CategoriesController(BAPStockContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _context.Categories
            .AsNoTracking()
            .Select(c => new CategoryListItemViewModel
            {
                CategoryId = c.CategoryId,
                CategoryName = c.CategoryName,
                VariantLabel = c.VariantLabel,
                SortOrder = c.SortOrder,
                ProductCount = c.Products.Count(p => p.IsActive),
                IsActive = c.IsActive
            })
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryName)
            .ToListAsync();

        return View(items);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CategoryFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var nameExists = await _context.Categories
            .AnyAsync(c => c.CategoryName == model.CategoryName.Trim());

        if (nameExists)
        {
            ModelState.AddModelError(nameof(model.CategoryName), "ชื่อหมวดหมู่นี้มีอยู่แล้ว");
            return View(model);
        }

        var category = new Category
        {
            CategoryName = model.CategoryName.Trim(),
            VariantLabel = model.VariantLabel.Trim(),
            SortOrder = model.SortOrder,
            IsActive = model.IsActive
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"สร้างหมวดหมู่ {category.CategoryName} สำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound();
        }

        return View(new CategoryFormViewModel
        {
            CategoryId = category.CategoryId,
            CategoryName = category.CategoryName,
            VariantLabel = category.VariantLabel,
            SortOrder = category.SortOrder,
            IsActive = category.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryFormViewModel model)
    {
        if (!model.CategoryId.HasValue)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var category = await _context.Categories.FindAsync(model.CategoryId.Value);
        if (category == null)
        {
            return NotFound();
        }

        var nameExists = await _context.Categories
            .AnyAsync(c =>
                c.CategoryId != model.CategoryId.Value &&
                c.CategoryName == model.CategoryName.Trim());

        if (nameExists)
        {
            ModelState.AddModelError(nameof(model.CategoryName), "ชื่อหมวดหมู่นี้มีอยู่แล้ว");
            return View(model);
        }

        category.CategoryName = model.CategoryName.Trim();
        category.VariantLabel = model.VariantLabel.Trim();
        category.SortOrder = model.SortOrder;
        category.IsActive = model.IsActive;

        await _context.SaveChangesAsync();

        TempData["Success"] = $"บันทึกหมวดหมู่ {category.CategoryName} สำเร็จ";
        return RedirectToAction(nameof(Index));
    }
}
