using System.ComponentModel.DataAnnotations;

namespace BAPStockManagement.ViewModels.Stock;

public class EditTransactionDocumentViewModel
{
    public long DocumentId { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public short Direction { get; set; }

    [Required(ErrorMessage = "กรุณาระบุวันที่")]
    [Display(Name = "วันที่")]
    [DataType(DataType.Date)]
    public DateOnly TxnDate { get; set; }

    [Required(ErrorMessage = "กรุณากรอกชื่อร้าน / เลขที่อ้างอิง")]
    [MaxLength(50)]
    [Display(Name = "ชื่อร้าน / เลขที่อ้างอิง")]
    public string RefNo { get; set; } = string.Empty;

    [MaxLength(300)]
    [Display(Name = "หมายเหตุ")]
    public string? Note { get; set; }

    public List<EditTransactionLineViewModel> Lines { get; set; } = [];
}

public class EditTransactionLineViewModel
{
    public long? TransactionId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "กรุณาเลือกสินค้า")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "กรุณาเลือกสี/รุ่น")]
    public int VariantId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "จำนวนต้องไม่ติดลบ")]
    public int QtyPieces { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "จำนวนต้องไม่ติดลบ")]
    public int QtyCases { get; set; }

    // Display helpers for GET Edit (not required on POST)
    public string? Sku { get; set; }

    public string? ProductName { get; set; }

    public string? VariantName { get; set; }

    public string? Unit { get; set; }
}
