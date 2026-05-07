# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All .NET commands run from `src/DigitalPreservation/`.

```bash
# Build
dotnet build DigitalPreservation.sln

# Run all tests (CI skips 'Manual' category)
dotnet test DigitalPreservation.sln --filter 'Category!=Manual'

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestName"

# Run only integration tests
dotnet test --filter "Category=Integration"

# Add a Preservation API migration
dotnet ef migrations add "<migration-name>" -p Preservation.API -o Data/Migrations

# Add a Storage API migration
dotnet ef migrations add "<migration-name>" -p Storage.API -o Data/Migrations
```

Docker:
```bash
# Start local dependencies (Postgres databases)
docker compose -f docker-compose.local.yml up

# Build and run all services
docker compose build && docker compose up
```

For the Python `iiif-builder` service (`src/iiif-builder/`):
```bash
pip install -r requirements.txt
python iiif_builder.py
```

## System Overview

The stack's purpose is to create versioned **OCFL objects stored in AWS S3**. Everything else exists to support this. The system is designed so that the entire preservation state can be recovered from OCFL alone.

The data flow for a typical preservation operation:
1. A client creates a **Deposit** (staging area in S3) via Preservation API
2. Files and a **METS** XML file are uploaded to the deposit, or:
3. ...a METS file is generated and edited by the platform (MetsManager class) as files and folders are added, and external tools ran
3. A **diff ImportJob** is generated — comparing deposit contents against any existing version of the Archival Group in Fedora/OCFL
4. The ImportJob is executed — Storage API writes to **Fedora**, which writes **OCFL** objects to S3
5. Preservation API polls Storage API for import job completion and updates its own DB
6. The **Activity Stream** records the event; **iiif-builder** picks it up and publishes a IIIF manifest

## Architecture

### .NET Solution (`src/DigitalPreservation/`)

**Runnable services:**

- **`Storage.API`** — the exclusive gateway to Fedora. *Nothing else talks to Fedora directly.* Handles import/export of Archival Groups (hierarchical structures of Containers and Binaries) into OCFL via Fedora 6. Owns its own PostgreSQL DB (local: port 5434). Import processing runs either in-process or via `Storage.API.Importer` depending on feature flag.

- **`Storage.API.Importer`** — a separately deployable ECS service that consumes import jobs from SQS and runs them. Shares all the same Storage/Fedora/OCFL code as `Storage.API`; exists so import processing can scale independently.

- **`Preservation.API`** — the application-layer API. Understands METS, manages Deposits, generates diff-based ImportJobs, and triggers pipeline runs via AWS SNS. Owns its own PostgreSQL DB (local: port 5433). Consumed by: the UI, iiif-builder, Goobi workflow, admin scripts.

- **`Pipeline.API`** — receives pipeline job requests from SQS, then runs **Brunnhilde** (a Python tool that wraps Siegfried for format identification and performs virus scanning) as an external process against the deposit files. Writes characterization and virus metadata back into the deposit, then reports completion to Preservation API. Runs on an EC2 cluster (not Fargate) because it spawns child processes.

- **`DigitalPreservation.UI`** — Razor Pages/MVC web UI. Provides human access to Preservation API functions including deposit creation, large file uploads, and METS editing.

**Shared libraries:**

| Project | Purpose |
|---|---|
| `DigitalPreservation.Common.Model` | All shared domain models: `ArchivalGroup`, `Binary`, `Container`, `Deposit`, `ImportJob`, `PipelineJob`, METS wrappers, etc. |
| `DigitalPreservation.Core` | Cross-cutting: Azure AD auth helpers, `CorrelationIdMiddleware`, forwarded headers config, `AuthFilterIdentifier` |
| `DigitalPreservation.CommonApiClient` | Base HTTP client infrastructure, `TokenScope` |
| `DigitalPreservation.Workspace` | `WorkspaceManager` — the key class that merges the S3 file tree with the METS structure into a `CombinedDirectory` used by both Preservation API (for diff) and Pipeline API (for characterization) |
| `Storage.Repository.Common` | S3 access (`IStorage`), METS parsing/writing (`MetsParser`, `MetsManager`, `PremisManager`), OCFL helpers, shared by Storage and Pipeline |
| `Storage.Client` / `Preservation.Client` | HTTP clients for consuming Storage and Preservation APIs (follow ADR-0000 pattern below) |
| `LeedsDlipServices` | Leeds Identity Service client (mints PIDs/manifest URIs), MVP Catalogue API client |
| `DigitalPreservation.Utils` | Checksum, URI, string helpers |
| `Test.Helpers` | `DigitalPreservationAppFactory<TStartup>`: shared `WebApplicationFactory` wrapper for integration tests |

### Python Service (`src/iiif-builder/`)

A long-running service that polls the Preservation API activity stream for new or updated Archival Groups, then:
1. Loads the Archival Group and its METS from Preservation API
2. Calls the **Leeds Identity Service** to get the PID and public IIIF manifest URI for the item
3. Calls the **MVP Catalogue API** for descriptive metadata
4. Builds a IIIF manifest (combining METS structure for painted resources, catalogue metadata for description)
5. PUTs the manifest to **IIIF Cloud Services**

Tracks processed activities in its own PostgreSQL DB. Configured via `.env` file (see `src/iiif-builder/app/settings.py` for all env var names).

## Key Domain Concepts

**Archival Group** — the unit of preservation, corresponding to one OCFL object. May represent a single digitised item or a collection of born-digital files. Has a hierarchical structure of Containers (directories) and Binaries (files).

**Deposit** — a working staging area in S3, used to assemble content before preservation. States: `new`, `exporting`, `preserved`, `error`. Three template types: `None` (no managed METS), `RootLevel` (our standard layout), `BagIt` (BagIt-structured layout with `data/` prefix). Files must go in or below an `objects/` subfolder.

