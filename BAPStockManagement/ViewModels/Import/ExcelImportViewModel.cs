using Microsoft.AspNetCore.Http;

namespace BAPStockManagement.ViewModels.Import;

public class ExcelImportViewModel
{
    public IFormFile? ExcelFile { get; set; }

    public bool IncludeZeroStockProducts { get; set; } = true;

    /// <summary>
    /// ยืนยันว่าจะเคลียร์ประวัติธุรกรรมทั้งหมด แล้วตั้งยอดต้นจาก Excel
    /// </summary>
    public bool ConfirmResetHistory { get; set; }

    public bool HasResult { get; set; }

    public int ClearedTransactions { get; set; }

    public int OpeningTransactions { get; set; }

    public bool IsSuccess { get; set; }

    public string? ResultMessage { get; set; }

    public string? ImportRef { get; set; }

    public int ParsedRows { get; set; }

    public int ImportedProducts { get; set; }

    public int CreatedProducts { get; set; }

    public int CreatedVariants { get; set; }

    public int ImportedTransactions { get; set; }

    public int ImportedIssueTransactions { get; set; }

    public int AdjustmentTransactions { get; set; }

    public int TotalPieces { get; set; }

    public int TotalCases { get; set; }

    public int SkippedNoStockProducts { get; set; }

    public int BillDays { get; set; }

    public DateOnly? SnapshotDate { get; set; }

    public IReadOnlyList<string> ColorHeaders { get; set; } = [];

    public IReadOnlyList<string> CategoryNames { get; set; } = [];

    public IReadOnlyList<string> Warnings { get; set; } = [];
}
