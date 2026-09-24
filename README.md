# AIBridge

AIBridge is a Windows desktop application serving as an execution bridge between ChatGPT Brain, Antigravity (and other coding agents), and local code workspaces.

## Architecture

```text
ChatGPT Brain / Orchestrator
      |
      v (HTTP API: 127.0.0.1:8787)
AIBridge.exe (BridgeServer)
      |
      +---> GitEvidenceService (Before/After Snapshots, Diff, Secret Redaction)
      |
      v (TaskService)
AntigravityRunner (agy)
      |
      v
Local Workspace / GitHub Repository
```

### Roles & Responsibilities

* **ChatGPT**: Brain (analyzes requirements, plans, generates prompts, delegates tasks, and reviews output using Git evidence).
* **AIBridge**: Executor & Evidence Layer (provides embedded REST API + WPF UI to execute tasks via coding agents, captures Git evidence, safe push).
* **Antigravity**: Primary Coding Agent (executes workspace modifications via `agy` CLI).

---

## Current Status

`Phase 04 - GitHub Integration & Evidence Layer` (Completed)

Phase 04 adds local Git discovery, pre/post task repository snapshotting, commit detection, bounded diff evidence collection (up to 256 KB) with secret redaction, remote branch push status, protected branch policies (`main`/`master`), and WPF compact evidence inspection.

---

## Local Bridge API Documentation

- **Base URL**: `http://127.0.0.1:8787` (Localhost only)
- **Authentication**: `Authorization: Bearer <API_TOKEN>` or `X-AIBridge-Token: <API_TOKEN>`
- **Request Size Limit**: 256 KB

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

---

## Git Evidence & Safety Policies (Phase 04)

### Evidence Collection Lifecycle
1. **Task Accepted**: `GitEvidenceService.CaptureSnapshotAsync()` captures `BeforeSnapshot` (HEAD SHA, branch, dirty state, untracked files).
2. **Task Execution**: Antigravity executes task via `agy`.
3. **Task Completion**: `GitEvidenceService.CaptureSnapshotAsync()` captures `AfterSnapshot` and calculates `GitEvidence`.
4. **Diff Bounding & Redaction**: Diff text is bounded to 256 KB (`DiffTruncated = true` if exceeded) and scanned for credentials (`SecretRedactor`).

### Push & Protection Policy
* **Default AutoPush**: `GitAutoPush = false` (Manual explicit trigger required).
* **Protected Branches**: `main` and `master` are protected by default (`AllowPushToProtectedBranches = false`).
* **Force Push**: Forbidden (`--force` and `--force-with-lease` are disabled).
* **Non-Git Workspaces**: Fully supported; task execution completes with `status = NOT_A_GIT_REPOSITORY`.
* **Git Not Installed**: Task execution completes with `status = GIT_NOT_INSTALLED`.

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
- [ ] **Phase 05 - ChatGPT Brain Foundation** (Brain-to-Bridge API connection & prompt exchange layer)
- [ ] **Phase 06 - AI Review / Retry Loop** (Automated result evaluation & refinement loop)
- [ ] **Phase 07 - Autonomous Orchestration** (Multi-agent end-to-end task execution)
