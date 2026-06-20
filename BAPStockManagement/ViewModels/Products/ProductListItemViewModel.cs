namespace BAPStockManagement.ViewModels.Products;

public class ProductListItemViewModel
{
    public int ProductId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public int VariantCount { get; set; }

    public int TotalPieces { get; set; }

    public int TotalCases { get; set; }

    public bool IsActive { get; set; }
}
