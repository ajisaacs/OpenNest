# Server nesting

In Database mode, Auto Nest can send the job to the nest server instead of nesting on the
workstation. A worker container solves queued jobs one at a time under its own CPU and memory
limits, and the workstation reviews and keeps the result exactly as it would a local run.
Local nesting stays the default and the fallback when the server cannot take a job.

## Desktop

The Auto Nest dialog's Plates tab shows **Nest on the server** in Database mode only. It is
enabled for built-in engines only: the server loads no plug-ins from `Engines/`. The dialog
remembers the last choice (`AutoNestOnServer` in the user settings).

With the box ticked, `ServerAutoNest`:

1. Snapshots the request (`RemoteNestSubmission`) and writes its package (`NestJobPackage`).
2. Submits it. If the server cannot take it (offline, too old, refused), the user may nest on
   this PC instead; declining discards the run.
3. Polls every two seconds behind the same owned modal progress window a local run uses,
   showing the queue position or that the job is running. Transient poll failures only
   show a waiting message; the job keeps running on the server. A live plan strip is not
   shown yet.
4. On success, replays the result here (below) and continues with the usual validation
   report, review, Keep or Discard and commit. A failed job shows its error.

Stop (or closing the progress window) cancels the server job, best effort, and discards the
proposal, exactly like stopping a local run. The input drawings stay locked by the modal
progress for the whole wait.

## What travels

**The package** (`OpenNest.Api.NestJobPackage`) is a ZIP of:

- `job.json`: `NestJobDocument`: engine name, each item's quantity, priority, rotation range
  and drawing index, the drawing names in order (a guard against reordering), every stock
  entry as `NestStockJson` (size, quantity, spacing, quadrant, cost, clamp zones in plate
  coordinates, clamp rail, irregular outline), and the job options. A server job never sets
  `SearchUntilCancelled` and never runs under a `DeadlineNestingEngine`: it has no time
  limit, so a throttled worker finds the same nest a fast one does, later.
- `drawings.nest`: a throwaway nest of exactly the items' drawings, written by `NestWriter`.
  The caller's nest is never written. Drawings are uploaded rather than referenced from the
  drawing library or sets, because the nest being edited may hold unsaved edits and drawings
  that are in neither; the server must nest what the workstation has.

**The result** is a `NestSolveTranscript`: every engine solve the pipeline made on the worker,
in order, as what was asked (part and stock ids with quantities) and the raw `NestJobResult`
returned. Poses keep their IEEE values, nonfinite ones included. No bound plates or
validation travel back.

## Replay on the desktop

`RemoteNestSubmission.Complete` first checks that each item's drawing still has the geometry
it had when the job was sent; a changed drawing refuses the result. It then runs
`NestPipeline.Run` with a `ReplayNestingEngine` that answers each solve with the next recorded
result, after checking it is asked the same question. Cutout bundling, the ordinary-solve
comparison and clamp-removal recovery therefore happen here exactly as on the worker, and the
result is validated by `NestLayoutCheck` and bound to the caller's own drawings like a local
run. A recorded sheet that matches one of the request's stock entries uses that entry itself.

A replay that asks a different question, or more or fewer solves than were recorded, fails
with `NestReplayMismatchException`. That happens only if the drawings read back on the worker
differ enough to change a geometric decision, which `.nest` coordinates (10 decimal places)
make unlikely. The user then runs Auto Nest again; nothing partial is kept.

## Server

`/api/nest-jobs` (`NestJobRoutes`, `NestDatabase.Jobs`, schema version 15) stores packages and
results as opaque bytes. The HTTP server never opens either and references neither the engine
nor `OpenNest.IO`.

| Route | Who | Does |
|---|---|---|
| `POST /api/nest-jobs` | desktop | multipart `metadata` (`NestJobSubmission`) and `file` (package, at most 256 MB); returns the queued job |
| `GET /api/nest-jobs/{id}` | desktop | state, attempts, error, queue position |
| `GET /api/nest-jobs/{id}/result` | desktop | the transcript; 409 until the job succeeded |
| `POST /api/nest-jobs/{id}/cancel` | desktop | queued: cancelled at once; running: flagged for the worker |
| `POST /api/nest-jobs/claim` | worker | the oldest queued job on a fresh lease, or 204 |
| `GET /api/nest-jobs/{id}/package` | worker | the package while queued or running |
| `POST /api/nest-jobs/{id}/heartbeat` | worker | renews the lease and reports a cancel request; 409 once the worker no longer holds it |
| `PUT /api/nest-jobs/{id}/result?workerId=` | worker | stores the transcript (at most 64 MB); a job cancelled meanwhile ends Cancelled |
| `POST /api/nest-jobs/{id}/fail` | worker | ends the job Failed with a reason |
| `POST /api/nest-jobs/{id}/cancelled` | worker | confirms a requested stop |
| `POST /api/nest-jobs/{id}/release` | worker | gives the job back without spending an attempt (worker shutting down) |

States: `Queued`, `Running`, then one of `Succeeded`, `Failed` and `Cancelled`. Only one job
runs at a time: a claim returns nothing while any job is running, so another worker container
never takes a second job. Claims are serialized by the database's monitor and transaction.

**Crash recovery.** A running job holds a two-minute lease (`NestJobRules.Lease`), which the
worker renews every 20 seconds. Each claim first settles lapsed leases: a job someone asked to
cancel ends Cancelled; otherwise it goes back to the front of the queue, or fails after three
attempts (a job that keeps killing its worker, for example by running out of memory). A
server restart keeps every job; leases lapse and recover the same way.

Finished jobs drop their package at once, and are deleted with their result seven days after
they finish. Like the rest of the API, these routes have no authentication; keep the server on
a trusted LAN.

## Worker and container

`OpenNest.Server.Worker` (Generic Host) polls `OPENNEST_SERVER_URL` every
`OPENNEST_WORKER_POLL_SECONDS` (default 5) and goes straight to the next job after one
finishes. It runs each job through `NestPipeline` with the named built-in engine wrapped in a
`RecordingNestingEngine`, at below-normal process priority. It talks to the server over HTTP
and never opens the SQLite file, so it may run anywhere that reaches the server, including a
host whose Docker data sits on NFS. On stop it gives a running job back (`release`), within the
compose file's 30-second grace period.

The server image contains both processes. `OPENNEST_ROLE` picks one: `server` (default) or
`worker` (`/app/entrypoint.sh`). The image health check (`/app/healthcheck.sh`) asks `/healthz`
for the server; for the worker it requires the alive file
(`OPENNEST_WORKER_ALIVE_FILE`, default `/tmp/opennest-worker.alive`), touched every poll round
and every accepted heartbeat, to be under two minutes old.

`compose.server.yaml` adds `opennest-worker` with `cpus: ${OPENNEST_WORKER_CPUS:-2}` and
`mem_limit: ${OPENNEST_WORKER_MEMORY:-4g}`. .NET reads the CPU quota for
`Environment.ProcessorCount`, so the engines' parallel loops scale down to it. Fewer CPUs only
make a job slower. Leave enough CPU and memory for the server itself.

## Verification

`OpenNest.Engine.Tests` `RemoteNestReplayTests`: replay equals the local proposal (including
the cutout trial plus ordinary solve), edited drawings and different jobs refuse, and stock
and nonfinite poses survive the wire. `OpenNest.Server.Tests` `NestJobQueueTests` (states,
single running job, concurrent claims, cancellation, lease lapse, retry limit, release,
retention, restart) and `NestJobApiTests` (HTTP plus a real worker run end to end).
