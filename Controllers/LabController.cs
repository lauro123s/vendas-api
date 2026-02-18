using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/lab")]
public class LabController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public LabController(IConfiguration cfg) => _cfg = cfg;

    // GET /api/lab/db
    [HttpGet("db")]
    public async Task<IActionResult> Db()
    {
        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            return Problem("ConnectionString PantheraDb não configurada.");

        await using var conn = new NpgsqlConnection(cs);

        // Postgres: current_database()
        var db = await conn.ExecuteScalarAsync<string>("select current_database();");
        return Ok(new { database = db });
    }

    // GET /api/lab/columns?table=Pedido  (opcional: &schema=public)
    [HttpGet("columns")]
    public async Task<IActionResult> Columns([FromQuery] string table, [FromQuery] string? schema = "public")
    {
        if (string.IsNullOrWhiteSpace(table))
            return BadRequest("Parâmetro 'table' é obrigatório.");

        // segurança básica contra nomes maliciosos
        table = table.Trim();
        schema = string.IsNullOrWhiteSpace(schema) ? "public" : schema.Trim();

        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            return Problem("ConnectionString PantheraDb não configurada.");

        await using var conn = new NpgsqlConnection(cs);

        // Postgres: information_schema.columns
        // Nota: em Postgres nomes podem ser case-sensitive se a tabela foi criada com aspas.
        // Por isso usamos table_name = @table (como está gravado no catálogo).
        var sql = @"
select
  column_name as ""name"",
  data_type as ""type"",
  is_nullable as ""nullable""
from information_schema.columns
where table_schema = @schema
  and table_name = @table
order by ordinal_position;
";

        var cols = await conn.QueryAsync(sql, new { table, schema });
        return Ok(cols);
    }
}
