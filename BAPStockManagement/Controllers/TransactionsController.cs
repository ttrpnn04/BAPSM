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

        // ไม่ระบุวัน = ย้อนหลัง 30 วัน, ระบุแค่วันใดวันหนึ่ง = กรองเฉพาะวันนั้น
        DateOnly from;
        DateOnly to;
        if (fromDate.HasValue && toDate.HasValue)
        {
            from = fromDate.Value;
            to = toDate.Value;
        }
        else if (fromDate.HasValue)
        {
            from = to = fromDate.Value;
        }
        else if (toDate.HasValue)
        {
            from = to = toDate.Value;
        }
        else
        {
            from = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            to = DateOnly.FromDateTime(DateTime.Today);
        }

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

        var query = _context.StockDocuments
            .AsNoTracking()
            .Include(d => d.TransactionType)
            .Include(d => d.StockTransactions)
            .Where(d => d.TxnDate >= from && d.TxnDate <= to);

        if (transactionTypeId.HasValue)
        {
            query = query.Where(d => d.TransactionTypeId == transactionTypeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(d =>
                (d.RefNo != null && d.RefNo.Contains(term)) ||
                (d.Note != null && d.Note.Contains(term)) ||
                d.StockTransactions.Any(t =>
                    t.Variant.Product.Sku.Contains(term) ||
                    t.Variant.Product.ProductName.Contains(term) ||
                    t.Variant.VariantName.Contains(term)));
        }

        var totalItems = await query.CountAsync();
        var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling((double)totalItems / pageSize);
        if (page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(d => d.TxnDate)
            .ThenByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.DocumentId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new TransactionDocumentListItemViewModel
            {
                DocumentId = d.DocumentId,
                TxnDate = d.TxnDate,
                TypeName = d.TransactionType.TypeName,
                Direction = d.TransactionType.Direction,
                RefNo = d.RefNo,
                Note = d.Note,
                LineCount = d.StockTransactions.Count,
                TotalPieces = d.StockTransactions.Sum(t => t.QtyPieces),
                TotalCases = d.StockTransactions.Sum(t => t.QtyCases),
                CreatedBy = d.CreatedBy,
                CreatedAt = d.CreatedAt
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
            FromDate = fromDate,
            ToDate = toDate,
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
    public async Task<IActionResult> Details(long id)
    {
        var document = await _context.StockDocuments
            .AsNoTracking()
            .Include(d => d.TransactionType)
            .Include(d => d.StockTransactions)
                .ThenInclude(t => t.Variant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(d => d.DocumentId == id);

        if (document == null)
        {
            return NotFound();
        }

        var createdBy = document.CreatedBy;
        if (!string.IsNullOrWhiteSpace(createdBy))
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == createdBy);
            createdBy = user?.Email ?? user?.UserName ?? createdBy;
        }

        var model = new TransactionDocumentDetailsViewModel
        {
            DocumentId = document.DocumentId,
            TxnDate = document.TxnDate,
            TypeName = document.TransactionType.TypeName,
            Direction = document.TransactionType.Direction,
            RefNo = document.RefNo,
            Note = document.Note,
            CreatedBy = createdBy,
            CreatedAt = document.CreatedAt,
            Lines = document.StockTransactions
                .OrderBy(t => t.Variant.Product.Sku)
                .ThenBy(t => t.Variant.VariantName)
                .ThenBy(t => t.TransactionId)
                .Select(t => new TransactionDocumentLineViewModel
                {
                    TransactionId = t.TransactionId,
                    CategoryName = t.Variant.Product.Category.CategoryName,
                    Sku = t.Variant.Product.Sku,
                    ProductName = t.Variant.Product.ProductName,
                    VariantName = t.Variant.VariantName,
                    QtyPieces = t.QtyPieces,
                    QtyCases = t.QtyCases
                })
                .ToList()
        };

        return View(model);
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

        var currentStocks = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && v.Product.IsActive && v.Product.Category.IsActive)
            .Select(v => new StockExportRow
            {
                CategoryName = v.Product.Category.CategoryName,
                CategorySortOrder = v.Product.Category.SortOrder,
                ProductSortOrder = v.Product.SortOrder,
                Sku = v.Product.Sku,
                ProductName = v.Product.ProductName,
                Unit = v.Product.Unit,
                VariantName = v.VariantName,
                QtyPieces = v.StockBalance != null ? v.StockBalance.QtyPieces : 0,
                QtyCases = v.StockBalance != null ? v.StockBalance.QtyCases : 0
            })
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
        WriteStockMatrixSheet(stockSheet, currentStocks);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private sealed class StockExportRow
    {
        public string CategoryName { get; set; } = string.Empty;
        public int CategorySortOrder { get; set; }
        public int ProductSortOrder { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string VariantName { get; set; } = string.Empty;
        public int QtyPieces { get; set; }
        public int QtyCases { get; set; }
    }

    private static void WriteStockMatrixSheet(IXLWorksheet worksheet, IReadOnlyList<StockExportRow> stocks)
    {
        var colorColumns = ProductVariantDefaults.ColorNames.ToList();
        var caseColumn = 4 + colorColumns.Count;
        var totalColumn = caseColumn + 1;
        var lastColumn = totalColumn;

        // กลุ่มจักรยาน: รวมยอดก้อนเดียวจาก LION ถึง รถหัดเดิน-2BL-E
        var bikeCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "กลุ่มจักรบยาน-1(LION)",
            "กลุ่มจักรยาน-5-BL-V",
            "กลุ่มรถหัดเดิน-1",
            "กลุ่มรถหัดเดินไฟฟ้า-1-BL-L",
            "กลุ่มรถหัดเดิน-1-BL-L",
            "กลุ่มรถหัดเดิน-2BL-E"
        };

        worksheet.Cell(1, 4).Value = "สต็อก";
        worksheet.Range(1, 4, 1, caseColumn).Merge();
        worksheet.Range(1, 4, 1, caseColumn).Style.Font.Bold = true;
        worksheet.Range(1, 4, 1, caseColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Range(1, 4, 1, caseColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");

        worksheet.Cell(1, totalColumn).Value = "รวม";
        worksheet.Cell(1, totalColumn).Style.Font.Bold = true;
        worksheet.Cell(1, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Cell(1, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");

        worksheet.Cell(2, 2).Value = "รหัสสินค้า";
        worksheet.Cell(2, 2).Style.Font.Bold = true;
        worksheet.Cell(2, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
        worksheet.Cell(2, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

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

        worksheet.Cell(2, caseColumn).Value = ProductUnits.Case;
        worksheet.Cell(2, caseColumn).Style.Font.Bold = true;
        worksheet.Cell(2, caseColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
        worksheet.Cell(2, caseColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Cell(2, caseColumn).Style.Alignment.WrapText = true;

        worksheet.Cell(2, totalColumn).Value = "รวม";
        worksheet.Cell(2, totalColumn).Style.Font.Bold = true;
        worksheet.Cell(2, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
        worksheet.Cell(2, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var categories = stocks
            .GroupBy(s => new { s.CategoryName, s.CategorySortOrder })
            .OrderBy(g => g.Key.CategorySortOrder)
            .ThenBy(g => g.Key.CategoryName)
            .Select(g => new
            {
                g.Key.CategoryName,
                Products = g
                    .GroupBy(s => new { s.ProductSortOrder, s.Sku, s.ProductName, s.Unit })
                    .OrderBy(p => p.Key.ProductSortOrder == 0 ? int.MaxValue : p.Key.ProductSortOrder)
                    .ThenBy(p => p.Key.Sku)
                    .ThenBy(p => p.Key.ProductName)
                    .ToList()
            })
            .ToList();

        var row = 2;
        var bikeColorTotals = colorColumns.ToDictionary(c => c, _ => 0);
        var bikeCaseTotal = 0;
        var bikeGrandTotal = 0;
        var hasBikeRows = false;

        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var category = categories[categoryIndex];
            var isBikeCategory = bikeCategoryNames.Contains(category.CategoryName);
            var nextIsBike = categoryIndex + 1 < categories.Count &&
                bikeCategoryNames.Contains(categories[categoryIndex + 1].CategoryName);

            if (row == 2)
            {
                worksheet.Cell(2, 3).Value = category.CategoryName;
                worksheet.Cell(2, 3).Style.Font.Bold = true;
                worksheet.Cell(2, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
            }
            else
            {
                row++;
                worksheet.Cell(row, 2).Value = "รหัสสินค้า";
                worksheet.Cell(row, 3).Value = category.CategoryName;
                worksheet.Range(row, 2, row, 3).Style.Font.Bold = true;
                worksheet.Range(row, 2, row, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
            }

            var categoryColorTotals = colorColumns.ToDictionary(c => c, _ => 0);
            var categoryCaseTotal = 0;
            var categoryGrandTotal = 0;
            var indexInCategory = 0;

            foreach (var productStock in category.Products)
            {
                row++;
                indexInCategory++;

                var variants = productStock.ToList();
                var isColorProduct = variants.Any(v =>
                    !string.Equals(v.VariantName, ProductVariantDefaults.StandardVariantName, StringComparison.OrdinalIgnoreCase));
                var totalPieces = variants.Sum(v => v.QtyPieces);
                var totalCases = variants.Sum(v => v.QtyCases);
                var isEmpty = totalPieces == 0 && totalCases == 0;
                var totalQty = isColorProduct
                    ? totalPieces
                    : (ProductUnits.IsCaseUnit(productStock.Key.Unit) ? totalCases : totalPieces);

                worksheet.Cell(row, 1).Value = indexInCategory;
                worksheet.Cell(row, 2).Value = productStock.Key.Sku;
                worksheet.Cell(row, 3).Value = productStock.Key.ProductName;

                if (isColorProduct)
                {
                    var byColor = variants
                        .GroupBy(v => ProductVariantDefaults.NormalizeVariantName(v.VariantName))
                        .Where(g => !string.IsNullOrWhiteSpace(g.Key) &&
                                    !g.Key.Equals(ProductVariantDefaults.StandardVariantName, StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyPieces), StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < colorColumns.Count; i++)
                    {
                        var column = 4 + i;
                        var colorName = colorColumns[i];
                        var qty = byColor.TryGetValue(colorName, out var value) ? value : 0;
                        if (qty > 0)
                        {
                            worksheet.Cell(row, column).Value = qty;
                        }

                        worksheet.Cell(row, column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        categoryColorTotals[colorName] += qty;
                        if (isBikeCategory)
                        {
                            bikeColorTotals[colorName] += qty;
                        }
                    }

                    if (totalCases > 0)
                    {
                        worksheet.Cell(row, caseColumn).Value = totalCases;
                    }

                    categoryCaseTotal += totalCases;
                    if (isBikeCategory)
                    {
                        bikeCaseTotal += totalCases;
                    }
                }
                else
                {
                    if (totalQty > 0)
                    {
                        worksheet.Cell(row, caseColumn).Value = totalQty;
                    }

                    categoryCaseTotal += totalQty;
                    if (isBikeCategory)
                    {
                        bikeCaseTotal += totalQty;
                    }
                }

                worksheet.Cell(row, caseColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                worksheet.Cell(row, totalColumn).Value = totalQty;
                worksheet.Cell(row, totalColumn).Style.Font.Bold = true;
                worksheet.Cell(row, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Cell(row, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");

                categoryGrandTotal += totalQty;
                if (isBikeCategory)
                {
                    bikeGrandTotal += totalQty;
                    hasBikeRows = true;
                }

                if (isEmpty)
                {
                    worksheet.Range(row, 1, row, lastColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
                    worksheet.Cell(row, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
                }
            }

            if (isBikeCategory)
            {
                if (!nextIsBike && hasBikeRows)
                {
                    row = WriteStockTotalRow(
                        worksheet,
                        row,
                        "รวมจักรยาน (LION - รถหัดเดิน)",
                        colorColumns,
                        bikeColorTotals,
                        bikeCaseTotal,
                        bikeGrandTotal,
                        caseColumn,
                        totalColumn,
                        lastColumn);
                }
            }
            else
            {
                row = WriteStockTotalRow(
                    worksheet,
                    row,
                    $"รวม{category.CategoryName}",
                    colorColumns,
                    categoryColorTotals,
                    categoryCaseTotal,
                    categoryGrandTotal,
                    caseColumn,
                    totalColumn,
                    lastColumn);
            }
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(row, 2), lastColumn);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        worksheet.Column(1).Width = 5;
        worksheet.Column(2).Width = 16;
        worksheet.Column(3).Width = 42;
        for (var column = 4; column <= lastColumn; column++)
        {
            worksheet.Column(column).Width = column == totalColumn ? 8 : (column == caseColumn ? 14 : 7);
        }

        worksheet.Row(2).Height = 44;
        worksheet.SheetView.FreezeRows(2);
        worksheet.SheetView.FreezeColumns(3);
    }

    private static int WriteStockTotalRow(
        IXLWorksheet worksheet,
        int currentRow,
        string label,
        IReadOnlyList<string> colorColumns,
        IReadOnlyDictionary<string, int> colorTotals,
        int caseTotal,
        int grandTotal,
        int caseColumn,
        int totalColumn,
        int lastColumn)
    {
        var row = currentRow + 1;
        worksheet.Cell(row, 3).Value = label;
        worksheet.Range(row, 1, row, 3).Merge();
        worksheet.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        worksheet.Range(row, 1, row, lastColumn).Style.Font.Bold = true;
        worksheet.Range(row, 1, row, lastColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

        for (var i = 0; i < colorColumns.Count; i++)
        {
            var column = 4 + i;
            var qty = colorTotals[colorColumns[i]];
            if (qty > 0)
            {
                worksheet.Cell(row, column).Value = qty;
            }

            worksheet.Cell(row, column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        if (caseTotal > 0)
        {
            worksheet.Cell(row, caseColumn).Value = caseTotal;
        }

        worksheet.Cell(row, caseColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Cell(row, totalColumn).Value = grandTotal;
        worksheet.Cell(row, totalColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Cell(row, totalColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#92D050");
        return row;
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
            .Select(t => ProductVariantDefaults.NormalizeVariantName(t.Variant.VariantName))
            .Where(name =>
                !string.IsNullOrWhiteSpace(name) &&
                !name.Equals(ProductVariantDefaults.StandardVariantName, StringComparison.OrdinalIgnoreCase) &&
                !standardColors.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
                .GroupBy(t => ProductVariantDefaults.NormalizeVariantName(t.Variant.VariantName))
                .ToDictionary(g => g.Key, g => g.Sum(t => t.QtyPieces), StringComparer.OrdinalIgnoreCase);
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
                .Where(t => ProductVariantDefaults.NormalizeVariantName(t.Variant.VariantName)
                    .Equals(colorColumns[i], StringComparison.OrdinalIgnoreCase))
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
        if (name.Contains("เขียว")) return XLColor.FromHtml("#92D050");
        if (name.Contains("ชมพู") || name.Contains("ชม")) return XLColor.FromHtml("#FF99FF");
        if (name.Contains("เหลือง") || name.Contains("ทอง") || name.Contains("ครีม")) return XLColor.FromHtml("#FFFF00");
        if (name.Contains("ม่วง")) return XLColor.FromHtml("#E4C1E0");
        if (name.Contains("น้ำตาล") || name.Contains("ตาล")) return XLColor.FromHtml("#FFC000");
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

        await PopulateCreateLookupsAsync(selectedVariantId, direction);

        var selectedTransactionTypeId = 0;
        if (direction.HasValue)
        {
            var selectedTypeQuery = _context.TransactionTypes
                .AsNoTracking()
                .Where(t => t.IsActive && t.Direction == direction.Value);

            if (direction.Value > 0)
            {
                selectedTransactionTypeId = await selectedTypeQuery
                    .OrderBy(t => t.TransactionTypeId)
                    .Select(t => t.TransactionTypeId)
                    .FirstOrDefaultAsync();
            }
            else
            {
                selectedTransactionTypeId = await selectedTypeQuery
                    .OrderBy(t => t.TransactionTypeId)
                    .Select(t => t.TransactionTypeId)
                    .FirstOrDefaultAsync();
            }
        }

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
            ModelState.AddModelError(string.Empty, "กรุณาระบุจำนวนชิ้นหรือกระสอบ/ลัง/เส้นอย่างน้อย 1 รายการ");
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
            .OrderBy(id => id)
            .ToList();

        await using var dbTx = await _context.Database.BeginTransactionAsync();
        try
        {
            var variants = new List<ProductVariant>();
            foreach (var variantId in variantIds)
            {
                // Lock balance rows in ascending VariantId order to avoid deadlocks.
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE StockBalances WITH (UPDLOCK, ROWLOCK)
                    SET LastUpdated = LastUpdated
                    WHERE VariantID = {variantId}");

                var locked = await _context.ProductVariants
                    .Include(v => v.StockBalance)
                    .Include(v => v.Product)
                        .ThenInclude(p => p.Category)
                    .FirstOrDefaultAsync(v =>
                        v.VariantId == variantId &&
                        v.IsActive &&
                        v.Product.IsActive &&
                        v.Product.Category.IsActive);

                if (locked == null)
                {
                    await dbTx.RollbackAsync();
                    if (isAjax) return BadRequest(new { message = "ไม่พบสินค้าที่เลือก" });
                    ModelState.AddModelError(nameof(model.VariantId), "ไม่พบสินค้าที่เลือก");
                    return View(model);
                }

                variants.Add(locked);
            }

            var variantMap = variants.ToDictionary(v => v.VariantId);

            if (!hasLineItems && model.ProductId.HasValue && variantMap[model.VariantId].ProductId != model.ProductId.Value)
            {
                await dbTx.RollbackAsync();
                if (isAjax) return BadRequest(new { message = "สี/รุ่นไม่ตรงกับสินค้าที่เลือก" });
                ModelState.AddModelError(nameof(model.VariantId), "สี/รุ่นไม่ตรงกับสินค้าที่เลือก");
                return View(model);
            }

            var txnType = await _context.TransactionTypes
                .FirstOrDefaultAsync(t => t.TransactionTypeId == model.TransactionTypeId && t.IsActive);

            if (txnType == null)
            {
                await dbTx.RollbackAsync();
                if (isAjax) return BadRequest(new { message = "ประเภทรายการไม่ถูกต้อง" });
                ModelState.AddModelError(nameof(model.TransactionTypeId), "ประเภทรายการไม่ถูกต้อง");
                return View(model);
            }

            if (txnType.Direction < 0)
            {
                foreach (var group in submittedLines.GroupBy(l => l.VariantId))
                {
                    var variant = variantMap[group.Key];
                    var currentPieces = variant.StockBalance?.QtyPieces ?? 0;
                    var currentCases = variant.StockBalance?.QtyCases ?? 0;
                    var requestedPieces = group.Sum(l => l.QtyPieces);
                    var requestedCases = group.Sum(l => l.QtyCases);

                    if (requestedPieces > currentPieces || requestedCases > currentCases)
                    {
                        var msg = $"สต็อกไม่เพียงพอ: {variant.VariantName} (คงเหลือ {currentPieces:N0} ชิ้น, {currentCases:N0} กระสอบ/ลัง/เส้น)";
                        await dbTx.RollbackAsync();
                        if (isAjax) return BadRequest(new { message = msg });
                        ModelState.AddModelError(string.Empty, msg);
                        return View(model);
                    }
                }
            }

            var refNo = string.IsNullOrWhiteSpace(model.RefNo) ? null : model.RefNo.Trim();
            var note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
            var userId = _userManager.GetUserId(User);

            var document = new StockDocument
            {
                TransactionTypeId = model.TransactionTypeId,
                TxnDate = model.TxnDate,
                RefNo = refNo,
                Note = note,
                CreatedBy = userId
            };
            _context.StockDocuments.Add(document);
            await _context.SaveChangesAsync();

            var transactions = submittedLines.Select(line => new StockTransaction
                {
                    DocumentId = document.DocumentId,
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
            await dbTx.CommitAsync();

            if (isAjax) return Ok(new { success = true, count = transactions.Count, documentId = document.DocumentId });
            TempData["Success"] = $"บันทึกบิลสำเร็จ {transactions.Count:N0} รายการ";
            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await dbTx.RollbackAsync();
            throw;
        }
    }

    private async Task<int?> EnsureDefaultVariantForProductAsync(int productId)
    {
        var existingVariant = await _context.ProductVariants
            .Where(v =>
                v.ProductId == productId &&
                v.IsActive &&
                v.Product.IsActive &&
                v.Product.Category.IsActive)
            .OrderBy(v => v.VariantName == ProductVariantDefaults.StandardVariantName ? 0 : 1)
            .ThenByDescending(v => (v.StockBalance != null ? v.StockBalance.QtyPieces : 0) +
                                   (v.StockBalance != null ? v.StockBalance.QtyCases : 0))
            .ThenBy(v => v.SortOrder)
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
            .Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder)
            .Select(v => (int?)v.VariantId)
            .FirstOrDefault();
    }

    private async Task PopulateCreateLookupsAsync(int? selectedVariantId = null, short? modalDirection = null)
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
                v.Product.Unit,
                QtyPieces = v.StockBalance != null ? v.StockBalance.QtyPieces : 0,
                QtyCases = v.StockBalance != null ? v.StockBalance.QtyCases : 0,
                Label = v.VariantName
            })
            .ToListAsync();

        ViewBag.Variants = variants;
        ViewBag.SelectedVariantId = selectedVariantId;

        var typesQuery = _context.TransactionTypes
            .Where(t => t.IsActive)
            .AsQueryable();

        if (modalDirection.HasValue)
        {
            typesQuery = typesQuery.Where(t => t.Direction == modalDirection.Value);
        }

        var types = await typesQuery
            .OrderBy(t => t.TransactionTypeId)
            .ToListAsync();

        ViewBag.TransactionTypes = new SelectList(types, "TransactionTypeId", "TypeName");
        ViewBag.TransactionTypesList = types;
    }
}
