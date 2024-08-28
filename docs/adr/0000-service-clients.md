# API Service Clients

* Status: proposed
* Author: Donald Gray
* Date: 2024-08-22

## Context and Problem Statement

### Context

How best can we create clients for our API services to make it easy for developers to consume.

## Decision Outcome

The following approach will be taken for services that expose an API to be consumed by other services (e.g. Storage and Preservation APIs), for clarity I'll use `Storage` in following examples:

* `Storage.API` is the main project containing API
* `Storage.Model` is a class library containing any public models exposed by `Storage.API` and, where appropriate, methods to help working with them.
* `Storage.Client` is separate class library that contains classes for consuming `Storage.API`. It will:
  * Expose an `IStorageClient` interface for interacting with API. This could be multiple different interfaces if it makes sense, e.g. `IStorageAdminClient` or `IStorageReadonlyClient` etc.
  * Contain a concrete implementation of `IStorageClient`. This is `internal` as this will allow us to rewrite the implementation if desired. We should consider 
  * Contain a `StorageOptions` class that contains any properties to configure client. This class contains a `public const string Storage = "Storage";` property which is the name that this should be registered as in consuming AppSettings (so all appsettings would be `Storage:Foo`).
  * Contain an extension method `AddStorageClient(this IServiceCollection serviceCollection, IConfiguration configuration, string componentName)` for ease of registering in consuming client. This will:
    * Register `IOptions<StorageOptions>` dependency.
    * Register `IStorageApiClient`, using values from `IOptions<StorageOptions>` to bootstrap.
    * Add `x-requested-by` header with value of `componentName`
    * Add `TimingHandler` to log individual request timing

### Positive Outcomes

Consuming client is relatively straight forward:

```cs
// program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddStorageClient(builder.Configuration, "Preservation-API")
    .AddTransient<StorageService>();

// consumer
public class StorageService(IStorageClient storageClient)
{
    public async Task<Storage> GetStorage(int id) => storageClient.Get(id);
}
```

`Storage.Model` and/or `Storage.Client` could be exposed as a nuget package if required to be used elsewhere.

`IStorageClient` could contain higher level function that make multiple calls to the actual API for ease of use. e.g. For paginated responses `IStorageClient` could have a `GetAll()` method that iterates over all pages.