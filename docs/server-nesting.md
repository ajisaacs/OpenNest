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
3. Follows the job (`NestJobWatch`) behind the same owned modal progress window a local run
   uses: the queue position, then the worker's stage ("On the nest server: …"), and the
   worker's **Best plan so far** in the same sheet strip a local run fills
   (`RemoteNestPlanPreview`, below). Updates come from the job's event stream; when it drops,
   the client polls once and reconnects with the last event id, and a server without the
   stream is polled every two seconds throughout. Connection trouble only shows a waiting
   message; the job keeps running on the server.
4. On success, replays the result here (below) and continues with the usual validation
   report, review, Keep or Discard and commit. Keep is recorded on the server before anything
   is committed (below: [Keep once](#keep-once)), and Discard is recorded too. A failed job
   shows its error.

Stop (or closing the progress window) cancels the server job, best effort, and discards the
proposal, exactly like stopping a local run; the worker stops within one report interval
(two seconds) plus however long the engine takes to notice its cancellation token. The input
drawings stay locked by the modal progress for the whole wait.

**Finish notice.** When the job ends (succeeded or failed) while no OpenNest window is active
or the main window is minimized, `ServerNestNotice` shows a notification-area balloon, which
Windows 10 and 11 display as a notification. Clicking it (or the tray icon) restores OpenNest
and activates its topmost window: the check of the result, then the review. The icon goes
away once OpenNest is in front again, however the user returns. A real toast would need an
AppUserModelID registered on a Start menu shortcut, which the Velopack install (see
[desktop auto-update](auto-update.md)) does not promise, so no packaging change is needed.
Nothing is kept without the review's explicit **Keep**.

## My Server Jobs

**Nest > My Server Jobs...** (Database mode only, beside Auto Nest) lists the server's jobs,
newest first: nest, state, result, engine, submitter, submitted and finished times. **Only my
jobs** (on by default) shows this Windows user's jobs from any workstation; **Show** narrows to
active, finished or ready-to-review jobs. A queued job shows its place in the queue, a running
one the worker's stage, a failed one its error, and a finished one its summary (parts placed,
sheets, utilization and anything left unplaced). Results waiting for review are bold.

The list polls `GET /api/nest-jobs` every three seconds while the window is open; the list's
ETag turns an unchanged poll into a 304. Polling rather than the per-job event stream: the
list must also show jobs other desktops submit and jobs removed or expired, which no single
job's stream announces, and one conditional request covers every row. The window also
refreshes at once after anything this desktop does to a job.

- **Open** (or double-click) a result that is ready to review. OpenNest looks for the nest
  the job was sent from: the open tab bound to its server nest id (`NestId`, sent with the
  job), then an open tab with its name; otherwise it offers the active nest, or asks the user
  to open the nest first. It downloads the job's package and result, rebuilds the job from
  `job.json` against that nest's own drawings (`RemoteNestSubmission.Reopen`) and replays it
  behind the owned modal progress, then shows the validation report and review exactly as the
  session that sent it would have. Each drawing is found by name, the one with unchanged
  geometry when several share it; a drawing missing or changed since the job was sent refuses
  the result (`NestReplayMismatchException`) and leaves the job unreviewed, so it can be opened
  again once the drawing is back. Settings that live only on the sending dialog (ledger stock
  codes, saved plate-size offers) are not reapplied by a later Open.
- **Cancel Job** stops a queued or running job (`POST .../cancel`).
- **Remove** deletes a finished job and its stored result from the server, asking first when
  nobody has kept or discarded the result yet. Active jobs must be cancelled first.

A job kept or discarded shows **Kept** or **Discarded** (who and when in the row's tooltip) and
cannot be opened again.

**Status bar.** In Database mode, a status-bar item counts this user's results waiting for
review ("2 server nests to review"), including results that finished while OpenNest was
closed. It reads the server at startup, every minute and after any job change from this
desktop, stays hidden when there is nothing to review or the server cannot be reached, and
opens My Server Jobs when clicked. Nothing pops up.

## Keep once

A succeeded job's result is reviewed once: `POST .../keep` or `POST .../discard`, with
`If-Match` naming the job revision the desktop reviewed, moves it from unreviewed to `Kept` or
`Discarded` only while it is still unreviewed at that revision (one conditional `UPDATE`
under the database monitor). Anything else answers 409 with the job as it is, so of two
desktops reviewing the same result the second is told who kept it, and when, and commits
nothing. The desktop records the Keep first and commits only when the server accepts it; if
the server cannot be reached, nothing is committed and the result stays on the server to open
again from My Server Jobs. A server too old for the route keeps as before. Discard is best
effort: a result left unreviewed only stays in the list. Keep and Discard drop the job's
package and result on the server.

## What travels

**The package** (`OpenNest.Api.NestJobPackage`) is a ZIP of:

- `job.json`: `NestJobDocument`: engine name, each item's quantity, priority (higher first;
  version 1 jobs nested lower numbers first and are converted when read), rotation range
  and drawing index, the drawing names in order (a guard against reordering), each drawing's
  geometry fingerprint (a SHA-256 of the exact motions the freshness check compares, which a
  later Open checks the nest's drawings against; workers ignore it), every stock
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

**Live progress** travels separately while the job runs, both as versioned JSON the server
never opens: `RemoteNestProgress` (the stage of the solve in progress: `NestJobStage`, stock,
plate index and committed counts) and `RemoteNestPlan` (the engine's best plan so far, as the
raw placements of `NestJobStage.PlanImproved`). The desktop rebuilds the request's job from
its own drawings (the same part ids the worker solved) and draws the plan on detached
drawings, exactly as a local run's `PlanSheets`; a bundled cutout trial's frame proxies are
bundled once on the desktop the first time a plan names one. Like a local plan it is
unvalidated and never kept.

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
| `GET /api/nest-jobs` | desktop | the job list, newest first: `filter` (`all`, `active`, `finished`, `unreviewed`), `submittedBy` (a bare user name matches that user on any workstation, ignoring case), `limit` (default 200, at most 500); ETag and `If-None-Match` |
| `GET /api/nest-jobs/{id}` | desktop | state, attempts, error, queue position, review, summary |
| `GET /api/nest-jobs/{id}/result` | desktop | the transcript; 409 until the job succeeded, and once it was kept or discarded |
| `POST /api/nest-jobs/{id}/keep` | desktop | `If-Match` job revision, optional `{reviewedBy}`; marks the result Kept once (409 with the job otherwise) |
| `POST /api/nest-jobs/{id}/discard` | desktop | the same for Discarded |
| `DELETE /api/nest-jobs/{id}` | desktop | `If-Match`; removes a finished job and its result (409 while active or changed) |
| `GET /api/nest-jobs/{id}/events` | desktop | Server-Sent Events of the job until it is final (below) |
| `GET /api/nest-jobs/{id}/plan` | desktop | the best plan so far, revision in `X-Plan-Revision`; 204 when there is none (the polling fallback) |
| `POST /api/nest-jobs/{id}/cancel` | desktop | queued: cancelled at once; running: flagged for the worker |
| `POST /api/nest-jobs/claim` | worker | the oldest queued job on a fresh lease, or 204 |
| `GET /api/nest-jobs/{id}/package` | worker, desktop | the package while queued or running, or succeeded and unreviewed (a desktop reopening the result) |
| `POST /api/nest-jobs/{id}/heartbeat` | worker | renews the lease and reports a cancel request; 409 once the worker no longer holds it |
| `PUT /api/nest-jobs/{id}/progress?workerId=` | worker | stores the latest `RemoteNestProgress` (at most 64 KB); answers like a heartbeat |
| `PUT /api/nest-jobs/{id}/plan?workerId=` | worker | stores the latest `RemoteNestPlan` (at most 16 MB); answers like a heartbeat |
| `PUT /api/nest-jobs/{id}/result?workerId=&parts=&unplaced=&sheets=&utilization=` | worker | stores the transcript (at most 64 MB) and its summary for job lists; a job cancelled meanwhile ends Cancelled |
| `POST /api/nest-jobs/{id}/fail` | worker | ends the job Failed with a reason |
| `POST /api/nest-jobs/{id}/cancelled` | worker | confirms a requested stop |
| `POST /api/nest-jobs/{id}/release` | worker | gives the job back without spending an attempt (worker shutting down) |

States: `Queued`, `Running`, then one of `Succeeded`, `Failed` and `Cancelled`. A succeeded
job's `review` is `none` until it is kept or discarded. Schema version 17 adds `nestId`,
`review`, `reviewedBy`, `reviewedAt` and the summary columns (`partsPlaced`, `partsUnplaced`,
`sheets`, `utilization`), added only where missing. The worker computes the summary
(`NestJobSummaries`) because the server never opens results; older workers send none. Only one job
runs at a time: a claim returns nothing while any job is running, so another worker container
never takes a second job. Claims are serialized by the database's monitor and transaction.

## Progress and the event stream

The worker is a separate process that never opens the SQLite file (it may run on another
host, and the server's file may sit on NFS, where SQLite locking is unreliable), so progress
takes the route everything else from the worker takes: HTTP to the server, which stores it in
the job's row. The server is the one writer; nothing else had to change to carry it across
processes.

**Worker.** `RecordingNestingEngine.Progress` taps each engine solve's own progress, before
the pipeline would turn plans into sheets (the worker draws nothing). `NestJobReporter` keeps
only the latest stage and the latest plan and, every `NestJobRules.ProgressInterval` (two
seconds), stores whichever changed: at most one small write and one plan write per interval,
however chatty the engine. A bundled cutout trial reports no commits, as in a local run.
Reports renew the lease and answer with the cancel flag; a round with nothing new reads the
job instead, which costs the server no write. Either way a cancel request reaches the worker
within one interval. The heartbeat still runs every 20 seconds.

**Server.** Schema version 16 adds to `nest_jobs`: `revision` (bumped by every change a
watcher shows: submit, claim, cancel request, progress, plan, finish, release, lapse;
heartbeats leave it alone), `progress`, `plan` and `planRevision` (the revision the plan was
stored at). A finished job keeps its last progress but drops its plan; a job given back or
lapsed drops both, so the next attempt starts clean. Every job write also completes
`NestDatabase.JobsChanged`, an in-process signal, so streams wake at once without polling the
database; each stream also re-reads at every keep-alive.

**Stream.** `GET /api/nest-jobs/{id}/events` (`text/event-stream`, 404 for an unknown job) sends:

- `event: plan`, `id:` the plan's revision, `data:` the `RemoteNestPlan`, whenever a newer
  plan was stored, always before the job event that names it;
- `event: job`, `id:` the job's revision, `data:` the job record (with `progress`,
  `revision` and `planRevision`), on every change and when the queue position moves;
- `: keep-alive` comments every 15 seconds while nothing changes;
- `event: gone` if the job is removed meanwhile.

The stream ends after the job event that is final. A reconnect sends `Last-Event-ID`: the
server sends only a newer plan and a newer job, except that a final job is always sent before
the stream ends. Ids never go backwards on one stream. SSE rather than SignalR: one-way
updates over plain HTTP need no hub, client library or sticky connection, and degrade to the
polling endpoints the server already had. The client treats 45 seconds without a line as a
dropped stream.

**Crash recovery.** A running job holds a two-minute lease (`NestJobRules.Lease`), which the
worker renews every 20 seconds. Each claim first settles lapsed leases: a job someone asked to
cancel ends Cancelled; otherwise it goes back to the front of the queue, or fails after three
attempts (a job that keeps killing its worker, for example by running out of memory). A
server restart keeps every job; leases lapse and recover the same way.

**Retention.** Failed and cancelled jobs drop their package at once; a succeeded job keeps it
until it is kept or discarded (a later Open rebuilds the job from it), then drops it with the
result. Every finished job, reviewed or not, is deleted with its package and result
`OPENNEST_JOB_RETENTION_DAYS` after it finished (default 7; a positive number of days,
fractions allowed; anything else fails startup). Expired jobs are removed whenever a worker
claims and whenever a job list is read. `compose.server.yaml` passes the variable through and
`server.env.example` documents it. Like the rest of the API, these routes have no
authentication; submitter and reviewer are the desktop's `user@machine`, not a verified
identity. Keep the server on a trusted LAN.

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
and nonfinite poses survive the wire. `RemoteNestProgressTests`: a wire plan draws as the
local `PlanSheets`, including a cutout trial's proxies. `OpenNest.Server.Tests`
`NestJobQueueTests` (states, single running job, concurrent claims, cancellation, lease
lapse, retry limit, release, retention, restart), `NestJobApiTests` (HTTP plus a real worker
run end to end) and `NestJobEventsTests` (stream order ending at the final state,
`Last-Event-ID` reconnects, the watch reconnecting a dropped stream and polling a server
without one, an engine plan reaching the desktop, a cancel seen within one report interval).
`NestJobListTests` (list filters and submitter matching, queue positions, summary and nest id,
Keep and Discard once including concurrent keeps, removal, configurable retention, the v16
upgrade) and `NestJobListApiTests` (the list and its 304, the worker's summary, a second
desktop's Keep refused over HTTP, If-Match, removal, a result reopened from its package in a
later session and refused for a changed or missing drawing). `RemoteNestReplayTests` also pins
drawing fingerprints and a reopened replay. `OpenNest.WinForms.Tests` `ServerAutoNestTests`:
the progress window's stage text and plan strip, and the finish notice; `ServerJobsFormTests`:
My Server Jobs rows and per-state actions, Cancel Job, Remove (with its confirmation), Open,
the status-bar count, and a Keep refused because another desktop kept first.

A live check against the container: submit a job, then
`curl -N http://<server>:8090/api/nest-jobs/<id>/events` shows `job` and `plan` events until
the final job event. For job lists: `curl http://<server>:8090/api/nest-jobs?filter=unreviewed`
lists the result; `POST .../keep` with `If-Match: "<revision>"` answers 200 once and 409 for a
second desktop; `POST .../cancel` on a queued job ends it Cancelled at once.
