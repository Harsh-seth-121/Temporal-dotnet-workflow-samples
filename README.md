# Temporal .NET workflow samples

A self-contained Temporal deployment you can run on one machine, with a .NET
worker and metrics from both sides of the wire already graphed.

```sh
make up      # Postgres, Temporal, the worker, Prometheus and Grafana
make smoke   # prove it works end to end
```

From wiped volumes, both commands together take about a minute once images are
pulled. Nothing is installed on your machine: the stack runs entirely in
containers, and the smoke test drives a workflow through the `temporal` CLI inside
one of them, so you can verify the box before building any .NET.

## URLs

`make up` prints these when it finishes. `make urls` prints them again any time,
and marks anything that is not running.

| What | URL | Notes |
|---|---|---|
| Temporal UI | http://localhost:8088 | Workflow history, search, stack traces |
| Grafana | http://localhost:3001 | Provisioned, no login |
| Prometheus | http://localhost:9090 | Query browser; `/targets` shows scrape health |
| Server metrics | http://localhost:8000/metrics | All four roles, split by the `service_name` label |
| Worker metrics | http://localhost:9464/metrics | .NET SDK metrics |
| Database UI | http://localhost:8089/?pgsql=postgresql | Browse the schema and its rows |
| Load generator | http://localhost:9095/metrics | Only while `make load` is running |

Straight to a dashboard:

- Server: http://localhost:3001/d/temporal-sandbox-server
- .NET SDK: http://localhost:3001/d/temporal-sdk-core

Two endpoints are not browser pages:

| What | Address | Notes |
|---|---|---|
| Temporal | `localhost:7233` | gRPC. The `temporal` CLI's default, so host commands just work |
| Postgres | `postgresql://temporal:temporal@localhost:55432/temporal` | Temporal's persistence |

Every port is published on loopback only, so none of this is reachable from your
network. Ports live in `deploy/.env`; change one there and the printed URLs follow.

Neither of those two opens in a browser. `localhost:55432` speaks the Postgres
wire protocol, so a browser pointed at it gets nothing back. Adminer is the way
in, and its link above arrives with the driver and host already filled. That
leaves the username and password, both `temporal` unless you edited `deploy/.env`.
Once you are in, `temporal` holds the workflow schema (`executions`,
`history_node`, `task_queues`) and `temporal_visibility` holds the rows behind
workflow search.

### Dashboards

**Temporal Core SDK (OTel) Metrics** comes from Temporal's own dashboards repo,
vendored unmodified apart from a pinned UID. It is the right one for .NET, which
is built on sdk-core. See `VENDORED.md`.

**Temporal Server (sandbox)** is written here: service availability, gRPC and
persistence latency, task matching, workflow completions, and shard stats.

Panels graphing failures look empty on a healthy box. Temporal only exposes a
counter once it has incremented, so a blank failure series means nothing has gone
wrong rather than that the panel is broken.

## Common commands

| Command | Does |
|---|---|
| `make up` | Start everything and wait until it is healthy |
| `make down` | Stop, keeping data |
| `make reset` | Stop and delete the volumes, for a genuinely clean box |
| `make smoke` | End-to-end check; exits non-zero if anything is wrong |
| `make urls` | Print every URL, marking anything not running |
| `make load MODE=demo` | Start the load generator; also `bench` and `soak` |
| `make unload` | Stop the load generator, leaving the stack up |
| `make run NAME=you` | Run one workflow and print the result |
| `make onboard` | Run the patching and child-workflow sample; `PAUSE=` holds a run open |
| `make worker` | Run the worker on the host instead of in a container |
| `make test` | Run the test suite |
| `make logs` | Follow logs from every service |

`make worker` is the fast iteration loop: stop the container, run the worker from
your editor against the containerized server, skip the image rebuild.

## Layout

