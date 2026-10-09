#!/usr/bin/env bash
# Asserts that the load generator is actually doing work, not merely up.
#
# This exists because "the container is running" and "the metrics endpoint answers"
# are both true of a generator that completes nothing. The runner waits for each
# workflow before starting the next, and a workflow whose argument shape does not
# match fails as a workflow task, which Temporal retries forever. The goroutines
# block, nothing completes, and the failure counter stays at zero, so the obvious
# signal is the misleading one. The only trustworthy reading is the completed
# counter going up.
#
# Reads the counter straight off the generator rather than out of Prometheus. That
# job is scraped every 60s, so proving an increment through Prometheus would mean
# waiting up to two scrapes to answer a question the endpoint can settle in seconds.
# Whether Prometheus is configured to scrape it at all is a separate fact, asserted
# in scripts/smoke.sh.
set -uo pipefail

cd "$(dirname "$0")/.."

[ -f deploy/.env ] || { echo "deploy/.env is missing. Run 'make up' first." >&2; exit 1; }

# Same shape as scripts/smoke.sh rather than scripts/print-urls.sh: a missing value
# has to stop this script, not quietly produce a URL with a hole in it.
env_value() {
    local value
    value=$(sed -n "s/^$1=//p" deploy/.env | tr -d '\r' | head -1)
    if [ -z "$value" ]; then
        echo "deploy/.env has no value for $1" >&2
        return 1
    fi
    printf '%s' "$value"
}

port=$(env_value PORT_LOADGEN) || exit 1
URL="http://localhost:${port}/metrics"

COMPOSE=(docker compose --env-file deploy/.env -f deploy/docker-compose.yml --profile load)

# Errors are not redirected away here. A curl that fails for one reason is worth
# telling apart from one that fails for another, and the last failure is printed
# verbatim when the assertion gives up.
completed() {
    curl -sf --max-time 5 "$URL" \
        | awk '/^benchmark_runner_invocations_completed_total /{print $2; found=1} END{exit !found}'
}

state() {
    "${COMPOSE[@]}" ps -a --format json loadgen 2>/dev/null \
        | python3 -c 'import sys, json
for line in sys.stdin:
    line = line.strip()
    if line:
        print(json.loads(line).get("State", "unknown")); break
else:
    print("absent")'
}

DEADLINE=$(( SECONDS + ${LOAD_ASSERT_SECONDS:-45} ))

printf '  %-46s ' "load generator completing workflows"

first=""
last_err="no reading taken"
while :; do
    # Re-checked every pass, not sampled once before the loop. A container that is
    # crash-looping is running at some instants and not at others, and a single
    # early sample is exactly the one that misses it.
    current=$(state)
    case "$current" in
        running) ;;
        absent)
            echo "FAILED"
            echo "      no loadgen container. 'make load' should have started one." >&2
            exit 1
            ;;
        *)
            echo "FAILED"
            echo "      loadgen is $current, not running." >&2
            echo "      'make logs' will show why it stopped." >&2
            exit 1
            ;;
    esac

    if reading=$(completed 2>&1); then
        if [ -z "$first" ]; then
            first="$reading"
        elif awk -v a="$first" -v b="$reading" 'BEGIN{exit !(b > a)}'; then
            echo "ok"
            echo "      completed $first -> $reading workflows"
            exit 0
        fi
    else
        last_err="$reading"
    fi

    if (( SECONDS >= DEADLINE )); then
        echo "FAILED"
        if [ -n "$first" ]; then
            # The endpoint answered, so the generator is up and simply not finishing
            # anything. Naming the counter that did not move is the useful part.
            queue=$(env_value TEMPORAL_TASK_QUEUE 2>/dev/null) || queue="the task queue"
            echo "      completed stayed at $first. The generator is running but nothing" >&2
            echo "      is finishing. Check that a worker is polling ${queue} and that it" >&2
            echo "      registers the workflow type the generator is driving." >&2
        else
            echo "      never read the counter from ${URL}" >&2
            echo "      last error: ${last_err}" >&2
        fi
        exit 1
    fi
    sleep 3
done
