using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/relatorios/mesas")]
public class RelatorioMesasController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public RelatorioMesasController(IConfiguration cfg) => _cfg = cfg;

    private NpgsqlConnection Conn()
    {
        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionString PantheraDb não configurada.");
        return new NpgsqlConnection(cs);
    }

    // GET /api/relatorios/mesas/abertas
    [HttpGet("abertas")]
    public async Task<IActionResult> MesasAbertas(CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    ordem     AS ""Mesa"",
    estado    AS ""Estado"",
    nota_time AS ""HoraAbertura"",
    nota_user AS ""Atendente""
FROM ""N_Mesas""
WHERE ordem BETWEEN 1 AND 40
  AND estado = 'OCUPADA'
ORDER BY nota_time DESC;
";

        await using var conn = Conn();
        await conn.OpenAsync(ct);

        var cmd = new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 60);
        var rows = await conn.QueryAsync(cmd);
        return Ok(rows);
    }

    // GET /api/relatorios/mesas/todas
    [HttpGet("todas")]
    public async Task<IActionResult> Todas(CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    ordem     AS ""Mesa"",
    estado    AS ""Estado"",
    nota_time AS ""HoraAbertura"",
    nota_user AS ""Atendente""
FROM ""N_Mesas""
WHERE ordem BETWEEN 1 AND 40
ORDER BY ordem;
";

        await using var conn = Conn();
        await conn.OpenAsync(ct);

        var cmd = new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 60);
        var rows = await conn.QueryAsync(cmd);
        return Ok(rows);
    }
}
