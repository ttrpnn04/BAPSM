using System;
using System.Collections.Generic;

namespace BAPStockManagement.Models;

public class SaleBill
{
    public int SaleBillId { get; set; }

    public string BillNo { get; set; } = null!;

    public DateTime BillDate { get; set; }

    public int CustomerId { get; set; }

    public int PaymentTermsDays { get; set; } = 30;

    public DateTime DueDate { get; set; }

    public string CustomerName { get; set; } = null!;

    public string? CustomerAddress { get; set; }

    public string? CustomerDistrict { get; set; }

    public string? CustomerProvince { get; set; }

    public string? CustomerPhone { get; set; }

    public string? CustomerSalesZone { get; set; }

    public string? CustomerShippingInfo { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public long? StockDocumentId { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual StockDocument? StockDocument { get; set; }

    public virtual ICollection<SaleBillLine> Lines { get; set; } = new List<SaleBillLine>();
}
