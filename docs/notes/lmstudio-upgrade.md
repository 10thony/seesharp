# LM Studio — official upgrade brief (hardware agent)

Use this document to **research, plan, and apply upgrades** for the LM Studio instance that serves this project. **Use official LM Studio distribution channels only.** Do not install builds from unofficial mirrors, random GitHub forks, or third-party repackagers unless this policy is explicitly revised.

---

## Scope and policy

| Allowed | Not in scope (unless instructed otherwise) |
|--------|-----------------------------------------------|
| **Stable / production** releases from LM Studio | Beta, experimental, or nightly builds |
| Installers and scripts hosted on **`lmstudio.ai`** | Community builds of models (HF) for *LM Studio app* install |
| Documentation on **`lmstudio.ai/docs`** | Building LM Studio from source |

**Official stable download hub:** [https://lmstudio.ai/download](https://lmstudio.ai/download)

**Release history / what changed:** [https://lmstudio.ai/changelog](https://lmstudio.ai/changelog)

**Beta / experimental (do not use for this brief unless the operator overrides policy):** [https://lmstudio.ai/beta-releases](https://lmstudio.ai/beta-releases)

---

## Why upgrade (context for this repo)

This project calls LM Studio over an OpenAI-compatible base URL (e.g. `http://<host>:1234/v1`). Older LM Studio builds can show **Jinja chat template** errors (for example `"No user query found in messages."`) with certain models and APIs. Newer **stable** releases often fix template, `/v1/responses`, and runtime issues. Treat upgrades as **infrastructure maintenance**, not application code changes.

---

## Pre-upgrade checklist

1. **Record current state**
   - LM Studio **app version** (About / settings — exact string).
   - **Listen address and port** for the local server (default is often `1234`; confirm).
   - Whether **headless (`llmster`)** or **full GUI** is used.
   - OS: Windows build vs Linux distro + version (for choosing the correct official installer).

2. **Read the changelog**
   - Open [Changelog](https://lmstudio.ai/changelog) and scan entries **newer than** the installed version.
   - Note fixes relevant to: OpenAI-compatible API, `/v1/responses`, Jinja / chat template, Qwen or other families you load.

3. **Maintenance window**
   - Stop or drain clients that hit the API during the upgrade.
   - Ensure enough disk space for the installer and any cache updates.

4. **Rollback plan**
   - Keep the **previous installer** or know the **prior stable version** from the changelog so you can reinstall if needed.

---

## Official acquisition paths (stable only)

**Desktop / full app**

- Download the appropriate artifact for the OS from **[lmstudio.ai/download](https://lmstudio.ai/download)** only.
- Install over the existing installation per LM Studio’s normal installer behavior for that OS.

**Headless / scripted install (official scripts from LM Studio)**

These are documented on the download page and are **official** distribution helpers, not third-party tools:

- **macOS / Linux:** `curl -fsSL https://lmstudio.ai/install.sh | bash`
- **Windows (PowerShell):** `irm https://lmstudio.ai/install.ps1 | iex`

Use the script appropriate to the target OS. Verify TLS, corporate proxy, and execution policy (Windows) before running.

**Documentation index (reference, not a download)**

- [https://lmstudio.ai/docs](https://lmstudio.ai/docs) — app, developer API, LM Link, etc.

---

## Upgrade procedure (recommended order)

1. **Compare** installed version vs latest **stable** on [Download](https://lmstudio.ai/download) / [Changelog](https://lmstudio.ai/changelog).
2. If already current, **stop** unless a security or ops policy requires reinstall.
3. **Back up** critical state if applicable (exported presets, documented non-default ports, firewall rules).
4. **Apply** the official stable installer or official install script for that machine.
5. **Launch** LM Studio (or `llmster` if headless) and confirm the **new version** in-app or via documented CLI.
6. **Restore** server settings: bind address, port, API enabled, auth if used.
7. **Smoke test** from another machine:
   - `GET http://<host>:<port>/v1/models` (or your configured base URL + `/v1/models`).
   - A minimal **chat completions** or **responses** call consistent with how this repo calls LM Studio.

---

## Post-upgrade verification (operator acceptance)

- [ ] Version matches intended **stable** release.
- [ ] Local API responds on the expected host/port.
- [ ] Loaded model(s) still load; **runtime** (CUDA/ROCm/Vulkan/CPU) still selected as before.
- [ ] Repeat any previously failing prompt scenario (e.g. same model + API path) if upgrades were driven by template errors.

---

## Support and bug reporting (official)

- **Bug tracker:** [https://github.com/lmstudio-ai/lmstudio-bug-tracker](https://github.com/lmstudio-ai/lmstudio-bug-tracker)
- **Community:** Discord linked from [https://lmstudio.ai/docs](https://lmstudio.ai/docs) (for questions; not a substitute for reading changelog/docs).

Use these after confirming the install is **official stable** and the issue reproduces on the latest stable.

---

## Agent instructions (summary)

1. Use **only** [lmstudio.ai/download](https://lmstudio.ai/download) and official **`install.sh` / `install.ps1`** from `lmstudio.ai` for installation or upgrade of stable LM Studio.
2. Use [lmstudio.ai/changelog](https://lmstudio.ai/changelog) to justify the upgrade and to pick a target version.
3. Do **not** deploy beta or experimental builds unless the operator changes the policy above.
4. Document **before/after version**, **time**, and **smoke test** results for the machine you maintain.
