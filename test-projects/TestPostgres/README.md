# Test PostgreSQL Container

This stack uses the official Docker Hub PostgreSQL image: `postgres:16-alpine`.

## Start

```powershell
docker compose up -d
```

## Verify

```powershell
docker compose ps
docker exec -it test-postgres psql -U appuser -d appdb -c "select version();"
```

## Connection details

- Host: `localhost`
- Port: `5432`
- Database: `appdb`
- Username: `appuser`
- Password: `apppassword`

## Stop

```powershell
docker compose down
```
