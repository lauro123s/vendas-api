using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PedidosController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public PedidosController(IConfiguration cfg) => _cfg = cfg;

    // GET /api/Pedidos?top=200
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int top = 200)
    {
        if (top < 1) top = 200;
        if (top > 2000) top = 2000;

        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            return Problem("ConnectionString PantheraDb não configurada.");

        using var conn = new NpgsqlConnection(cs);

        // Nota: usamos ORDER BY (SELECT NULL) para não depender do nome de coluna.
        // Se depois você me disser a coluna correta (ex: id, data, created_at),
        // eu ajusto para ordenar corretamente.
        var sql = $@"SELECT TOP ({top}) * FROM dbo.pedido";

        var rows = await conn.QueryAsync(sql);
        return Ok(rows);
    }
}
