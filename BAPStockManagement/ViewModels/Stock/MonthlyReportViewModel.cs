using BAPStockManagement.Models;

namespace BAPStockManagement.ViewModels.Stock;

public class MonthlyReportViewModel
{
    public int Year { get; set; }

    public int Month { get; set; }

    public string? CategoryFilter { get; set; }

    public string? Search { get; set; }

    public IReadOnlyList<string> Categories { get; set; } = [];

    public IReadOnlyList<VwMonthlySummary> Items { get; set; } = [];

    public int TotalIn { get; set; }

    public int TotalOut { get; set; }

    public int TotalNet { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public int TotalItems { get; set; }

    public int TotalPages => TotalItems == 0 ? 1 : (int)Math.Ceiling((double)TotalItems / PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public string MonthLabel => $"{Month:00}/{Year}";
}
