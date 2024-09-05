# Observability

* Status: proposed
* Author: Donald Gray
* Date: 2024-08-22

## Context and Problem Statement

How best can we track requests across service boundaries to see the full journey of requests.

## Decision Drivers

* Visibility - must be easy to see all logs / requests made for an individual request.
* Unintrusive - ideally this shouldn't require custom calls to a library/framework - it should be transparently recorded.

## Considered Options

* [AWS X-Ray](https://docs.aws.amazon.com/xray/latest/devguide/aws-xray.html)
* [OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
* Correlation Id logging

## Decision Outcome

Use Correlation Id logging with Serilog logs and implement OTel with [AWS OpenTelemetry](https://aws-otel.github.io/docs/getting-started/dotnet-sdk) and AWS X-Ray.

### Correlation Id

Correlation Id should be preserved across service boundaries if provided in `x-correlation-id` header. If no header provided a guid should be generated and added. This can be added via `.AddCorrelationIdHeaderPropagation()` to service collection.

All requests should log the current Id by defining `{CorrelationId}` in Serilog templates and calling `.Enrich.WithCorrelationId()` when configuring Serilog. e.g.

```cs
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((hostContext, loggerConfiguration)
    => loggerConfiguration
        .ReadFrom.Configuration(hostContext.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithCorrelationId());

builder.Services
    .AddHttpContextAccessor() // required for .Enrich.WithCorrelationId()
    .AddCorrelationIdHeaderPropagation();
```

### OTel X-Ray

Follow guide for [AWS OTel](https://aws-otel.github.io/docs/getting-started/dotnet-sdk) to configure metrics + tracing for httpClient, EF etc. Use drop-in where possible but we can configure as required.

Things to consider:
* Using a `DelegatingHandler` (e.g. alongside or replacing `TimingHandler`) to intercept HttpClients (there may be a built in better way)
* Using a custom `IPipelineBehavior` for internal tracing/metrics for Mediatr requests