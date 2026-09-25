# AIBridge

AIBridge is a Windows desktop application serving as an execution bridge between AI Brains (ChatGPT, Claude, Gemini, Local AI, etc.), Coding Agents (Antigravity today, extensible tomorrow), and local code workspaces.

## Architecture

```text
USER IDEA / REQUIREMENT
      |
      v
PlanningService (IPlanningService)
      |
      +---> IAIBrainService (Provider-independent reasoning)
      |         |
      |         v
      |     Registered Brain Provider (Mock Brain / OpenAI / Claude / Gemini)
      |         |
      |         v
      +---> PlanValidator (Duplicate ID check, reference verification, DFS cycle detection)
      |
      +---> FileProjectPlanStore (%LOCALAPPDATA%\AIBridge\projects\ - Atomic write & versioning)
      |
      v
ProjectPlan (Draft / AwaitingApproval)
      |
      v
HUMAN APPROVAL GATE (Explicit Human Approval Required — 0 Tasks Executed in Phase 06)
```

### Critical Architecture Principles

* **AI Provider Agnostic**: Core AIBridge planning & execution logic does **NOT** depend directly on ChatGPT, OpenAI, Claude, Gemini, or any specific AI vendor. Provider choice belongs to the user and is configuration-driven. Provider switching works seamlessly without altering planning rules.
* **Separation of Planning & Execution**: The planning layer transforms high-level requirements into strongly typed, reviewable development plans (`ProjectPlan` -> `PhasePlan[]` -> `TaskPlan[]`). **Phase 06 is PLANNING ONLY. It does NOT automatically execute generated tasks or invoke Antigravity.**
* **Coding Agent Replacement**: Coding Agents (Antigravity CLI today, future agents) are fully decoupled behind `ICodingAgentRunner`.

---

## Current Status

`Phase 07 — ChatGPT Web Brain & Coding-Agent Dispatch Foundation (Final Fix)` (Completed)

Phase 07 implements the execution-preparation and coding-agent dispatch layer. ChatGPT Web is the default AI Brain (no paid model API keys required). External instructions are packaged into canonical `ExecutionPromptPackage` instances, validated against stored authoritative plan criteria, sanitized, hashed, and dispatched via provider-independent `ICodingAgentService` and `AntigravityCodingAgent` to existing process runners and Git evidence tools. Successful agent execution transitions tasks to `Reviewing` (NOT `Passed`).

### Phase 07 Final Fix Security & Evidence Model

* **Trusted Human Approval State**: Human approval is a trusted AIBridge state managed by `IHumanApprovalService`. External Brain/API callers cannot self-approve Human Gates by sending boolean flags in HTTP dispatch DTOs.
* **Context & Plan-Version Scoped Approval**: Human approvals are bound to ProjectId, PhaseId, TaskId, and exact PlanVersion. Re-planning invalidates stale approvals. Approvals use single-use consumption semantics upon task dispatch.
* **Local Control Endpoint**: `POST /api/projects/{projectId}/phases/{phaseId}/tasks/{taskId}/approve-execution` represents a trusted local human action (NOT for ChatGPT/MCP tool exposure).
* **Execution & Evidence Identity Correlation**: Logical TaskPlan identity (`TaskId`), Execution identity (`ExecutionId`), and Runtime AgentTask identity (`AgentTaskId`) are explicitly distinguished and correlated. Git evidence is recorded and queried using the exact runtime execution identity (`AgentTaskId`/`ExecutionId`).
* **ExecutionReviewPackage**: Contains Git evidence correlated to that specific execution attempt. Successful workspace executions with 0 changes and evidence collection failures/unavailability are explicitly distinguished.
* **Agent Success != Task PASS**: Agent success sets status to `Reviewing`. Human or Brain review of `ExecutionReviewPackage` is required before moving to `Passed`.
* **ChatGPT Web Default Brain**: ChatGPT Web remains the default Brain without requiring OpenAI/Claude/Gemini API keys.

---

## Architecture

```text
ChatGPT Web
     │
     │ MCP
     ▼
AIBridge MCP Server
     │
     ▼
ExecutionPromptService
     │
     ▼
CodingAgentService
     │
     ▼
AntigravityCodingAgent
     │
     ▼
TaskService
     │
     ▼
AntigravityRunner
     │
     ▼
agy
     │
     ▼
Workspace / Git
     │
     ▼
GitEvidence
     │
     ▼
ExecutionReviewPackage
     │
     └──────── MCP ────────→ ChatGPT Web
```

