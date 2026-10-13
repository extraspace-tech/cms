using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Platform.Data;

/// <summary>
/// Migrations run by hand on shared environments (S0 spec §5). A build that is ahead of the database stops here,
/// so ECS keeps the previous version serving instead of a half-working new one.
/// </summary>
internal sealed class MigrationGuard(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"The database is missing {pending.Count} migration(s), first: {pending[0]}. Run the script in docs/migrations before deploying this build.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
