namespace BAPStockManagement.ViewModels.Stock;

public class TransactionIndexViewModel
{
    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public int? TransactionTypeId { get; set; }

    public string? Search { get; set; }

    public IReadOnlyList<TransactionTypeOption> TransactionTypes { get; set; } = [];

    public IReadOnlyList<TransactionListItemViewModel> Items { get; set; } = [];
}

public class TransactionTypeOption
{
    public int TransactionTypeId { get; set; }

    public string TypeName { get; set; } = string.Empty;
}

public class TransactionListItemViewModel
{
    public long TransactionId { get; set; }

    public DateOnly TxnDate { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public short Direction { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }

    public string? RefNo { get; set; }

    public string? Note { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
