using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IConfiguration _config;

    public OrdersController(IConfiguration config)
    {
        _config = config;
    }

    private NpgsqlConnection ApiConn()
    {
        var cs = _config.GetConnectionString("ApiDb");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionString 'ApiDb' não configurada.");
        return new NpgsqlConnection(cs);
    }

    // GET /api/orders/latest?take=100
    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] int take = 100, CancellationToken ct = default)
    {
        if (take < 1) take = 1;
        if (take > 500) take = 500;

        // PostgreSQL: LIMIT (sem TOP) e schema normalmente é public
        // Nota: Se a tua tabela foi criada com aspas e letras maiúsculas ("Orders"),
        // então tens de manter as aspas exatamente assim. Caso contrário, use orders.
        const string sqlOrders = @"
SELECT
  ""OrderId"", ""TableId"", ""TableName"", ""Status"",
  ""OpenedAt"", ""ClosedAt"", ""OperatorName"", ""Total"", ""UpdatedAt""
FROM ""Orders""
ORDER BY ""UpdatedAt"" DESC
LIMIT @take;
";

        await using var cn = ApiConn();
        await cn.OpenAsync(ct);

        var list = new List<object>();

        await using var cmd = new NpgsqlCommand(sqlOrders, cn)
        {
            CommandTimeout = 60
        };
        cmd.Parameters.AddWithValue("take", take);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                orderId = r["OrderId"]?.ToString(),
                tableId = r["TableId"] == DBNull.Value ? null : r["TableId"]?.ToString(),
                tableName = r["TableName"] == DBNull.Value ? null : r["TableName"]?.ToString(),
                status = r["Status"]?.ToString(),
                openedAt = r["OpenedAt"] == DBNull.Value ? null : (DateTime?)r["OpenedAt"],
                closedAt = r["ClosedAt"] == DBNull.Value ? null : (DateTime?)r["ClosedAt"],
                operatorName = r["OperatorName"] == DBNull.Value ? null : r["OperatorName"]?.ToString(),
                total = r["Total"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Total"]),
                updatedAt = r["UpdatedAt"] == DBNull.Value ? null : (DateTime?)r["UpdatedAt"]
            });
        }

        return Ok(list);
    }
}
