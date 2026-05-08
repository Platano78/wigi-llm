# Next Session Bootstrap Prompt

Copy-paste this into a fresh Claude session to pick up wigi-llm work on the two new visualization widgets: **Kaiju Inference Arena** and **Neural Nexus (Agent War Room)**.

---

## The prompt

> You are continuing development on **wigi-llm** — a public physical-control-panel project for the G.SKILL WigiDash touchscreen. Repo: https://github.com/Platano78/wigi-llm. Local path: `/home/platano/project/wigi-llm`.
>
> **Required reading before writing any code** (in order):
> 1. `/home/platano/project/wigi-llm/docs/wigidash-development-learnings.md` — exhaustive list of gotchas (build flow, ClickType.Double quirks, illegal `volatile double`, /metrics parsing pitfalls, threading rules, .NET 4.7.2 / LangVersion 5 limits). **This will save you several deploy-crash-iterate cycles.**
> 2. `/home/platano/project/wigi-llm/docs/kaijuarena.md` — PRD for Idea 1
> 3. `/home/platano/project/wigi-llm/docs/Agentwarroom.md` — PRD for Idea 2
> 4. `/home/platano/project/wigi-llm/CLAUDE.md` — repo conventions
>
> **State at handoff** (post the previous session's last commit):
> - Master branch is green, public, deployed
> - Working widgets: LLMLauncher (daily-driver), LLMStats, LLMBrainMonitor (cockpit + autopilot inference shooter), plus 4 experimental widgets (LLMControlCenter, LLMModelSelector, LLMRouterStatus, ClipboardAgent)
> - All build/deploy infrastructure is established and proven — see learnings doc
> - `wigidash/src/Shared/GpuInfo.cs` provides VRAM detection via nvidia-smi (used by all C# widgets except Launcher)
> - Existing reference for: Prometheus metric parsing (BrainMonitor), particle pools (BrainMonitor ParticleField + GameMode), glass-pane HUD layout (BrainMonitor HudPanel), settings persistence (Launcher), animation thread + clock-paced spawning (BrainMonitor GameMode)
>
> ## What to build
>
> ### Idea 1 — Kaiju Inference Arena
> A cinematic battle visualization where local LLM inference activity manifests as monsters attacking a city. Token generation rate = attack frequency; context window fill = city structural integrity; latency = monster charge-up animation; errors = critical-hit screen shake. Synthwave aesthetic, RGB565-aware (use dithering, avoid smooth gradients).
>
> Read `docs/kaijuarena.md` for the full PRD including:
> - Metric → visual mapping table
> - File layout (`KaijuArenaWidget/{Core,Data,Render}`)
> - 3-phase milestone plan (skeleton → battle → juice)
> - Sprite sheet asset requirements
>
> ### Idea 2 — Neural Nexus (Agent War Room)
> An orbital network visualization of MCP server topology + LLM node ecosystem. Each MCP server = a "Reactor Core" pulsing color by health; tool calls = "Energy Conduits" between nodes; data flow = particles zipping along conduits; latency = orbital speed; errors = flicker. Sci-Fi command center aesthetic.
>
> Read `docs/Agentwarroom.md` for the full PRD including:
> - Node/edge graph model (`GraphModel`, `OrbitalEngine`)
> - State-based color logic (Healthy/Warning/Critical)
> - Particle-flow physics (`Speed = 1/Latency`)
> - 3-phase milestone plan (skeleton → network → polish)
>
> ### Shared infrastructure
> Both widgets need:
> - **MCP Gateway poll** — `http://127.0.0.1:8090` every 1s (this is NEW vs. our previous llama.cpp /metrics work; verify the endpoint is reachable before designing around it)
> - **Status data** — parse `claude_statusline.json` (Claude Code's session state file, typically at `/dev/shm/claude_statusline.json` on Linux; check ~/.claude or the MCP Gateway response for the equivalent on the WigiDash widget host)
>
> Lift the existing `MetricsClient` / `Snapshot` pattern from `wigidash/src/LLMStatsWidget/MetricsClient.cs` for the gateway poll. Don't roll a new HTTP client per widget — a shared helper class shipped in each widget folder (or under `Shared/`) is the right abstraction.
>
> ## Build order recommendation
>
> 1. **Verify the data source first** — both PRDs assume an MCP Gateway at `http://127.0.0.1:8090` and a `claude_statusline.json` file. Curl + read those before designing widgets that depend on them. If they're missing or the schema is different from what the PRDs assume, escalate before writing C#.
>
> 2. **Build Neural Nexus first** (the simpler one) — pure procedural rendering (`FillEllipse` + `DrawLine`), no sprite sheets, just nodes-and-edges with particles. Smaller risk surface; lets us validate the data pipeline.
>
> 3. **Then Kaiju Arena** — needs sprite sheets (PRD calls for 4 sheets), tile-based city grid, more complex animation states. Reuses Nexus's MCP Gateway client + statusline parser if you stage them in a shared file.
>
> 4. **Optional intermediate**: if the MCP Gateway endpoint isn't ready, both ideas can be prototyped with the existing llama.cpp `/metrics` data we already know works — token rate maps to "attack rate" or "particle flow speed" trivially. Treat the PRDs' MCP Gateway dependency as a Phase 2 enhancement, not a Phase 1 blocker.
>
> ## Workflow expectations (read learnings doc for detail)
>
> - **Build via WSL→Windows cmd interop** with %TEMP% staging — UNC paths break msbuild. Pattern is in the learnings doc under "Build / deploy workflow"
> - **DLL deploy requires WigiDash exit** when the widget is loaded on a tile. First-time deploys for a brand-new GUID skip this since nothing's holding the file
> - **Top-level try/catch on every background thread** — uncaught exceptions terminate the host process, not just the widget
> - **Manual double-tap detection** — `ClickType.Double` doesn't fire reliably on touchscreen
> - **Verify with build, not just type-check** — pi and modern-C# habits ship illegal code in 4.7.2 (e.g., `volatile double`, 3-arg `double.TryParse`). The actual `msbuild` is the only ground truth
> - **Commit per logical change** with detailed messages explaining the *why*. Use `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` when AI-assisted
>
> ## Suggested first commands
>
> ```bash
> cd /home/platano/project/wigi-llm
>
> # Verify state
> git log --oneline -5
> git status
>
> # Probe the MCP Gateway endpoint the PRDs assume
> curl -s -m 3 http://127.0.0.1:8090/ | head -50
> curl -s -m 3 http://127.0.0.1:8090/health | head -20
>
> # Check for the statusline file
> ls -la /dev/shm/claude_statusline.json ~/.claude/*.json 2>/dev/null
>
> # See what reference patterns we already have
> ls wigidash/src/
> head -60 wigidash/src/LLMStatsWidget/MetricsClient.cs
> ```
>
> Then read the three docs above and propose a Phase 1 plan (skeleton + data pipeline) for whichever idea you start with. Don't dive into rendering until the data source is verified — both PRDs depend on metrics we haven't actually pulled before.
>
> ## What "done" looks like for this session
>
> Pick the smaller scope. Some good landing points:
> - Just the data pipeline (MCP Gateway client + statusline parser, with a stub widget that displays raw values)
> - Just the Neural Nexus skeleton (orbital nodes, no particles or HUD yet)
> - Just the Kaiju Arena city grid (static, no monsters yet)
>
> Each of those is a clean, deployable, visible win. **Don't try to ship both widgets fully in one session** — that's how the host crashed three times in the previous session before we got the threading hardening right.

---

## Why this prompt is structured this way

- **Required reading first** — the learnings doc compresses ~3 hours of debugging into a reference. Skipping it costs more than reading it.
- **State at handoff** — Claude needs to know what's already built so it doesn't reinvent. Existing patterns to lift are listed.
- **Build order recommendation** — opinionated. Nexus first because procedural-only rendering is lower risk than sprite-based. Verifies the data source before either widget locks in.
- **Workflow expectations bullet-list** — terse pointers; full detail is in learnings doc. Establishes that the WSL build dance, the WigiDash exit dance, and the threading discipline are non-negotiable.
- **Suggested first commands** — concrete probes the new session can run before writing anything. Front-loads the "is the data source even there" question.
- **What done looks like** — small, ship-shaped milestones. Discourages a "build everything" approach.

If during the next session you discover something not in the learnings doc that future-you would want to know, append it to `docs/wigidash-development-learnings.md` — it's the durable record.
