# Product Requirements Document (PRD)
## Project Name: The Neural Nexus (Agent War Room)

| **Metadata** | **Details** |
| :--- | :--- |
| **Version** | 1.0.0 |
| **Status** | Draft |
| **Platform** | WigiDash (G.SKILL) |
| **Tech Stack** | C# .NET 4.7.2, System.Drawing (GDI+), WinForms |
| **Resolution** | 1920x1080 (Overlay) |
| **Refresh Rate** | 30 FPS (Target) |
| **Color Depth** | 16-bit RGB565 (Constraint) |

---

## 1. Executive Summary
**The Neural Nexus** is a high-fidelity monitoring dashboard that visualizes the topology and health of an AI Agent ecosystem. Instead of static lists, it presents a **living orbital network** where MCP servers and LLM models are "Reactor Cores" connected by "Energy Conduits."

**Goal**: Provide an at-a-glance, cinematic view of system health, data flow, and latency, turning the abstract concept of "orchestration" into a visual command center.

---

## 2. User Experience & Metaphor

### 2.1 The Core Metaphor
| AI Metric | Visual Equivalent | Interaction |
| :--- | :--- | :--- |
| **MCP Server Status** | **Reactor Core** | Pulsing color (Green/Red) indicates health. |
| **Tool Calls / Traffic** | **Energy Conduits** | Lines connecting nodes; intensity = traffic. |
| **Data Flow** | **Data Packets** | Particles "zipping" along the lines. |
| **Latency** | **Orbital Speed** | High latency = Nodes "stutter" or "overheat." |
| **Active Agents** | **Core Count** | Number of active nodes in the orbital ring. |
| **Error Rate** | **Flicker/Flash** | Nodes flash red or "spark" on errors. |

### 2.2 Visual Style
*   **Aesthetic**: Sci-Fi Command Center / Radar Interface.
*   **Palette**: High-contrast Neon (Cyan, Magenta, Yellow) against Deep Black/Blue.
*   **Constraint Awareness**: **16-bit Color Depth (RGB565)**.
    *   *Design Decision*: Use **Alpha Blending** to simulate "Glow" (drawing multiple concentric circles with decreasing alpha). Use **Dithering** for any background shading to avoid color banding.

---

## 3. Functional Requirements

### 3.1 Rendering Engine (`NexusRenderer`)
*   **FR-01**: The widget must render a **Radar Grid** background that slowly rotates.
*   **FR-02**: The widget must implement **Orbital Mechanics** for nodes (circular movement around a center point).
*   **FR-03**: The widget must support **Neon Glow Effects** for nodes (simulated via layered drawing).
*   **FR-04**: The widget must render **Data Packets** (Particles) that travel along the connection lines.
*   **FR-05**: The widget must support **State-Based Coloring** (Healthy=Green/Cyan, Warning=Yellow, Critical=Red).

### 3.2 Data Ingestion (`NexusMapper`)
*   **FR-06**: Poll the local MCP Gateway (`http://127.0.0.1:8090`) every 1 second.
*   **FR-07**: Parse `claude_statusline.json` for raw metrics (Context, Tokens).
*   **FR-08**: Map raw metrics to "Visual Events" (e.g., `MCP Server Offline` = Node turns Red and stops emitting particles).

### 3.3 User Interface (HUD)
*   **FR-09**: Display a **System Health Summary** (e.g., "6/8 Healthy").
*   **FR-10**: Display a **Token Rate** indicator (e.g., "Tokens/sec: 45").
*   **FR-11**: Display a **Latency Meter** (Visual bar or node pulse speed).

---

## 4. Technical Architecture

### 4.1 High-Level Structure
```text
NeuralNexusWidget/
├── NeuralNexusWidget.cs        # Entry Point (IWidgetObject)
├── NeuralNexusWidgetInstance.cs # IWidgetInstance (The Loop)
├── Core/
│   ├── GraphModel.cs           # Manages Nodes, Edges, Particles
│   ├── ParticlePool.cs         # Object Pool for Data Packets
│   └── OrbitalEngine.cs        # Math for node rotation/positioning
├── Data/
│   ├── NexusMapper.cs          # Converts JSON -> Visual State
│   └── DataModels.cs           # DTOs for Gateway/Status
└── Render/
    ├── NexusRenderer.cs        # Main Draw Loop
    └── NeonEffects.cs          # Helper methods for Glow/Dither
```

### 4.2 The Render Loop (Pseudo-Code)
```csharp
public void Render(Graphics g, Bitmap bmp)
{
    // 1. Clear & Background
    g.Clear(Color.FromArgb(10, 10, 20)); 
    DrawRadarGrid(g);

    // 2. Update Physics (Particles, Orbital Rotation)
    UpdatePhysics();

    // 3. Draw Connections (Energy Conduits)
    DrawConduits(g);

    // 4. Draw Data Packets (Particles)
    DrawParticles(g);

    // 5. Draw Nodes (Reactor Cores with Glow)
    DrawNodes(g);

    // 6. Draw HUD (Health, Rate)
    DrawHUD(g);
}
```

### 4.3 Optimization Strategy
*   **Object Pooling**: Pre-allocate 300 `Particle` objects. Never `new` inside the loop.
*   **Sprite Caching**: No sprites (mostly vector/math), but any icons (e.g., for node labels) must be cached.
*   **Alpha Blending**: Use `Graphics.CompositingMode = CompositeMode.SourceOver` for the "Glow" effect.

---

## 5. Data Mapping & Logic

### 5.1 Node Health Logic
*   **Healthy**: Node pulses Green/Cyan. Particles flow freely.
*   **Warning**: Node pulses Yellow. Particle flow slows down.
*   **Critical**: Node pulses Red. Particle flow stops or reverses.

### 5.2 Particle Flow Logic
*   **Speed**: Particle speed = `1 / Latency`. (Faster tokens = Faster particles).
*   **Density**: Number of active particles = `Tool Call Frequency`.

---

## 6. Asset Requirements
*   **Background**: 1920x1080 Radial Gradient (or procedural grid).
*   **Icons**: Small 16x16 icons for specific node types (e.g., `Serena.png`, `MKG.png`).
*   **No External Sprites**: To keep the widget lightweight, all visuals (Nodes, Particles) should be drawn procedurally using `Graphics.FillEllipse` and `Graphics.DrawLine`.

---

## 7. Milestones

### Phase 1: The Skeleton (Week 1)
*   [ ] Setup C# .NET 4.7.2 Project.
*   [ ] Implement `IWidgetObject` and `IWidgetInstance`.
*   [ ] Create the 30 FPS Render Loop.
*   [ ] Implement "Orbital" math (Nodes rotating around center).

### Phase 2: The Network (Week 2)
*   [ ] Implement `GraphModel` (Nodes/Edges).
*   [ ] Draw "Energy Conduits" (Lines between nodes).
*   [ ] Implement Particle System (Data Packets).
*   [ ] Connect to MCP Gateway for basic data.

### Phase 3: The Polish (Week 3)
*   [ ] Implement "Neon Glow" effects.
*   [ ] Add Dithering/Shading effects.
*   [ ] Polish HUD and Status Indicators.
*   [ ] Tune visual balance (Node speed vs. Token rate).

---

## 8. Future Roadmap
*   **Interactivity**: Click a node to "Ping" the server.
*   **Custom Layouts**: Allow users to define their own node positions.
*   **Audio Feedback**: Play a subtle "hum" that changes pitch based on total system load.
*   **Alerts**: Flash the entire widget red on a critical system error.

---

**Approvals**:
*   [ ] Product Owner
*   [ ] Lead Developer

**Date**: May 8, 2026