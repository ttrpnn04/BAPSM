namespace BAPStockManagement.ViewModels.Stock;

public class TransactionIndexViewModel
{
    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public int? TransactionTypeId { get; set; }

    public string? Search { get; set; }

    public IReadOnlyList<TransactionTypeOption> TransactionTypes { get; set; } = [];

    public IReadOnlyList<TransactionDocumentListItemViewModel> Items { get; set; } = [];

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public int TotalItems { get; set; }

    public int TotalPages => TotalItems == 0 ? 1 : (int)Math.Ceiling((double)TotalItems / PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}

public class TransactionTypeOption
{
    public int TransactionTypeId { get; set; }

    public string TypeName { get; set; } = string.Empty;
}

public class TransactionDocumentListItemViewModel
{
    public long DocumentId { get; set; }

    public DateOnly TxnDate { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public short Direction { get; set; }

    public string? RefNo { get; set; }

    public string? Note { get; set; }

    public int LineCount { get; set; }

    public int TotalPieces { get; set; }

    public int TotalCases { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class TransactionDocumentDetailsViewModel
{
    public long DocumentId { get; set; }

    public DateOnly TxnDate { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public short Direction { get; set; }

    public string? RefNo { get; set; }

    public string? Note { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public IReadOnlyList<TransactionDocumentLineViewModel> Lines { get; set; } = [];

    public int TotalPieces => Lines.Sum(l => l.QtyPieces);

    public int TotalCases => Lines.Sum(l => l.QtyCases);
}

public class TransactionDocumentLineViewModel
{
    public long TransactionId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }
}
