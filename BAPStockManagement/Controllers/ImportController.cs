using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BAPStockManagement.Constants;
using BAPStockManagement.Data;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Import;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.AdminManage)]
public class ImportController : Controller
{
    private const long MaxExcelFileBytes = 10 * 1024 * 1024; // 10 MB
    private const string StandardVariantName = ProductVariantDefaults.StandardVariantName;

    private static readonly HashSet<string> CaseQuantityCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ยางในจักรยาน-COLUN",
            "ยางในมอเตอร์ไซค์ BLUE"
        };

    private readonly BAPStockContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        BAPStockContext context,
        UserManager<IdentityUser> userManager,
        ILogger<ImportController> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new ExcelImportViewModel
        {
            IncludeZeroStockProducts = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExcelFileBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxExcelFileBytes)]
    public async Task<IActionResult> Index(ExcelImportViewModel model)
    {
        if (model.ExcelFile == null || model.ExcelFile.Length == 0)
        {
            ModelState.AddModelError(nameof(model.ExcelFile), "กรุณาเลือกไฟล์ Excel");
            return View(model);
        }

        if (model.ExcelFile.Length > MaxExcelFileBytes)
        {
            ModelState.AddModelError(nameof(model.ExcelFile), "ขนาดไฟล์ต้องไม่เกิน 10 MB");
            return View(model);
        }

        if (!Path.GetExtension(model.ExcelFile.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.ExcelFile), "รองรับเฉพาะไฟล์ .xlsx");
            return View(model);
        }

        var safeFileName = Path.GetFileName(model.ExcelFile.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = "import.xlsx";
        }

        try
        {
            await using var memory = new MemoryStream();
            await model.ExcelFile.CopyToAsync(memory);
            var fileBytes = memory.ToArray();
            var importRef = BuildImportRef(fileBytes);

            var parsed = ParseWorkbook(fileBytes, safeFileName);
            if (parsed.Rows.Count == 0)
            {
                model.HasResult = true;
                model.IsSuccess = false;
                model.ResultMessage = "ไม่พบข้อมูลสินค้าที่พร้อมนำเข้า";
                return View(model);
            }

            // รวมสีชื่อซ้ำก่อนนำเข้า เพื่อไม่ให้เหลือ 17 สี
            await ProductVariantSeeder.NormalizeVariantAliasesAsync(_context, _logger);

            // ไฟล์นี้เคยนำเข้าบิลแล้วหรือยัง — ถ้าเคยแล้วจะ sync ยอดล่าสุดอย่างเดียว ไม่ใส่บิลซ้ำ
            var alreadyImportedThisFile = await _context.StockTransactions
                .AnyAsync(t => t.RefNo != null &&
                    (t.RefNo == importRef ||
                     t.RefNo.StartsWith(importRef + "-BASE") ||
                     t.RefNo.StartsWith(importRef + "-B") ||
                     t.RefNo.StartsWith(importRef + "-SET")));
            var importBillHistory = !alreadyImportedThisFile;

            var receiveTypeId = await GetOrCreateReceiveTypeAsync();
            var issueTypeId = await GetOrCreateIssueTypeAsync();
            var userId = _userManager.GetUserId(User);
            var categoryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var createdProducts = 0;
            var importedProducts = 0;
            var createdVariants = 0;
            var importedTransactions = 0;
            var importedIssueTransactions = 0;
            var adjustmentTransactions = 0;
            var totalPieces = 0;
            var totalCases = 0;
            var skippedNoStock = 0;
            var importedVariants = new List<ImportedVariant>();
            var syncStamp = DateTime.Now.ToString("yyyyMMddHHmmss");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var row in parsed.Rows)
                {
                    var rowHasQuantity = row.Variants.Any(v =>
                        v.QtyPieces > 0 || v.QtyCases > 0 || v.Issues.Count > 0);
                    if (!model.IncludeZeroStockProducts && !rowHasQuantity)
                    {
                        var existingProduct = await _context.Products.AnyAsync(p =>
                            p.Sku == row.Sku && p.ProductName == row.ProductName);
                        if (!existingProduct)
                        {
                            skippedNoStock++;
                            continue;
                        }
                    }

                    if (!categoryCache.TryGetValue(row.CategoryName, out var categoryId))
                    {
                        categoryId = await GetOrCreateCategoryAsync(
                            row.CategoryName,
                            UsesStandardVariant(row.CategoryName) ? "หน่วย" : "สี");
                        categoryCache[row.CategoryName] = categoryId;
                    }

                    var (product, createdProduct) = await GetOrCreateProductAsync(
                        categoryId,
                        row.Sku,
                        row.ProductName,
                        row.Unit);
                    importedProducts++;
                    if (createdProduct)
                    {
                        createdProducts++;
                    }

                    var rowTargetPieces = row.Variants.Sum(v => v.QtyPieces);
                    var rowTargetCases = row.Variants.Sum(v => v.QtyCases);
                    totalPieces += rowTargetPieces;
                    totalCases += rowTargetCases;

                    foreach (var variant in row.Variants)
                    {
                        var (variantEntity, createdVariant) = await GetOrCreateVariantAsync(product, variant.Name);
                        if (createdVariant)
                        {
                            createdVariants++;
                        }

                        importedVariants.Add(new ImportedVariant(variantEntity, variant));

                        var currentPieces = variantEntity.StockBalance?.QtyPieces ?? 0;
                        var currentCases = variantEntity.StockBalance?.QtyCases ?? 0;

                        if (importBillHistory)
                        {
                            // รอบแรกของไฟล์นี้: ตั้งยอดเปิดแล้วหักบิล ให้เหลือตรงคอลัมน์สุดท้าย
                            var requiredOpeningPieces = variant.QtyPieces + variant.Issues.Sum(i => i.QtyPieces);
                            var requiredOpeningCases = variant.QtyCases + variant.Issues.Sum(i => i.QtyCases);

                            adjustmentTransactions += AddAdjustmentTransactions(
                                variantEntity.VariantId,
                                requiredOpeningPieces - currentPieces,
                                requiredOpeningCases - currentCases,
                                receiveTypeId,
                                issueTypeId,
                                parsed.OpeningDate,
                                $"{importRef}-BASE",
                                $"Excel opening balance ({Truncate(safeFileName, 60)})",
                                userId);
                        }
                        else
                        {
                            // นำเข้าซ้ำ: ตั้งยอดให้เท่าคอลัมน์สต็อกล่าสุดโดยตรง ไม่บวกเพิ่ม ไม่ใส่บิลซ้ำ
                            adjustmentTransactions += AddAdjustmentTransactions(
                                variantEntity.VariantId,
                                variant.QtyPieces - currentPieces,
                                variant.QtyCases - currentCases,
                                receiveTypeId,
                                issueTypeId,
                                parsed.SnapshotDate,
                                $"{importRef}-SET-{syncStamp}",
                                $"Excel stock sync ({Truncate(safeFileName, 60)}): set to latest column",
                                userId);
                        }
                    }
                }

                await _context.SaveChangesAsync();

                if (importBillHistory)
                {
                    foreach (var imported in importedVariants)
                    {
                        foreach (var issue in imported.Parsed.Issues)
                        {
                            _context.StockTransactions.Add(new StockTransaction
                            {
                                VariantId = imported.Entity.VariantId,
                                TransactionTypeId = issueTypeId,
                                TxnDate = issue.Date,
                                QtyPieces = issue.QtyPieces,
                                QtyCases = issue.QtyCases,
                                RefNo = $"{importRef}-B{issue.Date.Day:00}",
                                Note = $"Excel บิล{issue.Date.Day} ({Truncate(safeFileName, 60)})",
                                CreatedBy = userId
                            });
                            importedIssueTransactions++;
                        }
                    }

                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();
                importedTransactions = adjustmentTransactions + importedIssueTransactions;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            model.HasResult = true;
            model.IsSuccess = true;
            model.ResultMessage = importBillHistory
                ? "นำเข้าข้อมูลสำเร็จ (ตั้งยอดตามไฟล์ล่าสุด + ประวัติบิล)"
                : "ซิงก์สต็อกสำเร็จ (ตั้งยอดให้ตรงคอลัมน์สุดท้ายของ Excel โดยไม่ใส่บิลซ้ำ)";
            model.ImportRef = importRef;
            model.ParsedRows = parsed.Rows.Count;
            model.ImportedProducts = importedProducts;
            model.CreatedProducts = createdProducts;
            model.CreatedVariants = createdVariants;
            model.ImportedTransactions = importedTransactions;
            model.ImportedIssueTransactions = importedIssueTransactions;
            model.AdjustmentTransactions = adjustmentTransactions;
            model.TotalPieces = totalPieces;
            model.TotalCases = totalCases;
            model.SkippedNoStockProducts = skippedNoStock;
            model.ColorHeaders = parsed.ColorHeaders;
            model.BillDays = importBillHistory ? parsed.BillDays : 0;
            model.SnapshotDate = parsed.SnapshotDate;
            model.Warnings = parsed.Warnings;
            if (!importBillHistory)
            {
                model.Warnings = parsed.Warnings
                    .Append("ไฟล์นี้เคยนำเข้าแล้ว — รอบนี้ปรับเฉพาะยอดสต็อกล่าสุด ไม่เพิ่มประวัติบิลซ้ำ")
                    .ToList();
            }
            model.CategoryNames = parsed.Rows
                .Select(r => r.CategoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            model.HasResult = true;
            model.IsSuccess = false;
            model.ResultMessage = $"นำเข้าไม่สำเร็จ: {ex.Message}";
            return View(model);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Excel import failed while saving for file {FileName}", safeFileName);
            model.HasResult = true;
            model.IsSuccess = false;
            model.ResultMessage = "นำเข้าไม่สำเร็จขณะบันทึกข้อมูล กรุณาลองใหม่อีกครั้ง";
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel import failed for file {FileName}", safeFileName);
            model.HasResult = true;
            model.IsSuccess = false;
            model.ResultMessage = "นำเข้าไม่สำเร็จ กรุณาตรวจสอบรูปแบบไฟล์แล้วลองใหม่";
            return View(model);
        }
    }

    private async Task<int> GetOrCreateCategoryAsync(string categoryName, string variantLabel)
    {
        var name = NormalizeCategoryName(categoryName);
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.CategoryName == name);

        if (category != null)
        {
            category.VariantLabel = variantLabel;
            if (!category.IsActive)
            {
                category.IsActive = true;
            }

            await _context.SaveChangesAsync();
            return category.CategoryId;
        }

        var newCategory = new Category
        {
            CategoryName = name,
            VariantLabel = variantLabel,
            SortOrder = 999,
            IsActive = true
        };
        _context.Categories.Add(newCategory);
        await _context.SaveChangesAsync();
        return newCategory.CategoryId;
    }

    private async Task<int> GetOrCreateReceiveTypeAsync()
    {
        var existing = await _context.TransactionTypes
            .Where(t => t.Direction == 1 && t.IsActive)
            .OrderBy(t => t.TransactionTypeId)
            .FirstOrDefaultAsync();
        if (existing != null)
        {
            return existing.TransactionTypeId;
        }

        var created = new TransactionType
        {
            TypeName = "รับเข้า",
            Direction = 1,
            IsActive = true
        };
        _context.TransactionTypes.Add(created);
        await _context.SaveChangesAsync();
        return created.TransactionTypeId;
    }

    private async Task<int> GetOrCreateIssueTypeAsync()
    {
        var existing = await _context.TransactionTypes
            .Where(t => t.Direction < 0 && t.IsActive)
            .OrderBy(t => t.TransactionTypeId)
            .FirstOrDefaultAsync();
        if (existing != null)
        {
            return existing.TransactionTypeId;
        }

        var created = new TransactionType
        {
            TypeName = "ปรับยอดลง",
            Direction = -1,
            IsActive = true
        };
        _context.TransactionTypes.Add(created);
        await _context.SaveChangesAsync();
        return created.TransactionTypeId;
    }

    private async Task<(Product Product, bool Created)> GetOrCreateProductAsync(
        int categoryId,
        string sku,
        string productName,
        string unit)
    {
        var normalizedSku = Truncate(sku.Trim(), 50);
        var normalizedName = Truncate(productName.Trim(), 300);

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Sku == normalizedSku && p.ProductName == normalizedName);
        if (product != null)
        {
            product.CategoryId = categoryId;
            product.Unit = ProductUnits.Normalize(unit);
            product.IsActive = true;
            product.UpdatedAt = DateTime.Now;
            return (product, false);
        }

        var created = new Product
        {
            CategoryId = categoryId,
            Sku = normalizedSku,
            ProductName = normalizedName,
            Unit = ProductUnits.Normalize(unit),
            IsActive = true,
            Note = "Imported from Excel"
        };
        _context.Products.Add(created);
        await _context.SaveChangesAsync();
        return (created, true);
    }

    private async Task<(ProductVariant Variant, bool Created)> GetOrCreateVariantAsync(Product product, string variantName)
    {
        var normalizedVariant = Truncate(ProductVariantDefaults.NormalizeVariantName(variantName), 100);
        var variant = await _context.ProductVariants
            .Include(v => v.StockBalance)
            .FirstOrDefaultAsync(v => v.ProductId == product.ProductId && v.VariantName == normalizedVariant);

        if (variant != null)
        {
            variant.IsActive = true;
            if (variant.StockBalance == null)
            {
                variant.StockBalance = new StockBalance
                {
                    QtyPieces = 0,
                    QtyCases = 0
                };
            }

            return (variant, false);
        }

        var maxSortOrder = await _context.ProductVariants
            .Where(v => v.ProductId == product.ProductId)
            .Select(v => (int?)v.SortOrder)
            .MaxAsync() ?? 0;

        var created = new ProductVariant
        {
            ProductId = product.ProductId,
            Product = product,
            VariantName = normalizedVariant,
            SortOrder = maxSortOrder + 1,
            IsActive = true,
            StockBalance = new StockBalance
            {
                QtyPieces = 0,
                QtyCases = 0
            }
        };
        _context.ProductVariants.Add(created);
        await _context.SaveChangesAsync();
        return (created, true);
    }

    private int AddAdjustmentTransactions(
        int variantId,
        int deltaPieces,
        int deltaCases,
        int receiveTypeId,
        int issueTypeId,
        DateOnly date,
        string refNo,
        string note,
        string? userId)
    {
        var count = 0;
        var receivePieces = Math.Max(0, deltaPieces);
        var receiveCases = Math.Max(0, deltaCases);
        if (receivePieces > 0 || receiveCases > 0)
        {
            _context.StockTransactions.Add(new StockTransaction
            {
                VariantId = variantId,
                TransactionTypeId = receiveTypeId,
                TxnDate = date,
                QtyPieces = receivePieces,
                QtyCases = receiveCases,
                RefNo = refNo,
                Note = note,
                CreatedBy = userId
            });
            count++;
        }

        var issuePieces = Math.Max(0, -deltaPieces);
        var issueCases = Math.Max(0, -deltaCases);
        if (issuePieces > 0 || issueCases > 0)
        {
            _context.StockTransactions.Add(new StockTransaction
            {
                VariantId = variantId,
                TransactionTypeId = issueTypeId,
                TxnDate = date,
                QtyPieces = issuePieces,
                QtyCases = issueCases,
                RefNo = refNo,
                Note = note,
                CreatedBy = userId
            });
            count++;
        }

        return count;
    }

    private static ParsedWorkbook ParseWorkbook(
        byte[] fileBytes,
        string fileName)
    {
        using var workbook = new XLWorkbook(new MemoryStream(fileBytes));
        var sheet = workbook.Worksheets.First();

        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastColumn == 0 || lastRow < 3)
        {
            throw new InvalidOperationException("ไฟล์ไม่มีข้อมูลที่อ่านได้");
        }

        var stockBlocks = FindQuantityBlocks(sheet, lastColumn, "สต็อก");
        var billBlocks = FindQuantityBlocks(sheet, lastColumn, "บิล");
        if (stockBlocks.Count == 0)
        {
            throw new InvalidOperationException("ไม่พบโครงคอลัมน์สต็อกในไฟล์ Excel");
        }

        if (billBlocks.Count == 0)
        {
            throw new InvalidOperationException("ไม่พบคอลัมน์บิลรายวันในไฟล์ Excel");
        }

        var latestStock = stockBlocks[^1];
        if (latestStock.Columns.Count < 2 ||
            !latestStock.Columns[^1].Name.Equals("ลัง", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "คอลัมน์สต็อกล่าสุดต้องลงท้ายด้วยคอลัมน์ 'ลัง'");
        }

        foreach (var bill in billBlocks)
        {
            if (bill.Columns.Count != latestStock.Columns.Count)
            {
                throw new InvalidOperationException(
                    $"โครงคอลัมน์ {bill.Title} ไม่ตรงกับคอลัมน์สต็อกล่าสุด");
            }
        }

        var (year, month) = ResolveImportPeriod(fileName);
        if (billBlocks.Count > DateTime.DaysInMonth(year, month))
        {
            throw new InvalidOperationException("จำนวนคอลัมน์บิลมากกว่าจำนวนวันในเดือนของไฟล์");
        }

        var openingDate = new DateOnly(year, month, 1).AddDays(-1);
        var snapshotDate = new DateOnly(year, month, billBlocks.Count);
        var warnings = new List<string>();

        for (var index = 0; index < billBlocks.Count; index++)
        {
            var expectedTitle = $"บิล{index + 1}";
            if (!billBlocks[index].Title.Equals(expectedTitle, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"หัวคอลัมน์ลำดับที่ {index + 1} เขียนว่า '{billBlocks[index].Title}' " +
                    $"ระบบยึดตำแหน่งเป็นวันที่ {index + 1}");
            }
        }

        var colorColumns = latestStock.Columns.Take(latestStock.Columns.Count - 1).ToList();
        var caseColumnIndex = latestStock.Columns.Count - 1;
        var currentCategory = NormalizeCategoryName(sheet.Cell(2, 3).GetString());
        if (string.IsNullOrWhiteSpace(currentCategory))
        {
            throw new InvalidOperationException("ไม่พบชื่อหมวดหมู่เริ่มต้นที่เซลล์ C2");
        }

        var rows = new List<ParsedRow>();
        for (var row = 3; row <= lastRow; row++)
        {
            var sku = sheet.Cell(row, 2).GetString().Trim();
            var productName = sheet.Cell(row, 3).GetString().Trim();

            if (!string.IsNullOrWhiteSpace(productName) &&
                (string.IsNullOrWhiteSpace(sku) ||
                 sku.Equals("รหัสสินค้า", StringComparison.OrdinalIgnoreCase)))
            {
                currentCategory = NormalizeCategoryName(productName);
                continue;
            }

            if (string.IsNullOrWhiteSpace(sku) ||
                sku.Equals("รหัสสินค้า", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(productName))
            {
                continue;
            }

            var isCaseQuantity = UsesCaseQuantity(currentCategory);
            var parsedVariants = new List<ParsedVariant>();

            if (isCaseQuantity)
            {
                var packSize = ParsePackSize(productName);
                var targetCases = ParseIntegerCell(
                    sheet.Cell(row, latestStock.Columns[caseColumnIndex].ColumnIndex));
                var issues = new List<ParsedIssue>();

                for (var dayIndex = 0; dayIndex < billBlocks.Count; dayIndex++)
                {
                    var rawIssue = ParseIntegerCell(
                        sheet.Cell(row, billBlocks[dayIndex].Columns[caseColumnIndex].ColumnIndex));
                    if (rawIssue == 0)
                    {
                        continue;
                    }

                    if (rawIssue % packSize != 0)
                    {
                        throw new InvalidOperationException(
                            $"แถว {row} ({sku}) บิล{dayIndex + 1} จำนวน {rawIssue} " +
                            $"หารขนาดลัง {packSize} ไม่ลงตัว");
                    }

                    issues.Add(new ParsedIssue(
                        new DateOnly(year, month, dayIndex + 1),
                        0,
                        rawIssue / packSize));
                }

                var unexpectedColorQty = colorColumns.Sum(c =>
                    ParseIntegerCell(sheet.Cell(row, c.ColumnIndex)));
                if (unexpectedColorQty > 0)
                {
                    throw new InvalidOperationException(
                        $"แถว {row} ({sku}) เป็นสินค้านับกระสอบ/ลัง แต่พบยอดในคอลัมน์สี");
                }

                parsedVariants.Add(new ParsedVariant(
                    StandardVariantName,
                    0,
                    targetCases,
                    issues));
            }
            else
            {
                for (var colorIndex = 0; colorIndex < colorColumns.Count; colorIndex++)
                {
                    var targetPieces = ParseIntegerCell(
                        sheet.Cell(row, colorColumns[colorIndex].ColumnIndex));
                    var issues = ReadPieceIssues(
                        sheet,
                        row,
                        billBlocks,
                        colorIndex,
                        year,
                        month);

                    // ทุกสีใน snapshot ต้องถูกประมวลผล รวมถึงค่า 0 เพื่อใช้ลดของเดิมเป็น 0
                    parsedVariants.Add(new ParsedVariant(
                        NormalizeVariantName(colorColumns[colorIndex].Name),
                        targetPieces,
                        0,
                        issues));
                }

                var standardTargetPieces = ParseIntegerCell(
                    sheet.Cell(row, latestStock.Columns[caseColumnIndex].ColumnIndex));
                var standardIssues = ReadPieceIssues(
                    sheet,
                    row,
                    billBlocks,
                    caseColumnIndex,
                    year,
                    month);
                if (standardTargetPieces > 0 || standardIssues.Count > 0 ||
                    UsesStandardVariant(currentCategory))
                {
                    parsedVariants.Add(new ParsedVariant(
                        StandardVariantName,
                        standardTargetPieces,
                        0,
                        standardIssues));
                }
            }

            rows.Add(new ParsedRow(
                currentCategory,
                Truncate(sku, 50),
                Truncate(productName, 300),
                isCaseQuantity ? ProductUnits.Case : ProductUnits.Piece,
                parsedVariants));
        }

        return new ParsedWorkbook(
            colorColumns.Select(c => NormalizeVariantName(c.Name)).ToList(),
            rows,
            openingDate,
            snapshotDate,
            billBlocks.Count,
            warnings);
    }

    private static List<QuantityBlock> FindQuantityBlocks(
        IXLWorksheet sheet,
        int lastColumn,
        string titleToken)
    {
        var blocks = new List<QuantityBlock>();
        for (var col = 1; col <= lastColumn; col++)
        {
            var title = sheet.Cell(1, col).GetString().Trim();
            var firstHeader = sheet.Cell(2, col).GetString().Trim();
            if (!title.Contains(titleToken, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(firstHeader))
            {
                continue;
            }

            var columns = new List<QuantityColumn>();
            for (var quantityCol = col; quantityCol <= lastColumn; quantityCol++)
            {
                var header = sheet.Cell(2, quantityCol).GetString().Trim();
                if (string.IsNullOrWhiteSpace(header))
                {
                    break;
                }

                columns.Add(new QuantityColumn(quantityCol, header));
            }

            if (columns.Count >= 2)
            {
                blocks.Add(new QuantityBlock(title, columns));
            }
        }

        return blocks;
    }

    private static List<ParsedIssue> ReadPieceIssues(
        IXLWorksheet sheet,
        int row,
        IReadOnlyList<QuantityBlock> billBlocks,
        int quantityIndex,
        int year,
        int month)
    {
        var issues = new List<ParsedIssue>();
        for (var dayIndex = 0; dayIndex < billBlocks.Count; dayIndex++)
        {
            var qtyPieces = ParseIntegerCell(
                sheet.Cell(row, billBlocks[dayIndex].Columns[quantityIndex].ColumnIndex));
            if (qtyPieces > 0)
            {
                issues.Add(new ParsedIssue(
                    new DateOnly(year, month, dayIndex + 1),
                    qtyPieces,
                    0));
            }
        }

        return issues;
    }

    private static (int Year, int Month) ResolveImportPeriod(string fileName)
    {
        var matches = Regex.Matches(
            Path.GetFileNameWithoutExtension(fileName),
            @"(?<!\d)(0[1-9]|1[0-2])(\d{2})(?!\d)");
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                "ชื่อไฟล์ต้องมีเดือนและปีแบบ MMYY เช่น 0769 เพื่อกำหนดวันที่ของบิล");
        }

        var match = matches[matches.Count - 1];
        var month = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var shortYear = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var year = shortYear >= 43
            ? 2500 + shortYear - 543
            : 2000 + shortYear;
        return (year, month);
    }

    private static int ParsePackSize(string productName)
    {
        var match = Regex.Match(productName, @"\((\d+)\s*เส้น\)");
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out var packSize) ||
            packSize <= 0)
        {
            throw new InvalidOperationException(
                $"ไม่พบขนาดบรรจุต่อหนึ่งลังในชื่อสินค้า '{productName}'");
        }

        return packSize;
    }

    private static bool UsesCaseQuantity(string categoryName)
    {
        return CaseQuantityCategories.Contains(NormalizeCategoryName(categoryName));
    }

    private static bool UsesStandardVariant(string categoryName)
    {
        var normalized = NormalizeCategoryName(categoryName);
        return UsesCaseQuantity(normalized) ||
            normalized.StartsWith("อะไหล่", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVariantName(string value)
    {
        return ProductVariantDefaults.NormalizeVariantName(value);
    }

    private static int ParseIntegerCell(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return 0;
        }

        if (cell.TryGetValue<double>(out var number))
        {
            return Math.Max(0, Convert.ToInt32(Math.Round(number, MidpointRounding.AwayFromZero)));
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        text = text.Replace(",", string.Empty);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Max(0, value);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            return Math.Max(0, Convert.ToInt32(Math.Round(floatValue, MidpointRounding.AwayFromZero)));
        }

        return 0;
    }

    private static string BuildImportRef(byte[] fileBytes)
    {
        var hash = SHA256.HashData(fileBytes);
        var hex = Convert.ToHexString(hash);
        return $"EXCEL-{hex[..12]}";
    }

    private static string NormalizeCategoryName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "นำเข้าจาก Excel";
        }

        return Truncate(value.Trim(), 150);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record QuantityColumn(int ColumnIndex, string Name);
    private sealed record QuantityBlock(string Title, IReadOnlyList<QuantityColumn> Columns);
    private sealed record ParsedIssue(DateOnly Date, int QtyPieces, int QtyCases);
    private sealed record ParsedVariant(
        string Name,
        int QtyPieces,
        int QtyCases,
        IReadOnlyList<ParsedIssue> Issues);
    private sealed record ParsedRow(
        string CategoryName,
        string Sku,
        string ProductName,
        string Unit,
        IReadOnlyList<ParsedVariant> Variants);
    private sealed record ParsedWorkbook(
        IReadOnlyList<string> ColorHeaders,
        IReadOnlyList<ParsedRow> Rows,
        DateOnly OpeningDate,
        DateOnly SnapshotDate,
        int BillDays,
        IReadOnlyList<string> Warnings);
    private sealed record ImportedVariant(ProductVariant Entity, ParsedVariant Parsed);
}
