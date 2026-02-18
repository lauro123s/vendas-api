using System.Data;
using Microsoft.Data.SqlClient;

namespace VendasApi.Services;

public class SyncService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SyncService> _logger;

    public SyncService(IConfiguration config, ILogger<SyncService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private SqlConnection PantheraConn() => new(_config.GetConnectionString("PantheraDb"));
    private SqlConnection ApiConn()      => new(_config.GetConnectionString("ApiDb"));

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var batchId = Guid.NewGuid();
        await LogJobAsync("SYNC_ALL", "OK", "Início da sincronização", batchId, started: true);

        await SyncTablesStatusAsync(batchId, ct);
        await SyncOrdersAsync(batchId, ct);
        await SyncExpensesAsync(batchId, ct);
        await SyncCashMovementsAsync(batchId, ct);

        await LogJobAsync("SYNC_ALL", "OK", "Fim da sincronização", batchId, finished: true);
    }

    // ------------------------
    // 1) MESAS -> TablesStatus
    // ------------------------
    private async Task SyncTablesStatusAsync(Guid batchId, CancellationToken ct)
    {
        var sql = @"
SELECT
  m.codigo AS TableId,
  m.estado AS Estado,
  m.sector AS Sector,
  m.cont AS Cont,
  m.nota_time AS MesaOpenedAt,

  (SELECT MAX(p.data) FROM Pedido p WHERE p.cont = m.cont) AS LastOrderAt,
  (SELECT SUM(ISNULL(p.quant,0) * ISNULL(p.valor,0)) FROM Pedido p WHERE p.cont = m.cont) AS CurrentTotal,
  (SELECT MIN(p.data) FROM Pedido p WHERE p.cont = m.cont) AS FirstOrderAt
FROM Mesa m;
";

        await using var src = PantheraConn();
        await src.OpenAsync(ct);

        await using var dst = ApiConn();
        await dst.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, src);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        int upserted = 0;

        while (await r.ReadAsync(ct))
        {
            var tableId = r["TableId"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(tableId)) continue;

            var estado = r["Estado"]?.ToString();

            // Normalização simples (podes ajustar depois)
            var status = "UNKNOWN";
            if (!string.IsNullOrWhiteSpace(estado))
            {
                var e = estado.Trim().ToLower();
                if (e.Contains("abert") || e.Contains("ocup")) status = "OPEN";
                else if (e.Contains("fech") || e.Contains("liv")) status = "CLOSED";
            }

            var mesaOpenedAt = r["MesaOpenedAt"] == DBNull.Value ? (DateTime?)null : (DateTime)r["MesaOpenedAt"];
            var firstOrderAt = r["FirstOrderAt"] == DBNull.Value ? (DateTime?)null : (DateTime)r["FirstOrderAt"];
            var openedAt = mesaOpenedAt ?? firstOrderAt;

            var lastOrderAt = r["LastOrderAt"] == DBNull.Value ? (DateTime?)null : (DateTime)r["LastOrderAt"];
            var currentTotal = r["CurrentTotal"] == DBNull.Value ? 0m : Convert.ToDecimal(r["CurrentTotal"]);

            var merge = @"
MERGE dbo.TablesStatus AS T
USING (SELECT
  @TableId AS TableId,
  @TableName AS TableName,
  NULL AS AreaName,
  @Sector AS SectorName,
  @Status AS Status,
  @OpenedAt AS OpenedAt,
  @LastOrderAt AS LastOrderAt,
  @CurrentTotal AS CurrentTotal,
  CASE WHEN @Status = 'OPEN' THEN 1 ELSE 0 END AS OrdersCount,
  NULL AS OperatorName
) AS S
ON T.TableId = S.TableId
WHEN MATCHED THEN UPDATE SET
  TableName = S.TableName,
  SectorName = S.SectorName,
  Status = S.Status,
  OpenedAt = S.OpenedAt,
  LastOrderAt = S.LastOrderAt,
  CurrentTotal = S.CurrentTotal,
  OrdersCount = S.OrdersCount,
  UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT (TableId, TableName, AreaName, SectorName, Status, OpenedAt, LastOrderAt, CurrentTotal, OrdersCount, OperatorName)
  VALUES (S.TableId, S.TableName, S.AreaName, S.SectorName, S.Status, S.OpenedAt, S.LastOrderAt, S.CurrentTotal, S.OrdersCount, S.OperatorName);
";

            await using var up = new SqlCommand(merge, dst);
            up.Parameters.AddWithValue("@TableId", tableId);
            up.Parameters.AddWithValue("@TableName", tableId);
            up.Parameters.AddWithValue("@Sector", (object?)r["Sector"]?.ToString() ?? DBNull.Value);
            up.Parameters.AddWithValue("@Status", status);
            up.Parameters.AddWithValue("@OpenedAt", (object?)openedAt ?? DBNull.Value);
            up.Parameters.AddWithValue("@LastOrderAt", (object?)lastOrderAt ?? DBNull.Value);
            up.Parameters.AddWithValue("@CurrentTotal", currentTotal);

            await up.ExecuteNonQueryAsync(ct);
            upserted++;
        }

        await LogJobAsync("SYNC_TABLES", "OK", $"Tables upserted: {upserted}", batchId);
        _logger.LogInformation("SYNC_TABLES OK: {count}", upserted);
    }

    // ------------------------
    // 2) PEDIDO -> Orders/Items
    // ------------------------
    private async Task SyncOrdersAsync(Guid batchId, CancellationToken ct)
    {
        var since = DateTime.Now.AddDays(-7);

        var sql = @"
SELECT
    cont,
    mesa,
    cliente,
    data,
    codigo,
    designacao,
    quant,
    valor
FROM Pedido
WHERE data >= @since;
";

        await using var src = PantheraConn();
        await src.OpenAsync(ct);

        await using var dst = ApiConn();
        await dst.OpenAsync(ct);

        var orders = new Dictionary<int, List<(string? mesa, DateTime? data, string? codigo, string? designacao, decimal quant, decimal valor)>>();

        await using (var cmd = new SqlCommand(sql, src))
        {
            cmd.Parameters.AddWithValue("@since", since);
            await using var r = await cmd.ExecuteReaderAsync(ct);

            while (await r.ReadAsync(ct))
            {
                var cont = (int)r["cont"];
                if (!orders.ContainsKey(cont))
                    orders[cont] = new();

                orders[cont].Add((
                    r["mesa"]?.ToString(),
                    r["data"] == DBNull.Value ? (DateTime?)null : (DateTime)r["data"],
                    r["codigo"]?.ToString(),
                    r["designacao"]?.ToString(),
                    r["quant"] == DBNull.Value ? 0m : (decimal)r["quant"],
                    r["valor"] == DBNull.Value ? 0m : (decimal)r["valor"]
                ));
            }
        }

        foreach (var kv in orders)
        {
            var cont = kv.Key;
            var orderId = cont.ToString();
            var items = kv.Value;

            var openedAt = items.Where(x => x.data != null).Min(x => x.data) ?? DateTime.Now;
            var total = items.Sum(x => x.quant * x.valor);
            var mesa = items.FirstOrDefault().mesa;

            // UPSERT order
            var mergeOrder = @"
MERGE dbo.Orders AS T
USING (SELECT
  @OrderId AS OrderId,
  @TableId AS TableId,
  @TableName AS TableName,
  'OPEN' AS Status,
  @OpenedAt AS OpenedAt,
  NULL AS ClosedAt,
  NULL AS OperatorName,
  @Total AS Total
) AS S
ON T.OrderId = S.OrderId
WHEN MATCHED THEN UPDATE SET
  TableId = S.TableId,
  TableName = S.TableName,
  Status = S.Status,
  OpenedAt = S.OpenedAt,
  ClosedAt = S.ClosedAt,
  Total = S.Total,
  UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT (OrderId, TableId, TableName, Status, OpenedAt, ClosedAt, OperatorName, Total)
  VALUES (S.OrderId, S.TableId, S.TableName, S.Status, S.OpenedAt, S.ClosedAt, S.OperatorName, S.Total);
";
            await using (var up = new SqlCommand(mergeOrder, dst))
            {
                up.Parameters.AddWithValue("@OrderId", orderId);
                up.Parameters.AddWithValue("@TableId", (object?)mesa ?? DBNull.Value);
                up.Parameters.AddWithValue("@TableName", (object?)mesa ?? DBNull.Value);
                up.Parameters.AddWithValue("@OpenedAt", openedAt);
                up.Parameters.AddWithValue("@Total", total);
                await up.ExecuteNonQueryAsync(ct);
            }

            // Replace items
            await using (var del = new SqlCommand("DELETE FROM dbo.OrderItems WHERE OrderId=@OrderId", dst))
            {
                del.Parameters.AddWithValue("@OrderId", orderId);
                await del.ExecuteNonQueryAsync(ct);
            }

            var insertItem = @"
INSERT INTO dbo.OrderItems (OrderId, ProductId, ProductName, Qty, UnitPrice, CreatedAt)
VALUES (@OrderId, @ProductId, @ProductName, @Qty, @UnitPrice, @CreatedAt);
";
            foreach (var it in items)
            {
                await using var ins = new SqlCommand(insertItem, dst);
                ins.Parameters.AddWithValue("@OrderId", orderId);
                ins.Parameters.AddWithValue("@ProductId", (object?)it.codigo ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ProductName", (object?)it.designacao ?? DBNull.Value);
                ins.Parameters.AddWithValue("@Qty", it.quant);
                ins.Parameters.AddWithValue("@UnitPrice", it.valor);
                ins.Parameters.AddWithValue("@CreatedAt", (object?)it.data ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync(ct);
            }
        }

        await LogJobAsync("SYNC_ORDERS", "OK", $"Orders synced: {orders.Count}", batchId);
        _logger.LogInformation("SYNC_ORDERS OK: {count}", orders.Count);
    }

    // ------------------------
    // 3) DESPESAS -> Expenses
    // ------------------------
    private async Task SyncExpensesAsync(Guid batchId, CancellationToken ct)
    {
        var since = DateTime.Now.AddDays(-30);

        var query = @"
SELECT id, data, descricao, valor, obs, user_r
FROM Despesas
WHERE data >= @since;
";

        await using var src = PantheraConn();
        await src.OpenAsync(ct);

        await using var dst = ApiConn();
        await dst.OpenAsync(ct);

        await using var cmd = new SqlCommand(query, src);
        cmd.Parameters.AddWithValue("@since", since);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        int upserted = 0;

        while (await r.ReadAsync(ct))
        {
            var merge = @"
MERGE dbo.Expenses AS T
USING (SELECT
  CAST(@Id AS NVARCHAR(50)) AS ExpenseId,
  @Tipo AS ExpenseType,
  @Obs AS Description,
  @Valor AS Amount,
  @Data AS SpentAt,
  @User AS OperatorName
) AS S
ON T.ExpenseId = S.ExpenseId
WHEN MATCHED THEN UPDATE SET
  ExpenseType = S.ExpenseType,
  Description = S.Description,
  Amount = S.Amount,
  SpentAt = S.SpentAt,
  OperatorName = S.OperatorName,
  UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT (ExpenseId, ExpenseType, Description, Amount, SpentAt, OperatorName)
  VALUES (S.ExpenseId, S.ExpenseType, S.Description, S.Amount, S.SpentAt, S.OperatorName);
";

            await using var up = new SqlCommand(merge, dst);
            up.Parameters.AddWithValue("@Id", (int)r["id"]);
            up.Parameters.AddWithValue("@Tipo", r["descricao"] ?? (object)DBNull.Value);
            up.Parameters.AddWithValue("@Obs", r["obs"] ?? (object)DBNull.Value);
            up.Parameters.AddWithValue("@Valor", r["valor"] ?? 0m);
            up.Parameters.AddWithValue("@Data", r["data"] ?? (object)DBNull.Value);
            up.Parameters.AddWithValue("@User", r["user_r"] ?? (object)DBNull.Value);

            await up.ExecuteNonQueryAsync(ct);
            upserted++;
        }

        await LogJobAsync("SYNC_EXPENSES", "OK", $"Expenses upserted: {upserted}", batchId);
        _logger.LogInformation("SYNC_EXPENSES OK: {count}", upserted);
    }

    // ------------------------
    // 4) CAIXA -> CashMovements
    // ------------------------
    private async Task SyncCashMovementsAsync(Guid batchId, CancellationToken ct)
    {
        var since = DateTime.Now.AddDays(-30);

        var query = @"
SELECT id, abertura, fecho, total_pago, desp_caixa
FROM N_MovimentosCaixa
WHERE abertura >= @since OR fecho >= @since;
";

        await using var src = PantheraConn();
        await src.OpenAsync(ct);

        await using var dst = ApiConn();
        await dst.OpenAsync(ct);

        await using var cmd = new SqlCommand(query, src);
        cmd.Parameters.AddWithValue("@since", since);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        int upserted = 0;

        while (await r.ReadAsync(ct))
        {
            var id = (int)r["id"];
            var abertura = r["abertura"] == DBNull.Value ? (DateTime?)null : (DateTime)r["abertura"];
            var fecho = r["fecho"] == DBNull.Value ? (DateTime?)null : (DateTime)r["fecho"];
            var movedAt = fecho ?? abertura ?? DateTime.Now;

            var totalPago = r["total_pago"] == DBNull.Value ? 0m : (decimal)r["total_pago"];
            var despCaixa = r["desp_caixa"] == DBNull.Value ? 0m : (decimal)r["desp_caixa"];

            await UpsertCashMovementAsync(dst, $"MCX-{id}-IN", "IN",  "Total pago (turno)", totalPago, movedAt, ct);
            await UpsertCashMovementAsync(dst, $"MCX-{id}-OUT","OUT", "Despesa caixa (turno)", despCaixa, movedAt, ct);

            upserted += 2;
        }

        await LogJobAsync("SYNC_CASH", "OK", $"Cash movements upserted: {upserted}", batchId);
        _logger.LogInformation("SYNC_CASH OK: {count}", upserted);
    }

    private static async Task UpsertCashMovementAsync(SqlConnection dst, string movementId, string type, string reason, decimal amount, DateTime movedAt, CancellationToken ct)
    {
        var merge = @"
MERGE dbo.CashMovements AS T
USING (SELECT
  @MovementId AS MovementId,
  @Type AS MovementType,
  @Reason AS Reason,
  @Amount AS Amount,
  @MovedAt AS MovedAt
) AS S
ON T.MovementId = S.MovementId
WHEN MATCHED THEN UPDATE SET
  MovementType = S.MovementType,
  Reason = S.Reason,
  Amount = S.Amount,
  MovedAt = S.MovedAt,
  UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
  INSERT (MovementId, MovementType, Reason, Amount, MovedAt)
  VALUES (S.MovementId, S.MovementType, S.Reason, S.Amount, S.MovedAt);
";
        await using var cmd = new SqlCommand(merge, dst);
        cmd.Parameters.AddWithValue("@MovementId", movementId);
        cmd.Parameters.AddWithValue("@Type", type);
        cmd.Parameters.AddWithValue("@Reason", reason);
        cmd.Parameters.AddWithValue("@Amount", amount);
        cmd.Parameters.AddWithValue("@MovedAt", movedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ------------------------
    // LOGS (destino ApiDb)
    // ------------------------
    private async Task LogJobAsync(string jobName, string status, string message, Guid batchId, bool started=false, bool finished=false)
    {
        await using var cn = ApiConn();
        await cn.OpenAsync();

        if (started)
        {
            var ins = @"
INSERT INTO dbo.SyncJobLog (JobName, Status, Message, StartedAt, BatchId)
VALUES (@JobName, @Status, @Message, SYSUTCDATETIME(), @BatchId);";
            await using var cmd = new SqlCommand(ins, cn);
            cmd.Parameters.AddWithValue("@JobName", jobName);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Message", message ?? "");
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        if (finished)
        {
            var upd = @"
UPDATE dbo.SyncJobLog
SET Status=@Status, Message=@Message, FinishedAt = SYSUTCDATETIME()
WHERE BatchId=@BatchId AND JobName=@JobName AND FinishedAt IS NULL;";
            await using var cmd = new SqlCommand(upd, cn);
            cmd.Parameters.AddWithValue("@JobName", jobName);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Message", message ?? "");
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        var insert = @"
INSERT INTO dbo.SyncJobLog (JobName, Status, Message, StartedAt, FinishedAt, BatchId)
VALUES (@JobName, @Status, @Message, SYSUTCDATETIME(), SYSUTCDATETIME(), @BatchId);";
        await using var c = new SqlCommand(insert, cn);
        c.Parameters.AddWithValue("@JobName", jobName);
        c.Parameters.AddWithValue("@Status", status);
        c.Parameters.AddWithValue("@Message", message ?? "");
        c.Parameters.AddWithValue("@BatchId", batchId);
        await c.ExecuteNonQueryAsync();
    }
}
