namespace VendasAPI.Models;

public class ProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public decimal Stock { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
