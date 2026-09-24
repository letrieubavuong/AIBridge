# AIBridge

AIBridge is a Windows desktop application serving as an execution bridge between ChatGPT Brain, Antigravity (and other coding agents), and local code workspaces.

## Architecture

```text
ChatGPT Brain / Orchestrator
      |
      v (HTTP API: 127.0.0.1:8787)
AIBridge.exe (BridgeServer)
      |
      v (TaskService)
AntigravityRunner (agy)
      |
      v
Local Workspace / GitHub
```

### Roles & Responsibilities

* **ChatGPT**: Brain (analyzes requirements, plans, generates prompts, delegates tasks, and reviews output).
* **AIBridge**: Executor / Local Bridge (provides embedded REST API + WPF UI to execute tasks via coding agents).
* **Antigravity**: Primary Coding Agent (executes workspace modifications via `agy` CLI).

---

## Current Status

`Phase 03 - Local Bridge API` (Completed)

Phase 03 embeds a local Kestrel HTTP API inside `AIBridge.exe` bound exclusively to `127.0.0.1:8787` with secure local Bearer token authentication, task registry, cancellation support, and live WPF UI synchronization.

---

## Local Bridge API Documentation (Phase 03)

- **Base URL**: `http://127.0.0.1:8787` (Localhost only)
- **Authentication**: `Authorization: Bearer <API_TOKEN>` or `X-AIBridge-Token: <API_TOKEN>`
- **Request Size Limit**: 256 KB

### Endpoints

| Method | Endpoint | Auth Required | Description |
|---|---|---|---|
| `GET` | `/api/health` | No | Check bridge health, app version, and machine timestamp |
| `GET` | `/api/environment` | Yes | Get Antigravity CLI installation, auth state, workspace state |
| `POST` | `/api/tasks` | Yes | Submit prompt & workspace task (returns 202 Accepted with taskId) |
| `GET` | `/api/tasks/current` | Yes | Query status of current running or latest task |
| `GET` | `/api/tasks/{taskId}` | Yes | Query detailed execution record, status, exit code, stdout/stderr |
| `POST` | `/api/tasks/{taskId}/cancel` | Yes | Cancel running task execution |
| `GET` | `/api/logs` | Yes | Get recent application log entries |

### API Request & Response Examples

#### Submit Task (`POST /api/tasks`)

Request:
```json
{
  "prompt": "Create a file named hello.txt with content: Hello World",
  "workspacePath": "C:\\path\\to\\workspace"
}
```

Response (`202 Accepted`):
```json
{
  "taskId": "a1b2c3d4e5f6...",
  "status": "Pending"
}
```

#### Health Check (`GET /api/health`)

Response (`200 OK`):
```json
{
  "status": "ok",
  "bridge": "running",
  "version": "1.0.0",
  "machineName": "DESKTOP-NAME",
  "timestamp": "2026-09-24T15:00:00.000Z"
}
```

---

## Requirements

* Windows x64
* .NET 10 SDK (`net10.0-windows`)

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
- [ ] **Phase 04 - ChatGPT Brain Integration** (API connection & prompt exchange layer)
- [ ] **Phase 05 - GitHub Integration** (Branch management & Pull Request creation)
- [ ] **Phase 06 - AI Review / Retry Loop** (Automated result evaluation & refinement loop)
- [ ] **Phase 07 - Autonomous Orchestration** (Multi-agent end-to-end task execution)
