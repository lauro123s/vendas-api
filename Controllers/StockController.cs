using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public StockController(IConfiguration cfg) => _cfg = cfg;

    // GET /api/stock?top=500&q=frango
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int top = 500, [FromQuery] string? q = null)
    {
        if (top < 1) top = 200;
        if (top > 5000) top = 5000;

        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            return Problem("ConnectionString PantheraDb não configurada.");

        using var conn = new NpgsqlConnection(cs);

        var sql = @"
SELECT TOP (@top)
    s.produto AS codigo,
    p.nome    AS nome,
    p.sector  AS sector,
    CAST(ISNULL(SUM(CAST(s.quant AS decimal(18,3))), 0) AS decimal(18,3)) AS stockDisponivel
FROM dbo.N_Produto_Stock s
LEFT JOIN dbo.Produto p
    ON p.cont = s.produto   -- ✅ CORRETO: Produto.cont liga com N_Produto_Stock.produto
WHERE
    (@q IS NULL OR @q = '' OR
     p.nome LIKE '%' + @q + '%' OR
     CAST(s.produto AS varchar(50)) LIKE '%' + @q + '%')
GROUP BY
    s.produto, p.nome, p.sector
ORDER BY
    p.nome;
";

        var rows = await conn.QueryAsync(sql, new { top, q });
        return Ok(rows);
    }
}
