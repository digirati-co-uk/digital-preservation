# Digital Preservation

Services related to Digital Preservation for University of Leeds DLIP project.

## Projects

There are a number of entry points (ie runnable) projects and various shared projects these applications use.

### Entry Points

* Storage.API - wrapper on top of underlying Fedora storage.
* Preservation.API - higher level Preservation API consuming Storage API.

### Shared

* DigitalPreservation.Core - various common helper functions.
* Storage.Client - HTTP client for Storage API.

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