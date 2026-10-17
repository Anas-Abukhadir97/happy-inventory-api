namespace HappyInventory.Application.Health;

public interface IHealthService
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
