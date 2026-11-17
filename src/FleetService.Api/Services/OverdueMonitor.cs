using FleetService.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FleetService.Api.Services;

public class OverdueMonitorOptions
{
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>Фоновая задача: раз в N секунд помечает просроченные заявки.</summary>
public class OverdueMonitor(IServiceScopeFactory scopes, ILogger<OverdueMonitor> log,
    Microsoft.Extensions.Options.IOptions<OverdueMonitorOptions> options, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds)), clock);
        do
        {
            try
            {
                var marked = await RunOnceAsync(scopes, clock.GetUtcNow().UtcDateTime, ct);
                if (marked > 0) log.LogInformation("Marked {Count} request(s) as overdue", marked);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Overdue check failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopes, DateTime nowUtc, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var candidates = await db.ServiceRequests
            .Where(r => !r.IsOverdue && r.DueAtUtc < nowUtc)
            .ToListAsync(ct);
        var toMark = candidates.Where(r => DueDatePolicy.IsOverdue(r, nowUtc)).ToList();
        foreach (var r in toMark) r.IsOverdue = true;
        await db.SaveChangesAsync(ct);
        return toMark.Count;
    }
}
