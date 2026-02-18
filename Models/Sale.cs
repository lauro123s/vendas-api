namespace VendasApi.Models;

public class Sale
{
    public long Id { get; set; }
    public DateTime SaleDate { get; set; }
    public int UserId { get; set; }
    public decimal Total { get; set; }
    public string PaymentMethod { get; set; } = "CASH";
    public string? Notes { get; set; }
}
