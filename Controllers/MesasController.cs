using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MesasController : ControllerBase
{
    private readonly IConfiguration _cfg;

    public MesasController(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            return Problem("ConnectionString PantheraDb não configurada.");

        using var conn = new NpgsqlConnection(cs);

        var sql = @"
            SELECT
                ordem      AS NumeroMesa,
                estado     AS Estado,
                nota_user  AS Funcionario
            FROM dbo.N_Mesas
            WHERE ordem BETWEEN 1 AND 40
            ORDER BY ordem;
        ";

        var rows = await conn.QueryAsync(sql);
        return Ok(rows);
    }
}
