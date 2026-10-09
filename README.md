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
| Database UI | http://localhost:8089 | Off by default. `make tools` starts it |

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

The database has no UI of its own. `make tools` adds Adminer on the URL above,
behind a compose profile so it stays out of the default stack. It opens
pre-pointed at the `postgresql` host; log in with the credentials from
`deploy/.env` and pick the `temporal` or `temporal_visibility` database.

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
| `make tools` | Start the optional database UI |
| `make run NAME=you` | Run one workflow and print the result |
| `make worker` | Run the worker on the host instead of in a container |
| `make test` | Run the test suite |
| `make logs` | Follow logs from every service |

`make worker` is the fast iteration loop: stop the container, run the worker from
your editor against the containerized server, skip the image rebuild.

## Layout

```
src/Sandbox.Abstractions/     ISampleModule, shared config
src/Sandbox.Samples.Hello/    HelloWorkflow and its activity
src/Sandbox.Worker/           host, runtime, metrics, sample discovery
src/Sandbox.Client/           starter
deploy/                       compose file, Prometheus and Grafana config
docker/                       worker image
scripts/                      startup gate, smoke test, URL banner
tests/                        workflow tests
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

## Load testing

Not wired up yet. The pieces are in place for it:

```yaml
  loadgen:
    image: ghcr.io/temporalio/benchmark-workers:main
    # Behind a profile so `make up` never starts it. Run it with
    # `docker compose --profile load up -d loadgen`. The profile also keeps the
    # startup gate honest: compose omits inactive-profile services from
    # `config --services`, so scripts/wait-healthy.sh will not wait for a
    # container that was never meant to start.
    profiles: [load]
    command: ["runner", "-c", "50", "-t", "HelloWorkflow", "-tq", "${TEMPORAL_TASK_QUEUE}"]
    environment:
      TEMPORAL_GRPC_ENDPOINT: temporal:7233
      TEMPORAL_NAMESPACE: ${TEMPORAL_NAMESPACE}
      PROMETHEUS_ENDPOINT: ":9095"
    depends_on:
      temporal: { condition: service_healthy }
```

plus a scrape job for `loadgen:9095`. That image drives any workflow type on any
task queue, so it exercises the worker here without running one of its own. Its
`benchmark_runner_*` metrics measure latency from the client's point of view,
which is a useful cross-check against what the SDK reports about itself.

`HelloWorkflow` takes a single string specifically so a generator passing one JSON
argument can call it unchanged.

Before running one, raise `NUM_HISTORY_SHARDS` in `deploy/.env`. It defaults to 4,
which is fine for development and low enough that throughput plateaus on shard
count rather than on anything you are trying to measure. Shard count is fixed when
the schema is created, so changing it means `make reset` first.

## Notes

- The worker runs as a container by default. It retries its initial connection,
  because Temporal's client does not and compose does not re-check dependencies
  when a container restarts.
- Workflow code lives in `.workflow.cs` files. `.editorconfig` scopes the analyzer
  exemptions workflows need to that extension, so ordinary code still gets flagged.
- The .NET SDK has no workflow sandbox. Determinism is on you: no `Task.Run`, no
  `Task.Delay`, no `ConfigureAwait(false)` inside a workflow.
