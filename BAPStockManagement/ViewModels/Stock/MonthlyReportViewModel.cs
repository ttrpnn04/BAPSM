using BAPStockManagement.Models;

namespace BAPStockManagement.ViewModels.Stock;

public class MonthlyReportViewModel
{
    public int Year { get; set; }

    public int Month { get; set; }

    public string? CategoryFilter { get; set; }

    public IReadOnlyList<string> Categories { get; set; } = [];

    public IReadOnlyList<VwMonthlySummary> Items { get; set; } = [];

    public int TotalIn => Items.Sum(i => i.QtyIn ?? 0);

    public int TotalOut => Items.Sum(i => i.QtyOut ?? 0);

    public int TotalNet => Items.Sum(i => i.NetChange ?? 0);

    public string MonthLabel => $"{Month:00}/{Year}";
}
