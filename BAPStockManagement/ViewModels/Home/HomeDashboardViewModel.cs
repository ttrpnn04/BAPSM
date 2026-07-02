namespace BAPStockManagement.ViewModels.Home;

public class HomeDashboardViewModel
{
    public int ActiveProductCount { get; set; }

    public int ActiveCategoryCount { get; set; }

    public int ActiveVariantCount { get; set; }

    public int TotalPieces { get; set; }

    public int TotalCases { get; set; }

    public int OutOfStockProductCount { get; set; }

    public int LowStockProductCount { get; set; }

    public int MonthInPieces { get; set; }

    public int MonthOutPieces { get; set; }

    public int MonthTransactionCount { get; set; }

    public DateTime? LastTransactionAt { get; set; }

    public IReadOnlyList<CategoryStockChartItem> CategoryStockChartItems { get; set; } = [];

    public IReadOnlyList<DailyMovementChartItem> DailyMovementChartItems { get; set; } = [];
}

public class CategoryStockChartItem
{
    public string CategoryName { get; set; } = string.Empty;

    public int QtyPieces { get; set; }
}

public class DailyMovementChartItem
{
    public string Label { get; set; } = string.Empty;

    public int InPieces { get; set; }

    public int OutPieces { get; set; }
}
