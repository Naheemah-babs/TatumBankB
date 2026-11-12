
using TatumWelcome2.BLL;

namespace TatumWelcome2.Services;

public class InterbankReconciliationService : BackgroundService
{
    private readonly InterbankOutgoingTransferService _transferService;

    public InterbankReconciliationService(
        InterbankOutgoingTransferService transferService)
    {
        _transferService = transferService;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using PeriodicTimer timer =
            new(TimeSpan.FromSeconds(15));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _transferService.ReconcilePendingTransfersAsync(
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            
        }
    }
}
