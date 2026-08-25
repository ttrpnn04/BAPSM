using System;
using System.Collections.Generic;

namespace BAPStockManagement.Models;

public class Customer
{
    public int CustomerId { get; set; }

    public int CustomerCode { get; set; }

    public string Name { get; set; } = null!;

    public string? Address { get; set; }

    public string? District { get; set; }

    public string? Province { get; set; }

    public string? Phone1 { get; set; }

    public string? Phone2 { get; set; }

    public string? SalesZone { get; set; }

    public string? TaxId { get; set; }

    public string? ShippingInfo { get; set; }

    public decimal CreditLimit { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<SaleBill> SaleBills { get; set; } = new List<SaleBill>();
}
