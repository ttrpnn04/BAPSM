namespace BAPStockManagement.Configuration;

public class BillingOptions
{
    public const string SectionName = "Billing";

    public string ChequePayee { get; set; } = "สุวเพ็ญ เทียนพรหมทอง";

    public string BankAccountKasikorn { get; set; } = "ธ.กสิกรไทย สาขา บิ๊กซี เลขที่ 016-8-91115-7";

    public string BankAccountScb { get; set; } = "ธ.ไทยพาณิชย์ สาขา บิ๊กซี เลขที่ 298-2-05553-9";

    public string SenderName { get; set; } = "BAP";

    public string SenderPhone { get; set; } = "098-616-1428";

    public string BillNoPrefix { get; set; } = "AV";

    public int DefaultPaymentTermsDays { get; set; } = 30;
}
