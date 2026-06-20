using BAPStockManagement.Models;

namespace BAPStockManagement.ViewModels.Stock;

public class StockIndexViewModel
{
    public string? CategoryFilter { get; set; }

    public string? Search { get; set; }

    public IReadOnlyList<string> Categories { get; set; } = [];

    public IReadOnlyList<VwCurrentStock> Items { get; set; } = [];

    public int TotalVariants => Items.Count;

    public int TotalPieces => Items.Sum(i => i.QtyPieces);

    public int TotalCases => Items.Sum(i => i.QtyCases);
}
