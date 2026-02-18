using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/ps")]
public class PsController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public PsController(IConfiguration cfg) => _cfg = cfg;

    private NpgsqlConnection Conn()
    {
        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionString PantheraDb não configurada.");
        return new NpgsqlConnection(cs);
    }

    private static CommandDefinition Cmd(string sql, object? param = null, CancellationToken ct = default, int timeout = 60)
        => new(sql, param, commandTimeout: timeout, cancellationToken: ct);

    [HttpGet("areas")]
    public async Task<IActionResult> Areas(CancellationToken ct = default)
        => await Run("N_Areas", @"SELECT * FROM ""N_Areas"" LIMIT 500;", ct);

    [HttpGet("mesas")]
    public async Task<IActionResult> Mesas(CancellationToken ct = default)
        => await Run("N_Mesas", @"SELECT * FROM ""N_Mesas"" LIMIT 500;", ct);

    [HttpGet("produto-preco")]
    public async Task<IActionResult> ProdutoPreco(CancellationToken ct = default)
        => await Run("N_Produto_Preco", @"SELECT * FROM ""N_Produto_Preco"" LIMIT 1000;", ct);

    [HttpGet("produto-stock")]
    public async Task<IActionResult> ProdutoStock(CancellationToken ct = default)
        => await Run("N_Produto_Stock", @"SELECT * FROM ""N_Produto_Stock"" LIMIT 1000;", ct);

    [HttpGet("vendas")]
    public async Task<IActionResult> Vendas(CancellationToken ct = default)
        => await Run("Vendas", @"SELECT * FROM ""Vendas"" LIMIT 1000;", ct);

    private async Task<IActionResult> Run(string label, string sql, CancellationToken ct)
    {
        try
        {
            await using var conn = Conn();
            await conn.OpenAsync(ct);

            var rows = await conn.QueryAsync(Cmd(sql, null, ct, timeout: 60));
            return Ok(rows);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            return Problem(
                detail: $"Tabela '{label}' não existe no PostgreSQL (ou o nome/capitalização está diferente). " +
                        $"Tenta criar a tabela ou confirma o nome real no schema public.",
                statusCode: 500,
                title: "Tabela não encontrada"
            );
        }
        catch (Exception ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Erro ao consultar a base"
            );
        }
    }
}
