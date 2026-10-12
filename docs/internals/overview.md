# Internals overview


This section is for people who run the platform, or who are changing it. It describes the parts that no API caller ever sees: the services that do work in the background, how they find out there is work to do, how the whole thing is built and deployed, and how to run it on your own machine.

> **Tip**
>
> If you are integrating with the platform, you want the
> [Preservation API](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/overview/) instead. Nothing on these pages is needed to use
> it. The [Components](https://digirati-co-uk.github.io/digital-preservation-docs/introduction/components/) page is the short version of what follows.


## The shape of the system

Six things run, and they divide cleanly into two groups.

**Services that answer HTTP requests:**

| Service | What it is |
|---|---|
| Preservation API | The application-facing API. Its own PostgreSQL database holds Deposits, jobs and history. |
| Preservation UI | An ASP.NET Core Razor Pages application over the Preservation API, plus direct S3 access for uploads. |
| [Storage API](https://digirati-co-uk.github.io/digital-preservation-docs/storage-api/overview/) | The only thing that talks to Fedora. Its own PostgreSQL database holds Import Jobs and Exports. |
| [Pipeline API](./pipeline-api.md) | Runs analysis tools over deposit files. Authenticates with an API key, not Entra ID, and runs on EC2 rather than Fargate because it spawns child processes. |

**Services that consume a queue or poll a feed, and expose nothing but a health check:**

| Service | What it is |
|---|---|
| Storage API Importer | Takes Import Job identifiers off an SQS queue and runs them. Shares all its code with the Storage API; it exists so that import work can be scaled separately from the API that accepts it. |
| iiif-builder | Polls the Preservation API's activity stream and builds IIIF Manifests. Python, not .NET, with its own PostgreSQL database. The copy in this repository is no longer what Leeds run — they have taken it and diverged — so it is not described here. |

Alongside those are Fedora, three PostgreSQL databases, the S3 buckets - one for OCFL, one or more for deposit workspaces - and the SNS topics and SQS queues that carry work between services.

## How work moves between services

Nothing polls a database to find work. Three different mechanisms carry it:

* **SNS to SQS.** The Preservation API asks for a pipeline run by publishing to a topic; the Storage API queues an Import Job the same way. A topic fans out to a queue, and a single-threaded consumer loop long-polls the queue. This is how the Pipeline API and the Storage API Importer get their work.
* **An in-process channel.** The same interfaces have in-memory implementations, selected by a feature flag, so that a developer can run the whole flow on one machine with no AWS messaging at all. Exports only ever work this way.
* **An HTTP callback, or a polled stream.** The Pipeline API calls the Preservation API back when a run finishes. The iiif-builder, which is outside the platform's trust boundary and could be any number of similar consumers, polls the activity stream instead.

The last of those is the important one architecturally: the [activity stream](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/activity-stream/) is how anything that derives something from preserved content - a IIIF Manifest, a search index, a backup - finds out that it has work to do, without the platform needing to know that it exists.

## In this section

  - [WorkspaceManager and METS](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/libraries/) — The two libraries at the centre of the Preservation API, and where their documentation will live.
  - [Pipeline API](./pipeline-api.md) — Brunnhilde, Siegfried, ClamAV and ExifTool; how a pipeline run is requested, executed and reported.
  - [Deployment](./deployment.md) — Images, workflows, configuration sections, feature flags, and running the stack locally.

## Where the code is

The source is one repository with two halves.

| Path | What is in it |
|---|---|
| `src/DigitalPreservation/` | The .NET 8 solution: the four APIs, the UI, the importer, and the shared libraries. |
| `src/iiif-builder/` | The Python iiif-builder service. |
| `src/mets-id-migration/` | A one-off, operator-run Python tool that drives the Preservation API to normalise METS identifiers. Not part of the running platform. |
| `Dockerfile.*` at the repository root | One per deployed .NET service. |
| `.github/workflows/` | Build, test, image push and deployment. |

Within the solution, the projects that carry most of the weight are `DigitalPreservation.Common.Model` (every shared resource type), `DigitalPreservation.Mets` (reading and writing METS), `DigitalPreservation.Workspace` (merging a deposit's files with its METS), and `Storage.Repository.Common` (S3 access and OCFL helpers, shared by the Storage and Pipeline APIs).
