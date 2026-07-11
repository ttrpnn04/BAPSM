using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BAPStockManagement.Constants;
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

    private static readonly string[] StockStopTokens =
    [
        "ลัง",
        "คงเหลือ",
        "รวม",
        "ยอดรวม"
    ];

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
            CategoryName = string.Empty,
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

            var parsed = ParseWorkbook(fileBytes, model.IncludeZeroStockProducts, model.CategoryName);
            if (parsed.Rows.Count == 0)
            {
                model.HasResult = true;
                model.IsSuccess = false;
                model.ResultMessage = "ไม่พบข้อมูลสินค้าที่พร้อมนำเข้า";
                return View(model);
            }

            var receiveTypeId = await GetOrCreateReceiveTypeAsync();
            var issueTypeId = await GetOrCreateIssueTypeAsync();
            var userId = _userManager.GetUserId(User);
            var categoryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var createdProducts = 0;
            var importedProducts = 0;
            var createdVariants = 0;
            var importedTransactions = 0;
            var totalPieces = 0;
            var skippedNoStock = 0;

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var row in parsed.Rows)
                {
                    if (!categoryCache.TryGetValue(row.CategoryName, out var categoryId))
                    {
                        categoryId = await GetOrCreateCategoryAsync(row.CategoryName);
                        categoryCache[row.CategoryName] = categoryId;
                    }

                    var (product, createdProduct) = await GetOrCreateProductAsync(categoryId, row.Sku, row.ProductName);
                    importedProducts++;
                    if (createdProduct)
                    {
                        createdProducts++;
                    }

                    var rowTargetPieces = row.Variants.Sum(v => v.QtyPieces);
                    totalPieces += rowTargetPieces;
                    if (rowTargetPieces == 0 && row.Variants.Count == 0)
                    {
                        skippedNoStock++;
                        continue;
                    }

                    var rowHadAdjustment = false;
                    foreach (var variant in row.Variants)
                    {
                        var (variantEntity, createdVariant) = await GetOrCreateVariantAsync(product, variant.Name);
                        if (createdVariant)
                        {
                            createdVariants++;
                        }

                        var currentPieces = variantEntity.StockBalance?.QtyPieces ?? 0;
                        var targetPieces = variant.QtyPieces;
                        var delta = targetPieces - currentPieces;
                        if (delta == 0)
                        {
                            continue;
                        }

                        rowHadAdjustment = true;
                        _context.StockTransactions.Add(new StockTransaction
                        {
                            VariantId = variantEntity.VariantId,
                            TransactionTypeId = delta > 0 ? receiveTypeId : issueTypeId,
                            TxnDate = DateOnly.FromDateTime(DateTime.Today),
                            QtyPieces = Math.Abs(delta),
                            QtyCases = 0,
                            RefNo = importRef,
                            Note = $"Stock snapshot from Excel ({Truncate(safeFileName, 60)}): set {currentPieces} -> {targetPieces}",
                            CreatedBy = userId
                        });

                        importedTransactions++;
                    }

                    if (!rowHadAdjustment && rowTargetPieces == 0)
                    {
                        skippedNoStock++;
                    }
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            model.HasResult = true;
            model.IsSuccess = true;
            model.ResultMessage = "นำเข้าข้อมูลสำเร็จ (ตั้งยอดสต็อกตามไฟล์ล่าสุด)";
            model.ImportRef = importRef;
            model.ParsedRows = parsed.Rows.Count;
            model.ImportedProducts = importedProducts;
            model.CreatedProducts = createdProducts;
            model.CreatedVariants = createdVariants;
            model.ImportedTransactions = importedTransactions;
            model.TotalPieces = totalPieces;
            model.SkippedNoStockProducts = skippedNoStock;
            model.ColorHeaders = parsed.ColorHeaders;
            model.CategoryName = parsed.Rows.FirstOrDefault()?.CategoryName ?? model.CategoryName;
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

    private async Task<int> GetOrCreateCategoryAsync(string categoryName)
    {
        var name = NormalizeCategoryName(categoryName);
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.CategoryName == name);

        if (category != null)
        {
            if (!category.IsActive)
            {
                category.IsActive = true;
                await _context.SaveChangesAsync();
            }

            return category.CategoryId;
        }

        var newCategory = new Category
        {
            CategoryName = name,
            VariantLabel = "สี",
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

    private async Task<(Product Product, bool Created)> GetOrCreateProductAsync(int categoryId, string sku, string productName)
    {
        var normalizedSku = Truncate(sku.Trim(), 50);
        var normalizedName = Truncate(productName.Trim(), 300);

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Sku == normalizedSku && p.ProductName == normalizedName);
        if (product != null)
        {
            product.CategoryId = categoryId;
            product.IsActive = true;
            product.UpdatedAt = DateTime.Now;
            return (product, false);
        }

        var created = new Product
        {
            CategoryId = categoryId,
            Sku = normalizedSku,
            ProductName = normalizedName,
            Unit = "คัน",
            IsActive = true,
            Note = "Imported from Excel"
        };
        _context.Products.Add(created);
        await _context.SaveChangesAsync();
        return (created, true);
    }

    private async Task<(ProductVariant Variant, bool Created)> GetOrCreateVariantAsync(Product product, string variantName)
    {
        var normalizedVariant = Truncate(variantName.Trim(), 100);
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

    private static ParsedWorkbook ParseWorkbook(byte[] fileBytes, bool includeZeroStockProducts, string? categoryNameOverride)
    {
        using var workbook = new XLWorkbook(new MemoryStream(fileBytes));
        var sheet = workbook.Worksheets.First();

        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastColumn == 0 || lastRow < 3)
        {
            throw new InvalidOperationException("ไฟล์ไม่มีข้อมูลที่อ่านได้");
        }

        var stockBlocks = new List<List<ColorColumn>>();
        for (var col = 1; col <= lastColumn; col++)
        {
            var title = sheet.Cell(1, col).GetString().Trim();
            if (!title.Contains("สต็อก", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var colors = new List<ColorColumn>();
            for (var colorCol = col; colorCol <= lastColumn; colorCol++)
            {
                var header = sheet.Cell(2, colorCol).GetString().Trim();
                if (string.IsNullOrWhiteSpace(header))
                {
                    break;
                }

                if (header.Contains("บิล", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("เข้า", StringComparison.OrdinalIgnoreCase) ||
                    StockStopTokens.Contains(header))
                {
                    break;
                }

                colors.Add(new ColorColumn(colorCol, header));
            }

            if (colors.Count >= 3)
            {
                stockBlocks.Add(colors);
            }
        }

        if (stockBlocks.Count == 0)
        {
            throw new InvalidOperationException("ไม่พบโครงคอลัมน์สต็อกในไฟล์ Excel");
        }

        var activeColors = stockBlocks[^1];
        var fixedCategoryName = !string.IsNullOrWhiteSpace(categoryNameOverride)
            ? NormalizeCategoryName(categoryNameOverride)
            : null;

        var rows = new List<ParsedRow>();
        for (var row = 3; row <= lastRow; row++)
        {
            var sku = sheet.Cell(row, 2).GetString().Trim();
            var productName = sheet.Cell(row, 3).GetString().Trim();
            if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(productName))
            {
                continue;
            }

            var variants = new List<ParsedVariant>();
            foreach (var color in activeColors)
            {
                var qtyPieces = ParseIntegerCell(sheet.Cell(row, color.ColumnIndex));
                variants.Add(new ParsedVariant(color.Name, qtyPieces));
            }

            var hasPositiveStock = variants.Any(v => v.QtyPieces > 0);
            if (hasPositiveStock || includeZeroStockProducts)
            {
                rows.Add(new ParsedRow(
                    fixedCategoryName ?? ResolveCategoryBySku(sku),
                    Truncate(sku, 50),
                    Truncate(productName, 300),
                    variants));
            }
        }

        return new ParsedWorkbook(
            activeColors.Select(c => c.Name).ToList(),
            rows);
    }

    private static string ResolveCategoryBySku(string sku)
    {
        var normalized = sku.Trim().ToUpperInvariant();
        if (normalized.StartsWith("BL-BR", StringComparison.OrdinalIgnoreCase))
        {
            return "BL-BR - ยางในมอเตอร์ไซต์";
        }

        if (normalized.StartsWith("BLC", StringComparison.OrdinalIgnoreCase))
        {
            return "BLC - ยางในจักรยาน";
        }

        if (normalized.StartsWith("BLB", StringComparison.OrdinalIgnoreCase))
        {
            return "BLB - จักรยาน";
        }

        if (normalized.StartsWith("BL", StringComparison.OrdinalIgnoreCase))
        {
            return "BL - รถเด็กหัดเดิน";
        }

        return "นำเข้าจาก Excel";
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

    private sealed record ColorColumn(int ColumnIndex, string Name);
    private sealed record ParsedVariant(string Name, int QtyPieces);
    private sealed record ParsedRow(
        string CategoryName,
        string Sku,
        string ProductName,
        IReadOnlyList<ParsedVariant> Variants);
    private sealed record ParsedWorkbook(IReadOnlyList<string> ColorHeaders, IReadOnlyList<ParsedRow> Rows);
}
