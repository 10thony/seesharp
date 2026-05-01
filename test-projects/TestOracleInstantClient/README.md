# Test Oracle Instant Client Container

This setup follows Oracle Container Registry usage for Instant Client.

## Prerequisites

1. Oracle account with access to Oracle Container Registry.
2. Docker Desktop running.

## Login and pull

From this folder:

```powershell
docker login container-registry.oracle.com
docker compose pull
```

Oracle may require that you accept license terms in the registry UI for the `database/instantclient` repository before pull succeeds.

## Start container

```powershell
docker compose up -d
```

## Enter container

```powershell
docker exec -it test-oracle-instant-client bash
```

## Stop container

```powershell
docker compose down
```
