#!/usr/bin/env bash
# Blocks until every service in the stack has settled, then reports.
#
# `docker compose up --wait` cannot be used here: it waits for services to be
# running or healthy, and the two admin-tools init containers are supposed to run
# once and exit. A clean exit 0 from those is success, but --wait scores it as a
# failure and returns non-zero.
#
# Settled means:
#   labelled sandbox.lifecycle=oneshot -> exited with code 0
#   everything else                    -> running, and healthy if it has a healthcheck
#
# The label is load-bearing. Without it a one-shot that is still working counts as
# "running with no healthcheck" and passes the gate, so `make up` would return
# before the namespace exists.
set -euo pipefail

cd "$(dirname "$0")/.."

COMPOSE=(docker compose --env-file deploy/.env -f deploy/docker-compose.yml)
TIMEOUT="${WAIT_TIMEOUT:-180}"
deadline=$(( SECONDS + TIMEOUT ))

# Compared against the containers actually present. Without this check, a stack
# that never started reports zero containers, which means zero failures and zero
# pending, which would otherwise be indistinguishable from success.
expected=$("${COMPOSE[@]}" config --services | grep -c .)

while :; do
    status="$("${COMPOSE[@]}" ps -a --format json | EXPECTED="$expected" python3 -c '
import sys, os, json

expected = int(os.environ["EXPECTED"])
pending, failed, ok = [], [], []
seen = 0

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    svc = json.loads(line)
    seen += 1
    name = svc.get("Service")
    state = svc.get("State")
    health = svc.get("Health") or ""
    code = svc.get("ExitCode", 0)

    # Labels arrive as one comma-joined k=v string. Only sandbox.lifecycle is read
    # and its value is a fixed single word, so comma-in-value is not a concern here.
    labels = dict(
        pair.split("=", 1)
        for pair in (svc.get("Labels") or "").split(",")
        if "=" in pair
    )

    if labels.get("sandbox.lifecycle") == "oneshot":
        if state == "exited":
            (ok if code == 0 else failed).append(f"{name} (exit {code})")
        else:
            pending.append(f"{name} ({state})")
    elif state == "running":
        if health and health != "healthy":
            (failed if health == "unhealthy" else pending).append(f"{name} ({health})")
        else:
            ok.append(name)
    else:
        failed.append(f"{name} ({state}, exit {code})")

if seen < expected:
    pending.append(f"{expected - seen} of {expected} containers not created yet")

print("FAILED" if failed else "PENDING" if pending else "READY")
print("; ".join(failed or pending or ok))
')"

    verdict="$(printf '%s' "$status" | head -1)"
    detail="$(printf '%s' "$status" | tail -1)"

    case "$verdict" in
        READY)
            echo "stack ready: ${detail}"
            exit 0
            ;;
        FAILED)
            echo "stack failed: ${detail}" >&2
            "${COMPOSE[@]}" ps -a >&2
            exit 1
            ;;
    esac

    if (( SECONDS >= deadline )); then
        echo "timed out after ${TIMEOUT}s waiting on: ${detail}" >&2
        "${COMPOSE[@]}" ps -a >&2
        exit 1
    fi

    sleep 2
done
