# Product Requirements Document (PRD)
## Project Name: Kaiju Inference Arena

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
**Kaiju Inference Arena** is a WigiDash widget that visualizes real-time AI inference activity as a cinematic battle between digital Kaiju (monsters) over a crumbling metropolis. Instead of abstract graphs, the user sees a living, breathing city where "Context Window" fills represent the city's structural integrity, and "Token Generation" manifests as kinetic attacks.

**Goal**: Transform the invisible process of LLM inference into an engaging, aesthetic, and informative "Hero Dashboard" for AI developers.

---

## 2. User Experience & Metaphor

### 2.1 The Core Metaphor
| AI Metric | Visual Equivalent | Interaction |
| :--- | :--- | :--- |
| **Context Window Fill** | **City Integrity** | As context fills, buildings crack and collapse. |
| **Token Generation Rate** | **Attack Frequency** | Faster token generation = Faster, more violent attacks. |
| **Latency / Wait Time** | **Charge-up** | Monsters glow and shake before unleashing a heavy attack. |
| **Tool Calls (Serena/MKG)** | **Special Abilities** | Specific attacks (e.g., Serena = Precision Laser, MKG = AoE Explosion). |
| **Errors / Timeouts** | **Critical Hits** | Screen shake, red flash, and debris particles. |
| **Active Agents** | **Kaiju Count** | More agents = More monsters on screen. |

### 2.2 Visual Style
*   **Aesthetic**: Synthwave / Retro-Futuristic.
*   **Palette**: High-contrast Neon (Cyan, Magenta, Yellow) against Deep Black/Blue.
*   **Constraint Awareness**: **16-bit Color Depth (RGB565)**.
    *   *Design Decision*: Use **Dithering** (checkerboard patterns) to simulate gradients. Avoid smooth gradients as they will "band" on 16-bit displays. Use solid, vibrant colors for maximum impact.

---

## 3. Functional Requirements

### 3.1 Rendering Engine (`ArenaRenderer`)
*   **FR-01**: The widget must render at a minimum of 30 FPS.
*   **FR-02**: The widget must support **Sprite Sheet Animation** for Kaiju (Idle, Attack, Hurt, Death states).
*   **FR-03**: The widget must render a **Tile-Based Cityscape** that degrades over time.
*   **FR-04**: The widget must implement a **Particle System** (Object Pooling) for:
    *   Debris (falling rubble).
    *   Energy Sparks (impact sites).
    *   Dust Clouds (movement trails).
*   **FR-05**: The widget must support **Screen Shake** on critical events (Errors/Hits).

### 3.2 Data Ingestion (`InferenceMapper`)
*   **FR-06**: Poll the local MCP Gateway (`http://127.0.0.1:8090`) every 1 second.
*   **FR-07**: Parse `claude_statusline.json` for raw metrics.
*   **FR-08**: Map raw metrics to "Battle Events" (e.g., `TokenRate > 50` triggers "Rapid Fire" attack).

### 3.3 User Interface (HUD)
*   **FR-09**: Display a "City Integrity" bar (Health) at the top.
*   **FR-10**: Display a "Turn Timer" (Time since last token).
*   **FR-11**: Display a "Battle Log" (Scrolling text of recent events).

---

## 4. Technical Architecture

### 4.1 High-Level Structure
```text
KaijuArenaWidget/
├── KaijuArenaWidget.cs       # Entry Point (IWidgetObject)
├── KaijuArenaWidgetInstance.cs # IWidgetInstance (The Loop)
├── Core/
│   ├── BattleState.cs        # Manages Health, Turns, Kaiju Data
│   ├── ParticlePool.cs       # Object Pool for Particles
│   └── SpriteManager.cs      # Loads/Draws Sprites
├── Data/
│   ├── InferenceMapper.cs    # Converts JSON -> Battle Events
│   └── DataModels.cs         # DTOs for Gateway/Status
└── Render/
    ├── ArenaRenderer.cs      # Main Draw Loop
    └── CityGrid.cs           # Tile-based city logic
```

