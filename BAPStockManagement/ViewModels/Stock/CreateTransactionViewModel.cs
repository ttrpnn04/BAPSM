using System.ComponentModel.DataAnnotations;

namespace BAPStockManagement.ViewModels.Stock;

public class CreateTransactionViewModel
{
    [Required(ErrorMessage = "กรุณาเลือกสินค้า")]
    [Display(Name = "สินค้า")]
    public int? ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "กรุณาเลือกสี/รุ่น")]
    [Display(Name = "สี/รุ่น")]
    public int VariantId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกประเภท")]
    [Display(Name = "ประเภท")]
    public int TransactionTypeId { get; set; }

    [Required(ErrorMessage = "กรุณาระบุวันที่")]
    [Display(Name = "วันที่")]
    [DataType(DataType.Date)]
    public DateOnly TxnDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Range(0, int.MaxValue, ErrorMessage = "จำนวนต้องไม่ติดลบ")]
    [Display(Name = "จำนวน (ชิ้น)")]
    public int QtyPieces { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "จำนวนต้องไม่ติดลบ")]
    [Display(Name = "จำนวน (ลัง)")]
    public int QtyCases { get; set; }

    [MaxLength(50)]
    [Display(Name = "เลขที่อ้างอิง")]
    public string? RefNo { get; set; }

    [MaxLength(300)]
    [Display(Name = "หมายเหตุ")]
    public string? Note { get; set; }
}
