using BAPStockManagement.Constants;
using BAPStockManagement.Data;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Stock;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class TransactionsController : Controller
{
    private readonly BAPStockContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public TransactionsController(BAPStockContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(DateOnly? fromDate, DateOnly? toDate, int? transactionTypeId, string? search, int page = 1, int pageSize = 25)
    {
        var allowedPageSizes = new[] { 10, 25, 50 };
        if (!allowedPageSizes.Contains(pageSize)) pageSize = 25;
        if (page < 1) page = 1;

        var from = fromDate ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var to = toDate ?? DateOnly.FromDateTime(DateTime.Today);

        if (from > to)
        {
            (from, to) = (to, from);
        }

        var types = await _context.TransactionTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.TransactionTypeId)
            .Select(t => new TransactionTypeOption
            {
                TransactionTypeId = t.TransactionTypeId,
                TypeName = t.TypeName
            })
            .ToListAsync();

        var query = _context.StockTransactions
            .AsNoTracking()
            .Include(t => t.TransactionType)
            .Include(t => t.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(t => t.TxnDate >= from && t.TxnDate <= to);

        if (transactionTypeId.HasValue)
        {
            query = query.Where(t => t.TransactionTypeId == transactionTypeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t =>
                t.Variant.Product.Sku.Contains(term) ||
                t.Variant.Product.ProductName.Contains(term) ||
                t.Variant.VariantName.Contains(term) ||
                (t.RefNo != null && t.RefNo.Contains(term)));
        }

        var totalItems = await query.CountAsync();
        var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling((double)totalItems / pageSize);
        if (page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(t => t.TxnDate)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionListItemViewModel
            {
                TransactionId = t.TransactionId,
                TxnDate = t.TxnDate,
                TypeName = t.TransactionType.TypeName,
                Direction = t.TransactionType.Direction,
                CategoryName = t.Variant.Product.Category.CategoryName,
                Sku = t.Variant.Product.Sku,
                ProductName = t.Variant.Product.ProductName,
                VariantName = t.Variant.VariantName,
                QtyPieces = t.QtyPieces,
                QtyCases = t.QtyCases,
                RefNo = t.RefNo,
                Note = t.Note,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        var createdByIds = items
            .Select(i => i.CreatedBy)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (createdByIds.Count > 0)
        {
            var userEmails = await _context.Users
                .AsNoTracking()
                .Where(u => createdByIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => u.Email ?? u.UserName ?? u.Id);

            foreach (var item in items)
            {
                if (item.CreatedBy != null && userEmails.TryGetValue(item.CreatedBy, out var email))
                {
                    item.CreatedBy = email;
                }
            }
        }

        return View(new TransactionIndexViewModel
        {
            FromDate = from,
            ToDate = to,
            TransactionTypeId = transactionTypeId,
            Search = search,
            TransactionTypes = types,
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        });
    }

    [Authorize(Roles = AppRoles.StockEdit)]
    [HttpGet]
    public async Task<IActionResult> CreateModal(int? variantId, int? productId)
    {
        var selectedVariantId = variantId;
        if (!selectedVariantId.HasValue && productId.HasValue)
        {
            selectedVariantId = await EnsureDefaultVariantForProductAsync(productId.Value);
        }

        var selectedProductId = productId;
        if (!selectedProductId.HasValue && selectedVariantId.HasValue)
        {
            selectedProductId = await _context.ProductVariants
                .AsNoTracking()
                .Where(v => v.VariantId == selectedVariantId.Value)
                .Select(v => (int?)v.ProductId)
                .FirstOrDefaultAsync();
        }

        await PopulateCreateLookupsAsync(selectedVariantId);

        return PartialView("_CreateModal", new CreateTransactionViewModel
        {
            ProductId = selectedProductId,
            VariantId = selectedVariantId ?? 0
        });
    }

    [Authorize(Roles = AppRoles.StockEdit)]
    [HttpGet]
    public async Task<IActionResult> Create(int? variantId, int? productId)
    {
        var selectedVariantId = variantId;
        if (!selectedVariantId.HasValue && productId.HasValue)
        {
            selectedVariantId = await EnsureDefaultVariantForProductAsync(productId.Value);
        }

        var selectedProductId = productId;
        if (!selectedProductId.HasValue && selectedVariantId.HasValue)
        {
            selectedProductId = await _context.ProductVariants
                .AsNoTracking()
                .Where(v => v.VariantId == selectedVariantId.Value)
                .Select(v => (int?)v.ProductId)
                .FirstOrDefaultAsync();
        }

        await PopulateCreateLookupsAsync(selectedVariantId);

        return View(new CreateTransactionViewModel
        {
            ProductId = selectedProductId,
            VariantId = selectedVariantId ?? 0
        });
    }

    [Authorize(Roles = AppRoles.StockEdit)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTransactionViewModel model)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!isAjax) await PopulateCreateLookupsAsync(model.VariantId);

        if (model.QtyPieces == 0 && model.QtyCases == 0)
        {
            ModelState.AddModelError(string.Empty, "กรุณาระบุจำนวนชิ้นหรือลังอย่างน้อย 1 รายการ");
        }

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var msgs = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { message = string.Join(" | ", msgs) });
            }
            return View(model);
        }

        var variant = await _context.ProductVariants
            .Include(v => v.StockBalance)
            .FirstOrDefaultAsync(v => v.VariantId == model.VariantId && v.IsActive);

        if (variant == null)
        {
            if (isAjax) return BadRequest(new { message = "ไม่พบสินค้าที่เลือก" });
            ModelState.AddModelError(nameof(model.VariantId), "ไม่พบสินค้าที่เลือก");
            return View(model);
        }

        if (model.ProductId.HasValue && variant.ProductId != model.ProductId.Value)
        {
            if (isAjax) return BadRequest(new { message = "สี/รุ่นไม่ตรงกับสินค้าที่เลือก" });
            ModelState.AddModelError(nameof(model.VariantId), "สี/รุ่นไม่ตรงกับสินค้าที่เลือก");
            return View(model);
        }

        var txnType = await _context.TransactionTypes
            .FirstOrDefaultAsync(t => t.TransactionTypeId == model.TransactionTypeId && t.IsActive);

        if (txnType == null)
        {
            if (isAjax) return BadRequest(new { message = "ประเภทรายการไม่ถูกต้อง" });
            ModelState.AddModelError(nameof(model.TransactionTypeId), "ประเภทรายการไม่ถูกต้อง");
            return View(model);
        }

        if (txnType.Direction < 0)
        {
            var balance = variant.StockBalance;
            var currentPieces = balance?.QtyPieces ?? 0;
            var currentCases = balance?.QtyCases ?? 0;

            if (model.QtyPieces > currentPieces || model.QtyCases > currentCases)
            {
                var msg = $"สต็อกไม่เพียงพอ (คงเหลือ {currentPieces:N0} ชิ้น, {currentCases:N0} ลัง)";
                if (isAjax) return BadRequest(new { message = msg });
                ModelState.AddModelError(string.Empty, msg);
                return View(model);
            }
        }

        var transaction = new StockTransaction
        {
            VariantId = model.VariantId,
            TransactionTypeId = model.TransactionTypeId,
            TxnDate = model.TxnDate,
            QtyPieces = model.QtyPieces,
            QtyCases = model.QtyCases,
            RefNo = string.IsNullOrWhiteSpace(model.RefNo) ? null : model.RefNo.Trim(),
            Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(),
            CreatedBy = _userManager.GetUserId(User)
        };

        _context.StockTransactions.Add(transaction);
        await _context.SaveChangesAsync();

        if (isAjax) return Ok(new { success = true });
        TempData["Success"] = "บันทึกรายการสต็อกสำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    private async Task<int?> EnsureDefaultVariantForProductAsync(int productId)
    {
        var existingVariant = await _context.ProductVariants
            .Where(v =>
                v.ProductId == productId &&
                v.IsActive &&
                v.VariantName != ProductVariantDefaults.StandardVariantName)
            .OrderBy(v => v.SortOrder)
            .Select(v => (int?)v.VariantId)
            .FirstOrDefaultAsync();
        if (existingVariant.HasValue)
        {
            return existingVariant;
        }

        var product = await _context.Products
            .Include(p => p.ProductVariants)
                .ThenInclude(v => v.StockBalance)
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive);
        if (product == null)
        {
            return null;
        }

        await ProductVariantSeeder.EnsureDefaultColorsAsync(product);
        await _context.SaveChangesAsync();

        return product.ProductVariants
            .Where(v => v.IsActive && v.VariantName != ProductVariantDefaults.StandardVariantName)
            .OrderBy(v => v.SortOrder)
            .Select(v => (int?)v.VariantId)
            .FirstOrDefault();
    }

    private async Task PopulateCreateLookupsAsync(int? selectedVariantId = null)
    {
        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.ProductVariants.Any(v => v.IsActive))
            .OrderBy(p => p.Category.SortOrder)
            .ThenBy(p => p.Category.CategoryName)
            .ThenBy(p => p.ProductName)
            .Select(p => new
            {
                p.ProductId,
                Label = p.Category.CategoryName + " | " + p.Sku + " - " + p.ProductName
            })
            .ToListAsync();

        var selectedProductId = selectedVariantId.HasValue && selectedVariantId.Value > 0
            ? await _context.ProductVariants
                .AsNoTracking()
                .Where(v => v.VariantId == selectedVariantId.Value)
                .Select(v => (int?)v.ProductId)
                .FirstOrDefaultAsync()
            : null;

        ViewBag.Products = new SelectList(products, "ProductId", "Label", selectedProductId);

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
                .ThenInclude(p => p.Category)
            .Where(v => v.IsActive && v.Product.IsActive)
            .OrderBy(v => v.Product.Category.SortOrder)
            .ThenBy(v => v.Product.Category.CategoryName)
            .ThenBy(v => v.Product.ProductName)
            .ThenBy(v => v.VariantName)
            .Select(v => new
            {
                v.VariantId,
                v.ProductId,
                Label = v.VariantName
            })
            .ToListAsync();

        ViewBag.Variants = variants;
        ViewBag.SelectedVariantId = selectedVariantId;

        var types = await _context.TransactionTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.TransactionTypeId)
            .ToListAsync();

        ViewBag.TransactionTypes = new SelectList(types, "TransactionTypeId", "TypeName");
        ViewBag.TransactionTypesList = types;
    }
}