### 4.2 The Render Loop (Pseudo-Code)
```csharp
public void Render(Graphics g, Bitmap bmp)
{
    // 1. Clear
    g.Clear(Color.FromArgb(10, 10, 20)); // Deep Black/Blue

    // 2. Update Physics (Particles, Camera Shake)
    UpdatePhysics();

    // 3. Draw Background (Parallax Grid)
    DrawBackground(g);

    // 4. Draw City (With Damage States)
    DrawCity(g);

    // 5. Draw Kaiju (Sprites + Animation Frames)
    DrawKaiju(g);

    // 6. Draw Particles (Debris, Sparks)
    DrawParticles(g);

    // 7. Draw HUD (Health, Log)
    DrawHUD(g);
}
```

### 4.3 Optimization Strategy
*   **Object Pooling**: No `new Particle()` inside the render loop. Pre-allocate 200 particles.
*   **Sprite Caching**: Load all PNGs once in `OnCreate()`. Never load in `Render()`.
*   **Dithering**: Use a pre-calculated dithering bitmap for shading instead of gradient brushes.

---

## 5. Data Mapping & Logic

### 5.1 City Destruction Logic
The city is a grid of `CityBlock` objects.
*   **State 0**: Intact (Full Health).
*   **State 1**: Cracked (50% Health).
*   **State 2**: Collapsed (20% Health).
*   **State 3**: Rubble (0% Health).

*Trigger*: Every 1000 tokens generated = 1 random city block takes damage.

### 5.2 Kaiju Behavior
*   **Idle**: Slowly bob up and down.
*   **Attack**:
    1.  **Charge**: Sprite scales up 10% + White Flash (Duration: Latency * 100ms).
    2.  **Strike**: Sprite lunges forward.
    3.  **Impact**: Spawn 20 particles at target location.
    4.  **Retreat**: Sprite returns to idle position.

---

## 6. Asset Requirements
*   **Sprite Sheet 1**: Kaiju A (Attacker) - 8 frames x 4 states (Idle, Charge, Attack, Hurt).
*   **Sprite Sheet 2**: Kaiju B (Defender) - 8 frames x 4 states.
*   **Sprite Sheet 3**: City Blocks - 4 states (Intact, Cracked, Collapsed, Rubble).
*   **Sprite Sheet 4**: Particles - 16x16 grid of explosions/debris.
*   **Background**: Parallax scrolling synthwave grid.

---

## 7. Milestones

### Phase 1: The Skeleton (Week 1)
*   [ ] Setup C# .NET 4.7.2 Project.
*   [ ] Implement `IWidgetObject` and `IWidgetInstance`.
*   [ ] Create the 30 FPS Render Loop.
*   [ ] Implement basic "City Grid" drawing (static).

### Phase 2: The Battle (Week 2)
*   [ ] Load Sprite Sheets.
*   [ ] Implement Kaiju Animation (Idle/Attack).
*   [ ] Implement Particle System (Debris).
*   [ ] Connect to MCP Gateway for basic data.

### Phase 3: The Juice (Week 3)
*   [ ] Implement Screen Shake.
*   [ ] Add Dithering/Shading effects.
*   [ ] Polish HUD and Battle Log.
*   [ ] Tune visual balance (Attack speed vs. Token rate).

---

## 8. Future Roadmap
*   **Audio**: Spawn an external process to play sound effects (Roars, Explosions) via `System.Media.SoundPlayer`.
*   **Interactivity**: Add a "Pause/Resume" button via WigiDash overlay.
*   **Multi-Agent**: Support 3+ Kaiju simultaneously.
*   **Weather**: Add rain/snow effects that intensify with "Error Rates."

---

**Approvals**:
*   [ ] Product Owner
*   [ ] Lead Developer

**Date**: May 8, 2026