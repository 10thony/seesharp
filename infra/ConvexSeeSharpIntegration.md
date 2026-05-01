# Integrate Convex Into SeeSharp (via `convex-dotnet-unofficial`)

This guide describes how to integrate Convex directly into `SeeSharp` so objects in `Models/` can be persisted and queried from your self-hosted Convex backend.

---

## 1) Current baseline

Already present in this repo:

- Self-hosted Convex infra in `infra/docker-compose.yml`
- Convex local env in `infra/.env.local`
  - `CONVEX_SELF_HOSTED_URL`
  - `CONVEX_SELF_HOSTED_ADMIN_KEY`
- Local clone of unofficial .NET SDK in `infra/convex-dotnet-unofficial`

Goal:

- Add Convex support to the main `SeeSharp` app (`Program.cs` + `Models/`) rather than a separate bridge.

---

## 2) Add package to SeeSharp

In `SeeSharp.csproj`, add:

- `Convex.Client` (currently prerelease on NuGet, e.g. `5.2.4-beta`)

Example:

```xml
<ItemGroup>
  <PackageReference Include="Convex.Client" Version="5.2.4-beta" />
</ItemGroup>
```

For full integration (codegen-first), also enable generator features in `SeeSharp.csproj`:

```xml
<PropertyGroup>
  <ConvexGenerateFunctions>true</ConvexGenerateFunctions>
  <ConvexGenerateArgs>true</ConvexGenerateArgs>
  <ConvexGenerateModels>true</ConvexGenerateModels>
  <ConvexGenerateTypedIds>true</ConvexGenerateTypedIds>
</PropertyGroup>
```

If you are temporarily not using TS backend codegen yet, you can suppress warnings:

```xml
<PropertyGroup>
  <ConvexSuppressGeneratorWarnings>true</ConvexSuppressGeneratorWarnings>
</PropertyGroup>
```

---

## 3) Load Convex config from `infra/.env.local`

Because `SeeSharp` currently reads env vars directly (e.g. `LMSTUDIO_API_KEY` in `Program.cs`), follow the same pattern:

1. Resolve `infra/.env.local` at startup.
2. Parse key/value lines.
3. Load:
   - `CONVEX_SELF_HOSTED_URL`
   - `CONVEX_SELF_HOSTED_ADMIN_KEY`
4. Fail fast if either is missing in local/dev runs.

Suggested placement:

- Add a small `ConvexConfig` helper in `Models/` (or `Dev/`) and call it from `Program.cs` during startup.

---

## 4) Add a shared singleton Convex service (required)

Create a new class that acts as the **single Convex gateway** for the whole app/CLI (example path):

- `Models/ConvexService.cs`

Responsibilities:

- Build exactly one `ConvexClient` instance from `CONVEX_SELF_HOSTED_URL` and reuse it (singleton lifetime).
- Apply admin auth once:
  - `await client.Auth.SetAdminAuthAsync(adminKey)`
- Expose typed methods for persistence operations used by `SeeSharp` and SeeSharp CLI.
- Route all Convex communication through this service so calls are not scattered across `Program.cs`/`Agent`.
- Keep Convex function names centralized in generated constants (avoid string scatter).
- Support all operation types needed by backend TS files:
  - queries: `client.Query<T>(...)`
  - mutations: `client.Mutate<T>(...)`
  - actions: `client.Action<T>(...)`

Why singleton:

- Convex client state (auth, retry/reconnect behavior, cache/subscriptions) is managed in one place.
- CLI/runtime call sites stay thin and testable.
- Connection/auth setup happens once instead of being duplicated.

Suggested API shape:

- `Task SaveAgentStateAsync(AgentSnapshot snapshot)`
- `Task<AgentSnapshot?> GetAgentStateAsync(string modelId)`
- `Task SaveToolExecutionAsync(ToolExecutionRecord record)`
- `Task<IReadOnlyList<ToolExecutionRecord>> GetRecentToolExecutionsAsync(...)`

---

## 5) Persisted model strategy for current `Models/`

Your current domain types (not all are persistence DTOs yet):

