using HappyInventory.Application.Health;
using HappyInventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HappyInventory.Infrastructure.Health;

public sealed class HealthService(InventoryDbContext database) : IHealthService
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken) &&
                await database.Users.IgnoreQueryFilters().AnyAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return false; }
    }
}
