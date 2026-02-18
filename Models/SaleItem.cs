namespace VendasApi.Models;

public class SaleItem
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public int ProductId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
}
