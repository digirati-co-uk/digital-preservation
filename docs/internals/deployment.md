# Deployment


How the platform is built, what gets deployed, how it is configured, and how to run it on your own machine.

## What gets deployed

Each .NET service has a Dockerfile at the root of the repository and an image in ECR.

> **Where the infrastructure is defined**
>
> Clusters, load balancers, hostnames, buckets and the environments themselves are Terraform, in
> [uol-dlip/preservation-ops](https://github.com/uol-dlip/preservation-ops). That is the authority
> for anything infrastructural; the names below are here only to explain which build output goes
> where. If the two disagree, preservation-ops is right and this page is stale.

| Service | Dockerfile | Image | Runs on |
|---|---|---|---|
| Storage API | `Dockerfile.StorageApi` | `dlip-pres-storage-api` | Fargate |
| Storage API Importer | `Dockerfile.StorageApiImporter` | `dlip-pres-storage-api-importer` | Fargate |
| Preservation API | `Dockerfile.PreservationApi` | `dlip-pres-preservation-api` | Fargate |
| Preservation UI | `Dockerfile.UI` | `dlip-pres-preservation-ui` | Fargate |
| Pipeline API | `Dockerfile.PipelineApi` | `dlip-pres-pipeline-api` | **EC2** |
| Deposit archiver | `Dockerfile.DepositArchiver` | `dlip-pres-deposit-archiver` | AWS Lambda |
| iiif-builder | `src/iiif-builder/Dockerfile` | `dlip-pres-iiif-builder` | Fargate |

The Pipeline API is the odd one out: it spawns external processes (Python, ClamAV, Siegfried) and needs a file system mount of the deposit bucket, so it runs on an EC2-backed ECS cluster rather than Fargate. Everything else runs on the Fargate cluster.

## The build

`.github/workflows/build.yml` runs on pushes to `main`, on tags, and on pull requests that touch `src/DigitalPreservation/`, `.github/` or a `Dockerfile.*`. It does three things in sequence:


1. **SonarCloud analysis**, building the solution and collecting coverage.

2. **Build and test**: `dotnet build` then `dotnet test` with `--filter 'Category!=Manual'`, so the tests that need a real Fedora or real AWS are excluded from CI.

3. **Build and push images**, one matrix job per Dockerfile, tagged with the commit SHA.


`.github/workflows/build_iiifbuilder.yml` does the same for the Python service, on changes under `src/iiif-builder/`. It is separate because the iiif-builder has no part in the .NET solution and changes on its own rhythm.

## The deployment

`.github/workflows/deploy.yml` is called by both build workflows. For each service it re-tags the image that was just pushed with the name of the target environment, then runs `aws ecs update-service --force-new-deployment` against that service so the tasks pick it up. There is no separate artefact: the environment tag *is* the deployment.

A push to `main` deploys to `development` automatically. A pull request deploys only if it carries the `deploy` label. Other environments are reached with a manual `workflow_dispatch`.

Cluster and service names come from GitHub environment variables, so the same workflow deploys every environment. The Pipeline API job uses the EC2 cluster variable; every other job uses the Fargate one.

> **Caution**
>
> The deposit archiver's job re-tags its image but has no step to restart anything, unlike every other
> service. The archiver is a Lambda rather than an ECS service, and re-tagging an image does not update
> a Lambda's code, so do not assume a merge has deployed it.


## Configuration

.NET services are configured the usual ASP.NET Core way: `appsettings.json`, overridden by `appsettings.{Environment}.json`, overridden by environment variables (with `:` written as `__`). In AWS, secrets and environment-specific values arrive as task definition environment variables.

Each service ships an `appsettings.Example.json` showing every section it expects. **Start from that file**, replacing the placeholder values - but read the caution about `DisableAuth` below before you do.

The sections, and what each is for:

### Storage API

| Section | What it configures |
|---|---|
| `ConnectionStrings` | Its own PostgreSQL database, and a read-only connection to Fedora's own database, which the API uses to populate large Containers quickly and to run [search](https://digirati-co-uk.github.io/digital-preservation-docs/storage-api/activity-and-content#fedora-search). |
| `RunMigrations` | Whether to apply EF Core migrations at startup. |
| `Fedora` | Fedora's root URI, admin credentials, request timeout, the OCFL bucket and key prefix, and whether a digest is required on every incoming Binary. |
| `Storage-AWS` | The AWS credential profile and region used for S3. |
| `AwsStorage` | The default deposit working bucket, and the key used for the S3 health check. |
| `Converter` | The public root URI the API mints its own resource URIs against. Get this wrong and every `id`, `content` and activity URI it emits points somewhere useless. |
| `AzureAd` | Identity provider instance, tenant, client id and the audiences accepted on incoming tokens. |
| `KnownClients` | The allow-list of machine callers keyed by the signed `azp`/`appid` claim, each with a friendly name and optionally its own deposit bucket. See [Authentication](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/authentication#being-a-known-client). |
| `FeatureFlags` | See below. |
| `ImportExport` | The SNS topic ARNs the API publishes import and export requests to. |

### Storage API Importer

The same sections as the Storage API, minus everything to do with serving HTTP: no `AzureAd`, no `KnownClients`, no `FeatureFlags`. Its `ImportExport` section carries the SQS **queue names** it consumes from, where the Storage API's carries the **topic ARNs** it publishes to. `RunMigrations` is off - the Storage API owns the schema.

### Preservation API

| Section | What it configures |
|---|---|
| `ConnectionStrings` | Its own PostgreSQL database. |
| `RunMigrations` | Whether to apply EF Core migrations at startup. |
| `Storage` | The root URI of the Storage API it calls. |
| `ResourceMutator` | The Storage API and Preservation API roots, used to rewrite URIs in both directions as resources pass through. |
| `Storage-AWS`, `AwsStorage` | AWS profile and region; the default deposit working bucket and health check key. |
| `AzureAd`, `KnownClients` | As for the Storage API. |
| `TokenProvider` | The registration the API uses to obtain its *own* token when calling the Storage API. |
| `PipelineOptions` | The SNS topic ARN pipeline run requests are published to. |
| `AccessConditions`, `RangeTypes` | The controlled vocabularies served at `/access-conditions` and `/range-types`. |
| `FeatureFlags` | See below. |

### Pipeline API

| Section | What it configures |
|---|---|
| `Serilog` | Logging. |
| `Storage-AWS`, `AWS` | AWS profile and region. |
| `ApiKeyOptions` | The API key and header name this service authenticates its callers with. It does not use Entra ID. |
| `StorageOptions` | The file system mount path at which the deposit bucket appears. |
| `PipelineToolOptions` | Paths to Python and Brunnhilde, the platform's directory separator, and the name of the folder within a deposit that tools are run over. |
| `PipelineOptions` | The SNS topic ARN and SQS queue name for pipeline jobs. |
| `Preservation`, `ConnectionStrings:PreservationApiBaseUrl` | Where to call the Preservation API back when a run finishes. |
| `AzureAd`, `TokenProvider` | The registration used for those callbacks. |
| `FeatureFlags` | See below. |

### Preservation UI

| Section | What it configures |
|---|---|
| `Preservation` | The root URI of the Preservation API. |
| `Storage-AWS`, `AwsStorage` | AWS profile and region, and the deposit bucket the UI uploads into directly. |
| `AzureAd` | The interactive sign-in registration: tenant, client id and secret, the OIDC callback path, and the scope requested when calling the Preservation API on a user's behalf. |
| `IdentityService` | The Leeds Identity Service root, timeout and API key. |
| `MvpCatalogueApi` | The catalogue API root, query template, timeout and API key. |
| `FeatureFlags` | See below. |

## Feature flags

| Flag | Services | Effect |
|---|---|---|
| `DisableAuth` | Preservation API, Storage API, Importer | Removes authentication entirely. Local development only. |
| `UseLocalHostedServiceForImport` | Storage API | `true` runs imports in the Storage API process; `false` publishes them to SNS for the Importer. |
| `UseLocalHostedServiceForExport` | Storage API | Must be `true`. See [Import processing](#import-processing). |
| `EnableIiifMediaEndpoints` | Preservation API | Enables the anonymous IIIF Manifest and media routes for a Deposit. |
| `EnableMetsIdNormalisation` | Preservation API | Enables the METS identifier normalisation endpoint and the activity-suppression it needs. |
| `NormaliseMetsIdsOnWrite` | Anything that writes METS | Normalises identifiers every time a METS file is written, rather than only on request. |
| `ShowPipeline`, `ShowNormaliseMetsIds` | Preservation UI | Show or hide those actions in the interface. |

> **DisableAuth in the Storage API example**
>
> `Storage.API/appsettings.Example.json` ships `FeatureFlags:DisableAuth` as `"true"`. Anyone following
> the "start from the example file" advice above gets a Storage API with authentication switched off -
> the flag skips the whole filter stack and the authentication middleware with it. Since the Storage
> API is the only thing that can write to Fedora, this is the worst place in the platform for that to
> be the default. The Preservation API's example correctly ships `"false"`. **Set it explicitly for
> every non-local deployment.**


> **Note**
>
> The Pipeline API's settings files carry `DisableAuth` and `UseLocalHostedServiceForPipeline`, and no
> code in that service reads either. It authenticates with `X-API-KEY` unconditionally. Changing them
> has no effect.


## Databases and migrations

Three PostgreSQL databases, owned separately and never shared:

| Database | Owner | Holds |
|---|---|---|
| Preservation | Preservation API | Deposits, import jobs, pipeline jobs, activity, history. |
| Storage | Storage API (and the Importer, read/write) | Import jobs and their results, exports. |
| iiif-builder | iiif-builder | One row per activity stream event it has read. |

The two .NET databases use EF Core migrations, applied at startup when `RunMigrations` is `true`. The iiif-builder's table is created by hand.

Fedora has a database of its own, which the Storage API reads directly - never writes - for search and for some storage-map work.

> **Tip**
>
> Everything in all of these is working state and history. The preserved content is the OCFL in S3, and
> nothing in any database is needed to recover it.


## Import processing

Import is the one part of the system that can be scaled independently, because it is the one part that can run for hours.

* With `FeatureFlags:UseLocalHostedServiceForImport` set to `true`, the Storage API queues jobs on an in-memory channel and a hosted service inside the same process runs them. Simple, and what a developer runs locally.
* With it `false`, the Storage API publishes the job identifier to an SNS topic, which fans out to an SQS queue. The **Storage API Importer** - a separate ECS service built from the same code - long-polls that queue and runs the jobs. The Storage API itself then does no import work at all, and can be scaled for request handling while the importer is scaled for throughput.

Either way the caller sees exactly the same thing: `201 Created` and an Import Job Result to poll.

Export has the same shape of switch but only one working setting: `UseLocalHostedServiceForExport` must be `true`, because the separate export service has not been written and the Storage API throws on startup otherwise. Exports are queued on an in-memory channel bounded at ten, so a restart loses anything still waiting.

## Running locally

The supported local setup runs the databases and the Python service in Docker and the .NET services from your IDE or `dotnet run`.


1. **Start the dependencies.**

   ```bash
   docker compose -f docker-compose.local.yml up -d db-preservation db-storage
   ```

   That brings up the Preservation database on 5433 and the Storage database on 5434. The same file also defines `db-iiifbuilder` (5437), the `iiifbuilder` service itself, and a local ClamAV daemon on 3310 for testing the Pipeline API - start those only if you need them.

2. **Run the services** from `src/DigitalPreservation/`: Storage API on `https://localhost:7000` and Preservation API on `https://localhost:7228`. Their Development settings point at a shared development Fedora and deposit bucket with authentication disabled, so a valid AWS profile is needed.

3. **Build and test** the solution:

   ```bash
   dotnet build DigitalPreservation.sln
   dotnet test DigitalPreservation.sln --filter 'Category!=Manual'
   ```


> **Caution**
>
> There is also a `docker-compose.yml` at the repository root that appears to run the whole stack. It
> does not: it defines only the Storage API, Preservation API, UI and a single database, the Storage
> API has no environment file, and there is no Pipeline API or importer. Use
> `docker-compose.local.yml` and run the .NET services yourself.

