using BAPStockManagement.Constants;
using BAPStockManagement.Data;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Stock;
using ClosedXML.Excel;
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

    [HttpGet]
    public async Task<IActionResult> ExportDailyReport(DateTime? fromDate, DateTime? toDate)
    {
        var from = DateOnly.FromDateTime((fromDate ?? DateTime.Today).Date);
        var to = DateOnly.FromDateTime((toDate ?? DateTime.Today).Date);
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var fileDatePart = from == to
            ? $"{from:ddMMyyyy}"
            : $"{from:ddMMyyyy}-{to:ddMMyyyy}";
        var fileName = $"BAP_StockReport_{fileDatePart}.xlsx";

        var transactions = await _context.StockTransactions
            .AsNoTracking()
            .Include(t => t.TransactionType)
            .Include(t => t.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(t => t.TxnDate >= from && t.TxnDate <= to)
            .OrderBy(t => t.TxnDate)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.TransactionId)
            .ToListAsync();

        var createdByIds = transactions
            .Select(t => t.CreatedBy)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

        var userNames = createdByIds.Count > 0
            ? await _context.Users
                .AsNoTracking()
                .Where(u => createdByIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => u.Email ?? u.UserName ?? u.Id)
            : new Dictionary<string, string>();

        var currentStocks = await _context.VwCurrentStocks
            .AsNoTracking()
            .OrderBy(s => s.CategoryName)
            .ThenBy(s => s.Sku)
            .ThenBy(s => s.ProductName)
            .ThenBy(s => s.VariantName)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        WriteTransactionMatrixSheet(
            workbook.Worksheets.Add("รับเข้า"),
            transactions.Where(t => t.TransactionType.Direction > 0).ToList(),
            from,
            to,
            "รับเข้า");

        WriteTransactionMatrixSheet(
            workbook.Worksheets.Add("ขายออก"),
            transactions.Where(t => t.TransactionType.Direction < 0).ToList(),
            from,
            to,
            "ขายออก");

        var stockSheet = workbook.Worksheets.Add("ยอดคงเหลือ");
        WriteStockMatrixSheet(stockSheet, currentStocks, from, to);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static void WriteStockMatrixSheet(IXLWorksheet worksheet, IReadOnlyList<VwCurrentStock> stocks, DateOnly from, DateOnly to)
    {
        var standardColors = ProductVariantDefaults.ColorNames.ToList();
        var extraColors = stocks
            .Select(s => s.VariantName)
            .Distinct()
            .Where(name => !standardColors.Contains(name))
            .OrderBy(name => name)
            .ToList();
        var colorColumns = standardColors.Concat(extraColors).ToList();
        var totalColumn = 4 + colorColumns.Count;

        worksheet.Cell(1, 1).Value = from == to
            ? $"สต็อกคงเหลือ ณ {from:dd/MM/yyyy}"
            : $"สต็อกคงเหลือ ({from:dd/MM/yyyy} - {to:dd/MM/yyyy})";
        worksheet.Range(1, 1, 1, 3).Merge();
        worksheet.Range(1, 1, 1, 3).Style.Fill.BackgroundColor = XLColor.Black;
        worksheet.Range(1, 1, 1, 3).Style.Font.FontColor = XLColor.White;
        worksheet.Range(1, 1, 1, 3).Style.Font.Bold = true;
        worksheet.Cell(1, totalColumn).Value = "สต็อก";
        worksheet.Cell(1, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
        worksheet.Cell(1, totalColumn).Style.Font.Bold = true;
        worksheet.Cell(1, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        worksheet.Cell(2, 1).Value = "#";
        worksheet.Cell(2, 2).Value = "รหัสสินค้า";
        worksheet.Cell(2, 3).Value = "รายการสินค้า-แบรนด์";
        worksheet.Range(2, 1, 2, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
        worksheet.Range(2, 1, 2, 3).Style.Font.Bold = true;
        worksheet.Range(2, 1, 2, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (var i = 0; i < colorColumns.Count; i++)
        {
            var column = 4 + i;
            var colorName = colorColumns[i];
            worksheet.Cell(2, column).Value = colorName;
            worksheet.Cell(2, column).Style.Fill.BackgroundColor = ResolveExcelColor(colorName);
            worksheet.Cell(2, column).Style.Font.Bold = true;
            worksheet.Cell(2, column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(2, column).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            worksheet.Cell(2, column).Style.Alignment.WrapText = true;
        }

        worksheet.Cell(2, totalColumn).Value = "สต็อก";
        worksheet.Cell(2, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
        worksheet.Cell(2, totalColumn).Style.Font.Bold = true;
        worksheet.Cell(2, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var groupedStocks = stocks
            .GroupBy(s => new { s.CategoryName, s.Sku, s.ProductName, s.Unit })
            .OrderBy(g => g.Key.CategoryName)
            .ThenBy(g => g.Key.Sku)
            .ThenBy(g => g.Key.ProductName)
            .ToList();

        var row = 3;
        var index = 1;
        foreach (var productStock in groupedStocks)
        {
            var byColor = productStock
                .GroupBy(s => s.VariantName)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.QtyPieces));
            var totalPieces = productStock.Sum(s => s.QtyPieces);

            worksheet.Cell(row, 1).Value = index++;
            worksheet.Cell(row, 2).Value = productStock.Key.Sku;
            worksheet.Cell(row, 3).Value = productStock.Key.ProductName;

            for (var i = 0; i < colorColumns.Count; i++)
            {
                var column = 4 + i;
                var qty = byColor.TryGetValue(colorColumns[i], out var value) ? value : 0;
                worksheet.Cell(row, column).Value = qty > 0 ? qty : "-";
                worksheet.Cell(row, column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            worksheet.Cell(row, totalColumn).Value = totalPieces;
            worksheet.Cell(row, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
            worksheet.Cell(row, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            if (totalPieces == 0)
            {
                worksheet.Range(row, 1, row, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
                worksheet.Cell(row, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
            }

            row++;
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(row - 1, 2), totalColumn);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 16;
        worksheet.Column(3).Width = 38;
        for (var column = 4; column <= totalColumn; column++)
        {
            worksheet.Column(column).Width = column == totalColumn ? 10 : 7;
        }

        worksheet.Row(2).Height = 44;
        worksheet.SheetView.FreezeRows(2);
    }

    private static void WriteTransactionMatrixSheet(
        IXLWorksheet worksheet,
        IReadOnlyList<StockTransaction> transactions,
        DateOnly from,
        DateOnly to,
        string title)
    {
        var standardColors = ProductVariantDefaults.ColorNames.ToList();
        var extraColors = transactions
            .Select(t => t.Variant.VariantName)
            .Distinct()
            .Where(name => !standardColors.Contains(name))
            .OrderBy(name => name)
            .ToList();
        var colorColumns = standardColors.Concat(extraColors).ToList();
        var totalColumn = 4 + colorColumns.Count;

        worksheet.Cell(1, 1).Value = from == to
            ? $"{title} วันที่ {from:dd/MM/yyyy}"
            : $"{title} ({from:dd/MM/yyyy} - {to:dd/MM/yyyy})";
        worksheet.Range(1, 1, 1, 3).Merge();
        worksheet.Range(1, 1, 1, 3).Style.Fill.BackgroundColor = XLColor.Black;
        worksheet.Range(1, 1, 1, 3).Style.Font.FontColor = XLColor.White;
        worksheet.Range(1, 1, 1, 3).Style.Font.Bold = true;
        worksheet.Cell(1, totalColumn).Value = title;
        worksheet.Cell(1, totalColumn).Style.Fill.BackgroundColor = title == "รับเข้า"
            ? XLColor.FromHtml("#92D050")
            : XLColor.FromHtml("#FF0000");
        worksheet.Cell(1, totalColumn).Style.Font.Bold = true;
        worksheet.Cell(1, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        worksheet.Cell(2, 1).Value = "#";
        worksheet.Cell(2, 2).Value = "รหัสสินค้า";
        worksheet.Cell(2, 3).Value = "รายการสินค้า-แบรนด์";
        worksheet.Range(2, 1, 2, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
        worksheet.Range(2, 1, 2, 3).Style.Font.Bold = true;
        worksheet.Range(2, 1, 2, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (var i = 0; i < colorColumns.Count; i++)
        {
            var column = 4 + i;
            var colorName = colorColumns[i];
            worksheet.Cell(2, column).Value = colorName;
            worksheet.Cell(2, column).Style.Fill.BackgroundColor = ResolveExcelColor(colorName);
            worksheet.Cell(2, column).Style.Font.Bold = true;
            worksheet.Cell(2, column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(2, column).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            worksheet.Cell(2, column).Style.Alignment.WrapText = true;
        }

        worksheet.Cell(2, totalColumn).Value = title == "รับเข้า" ? "เข้า" : "ออก";
        worksheet.Cell(2, totalColumn).Style.Fill.BackgroundColor = title == "รับเข้า"
            ? XLColor.FromHtml("#92D050")
            : XLColor.FromHtml("#FF0000");
        worksheet.Cell(2, totalColumn).Style.Font.Bold = true;
        worksheet.Cell(2, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var groupedTransactions = transactions
            .GroupBy(t => new
            {
                t.Variant.Product.Category.CategoryName,
                t.Variant.Product.Sku,
                t.Variant.Product.ProductName,
                t.Variant.Product.Unit
            })
            .OrderBy(g => g.Key.CategoryName)
            .ThenBy(g => g.Key.Sku)
            .ThenBy(g => g.Key.ProductName)
            .ToList();

        var row = 3;
        var index = 1;
        foreach (var productTransactions in groupedTransactions)
        {
            var byColor = productTransactions
                .GroupBy(t => t.Variant.VariantName)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.QtyPieces));
            var totalPieces = productTransactions.Sum(t => t.QtyPieces);

            worksheet.Cell(row, 1).Value = index++;
            worksheet.Cell(row, 2).Value = productTransactions.Key.Sku;
            worksheet.Cell(row, 3).Value = productTransactions.Key.ProductName;

            for (var i = 0; i < colorColumns.Count; i++)
            {
                var column = 4 + i;
                var qty = byColor.TryGetValue(colorColumns[i], out var value) ? value : 0;
                worksheet.Cell(row, column).Value = qty > 0 ? qty : "-";
                worksheet.Cell(row, column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            worksheet.Cell(row, totalColumn).Value = totalPieces;
            worksheet.Cell(row, totalColumn).Style.Fill.BackgroundColor = title == "รับเข้า"
                ? XLColor.FromHtml("#92D050")
                : XLColor.FromHtml("#FF0000");
            worksheet.Cell(row, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;
        }

        var summaryRow = row;
        worksheet.Cell(summaryRow, 1).Value = "รวม";
        worksheet.Range(summaryRow, 1, summaryRow, 3).Merge();
        worksheet.Cell(summaryRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        worksheet.Range(summaryRow, 1, summaryRow, totalColumn).Style.Font.Bold = true;
        worksheet.Range(summaryRow, 1, summaryRow, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        for (var i = 0; i < colorColumns.Count; i++)
        {
            var column = 4 + i;
            var total = transactions
                .Where(t => t.Variant.VariantName == colorColumns[i])
                .Sum(t => t.QtyPieces);
            worksheet.Cell(summaryRow, column).Value = total > 0 ? total : "-";
            worksheet.Cell(summaryRow, column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        worksheet.Cell(summaryRow, totalColumn).Value = transactions.Sum(t => t.QtyPieces);
        worksheet.Cell(summaryRow, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var usedRange = worksheet.Range(1, 1, summaryRow, totalColumn);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 16;
        worksheet.Column(3).Width = 38;
        for (var column = 4; column <= totalColumn; column++)
        {
            worksheet.Column(column).Width = column == totalColumn ? 10 : 7;
        }

        worksheet.Row(2).Height = 44;
        worksheet.SheetView.FreezeRows(2);
    }

    private static XLColor ResolveExcelColor(string name)
    {
        if (name.Contains("ดำ")) return XLColor.FromHtml("#111827");
        if (name.Contains("แดง")) return XLColor.FromHtml("#FF0000");
        if (name.Contains("น้ำเงิน")) return XLColor.FromHtml("#4472C4");
        if (name.Contains("ฟ้า") || name.Contains("ธงฟ้า")) return XLColor.FromHtml("#5CE1E6");
        if (name.Contains("ส้ม")) return XLColor.FromHtml("#ED7D31");
        if (name.Contains("เขียวออ่อน") || name.Contains("เขียวอ่อน")) return XLColor.FromHtml("#C6EF8C");
        if (name.Contains("เขียว")) return XLColor.FromHtml("#92D050");
        if (name.Contains("ชมพูอ่อน")) return XLColor.FromHtml("#FCE4D6");
        if (name.Contains("ชมพู") || name.Contains("ชม")) return XLColor.FromHtml("#FF99FF");
        if (name.Contains("เหลือง") || name.Contains("ทอง") || name.Contains("ครีม")) return XLColor.FromHtml("#FFFF00");
        if (name.Contains("ม่วง")) return XLColor.FromHtml("#E4C1E0");
        if (name.Contains("น้ำตาล")) return XLColor.FromHtml("#FFC000");
        if (name.Contains("เทา")) return XLColor.FromHtml("#D9D9D9");
        if (name.Contains("ขาว")) return XLColor.White;
        return XLColor.FromHtml("#F2F2F2");
    }

    [Authorize(Roles = AppRoles.StockEdit)]
    [HttpGet]
    public async Task<IActionResult> CreateModal(int? variantId, int? productId, short? direction)
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

        var selectedTransactionTypeId = direction.HasValue
            ? await _context.TransactionTypes
                .AsNoTracking()
                .Where(t => t.IsActive && t.Direction == direction.Value)
                .OrderBy(t => t.TransactionTypeId)
                .Select(t => t.TransactionTypeId)
                .FirstOrDefaultAsync()
            : 0;

        return PartialView("_CreateModal", new CreateTransactionViewModel
        {
            ProductId = selectedProductId,
            VariantId = selectedVariantId ?? 0,
            TransactionTypeId = selectedTransactionTypeId
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
        var hasLineItems = model.Lines.Any(l => l.VariantId > 0 || l.QtyPieces > 0 || l.QtyCases > 0);

        if (hasLineItems)
        {
            ModelState.Remove(nameof(model.ProductId));
            ModelState.Remove(nameof(model.VariantId));
            ModelState.Remove(nameof(model.QtyPieces));
            ModelState.Remove(nameof(model.QtyCases));
        }

        if (!isAjax) await PopulateCreateLookupsAsync(model.VariantId);

        var submittedLines = hasLineItems
            ? model.Lines
                .Where(l => l.VariantId > 0 && (l.QtyPieces > 0 || l.QtyCases > 0))
                .ToList()
            : new List<CreateTransactionLineViewModel>
            {
                new CreateTransactionLineViewModel
                {
                    VariantId = model.VariantId,
                    QtyPieces = model.QtyPieces,
                    QtyCases = model.QtyCases
                }
            };

        if (submittedLines.Count == 0)
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

        var variantIds = submittedLines
            .Select(l => l.VariantId)
            .Distinct()
            .ToList();

        var variants = await _context.ProductVariants
            .Include(v => v.StockBalance)
            .Where(v =>
                variantIds.Contains(v.VariantId) &&
                v.IsActive &&
                v.Product.IsActive &&
                v.Product.Category.IsActive)
            .ToDictionaryAsync(v => v.VariantId);

        if (variants.Count != variantIds.Count)
        {
            if (isAjax) return BadRequest(new { message = "ไม่พบสินค้าที่เลือก" });
            ModelState.AddModelError(nameof(model.VariantId), "ไม่พบสินค้าที่เลือก");
            return View(model);
        }

        if (!hasLineItems && model.ProductId.HasValue && variants[model.VariantId].ProductId != model.ProductId.Value)
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
            foreach (var group in submittedLines.GroupBy(l => l.VariantId))
            {
                var variant = variants[group.Key];
                var currentPieces = variant.StockBalance?.QtyPieces ?? 0;
                var currentCases = variant.StockBalance?.QtyCases ?? 0;
                var requestedPieces = group.Sum(l => l.QtyPieces);
                var requestedCases = group.Sum(l => l.QtyCases);

                if (requestedPieces > currentPieces || requestedCases > currentCases)
                {
                    var msg = $"สต็อกไม่เพียงพอ: {variant.VariantName} (คงเหลือ {currentPieces:N0} ชิ้น, {currentCases:N0} ลัง)";
                    if (isAjax) return BadRequest(new { message = msg });
                    ModelState.AddModelError(string.Empty, msg);
                    return View(model);
                }
            }
        }

        var refNo = string.IsNullOrWhiteSpace(model.RefNo) ? null : model.RefNo.Trim();
        var note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
        var userId = _userManager.GetUserId(User);
        var transactions = submittedLines.Select(line => new StockTransaction
            {
                VariantId = line.VariantId,
                TransactionTypeId = model.TransactionTypeId,
                TxnDate = model.TxnDate,
                QtyPieces = line.QtyPieces,
                QtyCases = line.QtyCases,
                RefNo = refNo,
                Note = note,
                CreatedBy = userId
            })
            .ToList();

        _context.StockTransactions.AddRange(transactions);
        await _context.SaveChangesAsync();

        if (isAjax) return Ok(new { success = true, count = transactions.Count });
        TempData["Success"] = $"บันทึกรายการสต็อกสำเร็จ {transactions.Count:N0} รายการ";
        return RedirectToAction(nameof(Index));
    }

    private async Task<int?> EnsureDefaultVariantForProductAsync(int productId)
    {
        var existingVariant = await _context.ProductVariants
            .Where(v =>
                v.ProductId == productId &&
                v.IsActive &&
                v.Product.IsActive &&
                v.Product.Category.IsActive &&
                v.VariantName != ProductVariantDefaults.StandardVariantName)
            .OrderBy(v => v.SortOrder)
            .Select(v => (int?)v.VariantId)
            .FirstOrDefaultAsync();
        if (existingVariant.HasValue)
        {
            return existingVariant;
        }

        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductVariants)
                .ThenInclude(v => v.StockBalance)
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive && p.Category.IsActive);
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
            .Where(p => p.IsActive && p.Category.IsActive && p.ProductVariants.Any(v => v.IsActive))
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
            .Where(v => v.IsActive && v.Product.IsActive && v.Product.Category.IsActive)
            .OrderBy(v => v.Product.Category.SortOrder)
            .ThenBy(v => v.Product.Category.CategoryName)
            .ThenBy(v => v.Product.ProductName)
            .ThenBy(v => v.VariantName)
            .Select(v => new
            {
                v.VariantId,
                v.ProductId,
                v.Product.Sku,
                QtyPieces = v.StockBalance != null ? v.StockBalance.QtyPieces : 0,
                QtyCases = v.StockBalance != null ? v.StockBalance.QtyCases : 0,
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
