using System;
using System.Collections.Generic;

namespace BAPStockManagement.Models;

public partial class StockTransaction
{
    public long TransactionId { get; set; }

    public int VariantId { get; set; }

    public int TransactionTypeId { get; set; }

    public DateOnly TxnDate { get; set; }

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }

    public string? RefNo { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public virtual TransactionType TransactionType { get; set; } = null!;

    public virtual ProductVariant Variant { get; set; } = null!;
}