### Critical Architecture Principles

* **Default AI Brain**: ChatGPT Web is the primary reasoning/orchestration brain. AIBridge is the deterministic local execution, state, and evidence bridge.
* **Coding Agent**: Antigravity CLI is the current coding executor agent (`AntigravityCodingAgent`). Future coding agents remain fully pluggable via `ICodingAgent`.
* **No Model API Key Required**: Default workflow requires zero OpenAI/Claude/Gemini API keys.
* **No Web Scraping or Session Hacks**: AIBridge does not scrape ChatGPT web sessions or manipulate browser cookies. Integrations use standard MCP (Model Context Protocol) tool streams over HTTP.
* **Trusted Local Human Gate**: Human approval is a trusted local WPF action. MCP tool surface cannot grant or manufacture human approvals.
* **AGENT SUCCESS != TASK PASS**: When an agent completes with ExitCode = 0, AIBridge marks the task as `Reviewing`. The task becomes `Passed` only after external Brain review of the `ExecutionReviewPackage` and Git evidence.
* **Security & Workspace Isolation**: External commands specify ProjectId/PhaseId/TaskId and instructions. Workspace paths are resolved from trusted local AIBridge project configuration; no arbitrary shell execution endpoints or file system tools are exposed via MCP.

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
| `POST` | `/api/planning/generate` | Yes | Generate structured `ProjectPlan` from user idea/requirement |
| `GET` | `/api/projects` | Yes | List all persisted project plans |
| `GET` | `/api/projects/{id}` | Yes | Retrieve specific `ProjectPlan` details |
| `GET` | `/api/projects/{id}/plan` | Yes | Retrieve full `ProjectPlan` structure |
| `POST` | `/api/projects/{id}/plan/revise` | Yes | Revise existing plan with AI, creating next plan version (v2, v3) |
| `POST` | `/api/projects/{id}/plan/approve` | Yes | Explicitly approve project plan (Human Gate requirement) |
| `GET` | `/api/projects/{id}/versions` | Yes | List plan version history |
| `GET` | `/api/projects/{id}/versions/{version}` | Yes | Retrieve specific historical plan version |
| `GET` | `/api/projects/{id}/progress` | Yes | Get plan-backed project progress & weighted completion percentage |
| `GET` | `/api/projects/{id}/phases/{phaseId}/progress` | Yes | Get phase progress derived from logical tasks |
| `GET` | `/api/coding-agents` | Yes | List registered coding agents (Antigravity) |
| `GET` | `/api/coding-agents/status` | Yes | Get active coding agent status |
| `POST` | `/api/projects/{id}/phases/{phaseId}/tasks/{taskId}/prepare` | Yes | Prepare canonical `ExecutionPromptPackage` (does not execute) |
| `POST` | `/api/projects/{id}/phases/{phaseId}/tasks/{taskId}/dispatch` | Yes | Dispatch task execution to coding agent |
| `GET` | `/api/projects/{id}/dispatchable-tasks` | Yes | List tasks ready for dispatch |
| `GET` | `/api/executions/current` | Yes | Get active execution details |
| `GET` | `/api/executions/{executionId}` | Yes | Get specific execution record |
| `POST` | `/api/executions/{executionId}/cancel` | Yes | Cancel running coding agent execution |
| `GET` | `/api/executions/{executionId}/review-package` | Yes | Get `ExecutionReviewPackage` for external Brain review |

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

## Test

To run the automated test suite (including planning, prompt packaging, agent registry, dispatch, and acceptance tests):

```bash
dotnet test AIBridge.sln -c Release
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
- [x] **Phase 06 - AI Planning / Phase & Task Planning** (Structured planning domain, PlanValidator, DFS cycle detection, atomic persistence, versioning, human gate approval, planning API, WPF planning UI)
- [x] **Phase 07 - ChatGPT Web Brain & Coding-Agent Dispatch Foundation** (ExecutionPromptPackage, ExecutionPromptService, ICodingAgent abstraction, AntigravityCodingAgent, dispatchability policy, ReviewPackage, WPF execution panel)
- [ ] **Phase 08 - ChatGPT Web ↔ AIBridge MCP / Plugin Integration**
- [ ] **Phase 09 - AI Review / Retry Loop & Autonomous Orchestration**