```
src/Sandbox.Abstractions/        ISampleModule, shared config
src/Sandbox.Samples.Hello/       HelloWorkflow and its activity
src/Sandbox.Samples.Onboarding/  parent with patches, abandoned long-running child
src/Sandbox.Worker/              host, runtime, metrics, sample discovery
src/Sandbox.Client/              starter for HelloWorkflow
src/Sandbox.Onboarding.Client/   starter for OnboardingWorkflow
deploy/                          compose file, Prometheus and Grafana config
docker/                          worker image
scripts/                         startup gate, smoke test, URL banner
tests/                           workflow tests
```

## Adding a sample

Samples are separate projects so that adding one is an addition rather than an
edit. `Sandbox.Worker` never names a sample: it finds them at startup.

1. Create `src/Sandbox.Samples.<Name>/` with your workflows and activities.
2. Implement `ISampleModule` to register them.
3. Add one `<ProjectReference>` to `src/Sandbox.Worker/Sandbox.Worker.csproj`,
   and the project to `Sandbox.sln`.

No change to the compose file, the worker image, or the metrics wiring.

If a module cannot be loaded or constructed, the worker refuses to start and says
which one. That is deliberate: a silently skipped module registers no workflows,
and the only symptom would be a workflow sitting unclaimed until it times out.

## Changing a workflow that is already running

```sh
make onboard           # set an account up, then leave a monitor watching it
```

`OnboardingWorkflow` runs four activities and finishes in about a second.
`AccountMonitorWorkflow` watches the account afterwards and runs for as long as you let
it. The two exist to show the two problems that only appear once workflows are long
enough to outlive a deploy, and they solve them differently.

### The parent patches

A worker that restarts replays every open workflow from the beginning. If the new code
issues different commands than the history records, the run stops with a non-determinism
error and stays stopped. Patching is how you change a workflow anyway:

```csharp
if (Workflow.Patched("email-welcome-instead-of-letter"))   // new runs
{
    await SendWelcomeEmail();
}
else                                                       // runs already in flight
{
    await QueueWelcomeLetter();
}
```

There are three phases, and `OnboardingWorkflow` is sitting in two of them at once so you
can see the order in one file:

| Phase | Call | Where this sample is |
|---|---|---|
| 1, patch in | `if (Workflow.Patched(id))`, both branches | `email-welcome-instead-of-letter` |
| 2, deprecate | `Workflow.DeprecatePatch(id)`, old branch gone | `screen-before-provision` |
| 3, remove | nothing | neither, yet |

You move from one phase to the next only when no run that needs the old behaviour is
still open. The two moves ask different questions, so they are two different queries:

```sh
# 1 to 2, before deprecating: any open run that has NOT recorded the marker still
# needs the else branch.
temporal workflow list --query 'WorkflowType = "OnboardingWorkflow"
  AND ExecutionStatus = "Running"
  AND TemporalChangeVersion NOT IN ("email-welcome-instead-of-letter")'

# 2 to 3, before deleting the DeprecatePatch line: any open run still carrying the
# marker needs the SDK to keep expecting it.
temporal workflow list --query 'WorkflowType = "OnboardingWorkflow"
  AND ExecutionStatus = "Running"
  AND TemporalChangeVersion = "screen-before-provision"'
```

`NOT IN`, not `IS NULL`, and the distinction is this workflow's own doing.
`TemporalChangeVersion` is a list, and a run that started before the email patch has
still recorded `["screen-before-provision"]`, so it is not null. Asking for null finds
nothing and reads as "safe to deprecate" at the exact moment it is not. Verified
against a run of the pre-patch code on this box: `IS NULL` missed it, `NOT IN` found
it.

`ExecutionStatus = "Running"` carries weight too. The parent finishes in about a
second, so without it both queries return a wall of closed runs and answer a question
nobody asked.

Moving early is not untidy, it strands live workflows. `make test` holds the line here: a
test runs a pre-patch copy of the workflow, captures the history it writes, and replays
today's code against it. Delete the `else` branch and that test reports
`Activity type of scheduled event 'QueueWelcomeLetter' does not match activity type of
activity command 'SendWelcomeEmail'`, which is the error your users would otherwise find.

