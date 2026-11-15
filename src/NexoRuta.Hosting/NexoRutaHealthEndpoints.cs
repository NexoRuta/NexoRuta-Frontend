using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

namespace NexoRuta.Hosting;

public static class NexoRutaHealthEndpoints
{
    public static IHealthChecksBuilder AddNexoRutaReadiness(
        this IHealthChecksBuilder checks, bool sharedDataProtection)
    {
        checks.AddCheck<ProcessLifecycleHealthCheck>("process", tags: ["readiness"]);
        if (sharedDataProtection)
            checks.AddCheck<SharedDataProtectionHealthCheck>("data-protection", tags: ["readiness"],
                timeout: TimeSpan.FromSeconds(2));
        return checks;
    }

    public static void MapNexoRutaHealthEndpoints(this WebApplication app)
    {
        var readiness = new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("readiness"),
            ResponseWriter = WriteSummaryAsync
        };
        app.MapHealthChecks("/health", readiness).AllowAnonymous();
        app.MapHealthChecks("/ready", readiness).AllowAnonymous();
        app.MapGet("/live", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { status = "live" });
        }).AllowAnonymous();
    }

    private static Task WriteSummaryAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString().ToLowerInvariant())
        }));
    }

}

public sealed class ProcessLifecycleHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ready = lifetime.ApplicationStarted.IsCancellationRequested
            && !lifetime.ApplicationStopping.IsCancellationRequested;
        return Task.FromResult(ready
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The process is starting or draining."));
    }
}

public sealed class SharedDataProtectionHealthCheck(SharedKeyDbContext keyDb) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await keyDb.DataProtectionKeys.AsNoTracking()
                .OrderBy(key => key.Id)
                .Select(key => key.Id)
                .Take(1)
                .ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("The shared Data Protection key store is unavailable.");
        }
    }
}
