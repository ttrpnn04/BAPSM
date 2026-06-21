namespace BAPStockManagement.ViewModels.Stock;

public class StockIndexViewModel
{
    public string? CategoryFilter { get; set; }

    public string? Search { get; set; }

    public IReadOnlyList<string> Categories { get; set; } = [];

    public IReadOnlyList<StockProductRowViewModel> Items { get; set; } = [];

    public int TotalVariants { get; set; }

    public int TotalPieces { get; set; }

    public int TotalCases { get; set; }

    public int OutOfStockCount { get; set; }

    public int LowStockCount { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalItems { get; set; }

    public int TotalPages => TotalItems == 0 ? 1 : (int)Math.Ceiling((double)TotalItems / PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}

public class StockProductRowViewModel
{
    public int ProductId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }

    public DateTime? LastUpdated { get; set; }

    public int? PrimaryVariantId { get; set; }

    public IReadOnlyList<StockColorRowViewModel> Colors { get; set; } = [];
}

public class StockColorRowViewModel
{
    public int VariantId { get; set; }

    public string VariantName { get; set; } = string.Empty;

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }
}
