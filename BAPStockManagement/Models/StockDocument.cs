namespace BAPStockManagement.Models;

public class StockDocument
{
    public long DocumentId { get; set; }

    public int TransactionTypeId { get; set; }

    public DateOnly TxnDate { get; set; }

    public string? RefNo { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public virtual TransactionType TransactionType { get; set; } = null!;

    public virtual ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();
}
