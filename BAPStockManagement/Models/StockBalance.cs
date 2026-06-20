using System;
using System.Collections.Generic;

namespace BAPStockManagement.Models;

public partial class StockBalance
{
    public int VariantId { get; set; }

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }

    public DateTime LastUpdated { get; set; }

    public virtual ProductVariant Variant { get; set; } = null!;
}
