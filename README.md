# AIBridge

AIBridge is a Windows desktop application serving as an execution bridge between AI Brains (ChatGPT, Claude, Gemini, Local AI, etc.), Coding Agents (Antigravity today, extensible tomorrow), and local code workspaces.

## Architecture

```text
AI Brain Provider (ChatGPT / OpenAI API / Claude / Gemini / Local AI / Custom)
      |
      v (IAIBrain / IAIBrainProvider abstraction)
AIBrainService (Provider-independent reasoning & decision layer)
      |
      v (HTTP API: 127.0.0.1:8787)
AIBridge.exe (BridgeServer + TaskService)
      |
      +---> GitEvidenceService (Before/After Snapshots, Diff, Secret Redaction)
      |
      v (Coding Agent Abstraction)
AntigravityRunner (agy)
      |
      v
Local Workspace / GitHub Repository
```

### Critical Architecture Principles

* **AI Provider Agnostic**: Core AIBridge logic does **NOT** depend directly on ChatGPT, OpenAI, Claude, Gemini, or any specific AI vendor. Provider choice belongs to the user. Changing AI provider or account requires zero changes to `TaskService`, Git evidence, Antigravity CLI, coding-agent execution, or orchestration logic.
* **Separation of Reasoning & Execution**: The AI Brain analyzes structured context (`BrainRequest`) and returns a structured decision (`BrainResponse`). The Brain does **NOT** directly execute decisions or commands; execution belongs to AIBridge orchestration and Human Gates.
* **Coding Agent Replacement**: Coding Agents (Antigravity CLI today, future agents) are fully decoupled behind `ICodingAgentRunner`.

---

## Current Status

`Phase 05 — Provider-Agnostic AI Brain Foundation` (Completed)

Phase 05 establishes the provider-agnostic AI Brain foundation, provider registry, deterministic Mock Brain provider, Windows DPAPI secret store (`ProtectedDataSecretStore`), context sanitizer and redaction layer, REST Brain endpoints, WPF AI Brain panel, progress tracking (Project & Phase progress), `AutomationMode` foundation, and Human Gate architectural hooks.

---

## Local Bridge API Documentation

- **Base URL**: `http://127.0.0.1:8787` (Localhost only)
- **Authentication**: `Authorization: Bearer <API_TOKEN>` or `X-AIBridge-Token: <API_TOKEN>`
- **Request Size Limit**: 256 KB (API Payload) / 512 KB (Brain Context Bounded)

### API Endpoints

| Method | Endpoint | Auth Required | Description |
|---|---|---|---|
| `GET` | `/api/health` | No | Check bridge health, app version, and machine timestamp |
| `GET` | `/api/environment` | Yes | Get Antigravity CLI installation, auth state, workspace state |
| `POST` | `/api/tasks` | Yes | Submit prompt & workspace task (returns 202 Accepted with taskId) |
| `GET` | `/api/tasks/current` | Yes | Query status of current running or latest task |
| `GET` | `/api/tasks/{taskId}` | Yes | Query detailed execution record, status, exit code, stdout/stderr |
| `POST` | `/api/tasks/{taskId}/cancel` | Yes | Cancel running task execution |
| `GET` | `/api/logs` | Yes | Get recent application log entries |
| `GET` | `/api/git/environment` | Yes | Get local Git CLI version, installation status, and discovery path |
| `GET` | `/api/git/status` | Yes | Query current Git repository status, branch, HEAD, dirty state, and remote |
| `GET` | `/api/tasks/{taskId}/git` | Yes | Retrieve Git evidence (before/after snapshots, diff, commit range, changed files) |
| `POST` | `/api/tasks/{taskId}/git/push` | Yes | Push task commit to remote under safe push policies (no force push) |
| `GET` | `/api/brain/providers` | Yes | Enumerate available Brain provider descriptors from registry |
| `GET` | `/api/brain/status` | Yes | Query active Brain provider status, state, model, and history summary |
| `POST` | `/api/brain/test` | Yes | Execute a safe connectivity test for active Brain provider |
| `POST` | `/api/brain/analyze` | Yes | Submit a structured `BrainRequest` and return a structured `BrainResponse` |

> [!NOTE]
> `/api/brain/analyze` evaluates context and returns structured decisions (`PASS`, `RETRY`, `BLOCKED`, `NEXT_TASK`, `NEXT_PHASE`, `STOP`, `ASK_HUMAN`). It **never** automatically executes Antigravity CLI.

---

## AI Brain Abstraction & Security Model

### Brain Core Contracts
* **`IAIBrain`**: Core contract `AnalyzeAsync(BrainRequest, CancellationToken)`. Contains zero vendor-specific types.
* **`IAIBrainProvider`**: Provider adapter interface extending `IAIBrain` with `Descriptor` and `TestConnectionAsync`.
* **`IAIBrainProviderRegistry`**: Dynamic enumeration and lookup of registered Brain providers.
* **`MockBrainProvider`**: Deterministic development/test provider for offline verification.

### Credential & Data Security
* **No Plaintext Keys**: API keys are encrypted at rest using Windows Data Protection API (`ProtectedDataSecretStore` / DPAPI). Never written in plaintext to `appsettings.json` or `AppConfig`.
* **Secret Redaction**: `BrainContextSanitizer` runs before context reaches external providers, stripping Bearer tokens, GitHub tokens, OpenAI keys, passwords, and API credentials.
* **Context Size Safeguards**: Total Brain context is bounded (max 512 KB, stdout max 128 KB, stderr max 64 KB, diff max 256 KB).

### Automation Mode & Human Gates
* **`AutomationMode`**: Supports `Manual`, `PhaseAuto`, and `FullAuto` (Default: `Manual`).
* **Human Gate Hooks**: `RequiresHumanApproval` flag and `HumanGateReason` prepare the architecture for gated transitions (architecture changes, protected branch operations, phase transitions).

---

## Requirements

* Windows x64
* .NET 10 SDK (`net10.0-windows`)
* Git CLI (optional but recommended for Git evidence layer)

---

## Build

To restore dependencies and build the solution:

```bash
dotnet restore
dotnet build AIBridge.sln -c Release
```

---

## Publish

To create a portable, self-contained `win-x64` release distribution:

```bash
dotnet publish src/AIBridge/AIBridge.csproj -c Release -r win-x64 --self-contained true
```

Output directory: `src/AIBridge/bin/Release/net10.0-windows/win-x64/publish/`

---

## Roadmap

- [x] **Phase 01 - Desktop Foundation** (WPF application layout, config service, logging, task abstraction)
- [x] **Phase 02 - Antigravity Integration** (Verified CLI installation, authentication, and execution runner)
- [x] **Phase 03 - Local Bridge API** (Embedded HTTP Server 127.0.0.1:8787, API auth token, task registry, REST endpoints)
- [x] **Phase 04 - GitHub Integration & Evidence Layer** (Git CLI wrapper, Before/After snapshots, evidence API, safe push policy, secret redaction)
- [x] **Phase 05 - Provider-Agnostic AI Brain Foundation** (IAIBrain contract, provider registry, MockBrainProvider, DPAPI secret store, Brain API, progress tracking)
- [ ] **Phase 06 - AI Planning / Phase & Task Planning** (Automated task decomposition & plan generation)
- [ ] **Phase 07 - AI Review / Retry Loop** (Automated result evaluation & refinement loop)
- [ ] **Phase 08 - Autonomous Orchestration** (Multi-agent end-to-end task execution)
