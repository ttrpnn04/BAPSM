using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Stock;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class TransactionsController : Controller
{
    private readonly BAPStockContext _context;

    public TransactionsController(BAPStockContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(DateOnly? fromDate, DateOnly? toDate, int? transactionTypeId, string? search)
    {
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

        var items = await query
            .OrderByDescending(t => t.TxnDate)
            .ThenByDescending(t => t.CreatedAt)
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

        return View(new TransactionIndexViewModel
        {
            FromDate = from,
            ToDate = to,
            TransactionTypeId = transactionTypeId,
            Search = search,
            TransactionTypes = types,
            Items = items
        });
    }

    [Authorize(Roles = AppRoles.StockEdit)]
    [HttpGet]
    public async Task<IActionResult> Create(int? variantId)
    {
        await PopulateCreateLookupsAsync();

        return View(new CreateTransactionViewModel
        {
            VariantId = variantId ?? 0
        });
    }

    [Authorize(Roles = AppRoles.StockEdit)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTransactionViewModel model)
    {
        await PopulateCreateLookupsAsync();

        if (model.QtyPieces == 0 && model.QtyCases == 0)
        {
            ModelState.AddModelError(string.Empty, "กรุณาระบุจำนวนชิ้นหรือลังอย่างน้อย 1 รายการ");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var variant = await _context.ProductVariants
            .Include(v => v.StockBalance)
            .FirstOrDefaultAsync(v => v.VariantId == model.VariantId && v.IsActive);

        if (variant == null)
        {
            ModelState.AddModelError(nameof(model.VariantId), "ไม่พบสินค้าที่เลือก");
            return View(model);
        }

        var txnType = await _context.TransactionTypes
            .FirstOrDefaultAsync(t => t.TransactionTypeId == model.TransactionTypeId && t.IsActive);

        if (txnType == null)
        {
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
                ModelState.AddModelError(string.Empty,
                    $"สต็อกไม่เพียงพอ (คงเหลือ {currentPieces:N0} ชิ้น, {currentCases:N0} ลัง)");
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
            CreatedBy = User.Identity?.Name
        };

        _context.StockTransactions.Add(transaction);
        await _context.SaveChangesAsync();

        TempData["Success"] = "บันทึกรายการสต็อกสำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateCreateLookupsAsync()
    {
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
                Label = v.Product.Category.CategoryName + " | " + v.Product.Sku + " - " +
                        v.Product.ProductName + " (" + v.VariantName + ")"
            })
            .ToListAsync();

        ViewBag.Variants = new SelectList(variants, "VariantId", "Label");

        var types = await _context.TransactionTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.TransactionTypeId)
            .ToListAsync();

        ViewBag.TransactionTypes = new SelectList(types, "TransactionTypeId", "TypeName");
    }
}
