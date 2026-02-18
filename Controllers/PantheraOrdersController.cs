using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasApi.Controllers;

[ApiController]
[Route("api/panthera/orders")]
public class PantheraOrdersController : ControllerBase
{
    private readonly IConfiguration _config;

    public PantheraOrdersController(IConfiguration config)
    {
        _config = config;
    }

    private NpgsqlConnection PantheraConn()
    {
        var cs = _config.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionString 'PantheraDb' não configurada.");
        return new NpgsqlConnection(cs);
    }

    // GET /api/panthera/orders/latest?take=50
    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (take < 1) take = 1;
        if (take > 500) take = 500;

        // PostgreSQL: CTE + LIMIT
        // - lastConts: pega os últimos "cont"
        // - depois agrega por cont/mesa para montar o "order"
        const string sql = @"
with lastconts as (
    select distinct cont
    from pedido
    where data is not null
    order by cont desc
    limit @take
)
select
  p.cont as ""orderId"",
  p.mesa as ""tableId"",
  min(p.data) as ""openedAt"",
  max(p.data) as ""updatedAt"",
  sum(coalesce(p.quant,0) * coalesce(p.valor,0)) as ""total"",
  case
    when sum(case when p.estado is null then 1 else 0 end) > 0 then 'OPEN'
    else 'CLOSED'
  end as ""status""
from pedido p
join lastconts lc on lc.cont = p.cont
group by p.cont, p.mesa
order by max(p.data) desc;
";

        await using var cn = PantheraConn();
        await cn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, cn)
        {
            CommandTimeout = 120
        };
        cmd.Parameters.AddWithValue("take", take);

        var list = new List<object>();

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                orderId = r["orderId"] == DBNull.Value ? 0 : Convert.ToInt32(r["orderId"]),
                tableId = r["tableId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["tableId"]),
                status = r["status"]?.ToString(),
                openedAt = r["openedAt"] == DBNull.Value ? null : (DateTime?)r["openedAt"],
                updatedAt = r["updatedAt"] == DBNull.Value ? null : (DateTime?)r["updatedAt"],
                total = r["total"] == DBNull.Value ? 0m : Convert.ToDecimal(r["total"])
            });
        }

        return Ok(list);
    }

    // GET /api/panthera/orders/{cont}/items
    [HttpGet("{cont:int}/items")]
    public async Task<IActionResult> Items([FromRoute] int cont, CancellationToken ct = default)
    {
        const string sql = @"
select
  codigo as ""productCode"",
  designacao as ""productName"",
  quant as ""qty"",
  valor as ""unitPrice"",
  (coalesce(quant,0) * coalesce(valor,0)) as ""lineTotal"",
  estado as ""status"",
  data as ""createdAt""
from pedido
where cont = @cont
order by data;
";

        await using var cn = PantheraConn();
        await cn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, cn)
        {
            CommandTimeout = 120
        };
        cmd.Parameters.AddWithValue("cont", cont);

        var items = new List<object>();

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            items.Add(new
            {
                productCode = r["productCode"]?.ToString(),
                productName = r["productName"]?.ToString(),
                qty = r["qty"] == DBNull.Value ? 0m : Convert.ToDecimal(r["qty"]),
                unitPrice = r["unitPrice"] == DBNull.Value ? 0m : Convert.ToDecimal(r["unitPrice"]),
                lineTotal = r["lineTotal"] == DBNull.Value ? 0m : Convert.ToDecimal(r["lineTotal"]),
                status = r["status"] == DBNull.Value ? "PENDENTE" : r["status"]?.ToString(),
                createdAt = r["createdAt"] == DBNull.Value ? null : (DateTime?)r["createdAt"]
            });
        }

        return Ok(items);
    }
}
