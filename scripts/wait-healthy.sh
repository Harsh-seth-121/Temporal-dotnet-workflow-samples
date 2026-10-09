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
#
# `ps -a` lists containers this gate has no business judging, because the service
# list it measures against does not contain them. Two kinds turn up:
#
#   - Services whose profile is inactive. Compose leaves those out of
#     `config --services`, so they are filtered out by name below.
#   - One-off containers from `docker compose run`, which borrow a real service's
#     name and its labels. A leftover one reads as a oneshot service that exited
#     non-zero; the compose oneoff label is what tells the two apart.
#
# Either kind otherwise fails every later `make up`, naming a service that is
# perfectly healthy. The inactive-profile one clears on `make down`, which selects
# every profile. The one-off does not: compose has not removed `run` containers on
# `down` since 2.10, which is why the teardown targets pass --remove-orphans.
#
# Both are filtered by name and by label rather than with `ps --orphans=false`.
# That flag happens to drop inactive-profile containers today, but compose
# documents an orphan as a service "not declared by project", which a disabled
# service still is, and it does not exist on older compose releases.
set -euo pipefail

cd "$(dirname "$0")/.."

COMPOSE=(docker compose --env-file deploy/.env -f deploy/docker-compose.yml)
TIMEOUT="${WAIT_TIMEOUT:-180}"
deadline=$(( SECONDS + TIMEOUT ))

# Compared against the containers actually present. Without this check, a stack
# that never started reports zero containers, which means zero failures and zero
# pending, which would otherwise be indistinguishable from success.
#
# The names are kept, not just the count: the same list that sets the target has
# to be the one that decides which containers count toward it, or the two can
# drift apart and the mismatch is exactly the bug being avoided.
services=$("${COMPOSE[@]}" config --services)

while :; do
    # The body below is single-quoted, so it can contain no apostrophe anywhere.
    #
    # The || is not decoration. Under `set -e` with pipefail, a compose that dies
    # mid-wait, say because the daemon restarted, would abort the script here with
    # no output at all, skipping every error branch below.
    status="$("${COMPOSE[@]}" ps -a --format json | SERVICES="$services" python3 -c '
import sys, os, json

services = set(os.environ["SERVICES"].split())
expected = len(services)
pending, failed, ok = [], [], []
seen, extra = set(), set()

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    svc = json.loads(line)
    name = svc.get("Service")
    state = svc.get("State")
    health = svc.get("Health") or ""
    code = svc.get("ExitCode") or 0

    # Not a service this run is responsible for. A missing name lands here too,
    # which keeps a malformed record from taking a slot in the count below.
    # Skipped from the verdict, but a running one is still named in the output:
    # it competes for the same task queue and database, so an unmentioned one
    # makes a saturated box look idle.
    if name not in services:
        if state == "running":
            extra.add(name)
        continue

    # Labels arrive as one comma-joined k=v string, so a comma inside any value
    # splits that value into fragments. Harmless here: no label on these images
    # carries one, and a fragment only becomes a key if it also contains an =.
    labels = dict(
        pair.split("=", 1)
        for pair in (svc.get("Labels") or "").split(",")
        if "=" in pair
    )

    # A `docker compose run` container, not the service itself. It borrows the
    # service name and labels, so judging it would mean judging a second copy of
    # a service against a list that counts one. Compared case-insensitively: the
    # value is compose internals, and matching it exactly would fail open.
    if labels.get("com.docker.compose.oneoff", "").lower() == "true":
        continue

    # A set, not a counter: two containers for one service must not offset a
    # service with none, which would silence the "not created yet" guard below.
    seen.add(name)

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

# Named rather than counted. A bare tally reads as a contradiction when a service
# has a container that was skipped above: the gate says none exists, `ps -a` shows
# one, and the two statements are about different things.
missing = services - seen
if missing:
    pending.append("no container yet: " + ", ".join(sorted(missing)))

note = "; also running, not waited on: " + ", ".join(sorted(extra)) if extra else ""

print("FAILED" if failed else "PENDING" if pending else "READY")
print("; ".join(failed or pending or ok) + note)
')" || { echo "could not read container state from compose" >&2; exit 1; }

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
