# AIBridge

AIBridge is a Windows desktop application serving as an execution bridge between ChatGPT Brain, Antigravity (and other coding agents), and local code workspaces.

## Architecture

```text
ChatGPT Brain
      |
      v
AIBridge
      |
      v
Coding Agent (e.g., Antigravity)
      |
      v
Workspace / GitHub
```

### Roles & Responsibilities

* **ChatGPT**: Brain (analyzes requirements, plans, generates prompts, delegates tasks, and reviews output).
* **AIBridge**: Executor / Bridge (receives task prompts and delegates execution to coding agents).
* **Antigravity**: Primary Coding Agent (executes workspace modifications).

---

## Current Status

`Phase 01 - Desktop Foundation`

Phase 01 establishes the WPF desktop UI, configuration persistence in `%LOCALAPPDATA%\AIBridge\config.json`, task model abstractions (`ICodingAgentRunner`), logging service, workspace/agent validation, and portable self-contained build setup.

---

## Requirements

* Windows x64
* .NET 10 SDK (`net10.0-windows`)

---

## Build

To restore dependencies and build the solution:

```bash
dotnet restore
dotnet build --configuration Debug
```

---

## Run

To run the application locally:

```bash
dotnet run --project src/AIBridge/AIBridge.csproj
```

---

## Publish

To create a portable, self-contained `win-x64` release distribution:

```bash
dotnet publish src/AIBridge/AIBridge.csproj -c Release -r win-x64 --self-contained true
```

The compiled binaries will be output to:
`src/AIBridge/bin/Release/net10.0-windows/win-x64/publish/`

You can copy the contents of the `publish/` directory to any 64-bit Windows machine to run `AIBridge.exe` without installing a runtime.

> **Note on Publish Single File**: Standard folder publish is used by default for WPF to ensure reliable loading of native Windows WPF assemblies and dependencies.

---

## Roadmap

- [x] **Phase 01 - Desktop Foundation** (WPF application layout, config service, logging, task abstraction)
- [ ] **Phase 02 - Antigravity Integration** (Verified CLI argument integration & process runner)
- [ ] **Phase 03 - Local Bridge API** (Embedded HTTP Server / WebSockets endpoint for local API control)
- [ ] **Phase 04 - ChatGPT Brain Integration** (API connection & prompt exchange layer)
- [ ] **Phase 05 - GitHub Integration** (Branch management & Pull Request creation)
- [ ] **Phase 06 - AI Review / Retry Loop** (Automated result evaluation & refinement loop)
- [ ] **Phase 07 - Autonomous Orchestration** (Multi-agent end-to-end task execution)