**METS** — XML metadata file that accompanies a deposit. The `MetsParser` (`Storage.Repository.Common/Mets/`) handles METS from Archivematica, EPrints, and Goobi, all of which have different structMap/fileSec layouts. The parser finds SHA256 digests and PRONOM format information from `premis:object` elements inside `mets:techMD`. When a Deposit has a managed METS (template != None), the API reads and writes it automatically.

**MetsParser vs MetsManager — deliberate dual approach**: `MetsParser` reads METS using `XDocument`/LINQ (`XNames` constants, no XmlGen dependency) because it must handle METS from third-party providers with unpredictable structure; flexibility and minimal coupling are the priority. `MetsManager` writes METS using the generated `DigitalPreservation.XmlGen` classes because we only ever write METS files we have created ourselves, so strong typing and schema correctness matter. The long-term goal is to make `MetsParser` a fully standalone library with no XmlGen dependency at all. Do not introduce XmlGen types into `MetsParser`, and do not suggest unifying the two approaches.

**CombinedDirectory** — the central data structure produced by `WorkspaceManager`. Merges the actual S3 file tree (`WorkingDirectory`) with the parsed METS physical structure tree, producing `CombinedFile` objects that carry both the deposit file and the corresponding METS file entry. Used to generate import job diffs and to validate that METS and deposit files are in sync.

**ImportJob** — a JSON document describing what to do to an Archival Group: `BinariesToAdd`, `BinariesToPatch`, `BinariesToDelete`, `BinariesToRename`, `ContainersToAdd`, `ContainersToDelete`, `ContainersToRename`. Generated by `GetDiffImportJob` by comparing the `CombinedDirectory` against the current state of the Archival Group in Fedora. SHA256 checksums from the METS are required for all binaries.

**Activity Stream** — Preservation API publishes events in IIIF Change Discovery `OrderedCollection` format whenever Archival Groups are created or updated. iiif-builder polls this (configurable interval, defaults to 60s) to trigger manifest rebuilds.

**Pipeline** — runs `Brunnhilde` against a deposit's `objects/` folder, producing characterization data (Siegfried/PRONOM format identification), virus scan results and definitions. Output is written back into the deposit's `metadata/` folder, then uploaded to the deposit workspace via `WorkspaceManager`. Pipeline API uses `X-API-KEY` auth (not Azure AD). Job states: `waiting` → `processing` → `metadataCreated` → `completed` / `completedWithErrors`.

## Key Patterns

**Feature folder / MediatR**: Each API organises work under `Features/<Domain>/Requests/` with `IRequest`/`IRequestHandler` pairs. Controllers dispatch to MediatR. Request classes carry the inputs; handler classes carry the dependencies.

**Service client ADR** (docs/adr/0000-service-clients.md): Each consumed API has a matching `*.Client` project with an `I*Client` interface (internal concrete impl), an options class with `public const string <Name>`, and an `Add*Client(IServiceCollection, IConfiguration, string componentName)` extension method. The `componentName` is sent as `x-requested-by`.

**Auth**:
- Users: Azure AD JWT (`Microsoft.Identity.Web`), configured in `AzureAd` appsettings section
- Machine-to-machine (.NET services calling each other): `X-Client-Identity` header, validated by `AuthFilterIdentifier`
- Pipeline API: `X-API-KEY` header (`ApiKeyMiddleware`)
- iiif-builder: OAuth2 Client Credentials (MSAL) to call Preservation API
- Feature flag `FeatureFlags:DisableAuth=true` disables all auth for local development

**Correlation IDs**: Propagated via `x-correlation-id` header by `CorrelationIdMiddleware`. Serilog enriches all log lines. New services should call `.AddCorrelationIdHeaderPropagation()`.

**Import job processing** (`Storage.API`): `FeatureFlags:UseLocalHostedServiceForImport=true` runs in-process (`InProcessImportJobQueue`); `false` uses SQS (`SqsImportJobQueue`). `Storage.API.Importer` is the dedicated SQS consumer for production. Export always runs in-process as a hosted service.

**Database migrations**: Applied automatically on startup via `TryRunMigrations()` when `RunMigrations: true` is set in config. Preservation API and Storage API have separate Postgres databases and separate EF `DbContext`s.

**Configuration**: Use `appsettings.Example.json` as the starting point for each service. The `Storage-AWS` config section controls AWS credential profile and region (`leeds`, `eu-west-1`).

**Testing**: Tests use `DigitalPreservationAppFactory<Program>` from `Test.Helpers`, which sets environment to `Testing` and loads `appsettings.Testing.json`. Integration tests carry `[Trait("Category", "Integration")]`. Manual-only tests use `[Trait("Category", "Manual")]` and are excluded from CI. FluentAssertions and XUnit throughout.

## CI/CD

GitHub Actions builds and tests the .NET solution, then pushes five Docker images to AWS ECR: `dlip-pres-storage-api`, `dlip-pres-storage-api-importer`, `dlip-pres-preservation-api`, `dlip-pres-preservation-ui`, `dlip-pres-pipeline-api`. The iiif-builder (`dlip-pres-iiif-builder`) has a separate workflow. Deployment retagged images with the environment name and restarts ECS services. Pipeline API uses an EC2 cluster (`EC2_CLUSTER_NAME`); all others use Fargate (`CLUSTER_NAME`). PRs with the `deploy` label are deployed to `development`; pushes to `main` deploy to `development` automatically.

## Documentation

Documentation can be found here: https://github.com/digirati-co-uk/digital-preservation-docs/tree/main/documentation

You might not wish to read all that as it may be too much context!