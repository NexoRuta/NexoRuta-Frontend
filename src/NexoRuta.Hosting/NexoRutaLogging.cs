using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace NexoRuta.Hosting;

public static class NexoRutaLogging
{
    public static IHostBuilder UseNexoRutaLogging(this IHostBuilder host, string serviceName)
        => host.UseSerilog((_, _, logger) => logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .Enrich.WithActivityContext()
            .WriteTo.Console(new JsonFormatter(renderMessage: true)));

    private static LoggerConfiguration WithActivityContext(this LoggerEnrichmentConfiguration enrich)
        => enrich.With(new ActivityContextEnricher());

    private sealed class ActivityContextEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (Activity.Current is not { } activity)
                return;

            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("trace_id", activity.TraceId.ToHexString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("span_id", activity.SpanId.ToHexString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("correlation_id", CorrelationId(activity.TraceId)));
        }

        private static string CorrelationId(ActivityTraceId traceId)
            => Guid.TryParseExact(traceId.ToHexString(), "N", out var id)
                ? id.ToString("D")
                : traceId.ToHexString();
    }
}