To watch the patch decide on the real box rather than in a test, you have to hand it a
history worth reading, and only pre-patch code writes one. `PAUSE=` parks the run just
after the welcome step, which is where that history exists:

```sh
# terminal 1. Cut OnboardingWorkflow.workflow.cs back to its pre-patch shape: replace
# Workflow.Patched(EmailPatch) with false, and drop the email arm of the if so only
# QueueWelcomeLetter is left. Keeping the local as false rather than deleting it is
# what keeps the file compiling, since the result still reports it. Leave
# DeprecatePatch alone.
docker compose --env-file deploy/.env -f deploy/docker-compose.yml stop worker
make worker

# terminal 2. The run queues a letter, then parks.
make onboard PAUSE=90s

# terminal 1 again, while it waits. Ctrl-C FIRST, then restore the file and start
# again. With nothing polling, the timer can fire while you work: the task waits in
# the queue instead of being picked up by the code you are trying to replace.
make worker
```

The parked run wakes on the restored worker, replays, finds no marker for the email
patch in its history, and `make onboard` prints `welcomed by letter` from code whose
default is email. That is the else branch doing its job. Run `make onboard` once more
and it prints `welcomed by email`, because a fresh run has no history to contradict the
patch.

Both orderings matter. Stopping the container worker first is not optional: leave it up
and two workers poll `sandbox` with different builds, the parked run goes to whichever
one claims it, and the result is a coin flip that reads like a flaky SDK. And Ctrl-C
before restoring, not after, because the cut-down worker would otherwise finish the run
itself when the timer fires and print `welcomed by letter` too. That is the same output
for the opposite reason, and nothing on screen would tell you which one you got. Put the
container back with `make up` when you are done.

The pause is read from the workflow's input, so it is fixed in history and replays the
same way, and a run started without it records no timers at all. Its position is
load-bearing: a run parked before the branch has already finished replaying by the time
it reaches `Patched`, so the answer would always be yes.

### The child continues as new instead

`AccountMonitorWorkflow` has no patches, deliberately. Every cycle it closes its current
run and opens a fresh one carrying its state forward, so each boundary resets what the
next deploy has to stay compatible with.

That is a bound, not an exemption. A run already mid-cycle gets replayed by the new
worker on its very next workflow task, same as any other open workflow, and a change
that run's history cannot account for fails that task and keeps failing it rather than
waiting politely for the boundary. What cycling buys is a short, known window: hold the
old workers until it turns over, or patch the change anyway. An hourly cycle makes
waiting cheap, which is the reason to cycle hourly. The same mechanism caps history
growth, which a run that never ends otherwise cannot do.

A wedged run is not a lost one. Temporal keeps retrying the failed workflow task, so
deploying the old code back recovers it with nothing else to do. That was measured here
rather than assumed: a monitor wedged mid-cycle by an added activity sat failing, then
completed normally once the change was rolled back.

The parent cannot make that trade. Its whole life is shorter than one deploy, so it has
nowhere to put a boundary.

| | Shipped default | Production shape |
|---|---|---|
| Check interval | 15s | 15m |
| Cycle length | 2m, so 8 checks | 1h |
| Cycles | 3, about 6 minutes | unlimited |

The defaults are sized to watch. `MonitorSettings.Production` in the sample carries the
other column, and the numbers travel in the workflow's input rather than in `deploy/.env`.

### The child outlives the parent

The parent starts the monitor and does not wait for it:

```csharp
Workflow.StartChildWorkflowAsync(..., new ChildWorkflowOptions
{
    Id = $"{Workflow.Info.WorkflowId}-monitor",
    ParentClosePolicy = ParentClosePolicy.Abandon,
});
```

`ParentClosePolicy` defaults to `Terminate`. Without that line the monitor is killed the
instant the parent returns, and the only symptom is a child that is somehow never
running. The child's ID is the parent's with a suffix, so either one finds the other, and
continue-as-new never changes a workflow ID, so that stays true for the monitor's whole
life.