- `Models/Agent.cs`
- `Models/ToolKit.cs`
- `Models/AgentUtilities.cs`
- `Models/AgentDefaults.cs`
- `Models/ThemedConsole.cs`

Recommendation:

- Do **not** persist runtime-heavy classes directly (`Agent`, `ToolKit`, etc.).
- Introduce persistence DTOs specifically for Convex, e.g.:
  - `Models/Persistence/AgentSnapshot.cs`
  - `Models/Persistence/TaskRunRecord.cs`
  - `Models/Persistence/AgentLoopTurnRecord.cs`
  - `Models/Persistence/ToolExecutionRecord.cs`

Why:

- Runtime classes contain services, behavior, and transient state.
- Convex documents should stay flat, serializable, and versionable.

---

## 6) Convex backend shape (`infra/convex` TypeScript)

Add Convex backend functions under an app-local backend folder (for example):

- `infra/convex/schema.ts`
- `infra/convex/functions/agent/save.ts`
- `infra/convex/functions/agent/getByModel.ts`
- `infra/convex/functions/toolExecution/log.ts`
- `infra/convex/functions/toolExecution/listByTaskRun.ts`

Minimum tables:

- `agentSnapshots`
  - `modelId`
  - `taskHash`
  - `repoContextSummary`
  - `lastAssistantText`
  - `updatedAt`
- `toolExecutions`
  - `taskRunId`
  - `toolName`
  - `argsJson`
  - `resultJson`
  - `ok`
  - `createdAt`
- `taskRuns`
  - `taskRunId`
  - `modelId`
  - `taskText`
  - `startedAt`
  - `completedAt`
  - `status`
  - `finalAssistantText`
- `agentLoopTurns`
  - `taskRunId`
  - `turnNumber`
  - `promptDigest`
  - `toolCallsJson`
  - `toolResultsJson`
  - `successfulToolExecutionsSoFar`
  - `contextResetCount`
  - `createdAt`

Then run convex deploy/dev against self-hosted:

```powershell
npx convex dev
```

With:

- `CONVEX_SELF_HOSTED_URL=http://127.0.0.1:33210`
- `CONVEX_SELF_HOSTED_ADMIN_KEY=...`

Important implementation note (codegen gap to cover explicitly):

- Convex codegen gives us function constants and typed args/models (and optional generated services), but it does **not** replace SeeSharp-specific orchestration.
- After writing/updating Convex `.ts` functions and running codegen, we must still write/maintain SeeSharp C# integration files that map CLI/runtime use-cases to those generated functions.
- In practice this means:
  1. Update/add Convex TS functions (`query`/`mutation`/`action`).
  2. Run Convex + C# codegen.
  3. Update `Models/ConvexService.cs` (and related DTO mapping files) so SeeSharp CLI can call those functions through a stable C# API.

---

## 7) Call Convex from SeeSharp runtime flow

Best insertion points in current app:

- In `Program.cs` startup, create/register and validate singleton `ConvexService`.
- In `Agent.ExecuteTaskWithToolLoopAsync(...)`:
  - save task run start record (`taskRuns`)
  - persist each loop turn as a first-class record (`agentLoopTurns`)
  - log each tool execution envelope (`ToolExecutionEnvelope` / `toolExecutions`)
  - save task run final outcome + completion state

High-value persisted data from current flow:

- Task text + model ID
- Full agent-loop telemetry (loop count, turn number, prompt/response envelope shape)
- Context reset attempts / retry counts
- Tool invocation signatures
- Successful/failed tool runs
- Final completion assessment

This gives replay/debug insight without changing the core behavior of `Agent`.
As a hard requirement for this integration, SeeSharp must persist:

- each task run start/end record,
- each agent loop turn,
- and each tool execution within turns.

---

## 8) Security and secret handling

- Keep `infra/.env.local` out of source control (already ignored in `.gitignore`).
- Never print admin key in logs.
- Prefer admin key only for trusted local/server processes.
- For future multi-user mode, switch to user JWT auth for client-facing paths.

---

## 9) Suggested phased rollout

