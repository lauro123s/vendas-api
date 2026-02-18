using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/relatorios/vendas-pedido")]
public class RelatorioVendasPedidoController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public RelatorioVendasPedidoController(IConfiguration cfg) => _cfg = cfg;

    private NpgsqlConnection Conn()
    {
        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionString PantheraDb não configurada.");
        return new NpgsqlConnection(cs);
    }

    // ================================
    // DIA
    // ================================
    // GET /api/relatorios/vendas-pedido/dia?date=2026-02-06&top=5000
    [HttpGet("dia")]
    public async Task<IActionResult> PorDia([FromQuery] DateTime date, [FromQuery] int top = 5000, CancellationToken ct = default)
    {
        if (top < 1) top = 5000;
        if (top > 50000) top = 50000;

        var ini = date.Date;
        var fim = ini.AddDays(1);

        const string sql = @"
select
    data       as ""Data"",
    operador   as ""Operador"",
    mesa       as ""Mesa"",
    designacao as ""Produto"",
    quant      as ""Quantidade"",
    valor      as ""ValorPago""
from ""Pedido""
where data >= @ini
  and data <  @fim
  and estado = 'ATENDIDO'
order by data desc
limit @top;
";

        await using var conn = Conn();
        await conn.OpenAsync(ct);

        var cmd = new CommandDefinition(sql, new { ini, fim, top }, commandTimeout: 60, cancellationToken: ct);
        var rows = await conn.QueryAsync(cmd);
        return Ok(rows);
    }

    // ================================
    // SEMANA
    // ================================
    // GET /api/relatorios/vendas-pedido/semana?date=2026-02-06&top=20000
    [HttpGet("semana")]
    public async Task<IActionResult> PorSemana([FromQuery] DateTime date, [FromQuery] int top = 20000, CancellationToken ct = default)
    {
        if (top < 1) top = 20000;
        if (top > 200000) top = 200000;

        var d = date.Date;
        int diff = ((int)d.DayOfWeek + 6) % 7; // segunda=0
        var ini = d.AddDays(-diff);
        var fim = ini.AddDays(7);

        const string sql = @"
select
    data       as ""Data"",
    operador   as ""Operador"",
    mesa       as ""Mesa"",
    designacao as ""Produto"",
    quant      as ""Quantidade"",
    valor      as ""ValorPago""
from ""Pedido""
where data >= @ini
  and data <  @fim
  and estado = 'ATENDIDO'
order by data desc
limit @top;
";

        await using var conn = Conn();
        await conn.OpenAsync(ct);

        var cmd = new CommandDefinition(sql, new { ini, fim, top }, commandTimeout: 90, cancellationToken: ct);
        var rows = await conn.QueryAsync(cmd);

        return Ok(new
        {
            semana_inicio = ini.ToString("yyyy-MM-dd"),
            semana_fim = fim.AddDays(-1).ToString("yyyy-MM-dd"),
            vendas = rows
        });
    }

    // ================================
    // MÊS
    // ================================
    // GET /api/relatorios/vendas-pedido/mes?year=2026&month=2&top=50000
    [HttpGet("mes")]
    public async Task<IActionResult> PorMes([FromQuery] int year, [FromQuery] int month, [FromQuery] int top = 50000, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100) return BadRequest("Ano inválido.");
        if (month < 1 || month > 12) return BadRequest("Mês inválido.");

        if (top < 1) top = 50000;
        if (top > 500000) top = 500000;

        var ini = new DateTime(year, month, 1);
        var fim = ini.AddMonths(1);

        const string sql = @"
select
    data       as ""Data"",
    operador   as ""Operador"",
    mesa       as ""Mesa"",
    designacao as ""Produto"",
    quant      as ""Quantidade"",
    valor      as ""ValorPago""
from ""Pedido""
where data >= @ini
  and data <  @fim
  and estado = 'ATENDIDO'
order by data desc
limit @top;
";

        await using var conn = Conn();
        await conn.OpenAsync(ct);

        var cmd = new CommandDefinition(sql, new { ini, fim, top }, commandTimeout: 120, cancellationToken: ct);
        var rows = await conn.QueryAsync(cmd);
        return Ok(rows);
    }
}