`make onboard` prints both IDs. Follow it:

```sh
temporal workflow describe --workflow-id <parent>            # Completed
temporal workflow describe --workflow-id <parent>-monitor    # Running, new run ID each cycle
```

A monitor is server state, not a container, so it keeps running after `make down`
returns. Nothing times a running workflow out: retention governs how long closed
histories are kept, and continue-as-new keeps opening fresh runs. The shipped default is
bounded at three cycles so a demo does not outlive the session. `MonitorSettings.Production`
has no bound at all, so `make reset` or a terminate is the only thing that clears one.

## Load testing

```sh
make load              # demo, 5 workflows in flight
make load MODE=bench   # 50, enough to find where this box binds
make load MODE=soak    # 20, meant to run for hours
make unload            # stop it, leave the rest of the stack up
```

The generator is `benchmark-workers`, behind the `load` compose profile so `make up`
never starts it. It drives `HelloWorkflow` in a closed loop: start a workflow, wait
for its result, start the next. There is no duration or rate flag, so concurrency is
the only throttle and the run continues until you stop it. The three modes differ by
that number and by how long you leave them going, which is why they are one target
with a `MODE=` rather than three.

`make load` blocks until it has seen the completed counter actually move. That check
matters more than it sounds: a generator pointed at a workflow whose argument shape
does not match fails as a workflow task, Temporal retries it forever, and the failure
counter stays at zero while nothing finishes. "The container is up" and "the endpoint
answers" are both true in that state.

Its own view of the run is on the **Load generator (client view)** row of the server
dashboard: throughput, and client-observed latency including the queueing the server
cannot see for itself. That is the reason the generator is scraped at all rather than
read off `workflow_success`.

### What the numbers are worth

Comparable to another run on the same machine, with the same shard count and the same
worker image. Nothing else.

Everything shares one laptop, so the generator competes for CPU with what it measures.
No container sets a CPU or memory limit. The worker runs at the SDK's default poller
and slot counts, untouched by this repo, and at `MODE=bench` those are the most likely
limiter rather than Temporal or .NET. `NUM_HISTORY_SHARDS` defaults to 4, low enough
that `bench` warns about it, and raising it means `make reset` because the count is
fixed when the schema is created.

So it answers "did my change make this faster", "where does the latency go", and "when
does the worker saturate". It is not a throughput figure for Temporal, and a screenshot
of it is not one either.

A soak is the one thing here that can leave your machine worse than it found it. At
demo concurrency a 90-second run put 479 workflows through and grew the Postgres volume
by about 18 MB, roughly 39 KB each, and `TEMPORAL_RETENTION` keeps them for 72h. An
hour at `bench` is gigabytes. `make down` does not reclaim it; `make reset` does, and
lowering retention before a long run is the cheaper option.

### Notes before a long run

- Stop the load before `make up`. That rebuilds and recreates the worker underneath a
  running generator.
- The first `make load` pulls a new image, so it takes longer than the minute the top
  of this file promises.
- After a soak the workflow list is thousands of generated runs. Filter by
  `WorkflowType` to find anything else.
- Prometheus shows the `loadgen` target red on an idle box. A static scrape config has
  no notion of an optional target, so that is the resting state rather than a fault.
- Scaling the worker does not work today: its host port is fixed, so replicas collide,
  and the scrape target would reach only one of them.

## Notes

- The worker runs as a container by default. It retries its initial connection,
  because Temporal's client does not and compose does not re-check dependencies
  when a container restarts.
- Workflow code lives in `.workflow.cs` files. `.editorconfig` scopes the analyzer
  exemptions workflows need to that extension, so ordinary code still gets flagged.
- The .NET SDK has no workflow sandbox. Determinism is on you: no `Task.Run`, no
  `Task.Delay`, no `ConfigureAwait(false)` inside a workflow.
