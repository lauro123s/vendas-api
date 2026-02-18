using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/pedidos")]
public class PedidosMesaController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public PedidosMesaController(IConfiguration cfg) => _cfg = cfg;

    // GET /api/pedidos/mesa/6?top=200
    [HttpGet("mesa/{mesa:int}")]
    public async Task<IActionResult> GetByMesa([FromRoute] int mesa, [FromQuery] int top = 200)
    {
        if (mesa < 1) return BadRequest("Mesa inválida.");
        if (top < 1) top = 200;
        if (top > 2000) top = 2000;

        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            return Problem("ConnectionString PantheraDb não configurada.");

        using var conn = new NpgsqlConnection(cs);

        var sql = @"
SELECT TOP (@top)
    codigo,
    designacao,
    quant,
    valor,
    mesa,
    data
FROM dbo.pedido
WHERE mesa = @mesa
  AND estado IS NULL
ORDER BY [data] DESC;
";
        var rows = await conn.QueryAsync(sql, new { top, mesa });
        return Ok(rows);
    }
}
