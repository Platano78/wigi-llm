# Inference Fingerprint — Plan

A 2D phase-space visualization of LLM runtime characteristics. Each model leaves a recognizable shape on the plot. Reveals model "personality," context-length cost, and operating-region patterns that a single tok/s number hides.

## Concept

Each `/metrics` poll produces one point:

- **X axis**: `llamacpp:prompt_tokens_seconds` — prefill throughput (prompt eval, batched)
- **Y axis**: `llamacpp:predicted_tokens_seconds` — generation throughput (autoregressive decode)

Why these two:
- Big dense models cluster low-X / low-Y (slow at both)
- Small models cluster high-X / high-Y
- MoE / sparse models have surprising X-Y asymmetry
- Long-context use degrades Y over time → trail drifts left as KV grows
- Quantization band changes overall position predictably (Q4 vs Q8 vs F16)

Two view modes, toggleable:

1. **Trail** — last 120 samples as fading dots, most recent bright
2. **Heatmap** — 2D histogram across all samples, colored by density

## Visual mockup (target 4×2 widget, 480×320)

```
┌──────────────────────────────────────────────────┐
│  qwen3.6-35b      [TRAIL · 47s · 89 pts]    ⚙   │
├──────────────────────────────────────────────────┤
│  60│                                              │
│ gen│              · ·                             │
│ tok│            ·●●●·                             │
│ /s │           ·●●●●·                             │
│  40│         · · ●●  ·                            │
│  20│       ·  ·                                   │
│   0└────┬────┬────┬────┬────┬────┬────             │
│         0    500  1k   1.5k 2k   2.5k             │
│                  prompt tok/s                     │
│                                                   │
│  μ: 41.2 / 1850     peak: 48.6 / 3204             │
└──────────────────────────────────────────────────┘
```

Heatmap variant uses gridded cells with brightness = sample density. Same axes.

## Architecture

New widget `LLMFingerprintWidget` (own GUID, separate from BrainMonitor — keeps BrainMonitor focused on live state, lets fingerprint persist long-running history without competing with autopilot).

```
LLMFingerprintWidget/
├── LLMFingerprintWidget.csproj
├── Widget.cs                 # boilerplate (lift from stats widget)
├── WidgetBase.cs
├── WidgetInstanceBase.cs
├── WidgetInstance.cs         # orchestrator + draw
├── MetricsClient.cs          # lifted from LLMStatsWidget, returns full snapshot
├── FingerprintSession.cs     # per-model trail + histogram state
├── PlotCanvas.cs             # axis rendering + scatter + heatmap renderers
├── HistogramSerializer.cs    # compact binary <-> WidgetManager settings
└── Properties/AssemblyInfo.cs
```

## Data model

```csharp
class FingerprintSession {
    string ModelName;
    RingBuffer<FingerprintPoint> Trail;   // capacity 120
    ushort[,] Histogram;                  // 24 × 16 cells
    float XMax, YMax;                     // auto-scaled
    int TotalSamples;
    DateTime FirstSeen;
}

struct FingerprintPoint {
    DateTime Timestamp;
    float PromptTps;
    float GenTps;
    bool Inferring;   // requests_processing > 0 at sample time
}
```

Histogram size 24×16 = 384 cells × 2 bytes (ushort) = **768 bytes per model**. Cap at 20 models tracked = ~15 KB total in widget settings.

## Persistence

Per-model state survives WigiDash restarts via `WidgetObject.WidgetManager.StoreSetting`:

- `FP_<modelname>_HIST` — base64-encoded histogram bytes
- `FP_<modelname>_META` — JSON: `{"samples": N, "firstSeen": "...", "xMax": F, "yMax": F}`

## Polling cadence

1.5s like BrainMonitor. The /metrics-stalls-during-inference issue applies here too — the Trail will be sparse during a long generation, dense after completion. Acceptable for fingerprint use; the heatmap accumulates over time.

Filter: skip the first 2 samples after a model change (cold-start outliers).

## Touch controls

