# Runbook: an Archival Group wedged by a half-written version

If Fedora dies, or an upload to S3 fails, while it is committing an import, the Archival Group can be
left in one of three states. Two of them make every later import of that group fail **with the same
error**, and they need **opposite repairs**: in one you remove an orphaned version directory, in the
other you must never remove anything. This page is how to tell them apart and repair each (#447, RFC
010 T9).

Everything below was rehearsed on 25 November 2026 against the image we deploy
(`fcrepo/fcrepo:6.5.1-tomcat9`, ocfl-java 2.2.1), with PostgreSQL 14 and a local S3 (LocalStack).
The dev rehearsal of state 1 is recorded on #447.

## Before you touch anything

- **Make sure nothing is writing to the group.** No active import job for it (`select id, archival_group,
  last_heartbeat from import_jobs where active and archival_group like '%<path>%'` on the Storage API
  database), and no Fedora commit running. A commit in progress looks exactly like states 1 and 2
  while it uploads.
- **Never delete a version directory that the root inventory lists.** That is a preserved version.
- **Move, don't delete.** Use `aws s3 mv --recursive` to a prefix outside the OCFL root, such as
  `quarantine/`, so a mistake can be undone.

## Where to look

You compare three things for the group. `<env>` is `dev`, `test` or `prod`; `<path>` is the Archival
Group's path under Fedora's root, as in the Storage API's `/repository/<path>`.

**1. The OCFL object in S3.** The bucket is `dlip-pres-<env>-fedora`, and Fedora's OCFL root is under
the prefix `initial/`. Each Archival Group is one OCFL object, at a path made from the SHA-256 of its
Fedora id:

```bash
P='<path>'
H=$(printf 'info:fedora/%s' "$P" | sha256sum | cut -c1-64)
ROOT="s3://dlip-pres-<env>-fedora/initial/${H:0:3}/${H:3:3}/${H:6:3}/$H"

aws s3 ls "$ROOT/"                                    # version directories: PRE v1/, v2/ ...
aws s3 cp "$ROOT/inventory.json" - | jq '{head, versions: (.versions | keys)}'
aws s3 cp "$ROOT/inventory.json.sha256" -             # the root inventory's digest
```

**2. ocfl-java's row in Fedora's database** (`/preservation/<env>/fedora_db` in Secrets Manager).
ocfl-java caches each object's inventory here and reads it from here, not from S3:

```sql
select version_id, inventory_digest, object_root_path, update_timestamp
from ocfl_object_details where object_id = 'info:fedora/<path>';
```

`object_root_path` is the same path as `$ROOT` above, without the bucket and `initial/`.

**3. What Fedora reports**, with Fedora's admin credentials (`/preservation/<env>/fedora_admin`):

```bash
F=https://fedora-<env>.dlip.digirati.io/fcrepo/rest
curl -s -u "$FEDORA_USER:$FEDORA_PASS" -H 'Accept: application/n-triples' "$F/<path>/fcr:versions" | grep -c contains   # versions
curl -s -o /dev/null -w '%{http_code}\n' -u "$FEDORA_USER:$FEDORA_PASS" "$F/<path>/<a file from the last import>"
```

## Which state is it?

| | How it arises | Import error | S3 root inventory | `vN+1/` in S3 | `ocfl_object_details` | Fedora |
|---|---|---|---|---|---|---|
| **1. Orphan version** | Fedora killed while uploading an update, or an S3 upload failed during the commit | Commit returns 409 | `head` is vN; vN+1 **not** in `versions` | Partly there | vN | N versions |
| **2. Half-created object** | Fedora killed while uploading a **new** group | Creating the group returns 500 | **No `inventory.json`**: only `0=ocfl_object_1.1` and part of `v1/` | – | No row | 404 for the group |
| **3. OCFL ahead of Fedora** | Fedora died in the last moments of a commit (#459) | Commit returns 409 | `head` is vN+1, **listed** in `versions` | Complete, listed | vN (or no row, or vN+1: see below) | N versions, or N+1 with the last import's files missing |

**The one question that separates 1 from 3:** is the highest version directory in S3 listed in the
root inventory's `versions`? Not listed: it's an orphan (state 1). Listed: it's a real version (state
3), and it must stay.

How each shows up:
- **States 1 and 3:** the job fails with `(TX) Fedora transaction commit returned HTTP status 409
  (Conflict)`. Fedora's log, for both, has
  `ObjectOutOfSyncException: Failed to create a new version of object info:fedora/<path>. Changes are
  out of sync with the current object state.` The message is identical, so never repair from it alone.
