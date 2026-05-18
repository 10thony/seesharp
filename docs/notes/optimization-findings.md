# SeeSharp Harness — Optimization Findings

Audit date: 2026-05-18

---

## Architecture Summary

The harness is a .NET 10 console app using the OpenAI .NET SDK pointed at a local
LM Studio server. It has a 5-tool agent loop (BASH, WEB\_CALL, READ\_FILE,
LIST\_FILE, EDIT\_FILE) with a multi-step contextualizer pipeline, a tool-call
parser model, and a task-completion validator model.

Core files: `Program.cs` (entry), `Agent.cs` (agent loop + prompts),
`ToolKit.cs` (tool definitions + execution), `AgentDefaults.cs` (constants),
`AgentUtilities.cs` (utilities), `ThemedConsole.cs` (console styling),
`LocalTestProjectMenu.cs` (dev harness menu).

---

## Efficiency

### 1. Excessive LLM calls per turn

Every single tool turn makes up to **3 LLM calls**: main model → tool-call
parser (small model) → completion validator (small model). With the
contextualizer doing 2 + N calls up front (pick files, summarize each, merge), a
10-file workspace with 5 tool turns burns ~20 LLM calls for one task. For local
models with limited throughput this is the single biggest bottleneck.

- The **tool-call parser** is only needed when `tool:` lines aren't found. Since
  the system prompt controls the format, the main model should be
  trained/prompted to always emit `tool:` lines reliably — eliminating the parser
  model entirely.
- The **completion validator** could be replaced with deterministic heuristics.
  `AnalyzeEvidenceState` already does most of the work; the LLM validator is a
  safety net that rarely overrides it.
- The contextualizer's per-file summarization could be **batched** (send 3–4
  files per call instead of 1).

### 2. Full system prompt re-sent every turn

`StartNewResponseAsync` creates a fresh context window with the full system
prompt (~3 KB) on every single turn. For local models with tiny context windows
(4K–8K tokens), this wastes 30–50% of available context on static boilerplate.
The system prompt should be cached or significantly shorter for continuation
turns.

### 3. Tool bridge message bloat

`BuildToolBridgeMessageAsync` includes the original task + previous assistant
response + all tool results + budget info + seen-tools hint. This grows linearly
per turn. For local models, a "rolling window" that only keeps the last 1–2 tool
results (with a compact task reminder) would be more context-efficient.

### 4. Synchronous-over-async blocking

`WebCall_Tool` and `Bash_Tool` both use `.GetAwaiter().GetResult()` to call
async methods synchronously. This blocks thread pool threads and can cause
deadlocks under load. These should be properly async end-to-end.

### 5. Regex recompilation on every call

Methods like `LooksLikeServiceActionCommand`, `LooksLikeFileWriteCommand`, and
`NormalizeBashSignature` compile regex patterns on every invocation. These should
use `[GeneratedRegex]` source generators or static `Regex` instances.

### 6. SHA256 fingerprinting is overkill for progress detection

`FingerprintSuccessfulToolResult` computes SHA256 hashes of serialized tool
results to detect stalls. A simpler fingerprint (length + first/last 100 chars)
would be equally effective and much cheaper.

### 7. Sequential tool execution

When `MaxToolCallsPerTurn = 2`, both tool calls execute sequentially via
`foreach`. They could run concurrently with `Task.WhenAll` since they're
independent.

---

## Maintainability

### 8. God class — `Agent.cs` is ~2 000 lines

This single class handles: agent loop orchestration, prompt generation, tool-call
parsing, contextualizer pipeline, task-completion assessment, evidence analysis,
context-window recovery, retry logic, progress tracking, and streaming.

Recommended decomposition:

| New class               | Responsibility                          |
|-------------------------|-----------------------------------------|
| `AgentLoop`             | Orchestration only                      |
| `PromptBuilder`         | System / compact / bridge prompts       |
| `ToolCallParser`        | Extraction + validation                 |
| `TaskCompletionValidator` | Assessment + evidence                 |
| `Contextualizer`        | File picking + summarization + merge    |

### 9. `ToolKit.cs` is ~1 200 lines doing too many things

Tool execution, docker-compose YAML parsing, SQL validation, shell process
management, redirect path extraction, and telemetry are all in one class. The
docker-compose autocorrect alone is ~200 lines.

Recommended decomposition:

