using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace VendasAPI.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public DashboardController(IConfiguration cfg) => _cfg = cfg;

    // GET /api/dashboard/summary?date=2026-02-06
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTime? date)
    {
        var cs = _cfg.GetConnectionString("PantheraDb");
        if (string.IsNullOrWhiteSpace(cs))
            return Problem("ConnectionString PantheraDb não configurada.");

        var day = (date ?? DateTime.Today).Date;
        var ini = day;
        var fim = day.AddDays(1);

        using var conn = new NpgsqlConnection(cs);

        // =========================
        // 1) KPIs do dia (dbo.Pedido)
        //    - Receita: apenas ATENDIDO
        //    - Pedidos: nº de mesas com estado NULL (mesas abertas)
        // =========================
        var kpiSql = @"
SELECT
    CAST(ISNULL(SUM(CASE 
        WHEN [estado] = 'ATENDIDO' THEN CAST([valor] AS decimal(18,2))
        ELSE 0
    END), 0) AS decimal(18,2)) AS Revenue,

    ISNULL(COUNT(DISTINCT CASE 
        WHEN [estado] IS NULL THEN [mesa]
        ELSE NULL
    END), 0) AS OpenTables
FROM dbo.Pedido
WHERE [data] >= @ini AND [data] < @fim;
";
        var kpi = await conn.QueryFirstAsync(kpiSql, new { ini, fim });

        decimal revenue = (decimal)kpi.Revenue;
        int openTables = (int)kpi.OpenTables;

        // Receita média (opcional): receita / mesas abertas
        decimal avgRevenue = openTables > 0 ? Math.Round(revenue / openTables, 2) : 0m;

        var kpis = new List<object>
        {
            new { label = "Today's Revenue", value = revenue,             prefix = "MZN ", bg = "kpi-green",  icon = "💰" },
            new { label = "Today's Order",   value = (decimal)openTables, prefix = "",     bg = "kpi-purple", icon = "🧾" },
            new { label = "Avg. Expense",    value = 0m,                  prefix = "MZN ", bg = "kpi-blue",   icon = "💸" },
            new { label = "Avg. Revenue",    value = avgRevenue,          prefix = "MZN ", bg = "kpi-orange", icon = "📈" }
        };

        // =========================
        // 2) Order Chart (últimos 7 dias)
        //    (mantive como estava: contagem por cont_dia)
        //    Se quiseres, posso ajustar para:
        //    - contagem de mesas abertas (estado NULL) por dia
        // =========================
        var weekIni = day.AddDays(-6);
        var weekFim = fim;

        var chartSql = @"
SELECT
  CAST([data] AS date) AS Dia,
  COUNT(DISTINCT [cont_dia]) AS Orders
FROM dbo.Pedido
WHERE [data] >= @weekIni AND [data] < @weekFim
GROUP BY CAST([data] AS date)
ORDER BY Dia;
";
        var chartRows = (await conn.QueryAsync(chartSql, new { weekIni, weekFim })).ToList();

        var labels = new List<string>();
        var values = new List<int>();

        var map = chartRows.ToDictionary(
            r => ((DateTime)r.Dia).Date,
            r => (int)r.Orders
        );

        for (int i = 0; i < 7; i++)
        {
            var d = weekIni.AddDays(i).Date;
            labels.Add(d.ToString("ddd"));
            values.Add(map.TryGetValue(d, out var v) ? v : 0);
        }

        // =========================
        // 3) Sales Breakdown (placeholder por agora)
        // =========================
        var salesBreakdown = new List<object>
        {
            new { name = "Total Order", value = 100 },
            new { name = "Running order", value = 0 },
            new { name = "Customer Growth", value = 0 },
            new { name = "Total Revenue", value = 100 }
        };

        // =========================
        // 4) Trending (Top 3 produtos do dia)
        //    Sugestão: filtrar também ATENDIDO (faz sentido para "vendidos")
        // =========================
        var trendingSql = @"
SELECT TOP (3)
    [designacao] AS Name,
    CAST(SUM(CAST([quant] AS decimal(18,3))) AS decimal(18,3)) AS Qty,
    CAST(SUM(CAST([valor] AS decimal(18,2))) AS decimal(18,2)) AS Total
FROM dbo.Pedido
WHERE [data] >= @ini AND [data] < @fim
  AND [estado] = 'ATENDIDO'
GROUP BY [designacao]
ORDER BY SUM(CAST([quant] AS decimal(18,3))) DESC;
";
        var trendingRows = await conn.QueryAsync(trendingSql, new { ini, fim });

        return Ok(new
        {
            date = day.ToString("yyyy-MM-dd"),

            kpis = kpis,

            salesMonthLabel = day.ToString("MMMM, yyyy"),
            salesBreakdown = salesBreakdown,

            orderChart = new
            {
                labels = labels,
                values = values
            },

            trending = trendingRows.Select(t => new
            {
                name = (string)t.Name,
                price = (decimal)t.Total, // total pago nesse produto no dia
                img = ""                  // o PHP pode colocar imagem padrão
            })
        });
    }
}