- **State 2:** the job fails creating the Archival Group. Fedora's log has `CorruptObjectException:
  Object info:fedora/<path> is missing its root sidecar file`, from `WildcardExceptionMapper`, and
  Fedora answers the `PUT` with 500. It never reaches a commit.
- **State 3 without an error:** if Fedora died after ocfl-java committed its row but before Fedora's
  own index committed, nothing fails. `ocfl_object_details` and `fcr:versions` show vN+1, but files the
  last import added answer 404, and its deletions are still listed. The next import of the group may
  then fail in confusing ways.

## Repairs

### State 1: move the orphan aside

1. Check again: root `head` is vN, and `vN+1/` is not in its `versions`. If more than one unlisted
   version directory exists, stop and ask: that isn't a state this page covers.
2. Move only the orphan prefix:
   ```bash
   aws s3 mv --recursive "$ROOT/vN+1/" "s3://dlip-pres-<env>-fedora/quarantine/$(date +%F)/${H}/vN+1/"
   ```
   Never anything else: not the root inventory, not a listed version.
3. Run the import again. It commits vN+1 normally.

No reindex and no restart: Fedora's own index and `ocfl_object_details` never moved off vN.
Rehearsed: the retry committed, and S3, the database and `fcr:versions` all showed the new version.

### State 2: move the half-created object aside

1. Check again: no `inventory.json` at `$ROOT`, no `ocfl_object_details` row, and Fedora answers 404
   for the group. Any one of those present means it isn't state 2.
2. Move the whole object root:
   ```bash
   aws s3 mv --recursive "$ROOT/" "s3://dlip-pres-<env>-fedora/quarantine/$(date +%F)/${H}/"
   ```
3. Wait 3 minutes before retrying. The failed attempt's Fedora transaction holds the group's lock
   until it expires, and until then a retry fails with 409 "Cannot update info:fedora/<path> because it
   is being updated by another transaction".
4. Run the import again.

No reindex and no restart: nothing about the group ever reached Fedora's database.

### State 3: bring Fedora up to OCFL, and remove nothing

1. **If `ocfl_object_details` still shows vN**, delete the row. It is only ocfl-java's cache of the
   inventory: on the next read ocfl-java finds no row, loads the inventory from S3 and caches it again.
   ```sql
   delete from ocfl_object_details where object_id = 'info:fedora/<path>';
   ```
   You may find the row already gone or already at vN+1. When an update fails with
   `ObjectOutOfSyncException`, ocfl-java drops the row itself, and the next read re-caches it from S3.
2. **Reindex the group from OCFL:**
   ```bash
   curl -s -o /dev/null -w '%{http_code}\n' -u "$FEDORA_USER:$FEDORA_PASS" -X POST "$F/<path>/fcr:reindex"   # 204
   ```
3. **Check:** `fcr:versions` shows N+1, and a file from the last import answers 200.
4. **Sort out the import job that died.** Its version landed, so the job's failure is wrong. Don't run
   a create again: the group exists. Before running an update again, check whether the group already
   matches the deposit, or you'll get a confusing extra version. #444 and #459 make the importer do
   this itself.

No restart needed: rehearsed without one.

## `fcr:reindex`

`POST /{path}/fcr:reindex` rebuilds Fedora's index entries for one Archival Group from its OCFL
inventory. It's needed only for state 3. Tried on a group that was already indexed:

- it answers 204, the group's version count and files are unchanged, and later imports commit
  normally;
- a second run changes nothing further;
- the first run **adds** a `…/fcr:metadata` row per binary to `containment` and `simple_search`, which
  ordinary commits don't write. The Storage API doesn't see them: its search requires a MIME type,
  which those rows lack, and its wide-container listing looks up children of a container, while these
  rows' parent is a binary.

So it's safe to run on a group you're unsure about.

## How ocfl-java writes a version, and why these states exist

From ocfl-java 2.2.1, `DefaultOcflStorage.storeNewImmutableVersion`, and confirmed in the rehearsal:

1. For a new object, it writes the `0=ocfl_object_1.1` marker first.
2. It uploads the new version **straight into `vN+1/`**, its own `inventory.json` included. It first
   checks that `vN+1/` doesn't exist, and if it does, fails with the `ObjectOutOfSyncException` above.
3. It replaces the root `inventory.json` and its sidecar, which is the moment the version exists.
4. It commits its `ocfl_object_details` row. Fedora's own database transaction commits after that.

If step 2 fails for an **update**, nothing removes what was uploaded. In the rehearsal, a commit that
failed on an S3 error, with Fedora still running, left an orphaned `v3/` just as a kill does. So state
1 can follow an S3 error during a commit, not only a dead Fedora. For a **new** object, a failure in
step 2 purges the whole object, so state 2 needs a kill. A kill between steps 3 and 4 gives state 3.

## Rehearsing state 1 on dev

A normal stop lets Fedora roll back and remove what it uploaded, so the rehearsal needs a hard kill
during the upload. ECS sends SIGTERM and kills the task 30 seconds later.

1. Prepare a test deposit whose commit uploads for well over 30 seconds: a few GB, for example.
2. Start its import with nothing else running. Watch for `Committing transaction` in Fedora's log, and
   then for keys appearing under the group's `vN+1/`.
3. Stop the Fedora task: `aws ecs stop-task --cluster dlip-pres-dev --task <task arn> --reason
   "#447 rehearsal"`. The upload must still be running 30 seconds later, when ECS kills the task.
4. Once Fedora is back, follow **Which state is it?** and **State 1** above. Record the result on #447.
