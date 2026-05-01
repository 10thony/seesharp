# Infra: Self-hosted Convex + .NET CLI bridge

This folder contains:

- `docker-compose.yml`: self-hosted Convex backend + dashboard.
- `.env.local`: local Convex URL/admin key for CLI use.
- `convex-dotnet-unofficial/`: cloned SDK source from `zakstam/convex-dotnet-unofficial`.
- `ConvexCliBridge/`: minimal .NET CLI bridge wired to `.env.local`.

## Start Convex

```powershell
docker compose -f .\docker-compose.yml up -d
```

## Run the .NET bridge CLI

```powershell
dotnet run --project .\ConvexCliBridge\ConvexCliBridge.csproj
```

The bridge reads:

- `CONVEX_SELF_HOSTED_URL`
- `CONVEX_SELF_HOSTED_ADMIN_KEY`

from `.\.env.local`, applies admin auth, and checks backend health.
