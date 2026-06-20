using System;
using System.Collections.Generic;

namespace BAPStockManagement.Models;

public partial class VwCurrentStock
{
    public string CategoryName { get; set; } = null!;

    public string Sku { get; set; } = null!;

    public string ProductName { get; set; } = null!;

    public int VariantId { get; set; }

    public string VariantName { get; set; } = null!;

    public string Unit { get; set; } = null!;

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }

    public DateTime? LastUpdated { get; set; }
}