1. **Phase 3 start: Full codegen-first integration**
   - Build `infra/convex` schema/functions first.
   - Enable codegen in `SeeSharp.csproj` (`Functions`, `Args`, `Models`, `TypedIds`).
   - Set `ConvexBackendPath` if needed so generator reads TS backend reliably.
2. **Phase 4: C# service binding for CLI/runtime**
   - Wire generated function constants/types into singleton `Models/ConvexService.cs`.
   - Add/maintain explicit C# wrapper methods for each required query/mutation/action.
   - Keep serialization/DTO mapping in C# so SeeSharp CLI has a stable interface even as TS evolves.
3. **Phase 5: Runtime persistence and retrieval**
   - Persist and query task runs, loop turns, snapshots, and tool execution records from the main `SeeSharp` runtime via `ConvexService`.
4. **Phase 6: Hardening**
   - Add indexes, retention policies, versioned DTO mapping, and migration notes.

---

## 10) Done criteria

Integration is complete when:

- `SeeSharp` starts with Convex enabled from `infra/.env.local`.
- Generated Convex function/arg/model types are used in `SeeSharp` (not raw string function names as the primary path).
- A singleton `ConvexService` is the only integration boundary used by SeeSharp runtime + CLI for query/mutation/action calls.
- Post-codegen C# wrappers/mappings exist for the Convex TS functions SeeSharp depends on.
- A run writes and reads Convex documents (`agentSnapshots`, `taskRuns`, `agentLoopTurns`, and `toolExecutions`) through the integrated runtime path.
- Task records are persisted for every run, including start/end status and final outcome.
- Agent loop telemetry is persisted for every turn taken by the agent during a task.
- `SeeSharp` can query prior run state from Convex to drive diagnostics/context behavior.
- No separate bridge app is required for normal operation.

---

## 11) Best-practice addenda from unofficial .NET client examples/tests

These are concise deltas based on the local `infra/convex-dotnet-unofficial` repo (README, examples, and integration tests), to keep SeeSharp aligned with the author's intended usage patterns without repeating earlier sections.

### 11.1 Build client with `ConvexClientBuilder` defaults, not only constructor

For SeeSharp server/CLI usage, prefer explicit builder configuration so retry/timeout/reconnect behavior is deliberate and visible:

- `UseDeployment(...)`
- `WithTimeout(...)`
- `WithAutoReconnect(...)` (or custom reconnection policy)
- Optional logging hooks for diagnostics in dev

Recommended baseline (server/CLI):

```csharp
var client = new ConvexClientBuilder()
    .UseDeployment(convexUrl)
    .WithTimeout(TimeSpan.FromSeconds(30))
    .WithAutoReconnect(maxAttempts: 5, delayMs: 1000)
    .Build();
```

Then apply admin auth once in startup:

```csharp
await client.Auth.SetAdminAuthAsync(adminKey);
```

### 11.2 Treat client lifetime as app-wide and dispose on shutdown

The examples repeatedly emphasize lifecycle cleanup:

- Dispose subscriptions when no longer needed.
- Dispose client during app shutdown.

For SeeSharp this means:

- Singleton `ConvexService` owns one `ConvexClient`.
- `ConvexService` should implement `IAsyncDisposable` (or `IDisposable`) and release client resources at process exit.

### 11.3 Function naming style: keep backend + C# consistent

The unofficial repo demonstrates both naming forms depending on project layout:

- `"module:function"` style (e.g. test fixtures)
- `"functions/name"` style (many examples)

Pick one convention for SeeSharp backend functions and apply it uniformly in:

- Convex TypeScript exports
- generated C# constants
- any temporary string fallback paths

Do not mix styles within the same SeeSharp backend unless required by migration compatibility.

### 11.4 Non-goal reminder (from SDK README)

The upstream client currently labels itself prerelease/experimental for production-hard guarantees. For SeeSharp rollout:

- keep Phase 6 hardening mandatory (timeouts/retry strategy, index strategy, retention, migration notes),
- and keep Convex persistence behind the `ConvexService` boundary so implementation details can evolve safely.
