using System;
using System.Collections.Generic;

namespace BAPStockManagement.Models;

public partial class VwMonthlySummary
{
    public string CategoryName { get; set; } = null!;

    public string Sku { get; set; } = null!;

    public string ProductName { get; set; } = null!;

    public string VariantName { get; set; } = null!;

    public int? TxnYear { get; set; }

    public int? TxnMonth { get; set; }

    public int? QtyIn { get; set; }

    public int? QtyOut { get; set; }

    public int? NetChange { get; set; }
}
