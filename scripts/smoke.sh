#!/usr/bin/env bash
# End-to-end check that the sandbox actually works.
#
# Asserts rather than observes. Every step either proves something or fails the
# script, so a green run means the box is genuinely wired up: a workflow executed
# through the worker, and both sides of the wire are being scraped.
#
# Needs no .NET on the host. The workflow is driven by the temporal CLI inside the
# admin-tools image, so a fresh clone can verify the stack before building anything.
set -uo pipefail

cd "$(dirname "$0")/.."

COMPOSE=(docker compose --env-file deploy/.env -f deploy/docker-compose.yml)
EXPECTED='"Hello, Smoke!"'

if [ ! -f deploy/.env ]; then
    echo "deploy/.env is missing. Run 'make up' first, which creates it." >&2
    exit 1
fi

# Takes everything after the first '=' so values containing '=' survive, and
# strips any carriage return so a file saved with Windows line endings does not
# produce a port number with an invisible character in it.
# Returns non-zero rather than calling exit: it runs inside a command
# substitution, and an exit there ends only the subshell, leaving the script to
# carry on with an empty value.
env_value() {
    local value
    value=$(sed -n "s/^$1=//p" deploy/.env | tr -d '\r' | head -1)
    if [ -z "$value" ]; then
        echo "deploy/.env has no value for $1" >&2
        return 1
    fi
    printf '%s' "$value"
}

# An assignment takes the exit status of its command substitution, so these guards
# genuinely stop the script.
port_prometheus=$(env_value PORT_PROMETHEUS) || exit 1
port_grafana=$(env_value PORT_GRAFANA)       || exit 1
port_adminer=$(env_value PORT_ADMINER)       || exit 1
NAMESPACE=$(env_value TEMPORAL_NAMESPACE)    || exit 1
TASK_QUEUE=$(env_value TEMPORAL_TASK_QUEUE)  || exit 1

PROMETHEUS_URL="http://localhost:${port_prometheus}"
GRAFANA_URL="http://localhost:${port_grafana}"
ADMINER_URL="http://localhost:${port_adminer}"

failures=0

# Metric assertions are retried rather than sampled once. Prometheus scrapes on an
# interval, and a worker that restarted moments ago has reset its in-process
# counters, so a single sample right after any disruption reports a failure that
# resolves itself a few seconds later. A smoke test that fails spuriously is worse
# than no smoke test.
RETRY_SECONDS="${SMOKE_RETRY_SECONDS:-60}"

check() {
    local label="$1"
    shift
    # Once anything has failed the run is already failing, so stop paying the
    # retry window for every remaining check.
    local window=$RETRY_SECONDS
    [ "$failures" -eq 0 ] || window=0
    local deadline=$(( SECONDS + window ))

    printf '  %-46s ' "$label"
    while :; do
        if "$@" >/tmp/smoke-step.out 2>&1; then
            echo "ok"
            return
        fi
        if (( SECONDS >= deadline )); then
            echo "FAILED (retried for ${RETRY_SECONDS}s)"
            sed 's/^/      /' /tmp/smoke-step.out | tail -5
            failures=$((failures + 1))
            return
        fi
        sleep 3
    done
}

echo "Waiting for the stack"
if ! scripts/wait-healthy.sh; then
    echo "stack never became healthy" >&2
    exit 1
fi

echo
echo "Running a workflow"
# --execution-timeout bounds this. Without it, a workflow nobody is polling for
# would hang the script forever rather than failing it.
workflow_output=$("${COMPOSE[@]}" run --rm --no-deps \
    --entrypoint temporal temporal-setup \
    workflow execute \
        --address temporal:7233 \
        --namespace "$NAMESPACE" \
        --task-queue "$TASK_QUEUE" \
        --type HelloWorkflow \
        --workflow-id "smoke-$(date +%s)" \
        --input '"Smoke"' \
        --execution-timeout 60s 2>&1)

printf '  %-46s ' "workflow returned ${EXPECTED}"
if printf '%s' "$workflow_output" | grep -qF "$EXPECTED"; then
    echo "ok"
else
    echo "FAILED"
    printf '%s\n' "$workflow_output" | tail -12 | sed 's/^/      /'
    failures=$((failures + 1))
fi

echo
echo "Checking metrics"

# Named, not counted. The load generator adds a fourth configured job that is down
# on an idle box, and a count of three then passes on a box where the server target
# is red and the load generator happens to be green.
core_targets_up() {
    curl -sf "${PROMETHEUS_URL}/api/v1/targets?state=active" \
        | python3 -c "
import sys, json
want = {'temporal-server', 'temporal-dotnet-worker', 'prometheus'}
up = {t['labels']['job'] for t in json.load(sys.stdin)['data']['activeTargets']
      if t['health'] == 'up'}
missing = want - up
if missing:
    print('not up: ' + ', '.join(sorted(missing)))
    raise SystemExit(1)
"
}

# That the job exists is asserted; that its target is up is not. Load is optional, so
# requiring it would fail on an idle box. But the job going missing is a real and
# silent failure: compose does not recreate Prometheus when a bind-mounted file
# changes, so anyone who added the new env keys by hand rather than regenerating
# deploy/.env keeps the old scrape config. Nothing errors, and the dashboard row just
# stays empty forever.
loadgen_job_configured() {
    curl -sf "${PROMETHEUS_URL}/api/v1/targets?state=active" \
        | python3 -c "
import sys, json
jobs = {t['labels']['job'] for t in json.load(sys.stdin)['data']['activeTargets']}
raise SystemExit(0 if 'loadgen' in jobs else 1)
"
}

dashboards_provisioned() {
    local count
    count=$(curl -sf "${GRAFANA_URL}/api/search?type=dash-db" \
        | python3 -c "import sys, json; print(len(json.load(sys.stdin)))")
    [ "$count" -ge "$1" ]
}

series_present() {
    local count
    count=$(curl -sf --get "${PROMETHEUS_URL}/api/v1/query" --data-urlencode "query=$1" \
        | python3 -c "import sys, json; print(len(json.load(sys.stdin)['data']['result']))")
    [ "$count" -gt 0 ]
}

check "prometheus has the core targets up"      core_targets_up
check "load generator scrape job configured"    loadgen_job_configured
check "server metrics present"                  series_present 'service_requests'
check "server exposes all four roles"           series_present 'count by (service_name) (service_requests)'
check "worker SDK metrics present"              series_present 'temporal_worker_task_slots_available'
check "workflow completion recorded by the SDK" series_present 'temporal_workflow_completed'
check "grafana datasource provisioned"          curl -sf "${GRAFANA_URL}/api/datasources"
check "grafana dashboards provisioned"          dashboards_provisioned 2

echo
echo "Checking the database UI"

# Asserts the link the banner prints, not just that the container serves pages.
# Adminer has no environment variable for the driver and its login form defaults
# to MySQL, so the query string is the whole mechanism; an Adminer release that
# changed that contract would otherwise fail silently in the browser.
adminer_pinned_to_postgres() {
    curl -sf "${ADMINER_URL}/?pgsql=postgresql" \
        | grep -q '<option value="pgsql" selected'
}

check "database UI opens on PostgreSQL"         adminer_pinned_to_postgres

echo
if [ "$failures" -eq 0 ]; then
    echo "smoke test passed"
    exit 0
fi

echo "smoke test failed: ${failures} check(s)" >&2
exit 1
