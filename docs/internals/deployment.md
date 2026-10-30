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

`.github/workflows/build.yml` runs on every push to `main`, every tag, and every pull request. A first job, `changes`, checks whether the pull request touches `src/DigitalPreservation/`, `.github/` or a `Dockerfile.*`; on a pull request that touches none of them, the jobs below report **Skipped** rather than not running at all - a required status check stays Pending forever for a workflow that was never triggered, but a job skipped by an `if:` satisfies it. Pushes to `main`, tags and manual runs always run everything, regardless of what changed.

1. **SonarCloud analysis** and **build and test** run in parallel, not in sequence - only image publishing waits on the test job:
   - SonarCloud analysis builds the solution (deliberately Debug, for accurate line-coverage attribution) and collects coverage, running the same `--filter 'Category!=Manual'` test set as the build-and-test job below.
   - Build and test: `dotnet build` then `dotnet test` with `--filter 'Category!=Manual'`, so tests that must not run unattended - ones needing a local corpus, a stopwatch, or that are really a fixture generator rather than a test - are excluded from CI. This job, `test-dotnet`, is the required status check on `main`.

2. **Build and push images**, one matrix job per Dockerfile, tagged with the commit SHA, once `test-dotnet` has passed.


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
| `AccessConditionProfile` | The access-condition vocabulary served at `/access-conditions` (issue #433): `RestrictionAxis` (label + `Values`), `RightsAxis` (label + optional `Groups` of rights-statement short labels), `Derived` (type, label, ordered `Values`, `Matrix` of restriction value → group key → value, with `__none__` and `*` keys) and `AllowMultipleRestrictions`. Only `RestrictionAxis.Values` is needed for the simple case; `appsettings.Example.json` shows it and `appsettings.json` carries the full Leeds profile. **Validated at startup**: a matrix that names an unknown row, column or value, or multi-select together with `Derived`, stops the service with the reasons in the log. The UI has no copy of its own; it fetches the API's and caches it for five minutes, so a profile change needs only the API redeployed. The documentation site describes the JSON shape. |
| `RangeTypes` | The logical-range type vocabulary served at `/range-types`. |
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
> The Pipeline API's settings files carry `DisableAuth`, but no code in that service reads it - it
> authenticates with `X-API-KEY` unconditionally, so changing `DisableAuth` has no effect.
> `UseLocalHostedServiceForPipeline` is different (issue #352): it now gates the internal
> `POST /pipeline/jobs` endpoint that lets Preservation API deliver pipeline jobs over plain HTTP in
> local development instead of SNS - every deployed environment leaves it unset (false), so that
> endpoint 404s there, but it is read.


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

### Heartbeat and reaping

Whichever mode runs a job, `ImportJobRunner` writes a `last_heartbeat` timestamp (the database's own clock) roughly every `ImportJobs:HeartbeatIntervalSeconds` (default 60) for as long as the job is actually executing. A job whose heartbeat has gone stale for longer than `ImportJobs:HeartbeatWindowMinutes` (default 10) - its processor crashed, or an ECS task was replaced mid-run - is **reaped**: a conditional `UPDATE` marks it inactive and records a `completedWithErrors` Import Job Result with a reason, so the Preservation API learns the job finished and the Archival Group stops being blocked. A job that has never sent a heartbeat (still queued) is never reaped by age - SQS retains a message for up to 14 days, so it may genuinely not have been picked up yet.

Two things trigger a reap: queuing a new job for the same Archival Group (`GetActiveJobsForArchivalGroup`), and `ImportJobReapService`, a periodic sweep in the Storage API (`ImportJobs:ReapSweepIntervalMinutes`, default 5) that checks every Archival Group, not just one a caller happens to be touching right now - without it, a group whose only job was abandoned would stay blocked until a human intervened. The sweep's reap is idempotent, so it is safe to run on more than one Storage API instance at once.

Startup fails fast if `HeartbeatIntervalSeconds` is configured at or above `HeartbeatWindowMinutes × 60`, since that would let a live job's own gaps between heartbeats be mistaken for abandonment.

A row that is already stuck with **no** heartbeat at all - the dead-lettered SQS messages from #245, or any job that was mid-run on the old (pre-heartbeat) importer when this deployed - is never reaped by this mechanism, by design: nothing here can tell "genuinely still queued" apart from "will never run again" for a job that never started. For those, an operator calls `POST /import/{jobId}/cancel` on the Storage API with a JSON body `{"reason": "..."}` (#366) - machine-auth only, like every other route on `ImportController`. It refuses a job that has a heartbeat (that one belongs to the automatic reap above) or is already inactive, and records a `completedWithErrors` result with the given reason, same as an automatic reap - including for a row old enough to predate the "waiting" result Preservation API now writes at queue time, where it synthesises a minimal one from the originally submitted job instead. This replaces the raw `update import_jobs set active = false where id = …` SQL that was previously the only remedy.

Export has the same shape of switch but only one working setting: `UseLocalHostedServiceForExport` must be `true`, because the separate export service has not been written and the Storage API throws on startup otherwise. Exports are queued on an in-memory channel bounded at ten, so a restart loses anything still waiting.

### Reaching Fedora: two network timeouts, and what guards each

The importer and both APIs reach Fedora, and the importer and the UI reach Storage API and Preservation API, through an internet-facing ALB, so that traffic leaves the VPC through a NAT gateway and comes back in. Fedora itself never calls back into either API. Two limits on that path matter for anything long-running, and they are easy to mistake for one another, or for Fedora itself being slow (issue #350 traces a real 2025 incident where they were):

- **The NAT gateway's fixed 350s idle timeout.** A connection silent for longer than that loses its NAT mapping, and the next packet on it fails - a broken pipe on a slow GET, or what looks like a hung PUT, even though Fedora already wrote the response: the ALB just can't deliver it back through the expired mapping. **TCP keepalive** (`DigitalPreservation.Core/Web/HttpClientBuilderX.cs`, `ConfigureTcpKeepAlive`) guards this - a probe sent every `keepAliveInterval` once a connection has been idle for `keepAliveTime` keeps the NAT mapping alive for as long as the connection is genuinely still in use. Configured on every client that talks to Fedora, Storage API or Preservation API; currently 120s idle / 30s interval / 5 retries - a dead connection is noticed in about 270s, comfortably under the 350s NAT limit and far faster than a naive 120s/60s/60 would manage (issue #383 review point 7).
- **The ALB's own idle-timeout ceiling** (around 4,000s). Nothing resets this one, and nothing normally needs to - but a single commit PUT on a very large import can run long enough to approach it. `FedoraTransactionMonitor`'s post-commit poll (see below) is the safety net for this gap, not the normal path for a commit that is simply still running. That poll is itself not a quick status check once the transaction has passed its 3-minute expiry (next bullet): per fcrepo's own source, a GET on it then blocks - Fedora synchronizes the expiry check against the commit - until the commit finishes, so in practice the "poll" is a second long request over the same ALB/NAT path as the commit PUT itself, and is exposed to the same failure modes.

**Fedora's own transaction expiry** is a third, unrelated timer: an atomic transaction (`fcr:tx`) expires after 3 minutes of *Fedora-side* inactivity (its default `fcrepo.session.timeout`, not overridden here) - nothing to do with the network. `ExecuteImportJobHandler` keeps a transaction alive with a periodic `POST` to it for as long as the import loop is still adding, patching or deleting resources, and checks the result of each keep-alive: if Fedora no longer recognises the transaction - it expired during a stall long enough to pass that 3 minutes - the loop fails the job early rather than writing further into a transaction that is already gone, or reaching a commit whose outcome would then be undefined.

**A returned commit still isn't automatically trustworthy.** `FedoraClient.CommitTransaction` records the commit PUT's HTTP status but does not throw for a non-2xx one, so the handler checks it explicitly: only a 2xx is a plain success. And if `FedoraTransactionMonitor`'s post-commit poll ever does have to cancel the commit PUT - because it found the transaction already gone on one of its (rare, 30-minute) checks - the PUT's own outcome is unknowable from a cancelled request alone: Fedora may have committed successfully before the NAT mapping expired, or the transaction may genuinely have been rolled back by Fedora's own expiry. The handler confirms which happened with a cheap version check against the Archival Group (`GetArchivalGroupVersion`) rather than assuming either outcome, and rather than a full read of the group - which, for a wide one, can itself take 10-30 minutes (#244).

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