| Gesture | Action |
|---|---|
| Single tap | Toggle Trail ↔ Heatmap |
| Double tap | Cycle which tracked model is displayed |
| Long press | Clear current model's history (with red-armed confirmation, like BrainMonitor's kill) |

## Build phases

### 2.2.1 — Core skeleton (~1 session)
- New widget project, csproj, GUID, deploy script
- MetricsClient lifted from stats widget, extended to return full StatsSnapshot
- Trail mode only, single model (whatever's loaded)
- Auto-scaled axes
- No persistence yet — trail starts empty on each load

### 2.2.2 — Heatmap + persistence (~1 session)
- 2D histogram accumulator
- Tap toggles trail ↔ heatmap
- HistogramSerializer for binary persistence via WidgetManager settings
- Heatmap colormap (cold blue → hot red, perceptually sensible)

### 2.2.3 — Multi-model (~1 session)
- Detect model changes from /v1/models loaded id
- Per-model FingerprintSession instances
- Header thumbnails of other tracked models
- Double-tap to cycle which model is shown
- Long-press to clear current model

### 2.2.4 — Polish (~½ session)
- Centroid (μ) marker overlay with label
- Peak X/Y markers
- "Resting position" detection — if `requests_processing == 0` for a sample, render the point dimmed (filtering the X axis when no inference is happening)
- Optional: export histogram as PNG to `%USERPROFILE%\Pictures\wigi-llm\fingerprint_<model>_<date>.png`

## Edge cases / gotchas

1. **`prompt_tokens_seconds` between requests** — gauge holds the last value when idle. Need to detect "not currently inferring" via `requests_processing == 0` and skip those samples (or mark them as "rest" with dimmer color).

2. **Cold-start** — first 1-2 samples after a model loads show unrepresentative throughput (KV cache warming, first batch overhead). Skip them.

3. **X axis scale** — prompt tok/s ranges from ~100 (small models, tiny prompts) to ~5000+ (small models, batched prompts). Need auto-fit with hysteresis so the axis doesn't jitter on outliers.

4. **Trail vs heatmap density** — trail is good for "what's happening now"; heatmap is good for "how does this model behave over hours." Toggling between them on tap lets the user pick the question.

5. **Model rename / reload** — if user changes the model file behind the same router slot, the fingerprint history might be misleading. Hash the model file path or args alongside the name to detect.

## Stretch (post-2.2.4)

| Idea | Why interesting |
|---|---|
| **Compare overlay** | Pick two models, show heatmaps blended (one red-channel, one blue-channel). See cluster overlap. |
| **Export PNG** | Long-press to save current view to Pictures. Shareable model "fingerprint cards." |
| **Trace replay** | Animate the trail through time — visualize a single long session's evolution. |
| **Twin discovery** | Compute histogram similarity (cosine over flattened cells) — show "this model behaves like X." |
| **Fingerprint card UI** | Compose name + small heatmap + key stats into a Pokemon-card-style export. |

## Dependencies on existing work

- `Shared/GpuInfo.cs` — not needed (no VRAM in fingerprint)
- BrainMonitor's MetricsClient pattern — lift verbatim
- `WidgetManager.StoreSetting` / `LoadSetting` — proven working in launcher

## Risks

- **Fingerprint usefulness scales with hours of data** — first-time users see boring trails until they've used a model meaningfully. The heatmap helps because samples accumulate even idle.
- **Histogram cell density on 4×2 panel** — 24×16 cells in 480×320 = ~20px wide × ~12px tall per cell. Fine for rendering, fine for visual reading.
- **WidgetManager settings size limit** — unknown. If 15 KB is over the cap, switch to a sidecar JSON file in resource path.

## Acceptance criteria

Phase 2.2 ships when:
1. Widget loads without crashing the host
2. Trail mode shows live points within 5 seconds of inference
3. Heatmap mode persists across WigiDash restarts
4. Per-model history correctly switches when the active model changes
5. Visual reading: at a glance, you can tell two distinct models apart by their fingerprint
