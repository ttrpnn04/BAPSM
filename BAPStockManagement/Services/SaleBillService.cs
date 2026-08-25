using BAPStockManagement.Configuration;
using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BAPStockManagement.Services;

public class SaleBillService
{
    private readonly BAPStockContext _context;
    private readonly BillingOptions _billing;

    public SaleBillService(BAPStockContext context, IOptions<BillingOptions> billingOptions)
    {
        _context = context;
        _billing = billingOptions.Value;
    }

    public async Task<int?> GetPrimarySaleOutTypeIdAsync()
    {
        return await _context.TransactionTypes
            .AsNoTracking()
            .Where(t => t.IsActive && t.Direction < 0)
            .OrderBy(t => t.TransactionTypeId)
            .Select(t => (int?)t.TransactionTypeId)
            .FirstOrDefaultAsync();
    }

    public async Task<string> GenerateBillNoAsync(DateTime billDate)
    {
        var prefix = string.IsNullOrWhiteSpace(_billing.BillNoPrefix) ? "AV" : _billing.BillNoPrefix.Trim();
        var datePart = billDate.ToString("ddMMyy");
        var stub = $"{prefix}-{datePart}-";

        var lastNo = await _context.SaleBills
            .AsNoTracking()
            .Where(b => b.BillNo.StartsWith(stub))
            .OrderByDescending(b => b.BillNo)
            .Select(b => b.BillNo)
            .FirstOrDefaultAsync();

        var next = 1;
        if (!string.IsNullOrEmpty(lastNo))
        {
            var parts = lastNo.Split('-');
            if (parts.Length >= 3 && int.TryParse(parts[^1], out var n))
            {
                next = n + 1;
            }
        }

        return $"{stub}{next:D3}";
    }

    /// <summary>
    /// Creates a sale bill from a stock-out document, or returns existing if already linked.
    /// </summary>
    public async Task<SaleBill> CreateFromStockDocumentAsync(
        long documentId,
        int customerId,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.SaleBills
            .FirstOrDefaultAsync(b => b.StockDocumentId == documentId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var document = await _context.StockDocuments
            .Include(d => d.StockTransactions)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken)
            ?? throw new InvalidOperationException("ไม่พบบิลสต็อก");

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("ไม่พบลูกค้า");

        var variantIds = document.StockTransactions.Select(t => t.VariantId).Distinct().ToList();
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => variantIds.Contains(v.VariantId))
            .ToDictionaryAsync(v => v.VariantId, cancellationToken);

        var billDate = document.TxnDate.ToDateTime(TimeOnly.MinValue);
        var terms = _billing.DefaultPaymentTermsDays;

        var lines = new List<SaleBillLine>();
        var sort = 0;
        foreach (var txn in document.StockTransactions.OrderBy(t => t.TransactionId))
        {
            if (!variants.TryGetValue(txn.VariantId, out var variant))
            {
                continue;
            }

            sort++;
            var unit = string.IsNullOrWhiteSpace(variant.Product.Unit) ? "คัน" : variant.Product.Unit.Trim();
            var useCases = ProductUnits.IsCaseUnit(unit);
            var qty = useCases
                ? (txn.QtyCases > 0 ? txn.QtyCases : txn.QtyPieces)
                : (txn.QtyPieces > 0 ? txn.QtyPieces : txn.QtyCases);

            if (qty <= 0)
            {
                continue;
            }

            var description = $"{variant.Product.ProductName} / {variant.VariantName}";
            if (description.Length > 300)
            {
                description = description[..300];
            }

            lines.Add(new SaleBillLine
            {
                Sku = Truncate(variant.Product.Sku, 50),
                Description = description,
                Qty = qty,
                Unit = Truncate(unit, 20)!,
                UnitPrice = 0,
                DiscountPerUnit = 0,
                SortOrder = sort
            });
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("ไม่มีรายการสินค้าสำหรับออกบิล");
        }

        var bill = new SaleBill
        {
            BillNo = await GenerateBillNoAsync(billDate),
            BillDate = billDate,
            CustomerId = customer.CustomerId,
            PaymentTermsDays = terms,
            DueDate = billDate.AddDays(terms),
            CustomerName = customer.Name,
            CustomerAddress = customer.Address,
            CustomerDistrict = customer.District,
            CustomerProvince = customer.Province,
            CustomerPhone = customer.Phone1,
            CustomerSalesZone = customer.SalesZone,
            CustomerShippingInfo = customer.ShippingInfo,
            Note = document.Note,
            CreatedAt = DateTime.Now,
            CreatedBy = createdBy,
            StockDocumentId = documentId,
            Lines = lines
        };

        _context.SaleBills.Add(bill);
        await _context.SaveChangesAsync(cancellationToken);
        return bill;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= max ? value : value[..max];
    }
}
