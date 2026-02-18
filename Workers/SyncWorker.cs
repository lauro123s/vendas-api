using Microsoft.Extensions.Hosting;
using VendasApi.Services;

namespace VendasApi.Workers;

public class SyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SyncWorker> _logger;

    public SyncWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<SyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _config.GetValue<int>("Sync:IntervalSeconds", 30);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<SyncService>();

            try
            {
                await sync.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no ciclo de sincronização");
            }

            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }
}
