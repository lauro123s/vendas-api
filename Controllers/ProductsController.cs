using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using VendasAPI.Models;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IConfiguration _cfg;

    public ProductsController(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> Get()
    {
        var cs = _cfg.GetConnectionString("PantheraDb");
        using var conn = new NpgsqlConnection(cs);

        var sql = @"
            SELECT TOP (1000)
                [Id],
                [Name],
                [Barcode],
                [Price],
                [Stock],
                [IsActive],
                [CreatedAt]
            FROM [dbo].[Products]
            ORDER BY [Id] DESC;
        ";

        var rows = await conn.QueryAsync<ProductDto>(sql);
        return Ok(rows);
    }
}
