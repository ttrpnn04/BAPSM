using Microsoft.AspNetCore.Http;

namespace BAPStockManagement.ViewModels.Import;

public class ExcelImportViewModel
{
    public IFormFile? ExcelFile { get; set; }

    public bool IncludeZeroStockProducts { get; set; } = true;

    public string? CategoryName { get; set; }

    public bool HasResult { get; set; }

    public bool IsSuccess { get; set; }

    public string? ResultMessage { get; set; }

    public string? ImportRef { get; set; }

    public int ParsedRows { get; set; }

    public int ImportedProducts { get; set; }

    public int CreatedProducts { get; set; }

    public int CreatedVariants { get; set; }

    public int ImportedTransactions { get; set; }

    public int TotalPieces { get; set; }

    public int SkippedNoStockProducts { get; set; }

    public IReadOnlyList<string> ColorHeaders { get; set; } = [];

    public IReadOnlyList<string> CategoryNames { get; set; } = [];
}
