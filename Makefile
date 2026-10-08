COMPOSE_FILE := deploy/docker-compose.yml
ENV_FILE     := deploy/.env
ENV_EXAMPLE  := deploy/.env.example
COMPOSE      := docker compose --env-file $(ENV_FILE) -f $(COMPOSE_FILE)

.DEFAULT_GOAL := help
.PHONY: help up down reset logs ps env build test run worker

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

up: env ## Start the stack and wait until everything is healthy
	$(COMPOSE) up -d
	@scripts/wait-healthy.sh

down: ## Stop the stack, keeping all data
	$(COMPOSE) down

reset: ## Stop the stack and delete all data volumes
	$(COMPOSE) down -v

logs: ## Follow logs from every service
	$(COMPOSE) logs -f

ps: ## Show service status
	$(COMPOSE) ps

build: ## Build the solution
	dotnet build

test: ## Run the test suite
	dotnet test

run: ## Start one HelloWorkflow and print the result
	@dotnet run --project src/Sandbox.Client -- "$(NAME)"

worker: ## Run the worker on the host against the containerized server
	dotnet run --project src/Sandbox.Worker
