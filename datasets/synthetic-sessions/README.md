# SeeSharp Synthetic Sessions for OpenRLHF

This folder ships two views of the same 10 SeeSharp agent trajectories.

## Files

| File | Format | Use for |
|---|---|---|
| `synthetic-sessions.jsonl` | Native SeeSharp `SessionRecorder` event stream (196 events, 10 sessions) | Replay / inspection in SeeSharp tooling; downstream conversion to other RL formats |
| `synthetic-sessions.openrlhf.jsonl` | One row per task, OpenAI/HF-chat `messages[]` shape with `rewards`, `scores`, `extra_logs` | `openrlhf.cli.train_sft` (`--data.input_key messages --data.apply_chat_template`) and PPO/REINFORCE++ with `--reward.remote_url` + `--algo.dynamic_filtering_enable` |

## Why this shape

OpenRLHF's `AgentExecutorBase` produces token-in-token-out trajectories (see [OpenRLHF README](https://github.com/OpenRLHF/OpenRLHF)). Each SeeSharp session is itself a trajectory: turn 1 prompt → assistant message with one `tool:` line → tool result → bridge message → repeat → final answer. The native JSONL already records that exact structure (event types `assistant_response`, `tool_calls`, `tool_results`, `turn_bridge`), so the only thing missing for OpenRLHF SFT is a flattened `messages[]` projection, which is what `synthetic-sessions.openrlhf.jsonl` provides.

For single-turn agent execution with a custom reward function, `rewards` and `scores` on each row plug directly into the `reward_func(queries, prompts, labels)` contract (see OpenRLHF README §"Single-Turn Agent"). For multi-turn agent training (`--train.agent_func_path`), you can recover the stepwise `reset/step` view by walking the per-turn `assistant_response` / `tool_results` events in the native file.

## The 10 sessions

| # | session_id | platform | task category | tool mix |
|---|---|---|---|---|
| 1 | syn0001a1b2c3 | windows | Postgres up + SQL file create | BASH ×4, EDIT_FILE ×1 |
| 2 | syn0002d4e5f6 | windows | Minimal API request guard | BASH ×2, EDIT_FILE ×2 |
| 3 | syn00037a8b9c | posix | Controller error-payload unification | BASH ×3, EDIT_FILE ×1 |
| 4 | syn0004ff1122 | windows | SignalR ChatHub validation | BASH ×2, EDIT_FILE ×1 |
| 5 | syn0005webcal | windows | WEB_CALL doc fetch → compose file | WEB_CALL ×1, EDIT_FILE ×1, BASH ×1 |
| 6 | syn0006cfged | windows | CONFIG_EDIT register custom tool + tighten limits | CONFIG_EDIT ×3, BASH ×1 |
| 7 | syn0007buildf | windows | Build-failure root-cause + surgical fix | BASH ×3, EDIT_FILE ×1 |
| 8 | syn0008mig01 | windows | Multi-table SQL migration with FKs | EDIT_FILE ×1, BASH ×2 |
| 9 | syn0009refac | windows | Refactor SeeSharp's own `AgentDefaults` | BASH ×2, EDIT_FILE ×1 |
| 10 | syn0010gitop | windows | Selective `git add` + conventional commit | BASH ×5 |

## OpenRLHF launch examples

SFT on the flattened view:

```bash
deepspeed --module openrlhf.cli.train_sft \
  --data.dataset datasets/synthetic-sessions/synthetic-sessions.openrlhf.jsonl \
  --data.input_key messages \
  --data.apply_chat_template \
  --train.batch_size 64 --train.micro_batch_size 2 \
  --actor.model_name_or_path Qwen/Qwen3.5-2B
```

Single-turn PPO with rule-based reward (`reward.remote_url` points to a Python file that re-reads `scores` from each row):

```bash
ray job submit --address="http://127.0.0.1:8265" \
  -- python3 -m openrlhf.cli.train_ppo_ray \
  --actor.model_name_or_path Qwen/Qwen3.5-2B \
  --data.prompt_dataset datasets/synthetic-sessions/synthetic-sessions.openrlhf.jsonl \
  --data.input_key task --data.label_key id \
  --reward.remote_url ./reward_func.py \
  --algo.dynamic_filtering_enable --algo.dynamic_filtering_range 0.0 1.0 \
  --rollout.n_samples_per_prompt 4
```

`reward_func.py` would look up each prompt's `id`, return the row's `scores`, and log `extra_logs` so the dynamic filter can keep only samples that beat the synthetic ceiling.
