using System;
using System.Collections.Generic;

namespace BAPStockManagement.Models;

public partial class TransactionType
{
    public int TransactionTypeId { get; set; }

    public string TypeName { get; set; } = null!;

    public short Direction { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<StockDocument> StockDocuments { get; set; } = new List<StockDocument>();

    public virtual ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();
}
