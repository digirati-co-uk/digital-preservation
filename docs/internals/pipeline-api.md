# Pipeline API


Pipeline API is the service that actually runs the characterization tools over a Deposit's files. It exists because that work does not fit anywhere else: it spawns external processes, it needs the files on a real file system rather than as S3 objects, and a single large deposit can occupy it for an hour. None of that belongs inside an API that has to answer requests.

This page is about how the service works. What a caller sees — where tool output lives, how to start a run and how to watch it — is on [Tool outputs and pipelines](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/tool-outputs-and-pipelines/).

## Shape of the thing

Pipeline API is an ASP.NET Core service with no database of its own. All the state about a run lives in Preservation API's database, and Pipeline API reports into it over HTTP. It has two moving parts:

* a **web API**, with two endpoints, guarded by an API key;
* a **background consumer**, `PipelineJobExecutorService`, which reads one message at a time from SQS and runs a job to completion before reading the next.

It reaches deposit files through a file system mount of the default working bucket, at `StorageOptions:FileMountPath`. This is the reason `POST /deposits/{id}/pipeline` declines a deposit that lives in any other bucket — Preservation API knows the pipeline could not reach it.

It runs on an **EC2-backed ECS cluster**, not Fargate, because of the mount and the child processes. There is one instance, so a rolling deployment briefly has two tasks on one host; the task definition therefore sets only a soft `memory_reservation` and no hard limit. That has consequences for the .NET garbage collector, which are dealt with in the image — see [Memory](#memory) below.

## How a job travels


1. Something calls `POST /deposits/{id}/pipeline` on Preservation API. The handler checks the deposit exists, that nobody else holds the lock, and that its workspace is in `AwsStorage:DefaultWorkingBucket`.

2. It mints a job identifier, writes a `PipelineRunJob` row with status `waiting`, and **publishes to the SNS topic** named by `PipelineOptions:PipelineJobTopicArn`. The message is small — deposit identifier, job identifier, who asked. Then it returns `204`.

3. The topic fans out to the SQS queue named by `PipelineOptions:PipelineJobQueue`. `SqsPipelineQueue.DequeueRequest` long-polls that queue for one message at a time.

4. Before running anything, the consumer asks Preservation API to move the job from `waiting` to `processing`. This is a **claim**, not a status report — see below.

5. `ExecutePipelineJob` does the work: Brunnhilde (Siegfried + ClamAV), ExifTool, optionally BagIt, then uploads the results back into the deposit and writes the objects into the METS.

6. It reports `completed` or `completedWithErrors` back to Preservation API, and releases the deposit lock.


Pipeline API also has its own entry point, `POST /pipeline`, which takes a `PipelineJob` body with a `depositName`, mints a job identifier, registers it with Preservation API as `waiting`, and publishes to the same SNS topic. It is a way in for something that cannot call Preservation API, and it is not how the UI or the API do it.

### The claim, and why it is a conditional UPDATE

SQS is at-least-once. The same start message can be delivered more than once for one job, and a job that ran twice would scan the deposit twice and write a second virus-scan PREMIS event into the METS for a scan that only ever happened once.

So starting a job is modelled as taking exclusive possession of it. `RunPipelineStatusHandler.ClaimJob` in Preservation API issues a single conditional UPDATE:

```sql
UPDATE pipeline_run_jobs
   SET status = 'processing', date_begun = now, last_updated = now
 WHERE deposit = @deposit AND id = @job AND status = 'waiting'
```

If it affects no rows, the job is either already running, already finished, or gone, and the API answers `409 Conflict`. `ProcessPipelineJobHandler.Handle` treats that one error code — and only that one — as a reason to abandon the delivery, before it reads the deposit, so a duplicate never reaches Brunnhilde. Both the handler and the runner log it at Information rather than Error: a duplicate delivery is the queue working correctly, and putting it into the error rate would page somebody for it.

Every *other* failure to record the start is deliberately not a reason to stop. `SqsPipelineQueue.DequeueRequest` deletes the message as soon as it reads it, so there is no redelivery to fall back on — a job abandoned because Preservation API had a transient blip would be abandoned for good. There is a test for this distinction, `ExecutePipelineJobClaimTests`, with a comment explaining why "bail out on any failed status update" is a tempting and wrong simplification.

### One scan, one event

The same rule has a second half, in the reader and writer of METS.

`MetadataReader` takes the scan's timestamp from the **last-modified time of the ClamAV log**, because that file was written by the scan itself. The obvious alternative — the time we happened to read the output — would change on every read, so a re-read would mint a fresh `eventDateTime` and the writer's duplicate check would never match.

Where there is no plausible time (`MetadataReader.ScanTimeFrom` treats anything before 2006 as an absent value wearing a date, since nothing in S3 predates S3), it returns `default` rather than inventing one. That routes the case to the writer's explicit "no scan time" branch, which prefers a duplicated event to an unrecorded scan — a decision taken in the open rather than arrived at by accident.

`MetadataManager.AlreadyRecorded` then matches a candidate event against the PREMIS virus-check events already in the `amdSec`, on that date. Two scans of the same file cannot share an instant, so a genuine re-scan writes a later time and is appended normally. Events accumulate; nothing is ever replaced.

## What a run actually does

`ExecutePipelineJob.ExecuteBrunnhilde` is a long method with a lot of early exits. Stripped to its spine:


1. **Check the mount and the objects folder.** No mount, or no `objects/` directory for this deposit, and the job ends `completedWithErrors` with the lock released.

2. **Start Brunnhilde** as a child process — `brunnhilde.py --hash sha256 <objectPath> <processMetadataPath> --overwrite` — writing into a scratch folder under `PipelineToolOptions:ProcessFolder` (`/usr/process-brunnhilde`), never straight into the deposit. Its stdout is streamed line by line looking for `Brunnhilde characterization complete.`; buffering the whole thing was costing many MB on a large run.

3. **Start ExifTool concurrently** — `exiftool -a -r <objectPath>` — with stdout copied straight to `exif/exif_output.txt` in the same scratch folder. It only reads `objects/` and writes its own subfolder, so it has no reason to wait for Brunnhilde, and its runtime hides behind ClamAV's.

4. **For a BagIt deposit, re-bag and verify.** The metadata and objects are copied into a second scratch area (`ProcessFolderBagit`, `/usr/process-bagit`), `bagit.py --source-organization uol-dlip --sha256` is run over it, and the new `manifest-sha256.txt` is compared with the deposit's existing one. Where they disagree, the METS digest is consulted as a tie-breaker; a real disagreement fails the job.

5. **Clear the metadata folder.** Everything under `metadata/` except `metadata/ad-hoc` is deleted from the workspace and the METS, via `WorkspaceManager.DeleteItems`.

6. **Capture the ClamAV version** with `clamscan --version` and write it to `virus-definition/virus-definition.txt` in the scratch folder.

7. **Upload.** Folders first, then files, each through `WorkspaceManager` so that S3, the cached file-system view and the METS stay in step. Status goes to `metadataCreated`. `tree.txt` is skipped.

8. **Add the objects to the METS** — every file under `objects/`, through `WorkspaceManager.AddItemsToMets`, which is where the metadata the tools just produced actually lands in the XML.

9. **Release the lock**, retrying for `ReleaseLockAttemptTime` seconds with a one-second pause between attempts. Failing to release it is a `completedWithErrors`, not a silent success: the alternative leaves a deposit locked forever with nothing to say so.

10. **Clean up** the scratch folders, stop the monitor timer, and force a compacting GC. A daily cron job in the image also sweeps anything older than seven days out of `/usr/process-brunnhilde`.


Between almost every one of those steps the run calls `CheckIfForceComplete`, which fetches the job's own status from Preservation API and looks for `completedWithErrors`. That is how an operator stops a wedged run from outside: set the status, and the job notices at its next checkpoint, releases the lock and gives up. A timer also polls every ten seconds while Brunnhilde is running, so a long scan can be killed mid-flight rather than only at a step boundary.

> **Refresh is expensive; most calls avoid it**
>
> `GetWorkspaceManager(request, refresh: false)` appears throughout the upload loop. It still fetches a fresh Deposit and METS ETag on every call — it has to, because each write changes the ETag and the next write is guarded by it — but it skips the full S3 walk. That walk was measured at about 40 seconds on a large deposit, repeated twenty-odd times for one job's metadata upload (LPII-135). Only `RefreshCombinedDirectory()` does the real thing.


## The tools, and where they come from

Everything is baked into the image, `Dockerfile.PipelineApi`. There is no tool download at runtime.

| Tool | Installed as | Notes |
|---|---|---|
| Siegfried | `go install github.com/richardlehane/siegfried/cmd/sf@v1.11.2`, binary copied to `/bin` | `sf -update` is run **as the app user**, because Siegfried resolves its signature file relative to `$HOME` and there is no shared location to override that |
| Brunnhilde | `pip3 install --upgrade brunnhilde` | Runs Siegfried and ClamAV itself; the build verifies it with `brunnhilde.py -V` |
| ClamAV | Debian packages `clamav`, `clamav-freshclam`, `clamav-daemon` | Runs as a daemon in the same container; see below |
| ExifTool | Debian package `exiftool` | Invoked directly, not through Brunnhilde |
| BagIt | `pip3 install bagit` | Only used for BagIt-layout deposits |

A daily cron job re-runs `sf -update`, `freshclam` and `pip3 install --upgrade brunnhilde`, so a long-lived container does not drift too far from current signatures.

`PYTHONUTF8=1` is set image-wide. Without a configured locale, Python falls back to a restrictive codec, and a deposit file with a macron in its name would crash or hang Brunnhilde mid-run — which, because jobs are processed one at a time, wedges every subsequent job at `waiting` indefinitely.

### ClamAV

This is the fiddliest part of the image, and worth understanding before changing anything.

Brunnhilde invokes the literal command `clamscan`, with no way to configure it. Running the real `clamscan` per job means reloading the whole signature database on every invocation. So the build moves the real binary to `clamscan.real` and puts a **shim** at `/usr/bin/clamscan`, which forwards to `clamdscan` against a long-running `clamd` on a Unix socket, adding `--multiscan` (measured at roughly 3x on a 2,796-file deposit) and stripping the `--max-scansize` / `--max-filesize` flags that `clamdscan` does not accept. If the socket is not there, the shim falls back to `clamscan.real` rather than failing.

`clamd` and `freshclam` are started by `pipeline-api-entrypoint.sh`, each in its own supervision loop with `--foreground` so that the loop can tell a crash from Debian's default double-fork. This matters: a dead `clamd` does not fail a scan, it produces "Connection refused" followed by "Infected files: 0" — a false clean result. The entrypoint runs as root to create `clamd`'s runtime directories, then drops to the app user with `setpriv`, passing `HOME` explicitly because `setpriv` does not reset it and Siegfried would otherwise look for its signatures under `/root`.

Three Debian defaults are patched in the image, each for a failure seen in practice: the `Example` line that stops both daemons starting at all; `MaxConnectionQueueLength` raised from 15 to 200 (bursts of concurrent jobs each open two connections to `clamd`); and `MaxThreads` raised from 12 to 50, without which about half of thirty concurrent `--multiscan` calls fail with "Not enough threads for multiscan".

### Memory

Two environment variables in the image exist because of how .NET behaves in a container with no hard memory limit:

* `DOTNET_GCConserveMemory=5` — biases the GC towards returning memory to the OS on collection rather than holding a high-water mark. Without it, measured memory climbed past 2.4 GB and stayed there across separate jobs. `9` was tried first and was worse: under real pressure it caused a blocking collection long enough to freeze the health endpoint for over two minutes, which is what actually got the task replaced.
* `DOTNET_gcServer=0` — workstation GC. The container gets roughly one vCPU, and server GC's longer, less frequent collections are the wrong trade-off for something that has to keep answering health checks.

`ExecutePipelineJob` also forces a full compacting collection in its `finally` block. That would be wrong in a normal request-handling API, but this service has a clear idle boundary after each job and generates too little garbage while polling to trigger a collection naturally.

## Configuration

| Section | Key | What it is |
|---|---|---|
| `StorageOptions` | `FileMountPath` | Where the working bucket is mounted. Everything else is relative to this. |
| `PipelineOptions` | `PipelineJobTopicArn` | SNS topic jobs are published to. Preservation API needs the same value. |
| `PipelineOptions` | `PipelineJobQueue` | SQS queue name (not URL) the consumer reads. |
| `PipelineToolOptions` | `PathToPython`, `PathToBrunnhilde` | How Brunnhilde is invoked. In the deployed image `PathToPython` is `brunnhilde.py` itself and `PathToBrunnhilde` is empty. |
| `PipelineToolOptions` | `ExifToolLocation`, `PathToClamScan`, `BagitProcessFilename`, `BagitScript` | The other three tools. |
| `PipelineToolOptions` | `ProcessFolder`, `ProcessFolderBagit` | Scratch space: `/usr/process-brunnhilde` and `/usr/process-bagit`. |
| `PipelineToolOptions` | `ObjectsFolder`, `DirectorySeparator` | `objects` and `/`. |
| `PipelineToolOptions` | `PipelineMetadataFolders` | `metadata/brunnhilde,metadata/exif,metadata/virus-definition` — the folders the pipeline owns, and the ones whose deletion failure aborts the run. |
| `PipelineToolOptions` | `ReleaseLockAttemptTime` | Seconds to keep retrying the lock release. |
| `ApiKeyOptions` | `ApiKey`, `ApiHeaderName` | Inbound authentication. |
| `TokenProvider`, `AzureAd` | | Outbound: the machine credentials used to call Preservation API. |

> **Two things in the config that do nothing**
>
> `FeatureFlags:UseLocalHostedServiceForPipeline` is present in every `appsettings` file and is read by nothing. `Program.cs` registers `InProcessPipelineQueue` and then `SqsPipelineQueue` as `IPipelineQueue`, so the last registration always wins and the service always goes through SNS/SQS — including locally. `InProcessPipelineQueue` is unreachable.
>
> `FeatureFlags:DisableAuth` is also present and also read by nothing here. Pipeline API's only gate is the API key, unconditionally. (Preservation API, Storage API and the Importer do honour that flag.)


## Endpoints

Both are guarded by `ApiKeyMiddleware`, which compares the value of the header named by `ApiKeyOptions:ApiHeaderName` — `X-API-KEY` — against the configured key and records the verdict in `HttpContext.Items`. `ApiKeyAttribute` on the controller turns a false verdict into `401`.

| Endpoint | What it does |
|---|---|
| `POST /pipeline` | Queue a run. Body is a `PipelineJob` with `depositName` and optionally `runUser`. Returns `204`. |
| `GET /pipeline?depositId=…` | Diagnostics: lists the files and directories under the mounted deposit path, plus `df` output. |
| `GET /health` | Liveness for the load balancer. |

Outbound, Pipeline API calls Preservation API as a machine client (`AddMachinePreservationClient(..., "PipelineAPI")`) for four things: fetching the deposit, reading the job list (used both by `CheckIfForceComplete` and to spot an already-force-completed job at dequeue time), releasing the lock, and reporting status.

Status reporting is `POST /deposits/pipeline-status`, hidden from the OpenAPI description because it is not for general use. The body is a `PipelineDeposit`:

```json
{
  "id": "hkbgyzjh9sep",
  "depositId": "e56fb7yg",
  "status": "processing",
  "runUser": "user@example.ac.uk",
  "errors": null
}
```

`id` is the job identifier, not the deposit's. `status` is one of the [job states](https://digirati-co-uk.github.io/digital-preservation-docs/preservation-api/tool-outputs-and-pipelines#job-states); `processing` is the claim described above, `completed` and `completedWithErrors` set `dateFinished`, and `errors` is a single string that Preservation API surfaces as a one-element `errors` array on the `ProcessPipelineResult`.

## Failure modes worth knowing

* **A message is deleted as soon as it is read.** There is no visibility-timeout retry. A job that fails for a transient reason is gone; the fix is to run the pipeline again.
* **The dequeue is bounded at 30 seconds.** A dead-but-not-errored connection to SQS can otherwise hang the single consumer loop forever with no exception (LPII-135). The bound deliberately does not cover the message deletion, because cancelling that mid-flight would let the message reappear and the job run twice.
* **A job already marked `completedWithErrors` is dropped at dequeue.** `SqsPipelineQueue.GetMessageModel` checks the job list before handing the message on, so a job force-completed while it was still queued never starts.
* **Brunnhilde processing one job at a time is load-bearing.** Anything that hangs the tool wedges every subsequent job at `waiting`. That is why the UTF-8 and ClamAV threading fixes above are in the image rather than left to chance.
