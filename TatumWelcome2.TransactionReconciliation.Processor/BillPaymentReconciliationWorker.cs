using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TatumWelcome2.BLL.IServices;

namespace TatumWelcome2.TransactionReconciliation.Processor;

/// <summary>
/// Background loop that settles Pending bill payments: every N seconds it
/// re-sends each one to Bank A. All the rules live in
/// ICustomerBillPaymentService; this class only decides WHEN to run it.
/// </summary>
public sealed class BillPaymentReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillPaymentReconciliationWorker> _logger;
    private readonly ReconciliationOptions _options;

    public BillPaymentReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ReconciliationOptions> options,
        ILogger<BillPaymentReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));

        _logger.LogInformation(
            "Bill payment reconciliation worker started, every {Seconds}s.",
            interval.TotalSeconds);

        await RunOnePassAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnePassAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown (Ctrl+C).
        }

        _logger.LogInformation("Bill payment reconciliation worker stopped.");
    }

    private async Task RunOnePassAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The service is SCOPED, so each pass gets its own scope.
            using IServiceScope scope = _scopeFactory.CreateScope();

            var service = scope.ServiceProvider
                .GetRequiredService<ICustomerBillPaymentService>();

            await service.ReconcilePendingAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed pass must not kill the worker.
            _logger.LogError(ex, "Bill payment reconciliation pass failed; will retry on the next tick.");
        }
    }
}