| New class                   | Responsibility                       |
|-----------------------------|--------------------------------------|
| `ShellExecutor`             | Process creation + telemetry         |
| `DockerComposeHelper`       | YAML parsing + service alias map     |
| `SqlGuard`                  | Raw-SQL and escaped-SQL detection    |

### 10. Untyped `Dictionary<string, object>` everywhere

Tool results, tool args, and tool outputs all use raw dictionaries. This causes
boxing, prevents compile-time safety, and makes the code hard to follow. Strongly
typed result records would help:

```csharp
record BashResult(string Command, string Stdout, string Stderr, int ExitCode, bool Ok);
record WebCallResult(string Url, int StatusCode, string Body);
record ToolError(string Error, string? Hint = null);
```

### 11. Duplicate code

- `ContextualizerPickDto` is defined in **both** `Agent.cs` (line 1719) and
  `AgentUtilities.cs` (line 12).
- Markdown fence stripping appears in both `Agent.cs` (lines 777–789, 1066–1079)
  and `AgentUtilities.StripMarkdownFence`.
- `TryGetArgAsString` in `Agent.cs` duplicates `OptionalArg` in `ToolKit.cs`.

### 12. Dead / deprecated weight

| Item | Location | Action |
|------|----------|--------|
| `Newtonsoft.Json` NuGet reference | `SeeSharp.csproj` | Remove — only `System.Text.Json` is used |
| Empty `Agents\` folder | `SeeSharp.csproj` | Remove `<Folder>` element |
| `LMStudioAgent` / `LMStudioToolKit` | `Agent.cs`, `ToolKit.cs` | Empty subclasses that add zero behavior — inline or delete |
| 3 deprecated tools | `ToolKit.cs` | READ\_FILE, LIST\_FILE, EDIT\_FILE are fully implemented despite being superseded by BASH |
| `CreateResponseResult` class | `Program.cs` line 476 | Dead — shadowed by SDK type usage in `Agent.cs` |
| `PromptUserToSelectModel` | `Program.cs` | Never called |
| Hardcoded `questions` list | `Program.cs` lines 95–123 | Legacy-only dead weight |

### 13. `AgentDefaults` tool names should be `const`, not `static readonly`

```csharp
// Current (static readonly — no compile-time inlining, can't use in switch labels)
public static readonly string BASH_TOOL_NAME = "BASH";

// Proposed (const — inlined at compile time, usable in case labels)
public const string BASH_TOOL_NAME = "BASH";
```

This also enables replacing the bizarre switch pattern:

```csharp
// Current
case var actualToolName when key == AgentDefaults.LIST_FILE_TOOL_NAME:

// Proposed (once names are const)
case AgentDefaults.LIST_FILE_TOOL_NAME:
```

### 14. Hardcoded server URI

```csharp
const string lmStudioBaseUri = "http://cobec-spark:1234/v1";
```

This should come from environment config like the other settings (e.g.
`LMSTUDIO_BASE_URI`).

### 15. `Program.cs` top-level bloat

480+ lines of top-level statements including utility functions
(`TryUnloadModelAsync`, `KeepOnlyModelsLoadedAsync`, `QuoteArg`, etc.) that
belong in proper classes. Consider an `LmStudioModelManager` or similar to house
model lifecycle logic.

---

## Priority Ranking

| Priority | Optimization | Category | Impact |
|----------|-------------|----------|--------|
| 1 | Eliminate tool-call parser LLM call | Efficiency | Cuts ~33% of LLM calls per turn |
| 2 | Replace LLM completion validator with deterministic heuristics | Efficiency | Cuts another ~33% of LLM calls |
| 3 | Batch contextualizer (fewer calls) | Efficiency | Cuts startup LLM calls by 60–80% |
| 4 | Decompose `Agent.cs` god class | Maintainability | Major readability / testability win |
| 5 | Strongly type tool results | Maintainability | Eliminates a whole class of runtime bugs |
| 6 | Remove dead code and unused deps | Maintainability | Reduces cognitive load |
| 7 | Fix async-over-sync | Efficiency | Prevents deadlocks, improves throughput |
| 8 | Compile regexes | Efficiency | Small perf win across many call sites |
| 9 | Make tool names `const` | Maintainability | Cleaner switch, minor perf |
| 10 | Externalize LM Studio URI | Maintainability | Config hygiene |
