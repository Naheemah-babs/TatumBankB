using TatumWelcome2.BLL;

namespace TatumWelcome2.Services;

public sealed class BillPaymentReconciliationService : BackgroundService
{
    private readonly BillPurchaseService _service;
    private readonly ILogger<BillPaymentReconciliationService> _logger;

    public BillPaymentReconciliationService(
        BillPurchaseService service,
        ILogger<BillPaymentReconciliationService> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using PeriodicTimer timer =
            new(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _service.ReconcilePendingAsync(
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Bill payment reconciliation failed.");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Application shutting down.
        }
    }
}