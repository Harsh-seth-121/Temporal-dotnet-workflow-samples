COMPOSE_FILE := deploy/docker-compose.yml
ENV_FILE     := deploy/.env
ENV_EXAMPLE  := deploy/.env.example
COMPOSE      := docker compose --env-file $(ENV_FILE) -f $(COMPOSE_FILE)

# Teardown and inspection need every profile. `docker compose down` only touches
# services in the active profile set, so anything started from a profile keeps
# running through a `down` and even a `reset`, which is exactly when you expect a
# clean machine. The load generator, behind the `load` profile, is the only such
# service today.
#
# `up` deliberately does NOT use this, or every `make up` would start the load
# generator too. That choice has a cost: run `make up` while a soak is going and
# compose sees a loadgen container outside the profile set it was handed, which it
# may report as an orphan. Never silence that warning by adding --remove-orphans
# here. Compose would delete the running loadgen, the soak would stop without
# saying so, and the first sign would be a throughput panel that went flat an hour
# ago. The teardown targets carry that flag precisely because they are supposed to
# remove things.
COMPOSE_ALL  := docker compose --env-file $(ENV_FILE) -f $(COMPOSE_FILE) --profile '*'

.DEFAULT_GOAL := help
.PHONY: help up down reset logs ps env build test run worker smoke urls load unload onboard

help: ## Show available targets
	@grep -hE '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) | awk -F':.*?## ' '{printf "  %-8s %s\n", $$1, $$2}'

env: ## Create deploy/.env from the example, and check it has not drifted
	@if [ ! -f $(ENV_FILE) ]; then \
		cp $(ENV_EXAMPLE) $(ENV_FILE); \
		echo "created $(ENV_FILE) from $(ENV_EXAMPLE)"; \
	else \
		missing=''; \
		for key in $$(grep -oE '^[A-Z_][A-Z0-9_]*' $(ENV_EXAMPLE) | sort -u); do \
			grep -qE "^$$key=" $(ENV_FILE) || missing="$$missing $$key"; \
		done; \
		if [ -n "$$missing" ]; then \
			echo "$(ENV_FILE) is missing keys that $(ENV_EXAMPLE) defines:" >&2; \
			for key in $$missing; do echo "  $$key" >&2; done; \
			echo "" >&2; \
			echo "Compose substitutes an empty string for undefined variables, which" >&2; \
			echo "fails in confusing ways. Add them to $(ENV_FILE), or delete it and" >&2; \
			echo "re-run to regenerate from the example." >&2; \
			exit 1; \
		fi; \
	fi

# --build matters here: without it compose reuses the worker image it built once,
# so editing source and re-running would silently keep running the old code.
up: env ## Start the stack and wait until everything is healthy
	$(COMPOSE) up -d --build
	@scripts/wait-healthy.sh
	@scripts/print-urls.sh

urls: ## Print every URL the stack exposes
	@scripts/print-urls.sh

# --remove-orphans is what clears a container left by `docker compose run`.
# Compose stopped removing those on `down` in 2.10: a one-off cannot be tied to a
# selected service, so it is treated as an orphan. `make smoke` runs one, and
# killing it before `--rm` fires leaves an exited container that scripts/wait-healthy.sh
# has to filter out by label. This is the other half of that, so the stray actually
# goes away instead of being ignored forever. `up` must never carry the flag: it
# runs without the load profile, so it would delete a running load generator.
down: ## Stop the stack, keeping all data
	$(COMPOSE_ALL) down --remove-orphans

reset: ## Stop the stack and delete all data volumes
	$(COMPOSE_ALL) down -v --remove-orphans

logs: ## Follow logs from every service
	$(COMPOSE_ALL) logs -f

ps: ## Show service status
	$(COMPOSE_ALL) ps

smoke: up ## Verify the whole box end to end
	@scripts/smoke.sh

# Concurrency per mode. The runner is closed-loop with no duration or count flag,
# so this is how many workflows it keeps in flight until you stop it, not a rate.
# demo keeps a dashboard readable, bench is sized to actually push the box, soak
# sits between the two and is meant to run for hours.
#
# := rather than ?= on MODE. A command-line `make load MODE=bench` overrides either
# way, but ?= would also let a MODE exported in the caller's shell silently pick the
# mode, which is a surprising way to start fifty concurrent workflows.
LOADGEN_C_DEMO  := 5
LOADGEN_C_BENCH := 50
LOADGEN_C_SOAK  := 20
MODE            := demo

# The whole recipe is one shell command, so an unknown MODE exits before any compose
# call rather than after. Concurrency is passed as a leading shell variable because
# the process environment beats --env-file, which lets one .env serve every mode.
load: up ## Start the load generator; MODE=demo|bench|soak
	@case '$(MODE)' in \
		demo)  concurrency=$(LOADGEN_C_DEMO) ;; \
		soak)  concurrency=$(LOADGEN_C_SOAK) ;; \
		bench) \
			concurrency=$(LOADGEN_C_BENCH); \
			shards=$$(sed -n 's/^NUM_HISTORY_SHARDS=//p' $(ENV_FILE) | tr -d '\r' | head -1); \
			if [ "$$shards" -le 4 ] 2>/dev/null; then \
				echo "warning: NUM_HISTORY_SHARDS is $$shards. At this concurrency the box" >&2; \
				echo "plateaus on shard count rather than on whatever you set out to measure." >&2; \
				echo "It is fixed at schema creation, so raising it means make reset, an edit" >&2; \
				echo "to $(ENV_FILE), then make up." >&2; \
			fi ;; \
		*) \
			echo "unknown MODE '$(MODE)'. Pick demo, bench, or soak." >&2; \
			exit 1 ;; \
	esac; \
	echo "starting the load generator: MODE=$(MODE), $$concurrency concurrent workflows"; \
	LOADGEN_CONCURRENCY=$$concurrency $(COMPOSE) --profile load up -d loadgen
	@scripts/assert-load-running.sh

# rm rather than stop, so nothing is left holding the previous run's concurrency and
# `make ps` reads truthfully. COMPOSE_ALL for consistency with the other teardown
# targets; naming a profiled service does activate its profile on its own.
unload: ## Stop the load generator, leaving the rest of the stack up
	$(COMPOSE_ALL) rm -sf loadgen

build: ## Build the solution
	dotnet build

test: ## Run the test suite
	dotnet test

run: ## Start one HelloWorkflow and print the result
	@dotnet run --project src/Sandbox.Client -- "$(NAME)"

# Both variables are always passed, so a blank arrives when one was not set, which is
# the same deal `run` makes with NAME. PAUSE parks the run just after the welcome step,
# which is how you keep it open long enough to change the workflow underneath it; see
# the patching section of the README.
onboard: ## Start one OnboardingWorkflow; ACCOUNT=, PAUSE= holds it open
	@dotnet run --project src/Sandbox.Onboarding.Client -- "$(ACCOUNT)" "$(PAUSE)"

worker: ## Run the worker on the host against the containerized server
	dotnet run --project src/Sandbox.Worker
