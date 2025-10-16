# Digital Preservation

hahah

Services related to Digital Preservation for University of Leeds DLIP project.

## Projects

There are a number of entry points (ie runnable) projects and various shared projects these applications use.

### Entry Points

* Storage.API - wrapper on top of underlying Fedora storage.
* Preservation.API - higher level Preservation API consuming Storage API.
* DigitalPreservation.UI - user interface for interacting with above services.

### Shared

* DigitalPreservation.Core - various common helper functions.
* Storage.Client - HTTP client for Storage API.
* Preservation.Client - HTTP client for Preservation API.

## Technology :robot:

Projects written in .net8 using libraries including:

* [Serilog](https://serilog.net/) - structured logging framework.
* [Mediatr](https://github.com/jbogard/MediatR) - mediator implementation for in-proc messaging.
* [XUnit](https://xunit.net/) - automated test framework.

## Getting Started

Each entry point has a Dockerfile. There is a docker compose file available for ease of running services. Rename `.env.dist` to `.env` as a starting point for configuration.

```bash
# build images
docker compose build

# run images
docker compose up
```

For local development `docker-compose.local.yml` can be used to run any dependencies (e.g. Postgres)

```bash
docker compose -f docker-compose.local.yml  up
```

You also need to read Fedora's PostgreSQL database through the bastion host; this should use port 5435

## Migrations

Migrations can be added by running the following:

```bash
cd src/DigitalPreservation

dotnet ef migrations add "<migration-name>" -p Preservation.API -o Data/Migrations
```

## Deployments

Github actions are used for deployments. 

The overall process is:
* `dotnet test` to confirm build + tests passing
* build and push Docker images to ECR, tagged with current branch/sha1
* push to *environment*, which consists of:
  * pulling docker image from ECR
  * retag image with environment-specific tag and push to ECR (images deployed with mutable tags)
  * restart ECS service to pull down latest image

Builds kicked off by:
* Create PR (non-draft) - push to `development`
  * Push can be avoided if PR has "no-deploy" label
* Push to `main` - push to `development` and `production`
* Create version tag - push to `production`
* Manual running action

> [!NOTE]
> Only `development` environment currently exists. Builds will need expanded when we have further environments.