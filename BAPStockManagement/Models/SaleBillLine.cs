using System.ComponentModel.DataAnnotations.Schema;

namespace BAPStockManagement.Models;

public class SaleBillLine
{
    public int SaleBillLineId { get; set; }

    public int SaleBillId { get; set; }

    public string? Sku { get; set; }

    public string Description { get; set; } = null!;

    public decimal Qty { get; set; }

    public string Unit { get; set; } = "คัน";

    public decimal UnitPrice { get; set; }

    public decimal DiscountPerUnit { get; set; }

    public int SortOrder { get; set; }

    public virtual SaleBill SaleBill { get; set; } = null!;

    [NotMapped]
    public decimal LineAmount => Qty * UnitPrice;

    [NotMapped]
    public decimal LineDiscountAmount => Qty * DiscountPerUnit;

    [NotMapped]
    public decimal LineNetAmount => LineAmount - LineDiscountAmount;
}
