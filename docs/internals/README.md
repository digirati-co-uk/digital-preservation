# Internals

How the platform is put together, how work reaches each service, and how it is built, configured and
run. For people changing the code or operating it — not for people using the API.

These pages are deliberately **not** on the
[documentation site](https://digirati-co-uk.github.io/digital-preservation-docs). That site is for a
developer with credentials calling the Preservation API, or library staff using the Preservation UI.
What is here answers a different question, for a different reader, and changes on a different rhythm.

| Page | What it covers |
|---|---|
| [overview.md](./overview.md) | The six running services, how work reaches them, where the code lives. |
| [pipeline-api.md](./pipeline-api.md) | SNS and SQS, the job claim, the tool chain, how a run proceeds and how it fails. |
| [deployment.md](./deployment.md) | Images, build and deploy workflows, configuration sections, feature flags, databases, running locally. |

## Where the boundaries are

Three repositories, three jobs:

- **This repository** — the code, and how it is built and configured. Dockerfiles, the GitHub
  Actions workflows, the `appsettings` sections each service reads, feature flags, database
  migrations, running the stack locally. That is what these pages document.
- **[uol-dlip/preservation-ops](https://github.com/uol-dlip/preservation-ops)** — the
  infrastructure, as Terraform: clusters, load balancers, hostnames, buckets, environments, KMS,
  the custom Fedora image. **It is the authority for anything infrastructural.** Where these pages
  name a cluster or a bucket it is to explain which build output goes where; if the two ever
  disagree, preservation-ops is right.
- **[digirati-co-uk/digital-preservation-docs](https://github.com/digirati-co-uk/digital-preservation-docs)**
  — the documentation site for people *using* the platform, plus runnable Python samples.

## What is not here

**iiif-builder.** Leeds have taken the service and run their own version, so documenting the copy in
this repository would describe something nobody operates. Further IIIF support is planned in the
Preservation API itself, which should make building an equivalent easier. The activity stream it
consumes is documented on the site, at
[Activity Stream](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/activity-stream/).

## Keeping these honest

Nothing checks these pages — no build, no link checking, no verification pass, unlike the site.
Treat anything infrastructural in them as a lead rather than a fact, and check it against the code,
`.github/workflows/`, the `appsettings.Example.json` files, or preservation-ops before relying on it.
