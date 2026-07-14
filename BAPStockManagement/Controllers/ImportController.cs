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

        if (!model.ConfirmResetHistory)
        {
            ModelState.AddModelError(
                nameof(model.ConfirmResetHistory),
                "กรุณายืนยันว่าจะเคลียร์ประวัติทั้งหมด แล้วตั้งยอดต้นจากสต็อกใน Excel");
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

            var parsed = ParseWorkbook(fileBytes, safeFileName);
            if (parsed.Rows.Count == 0)
            {
                model.HasResult = true;
                model.IsSuccess = false;
                model.ResultMessage = "ไม่พบข้อมูลสินค้าที่พร้อมนำเข้า";
                return View(model);
            }

            await ProductVariantSeeder.NormalizeVariantAliasesAsync(_context, _logger);

            var receiveTypeId = await GetOrCreateReceiveTypeAsync();
            var userId = _userManager.GetUserId(User);
            var categoryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var importRef = $"DEPLOY-{DateTime.Now:yyyyMMddHHmmss}";
            var openingDate = DateOnly.FromDateTime(DateTime.Today);

            var createdProducts = 0;
            var importedProducts = 0;
            var createdVariants = 0;
            var openingTransactions = 0;
            var totalPieces = 0;
            var totalCases = 0;
            var skippedNoStock = 0;
            var excelExactTargets = new Dictionary<int, ExcelStockTarget>();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var row in parsed.Rows)
                {
                    var rowHasQuantity = row.Variants.Any(v => v.QtyPieces > 0 || v.QtyCases > 0);
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
                            ProductCatalogRules.UsesStandardVariant(row.CategoryName, row.ProductName)
                                ? "หน่วย"
                                : "สี");
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

                    totalPieces += row.Variants.Sum(v => v.QtyPieces);
                    totalCases += row.Variants.Sum(v => v.QtyCases);

                    foreach (var variant in row.Variants)
                    {
                        var (variantEntity, createdVariant) = await GetOrCreateVariantAsync(product, variant.Name);
                        if (createdVariant)
                        {
                            createdVariants++;
                        }

                        excelExactTargets[variantEntity.VariantId] = new ExcelStockTarget(
                            product.ProductId,
                            variant.QtyPieces,
                            variant.QtyCases);
                    }
                }

                await _context.SaveChangesAsync();

                // เคลียร์ประวัติทั้งหมด — รายงานรายเดือนโล่ง แล้วนับต่อจากยอดต้น
                var clearedTransactions = await _context.StockTransactions.CountAsync();
                await _context.StockTransactions.ExecuteDeleteAsync();
                await _context.StockDocuments.ExecuteDeleteAsync();
                await _context.Database.ExecuteSqlRawAsync("""
                    UPDATE dbo.StockBalances
                    SET QtyPieces = 0, QtyCases = 0, LastUpdated = SYSDATETIME()
                    """);

                var openingDocument = new StockDocument
                {
                    TransactionTypeId = receiveTypeId,
                    TxnDate = openingDate,
                    RefNo = importRef,
                    Note = $"ยอดต้นจาก Excel ({Truncate(safeFileName, 60)}) — ตั้งต้นระบบ ไม่นำเข้าบิล",
                    CreatedBy = userId
                };
                _context.StockDocuments.Add(openingDocument);

                // ยอดต้นเฉพาะที่มีของใน Excel; สี=0 ของสินค้าในไฟล์เคลียร์เป็น 0 แล้ว
                foreach (var (variantId, target) in excelExactTargets)
                {
                    if (target.QtyPieces <= 0 && target.QtyCases <= 0)
                    {
                        continue;
                    }

                    _context.StockTransactions.Add(new StockTransaction
                    {
                        Document = openingDocument,
                        VariantId = variantId,
                        TransactionTypeId = receiveTypeId,
                        TxnDate = openingDate,
                        QtyPieces = Math.Max(0, target.QtyPieces),
                        QtyCases = Math.Max(0, target.QtyCases),
                        RefNo = importRef,
                        Note = $"ยอดต้นจาก Excel ({Truncate(safeFileName, 60)})",
                        CreatedBy = userId
                    });
                    openingTransactions++;
                }

                // สีของสินค้าในไฟล์ที่ไม่มีใน Excel targets → ยอดเป็น 0 อยู่แล้วหลังรีเซ็ต
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                model.HasResult = true;
                model.IsSuccess = true;
                model.ResultMessage =
                    "ตั้งต้นสำเร็จ — เคลียร์ประวัติแล้ว และตั้งยอดต้นจากสต็อกใน Excel (ไม่นำเข้าบิล)";
                model.ImportRef = importRef;
                model.ParsedRows = parsed.Rows.Count;
                model.ImportedProducts = importedProducts;
                model.CreatedProducts = createdProducts;
                model.CreatedVariants = createdVariants;
                model.ImportedTransactions = openingTransactions;
                model.ImportedIssueTransactions = 0;
                model.AdjustmentTransactions = openingTransactions;
                model.ClearedTransactions = clearedTransactions;
                model.OpeningTransactions = openingTransactions;
                model.TotalPieces = totalPieces;
                model.TotalCases = totalCases;
                model.SkippedNoStockProducts = skippedNoStock;
                model.ColorHeaders = parsed.ColorHeaders;
                model.BillDays = 0;
                model.SnapshotDate = openingDate;
                model.Warnings = parsed.Warnings
                    .Append("นำเข้าเฉพาะยอดสต็อกเป็นยอดต้น — ไม่นำเข้าประวัติบิลจาก Excel")
                    .Append($"เคลียร์รายการเก่า {clearedTransactions:N0} รายการ เพื่อให้รายงานโล่ง")
                    .ToList();
                model.CategoryNames = parsed.Rows
                    .Select(r => r.CategoryName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                return View(model);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
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
            _logger.LogError(ex, "Excel deploy import failed while saving for file {FileName}", safeFileName);
            model.HasResult = true;
            model.IsSuccess = false;
            model.ResultMessage = "นำเข้าไม่สำเร็จขณะบันทึกข้อมูล กรุณาลองใหม่อีกครั้ง";
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel deploy import failed for file {FileName}", safeFileName);
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

    private StockDocument GetOrCreatePendingDocument(
        Dictionary<string, StockDocument> cache,
        int transactionTypeId,
        DateOnly txnDate,
        string? refNo,
        string? note,
        string? userId)
    {
        var key = $"{transactionTypeId}|{txnDate:yyyy-MM-dd}|{refNo ?? string.Empty}|{note ?? string.Empty}";
        if (cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var document = new StockDocument
        {
            TransactionTypeId = transactionTypeId,
            TxnDate = txnDate,
            RefNo = refNo,
            Note = note,
            CreatedBy = userId
        };
        _context.StockDocuments.Add(document);
        cache[key] = document;
        return document;
    }

    private int AddAdjustmentTransactions(
        Dictionary<string, StockDocument> documentCache,
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
            var document = GetOrCreatePendingDocument(
                documentCache,
                receiveTypeId,
                date,
                refNo,
                note,
                userId);

            _context.StockTransactions.Add(new StockTransaction
            {
                Document = document,
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
            var document = GetOrCreatePendingDocument(
                documentCache,
                issueTypeId,
                date,
                refNo,
                note,
                userId);

            _context.StockTransactions.Add(new StockTransaction
            {
                Document = document,
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

    /// <summary>
    /// ตั้งยอดสินค้าที่อยู่ในไฟล์ Excel ให้ตรงคอลัมน์สต็อกล่าสุดแบบเป๊ะ
    /// และเคลียร์สี/รุ่นอื่นของสินค้านั้นที่ไม่อยู่ในไฟล์ให้เป็น 0
    /// </summary>
    private async Task<int> SyncImportedProductsToExcelExactAsync(
        IReadOnlyDictionary<int, ExcelStockTarget> excelExactTargets,
        IReadOnlyCollection<int> importedProductIds,
        Dictionary<string, StockDocument> documentCache,
        int receiveTypeId,
        int issueTypeId,
        DateOnly snapshotDate,
        string refNo,
        string note,
        string? userId)
    {
        if (importedProductIds.Count == 0)
        {
            return 0;
        }

        // อ่านยอดจริงจาก DB หลัง trigger อัปเดตแล้ว (อย่าใช้ค่าใน memory)
        var productVariants = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => importedProductIds.Contains(v.ProductId) && v.IsActive)
            .Select(v => new
            {
                v.VariantId,
                v.ProductId,
                QtyPieces = v.StockBalance != null ? v.StockBalance.QtyPieces : 0,
                QtyCases = v.StockBalance != null ? v.StockBalance.QtyCases : 0
            })
            .ToListAsync();

        var count = 0;
        foreach (var variant in productVariants)
        {
            var targetPieces = 0;
            var targetCases = 0;
            if (excelExactTargets.TryGetValue(variant.VariantId, out var target))
            {
                targetPieces = target.QtyPieces;
                targetCases = target.QtyCases;
            }

            var deltaPieces = targetPieces - variant.QtyPieces;
            var deltaCases = targetCases - variant.QtyCases;
            if (deltaPieces == 0 && deltaCases == 0)
            {
                continue;
            }

            count += AddAdjustmentTransactions(
                documentCache,
                variant.VariantId,
                deltaPieces,
                deltaCases,
                receiveTypeId,
                issueTypeId,
                snapshotDate,
                refNo,
                note,
                userId);
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
        var allBillBlocks = FindQuantityBlocks(sheet, lastColumn, "บิล");
        if (stockBlocks.Count == 0)
        {
            throw new InvalidOperationException("ไม่พบโครงคอลัมน์สต็อกในไฟล์ Excel");
        }

        if (allBillBlocks.Count == 0)
        {
            throw new InvalidOperationException("ไม่พบคอลัมน์บิลรายวันในไฟล์ Excel");
        }

        var latestStock = SelectLatestStockBlock(sheet, stockBlocks);
        if (latestStock.Columns.Count < 2 ||
            !latestStock.Columns[^1].Name.Equals("ลัง", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "คอลัมน์สต็อกล่าสุดต้องลงท้ายด้วยคอลัมน์ 'ลัง'");
        }

        // ใช้เฉพาะบิลที่อยู่ก่อนบล็อกสต็อกที่เลือก — ข้ามบิล/สต็อกที่ถูกซ่อนด้านขวา
        var stockStartCol = latestStock.Columns[0].ColumnIndex;
        var billBlocks = allBillBlocks
            .Where(b => b.Columns[0].ColumnIndex < stockStartCol)
            .ToList();
        var skippedHiddenBills = allBillBlocks.Count - billBlocks.Count;

        if (billBlocks.Count == 0)
        {
            throw new InvalidOperationException(
                "ไม่พบคอลัมน์บิลรายวันก่อนบล็อกสต็อกล่าสุดที่มองเห็นได้");
        }

        foreach (var bill in billBlocks)
        {
            if (bill.Columns.Count != latestStock.Columns.Count)
            {
                throw new InvalidOperationException(
                    $"โครงคอลัมน์ {bill.Title} ({bill.Columns.Count} คอลัมน์) " +
                    $"ไม่ตรงกับคอลัมน์สต็อกล่าสุด ({latestStock.Columns.Count} คอลัมน์) " +
                    $"หัวสต็อก: {string.Join(", ", latestStock.Columns.Select(c => c.Name))}");
            }
        }

        var (year, month) = ResolveImportPeriod(fileName);
        if (billBlocks.Count > DateTime.DaysInMonth(year, month))
        {
            throw new InvalidOperationException("จำนวนคอลัมน์บิลมากกว่าจำนวนวันในเดือนของไฟล์");
        }

        var openingDate = new DateOnly(year, month, 1).AddDays(-1);
        var snapshotDate = new DateOnly(year, month, billBlocks.Count);
        var warnings = new List<string>
        {
            $"ใช้บล็อกสต็อกที่มองเห็นคอลัมน์ที่ {latestStock.Columns[0].ColumnIndex} " +
            $"({latestStock.Columns.Count} คอลัมน์: {string.Join(", ", latestStock.Columns.Take(6).Select(c => c.Name))}" +
            $"{(latestStock.Columns.Count > 6 ? ", ..." : string.Empty)}) — บิล {billBlocks.Count} วัน"
        };

        if (skippedHiddenBills > 0)
        {
            warnings.Add(
                $"ข้ามบิล/สต็อกด้านขวาที่ถูกซ่อนใน Excel {skippedHiddenBills} ชุด " +
                "(เช่น บิล14) — ระบบยึดยอดตามสต็อกชุดขวาสุดที่ยังมองเห็น");
        }

        if (stockBlocks.Count > 1)
        {
            warnings.Add(
                $"ไฟล์มีบล็อกสต็อก {stockBlocks.Count} ชุด — ใช้ชุดขวาสุดที่ไม่ได้ถูกซ่อนคอลัมน์");
        }

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

            sku = NormalizeSku(sku);
            if (string.IsNullOrWhiteSpace(sku))
            {
                continue;
            }

            // กันกรณีหัวหมวดถูกอ่านเป็นสินค้า
            if (productName.StartsWith("กลุ่ม", StringComparison.OrdinalIgnoreCase) ||
                productName.Equals("รหัสสินค้า", StringComparison.OrdinalIgnoreCase))
            {
                currentCategory = NormalizeCategoryName(productName);
                continue;
            }

            var isPackedCase = ProductCatalogRules.UsesPackedCaseCategory(currentCategory);
            var isLooseCase = ProductCatalogRules.UsesLooseCaseUnit(currentCategory, productName);
            var isCaseQuantity = isPackedCase || isLooseCase;
            var parsedVariants = new List<ParsedVariant>();

            if (isPackedCase)
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
                        $"แถว {row} ({sku}) เป็นสินค้านับกระสอบ/ลัง/เส้น แต่พบยอดในคอลัมน์สี");
                }

                parsedVariants.Add(new ParsedVariant(
                    StandardVariantName,
                    0,
                    targetCases,
                    issues));
            }
            else if (isLooseCase)
            {
                // ยางนอก / สินค้าคละสี: ยอดมักอยู่คอลัมน์ขาว (หรือลัง) → นับเป็นกระสอบ/ลัง/เส้นตรง ๆ
                var targetCases =
                    colorColumns.Sum(c => ParseIntegerCell(sheet.Cell(row, c.ColumnIndex))) +
                    ParseIntegerCell(sheet.Cell(row, latestStock.Columns[caseColumnIndex].ColumnIndex));
                var issues = new List<ParsedIssue>();

                for (var dayIndex = 0; dayIndex < billBlocks.Count; dayIndex++)
                {
                    var billColumns = billBlocks[dayIndex].Columns;
                    var rawIssue =
                        colorColumns.Select((_, colorIndex) =>
                                ParseIntegerCell(sheet.Cell(row, billColumns[colorIndex].ColumnIndex)))
                            .Sum() +
                        ParseIntegerCell(sheet.Cell(row, billColumns[caseColumnIndex].ColumnIndex));
                    if (rawIssue == 0)
                    {
                        continue;
                    }

                    issues.Add(new ParsedIssue(
                        new DateOnly(year, month, dayIndex + 1),
                        0,
                        rawIssue));
                }

                parsedVariants.Add(new ParsedVariant(
                    StandardVariantName,
                    0,
                    targetCases,
                    issues));
            }
            else
            {
                var colorQtyByName = new Dictionary<string, (int Pieces, List<ParsedIssue> Issues)>(
                    StringComparer.OrdinalIgnoreCase);

                for (var colorIndex = 0; colorIndex < colorColumns.Count; colorIndex++)
                {
                    var colorName = NormalizeVariantName(colorColumns[colorIndex].Name);
                    if (string.IsNullOrWhiteSpace(colorName) ||
                        colorName.Equals(StandardVariantName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var targetPieces = ParseIntegerCell(
                        sheet.Cell(row, colorColumns[colorIndex].ColumnIndex));
                    var issues = ReadPieceIssues(
                        sheet,
                        row,
                        billBlocks,
                        colorIndex,
                        year,
                        month);

                    if (colorQtyByName.TryGetValue(colorName, out var existing))
                    {
                        existing.Issues.AddRange(issues);
                        colorQtyByName[colorName] = (existing.Pieces + targetPieces, existing.Issues);
                    }
                    else
                    {
                        colorQtyByName[colorName] = (targetPieces, issues.ToList());
                    }
                }

                foreach (var (colorName, data) in colorQtyByName)
                {
                    // รวมบิลซ้ำวันเดียวกันหลัง merge สีที่ยกเลิก (เช่น ชมพูอ่อน → ชมเข้ม)
                    var mergedIssues = data.Issues
                        .GroupBy(i => i.Date)
                        .Select(g => new ParsedIssue(
                            g.Key,
                            g.Sum(x => x.QtyPieces),
                            g.Sum(x => x.QtyCases)))
                        .OrderBy(i => i.Date)
                        .ToList();

                    parsedVariants.Add(new ParsedVariant(
                        colorName,
                        data.Pieces,
                        0,
                        mergedIssues));
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
                    ProductCatalogRules.UsesStandardVariant(currentCategory, productName))
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
        var previousTitleMatched = false;

        for (var col = 1; col <= lastColumn; col++)
        {
            var title = GetRow1Title(sheet, col);
            var titleMatched = title.Contains(titleToken, StringComparison.OrdinalIgnoreCase);
            // หัวข้อแถว 1 แบบ merge (เช่น "สต็อก") ทำให้ทุกคอลัมน์ในกลุ่มมีข้อความเดียวกัน
            // → เริ่มบล็อกเฉพาะคอลัมน์แรกของกลุ่ม กันไปใช้บล็อกย่อยด้านขวาที่คอลัมน์สีไม่ครบ
            var isStartOfBlock = titleMatched && !previousTitleMatched;
            previousTitleMatched = titleMatched;

            if (!isStartOfBlock)
            {
                continue;
            }

            var firstHeader = sheet.Cell(2, col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(firstHeader))
            {
                continue;
            }

            var columns = new List<QuantityColumn>();
            for (var quantityCol = col; quantityCol <= lastColumn; quantityCol++)
            {
                var sectionTitle = GetRow1Title(sheet, quantityCol);
                if (quantityCol > col &&
                    !string.IsNullOrWhiteSpace(sectionTitle) &&
                    !sectionTitle.Contains(titleToken, StringComparison.OrdinalIgnoreCase))
                {
                    // จบเมื่อพ้นช่วงหัวข้อนี้ (เช่น จากสต็อกไปบิล)
                    break;
                }

                var header = sheet.Cell(2, quantityCol).GetString().Trim();
                if (string.IsNullOrWhiteSpace(header))
                {
                    break;
                }

                if (IsSummaryQuantityHeader(header))
                {
                    continue;
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

    /// <summary>
    /// เลือกบล็อกสต็อกล่าสุดที่โครงครบ (ลงท้ายด้วยลัง)
    /// เลือกชุดขวาสุดที่คอลัมน์ยังไม่ถูกซ่อน — ให้ตรงกับที่เห็นใน Excel
    /// </summary>
    private static QuantityBlock SelectLatestStockBlock(
        IXLWorksheet sheet,
        IReadOnlyList<QuantityBlock> stockBlocks)
    {
        var candidates = stockBlocks
            .Where(b =>
                b.Columns.Count >= 2 &&
                b.Columns[^1].Name.Equals("ลัง", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "คอลัมน์สต็อกล่าสุดต้องลงท้ายด้วยคอลัมน์ 'ลัง'");
        }

        var visible = candidates
            .Where(b => !IsColumnHidden(sheet, b.Columns[0].ColumnIndex))
            .OrderByDescending(b => b.Columns[0].ColumnIndex)
            .ToList();

        if (visible.Count > 0)
        {
            return visible[0];
        }

        return candidates
            .OrderByDescending(b => b.Columns[0].ColumnIndex)
            .First();
    }

    private static bool IsColumnHidden(IXLWorksheet sheet, int columnIndex)
    {
        return sheet.Column(columnIndex).IsHidden;
    }

    private static string GetRow1Title(IXLWorksheet sheet, int column)
    {
        var cell = sheet.Cell(1, column);
        // ค่าในเซลล์ merge: ใช้ค่าจากมุมบนซ้ายของช่วง merge
        if (cell.IsMerged())
        {
            var range = cell.MergedRange();
            if (range != null)
            {
                return range.FirstCell().GetString().Trim();
            }
        }

        return cell.GetString().Trim();
    }

    private static bool IsSummaryQuantityHeader(string header)
    {
        var normalized = header.Trim();
        return normalized.Equals("คงเหลือ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("รวม", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ยอดรวม", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("รวมจำนวน", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("รวมทั้งหมด", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("total", StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeSku(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Excel บางแถวใส่หมายเหตุต่อท้ายรหัส เช่น "BLB-A036 ตะกร้าขาด 9 ใบ"
        var token = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        return Truncate(token, 50);
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
    private sealed record ExcelStockTarget(int ProductId, int QtyPieces, int QtyCases);
}
