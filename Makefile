COMPOSE_FILE := deploy/docker-compose.yml
ENV_FILE     := deploy/.env
ENV_EXAMPLE  := deploy/.env.example
COMPOSE      := docker compose --env-file $(ENV_FILE) -f $(COMPOSE_FILE)

# Teardown and inspection need every profile. `docker compose down` only touches
# services in the active profile set, so anything started from a profile keeps
# running through a `down` and even a `reset`, which is exactly when you expect a
# clean machine. Nothing in the default stack sits behind a profile today; the
# load generator sketched in the README does. `up` deliberately does NOT use this,
# or it would start them.
COMPOSE_ALL  := docker compose --env-file $(ENV_FILE) -f $(COMPOSE_FILE) --profile '*'

.DEFAULT_GOAL := help
.PHONY: help up down reset logs ps env build test run worker smoke urls

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

down: ## Stop the stack, keeping all data
	$(COMPOSE_ALL) down

reset: ## Stop the stack and delete all data volumes
	$(COMPOSE_ALL) down -v

logs: ## Follow logs from every service
	$(COMPOSE_ALL) logs -f

ps: ## Show service status
	$(COMPOSE_ALL) ps

smoke: up ## Verify the whole box end to end
	@scripts/smoke.sh

build: ## Build the solution
	dotnet build

test: ## Run the test suite
	dotnet test

run: ## Start one HelloWorkflow and print the result
	@dotnet run --project src/Sandbox.Client -- "$(NAME)"

worker: ## Run the worker on the host against the containerized server
	dotnet run --project src/Sandbox.Worker
