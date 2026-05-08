using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LLMBrainMonitorWidget
{
    /// <summary>
    /// Autopilot inference shooter. Token throughput = enemy spawn rate. The
    /// player ship auto-targets and fires; the user just watches the model
    /// "play through" its own throughput.
    ///
    /// Physics runs on a separate animation thread (~30 FPS) while in game mode.
    /// Enemy spawning is driven by accumulated token deltas pushed in by the
    /// poll loop. Idle model = empty starfield. Bursty inference = bullet hell.
    /// </summary>
    public class GameMode
    {
        private const float ShipY = 290f;     // bottom band, leaves the bottom 30 for HUD
        private const float ShipSpeed = 320f; // px/s horizontal tracking speed (was 180)
        private const float EnemySpeedMin = 30f;
        private const float EnemySpeedMax = 90f;
        private const float LaserSpeed = 420f;
        private const float ExplosionLife = 0.4f;
        private const int MaxEnemies = 80;
        private const int MaxLasers = 60;
        private const int MaxExplosions = 40;
        private const int Stars = 60;

        // Spawn budget — 1 enemy per N tokens generated. Tuned to give a
        // visible challenge at moderate throughput without flooding at high tps.
        private const double TokensPerEnemy = 18.0;

        // llama.cpp /metrics can stall during heavy inference, so token deltas
        // arrive in a single big lump after the request completes. Cap the
        // actual spawn rate so the screen doesn't fill instantly — debt
        // accumulates and drains at this rate.
        private const double MaxSpawnsPerSec = 5.0;
        private const float FireCooldownSec = 0.08f;     // ~12 shots/sec ceiling
        private const float FireAlignTolerancePx = 14f;  // generous — rough alignment fires

        private struct Enemy { public bool Alive; public float X, Y, Vy; public float W, H; public byte R, G, B; public int Hp; public int Type; }
        private struct Laser  { public bool Alive; public float X, Y, Vy; }
        private struct Burst  { public bool Alive; public float X, Y, Life; public byte R, G, B; }
        private struct Star   { public float X, Y, Vy; public byte Bright; }

        private readonly Enemy[] _enemies = new Enemy[MaxEnemies];
        private readonly Laser[]  _lasers  = new Laser[MaxLasers];
        private readonly Burst[]  _bursts  = new Burst[MaxExplosions];
        private readonly Star[]   _stars   = new Star[Stars];
        private readonly Random _rng = new Random();

        private float _shipX = 240f;
        private float _shipAim = 240f;
        private float _fireCooldown = 0f;
        private double _spawnDebt = 0;
        private float _spawnInterval = 0; // dt accumulator for rate-limited spawning
        private int _score = 0;
        private float _entryAnim = 1.0f; // 1->0 fade-in over first second

        public GameMode(int canvasW)
        {
            _shipX = canvasW / 2f;
            _shipAim = _shipX;
            for (int i = 0; i < Stars; i++)
            {
                _stars[i].X = (float)_rng.NextDouble() * canvasW;
                _stars[i].Y = (float)_rng.NextDouble() * 320f;
                _stars[i].Vy = 8f + (float)_rng.NextDouble() * 30f;
                _stars[i].Bright = (byte)(80 + _rng.Next(160));
            }
        }

        public void OnExit() { /* cleanup hook — nothing to release */ }

        // Called by the animation thread per frame.
        public void Step(float dt, int canvasW, int canvasH, double tokensPerSec, double tokenDelta)
        {
            if (_entryAnim > 0) _entryAnim = Math.Max(0, _entryAnim - dt * 1.5f);

            // Stars drift
            for (int i = 0; i < Stars; i++)
            {
                _stars[i].Y += _stars[i].Vy * dt;
                if (_stars[i].Y > canvasH)
                {
                    _stars[i].Y = -2;
                    _stars[i].X = (float)_rng.NextDouble() * canvasW;
                }
            }

            // Spawn budget from token delta — but rate-limit actual spawns.
            // Without rate limiting, a 200-token response that arrives all at
            // once (because /metrics stalls during inference) would spawn ~11
            // enemies in a single frame. Pacing makes the autopilot feel
            // continuous rather than wave-after-quiet.
            if (tokenDelta > 0) _spawnDebt += tokenDelta / TokensPerEnemy;
            _spawnInterval += dt;
            float spawnPeriod = (float)(1.0 / MaxSpawnsPerSec);
            while (_spawnDebt >= 1.0 && _spawnInterval >= spawnPeriod)
            {
                SpawnEnemy(canvasW, tokensPerSec);
                _spawnDebt -= 1.0;
                _spawnInterval -= spawnPeriod;
            }

            // Auto-target nearest enemy — pick whoever is alive and lowest (closest to ship)
            int target = -1;
            float bestY = -1f;
            for (int i = 0; i < MaxEnemies; i++)
            {
                if (!_enemies[i].Alive) continue;
                if (_enemies[i].Y < canvasH - 20 && _enemies[i].Y > bestY)
                {
                    bestY = _enemies[i].Y;
                    target = i;
                }
            }

            // Track horizontally toward target
            if (target >= 0)
            {
                _shipAim = _enemies[target].X;
                float dx = _shipAim - _shipX;
                float maxStep = ShipSpeed * dt;
                if (dx > maxStep) _shipX += maxStep;
                else if (dx < -maxStep) _shipX -= maxStep;
                else _shipX = _shipAim;
            }

            // Auto-fire whenever there's a target and cooldown clear. The
            // tight 6px alignment from the previous version meant the ship
            // spent most of its time chasing without firing; user reported
            // "blindly fires but not enough." Now: fire if roughly aligned,
            // and place the laser at the target column so it actually hits
            // even when the ship hasn't fully caught up.
            _fireCooldown -= dt;
            if (target >= 0 && _fireCooldown <= 0)
            {
                float laserX = _shipX;
                float dxToTarget = _enemies[target].X - _shipX;
                if (Math.Abs(dxToTarget) < FireAlignTolerancePx)
                {
                    // Roughly aligned — fire from the target column directly so
                    // the shot is guaranteed to enter the enemy's bounding box.
                    laserX = _enemies[target].X;
                }
                FireLaser(laserX, ShipY - 8);
                _fireCooldown = FireCooldownSec;
            }

            // Step enemies
            for (int i = 0; i < MaxEnemies; i++)
            {
                if (!_enemies[i].Alive) continue;
                _enemies[i].Y += _enemies[i].Vy * dt;
                if (_enemies[i].Y > canvasH + 20)
                {
                    _enemies[i].Alive = false;
                }
            }

            // Step lasers + collision
            for (int i = 0; i < MaxLasers; i++)
            {
                if (!_lasers[i].Alive) continue;
                _lasers[i].Y += _lasers[i].Vy * dt;
                if (_lasers[i].Y < -10) { _lasers[i].Alive = false; continue; }

                for (int j = 0; j < MaxEnemies; j++)
                {
                    if (!_enemies[j].Alive) continue;
                    float ex = _enemies[j].X;
                    float ey = _enemies[j].Y;
                    float ew = _enemies[j].W;
                    float eh = _enemies[j].H;
                    if (_lasers[i].X >= ex - ew / 2 && _lasers[i].X <= ex + ew / 2
                        && _lasers[i].Y >= ey - eh / 2 && _lasers[i].Y <= ey + eh / 2)
                    {
                        _enemies[j].Hp--;
                        _lasers[i].Alive = false;
                        if (_enemies[j].Hp <= 0)
                        {
                            _enemies[j].Alive = false;
                            _score++;
                            SpawnBurst(ex, ey, _enemies[j].R, _enemies[j].G, _enemies[j].B);
                        }
                        break;
                    }
                }
            }

            // Step bursts
            for (int i = 0; i < MaxExplosions; i++)
            {
                if (!_bursts[i].Alive) continue;
                _bursts[i].Life -= dt;
                if (_bursts[i].Life <= 0) _bursts[i].Alive = false;
            }
        }

        private void SpawnEnemy(int canvasW, double tps)
        {
            // Find a free slot
            int idx = -1;
            for (int i = 0; i < MaxEnemies; i++)
                if (!_enemies[i].Alive) { idx = i; break; }
            if (idx < 0) return;

            // Type by chance — most are simple, occasional tankier ones
            int type = _rng.Next(100) < 12 ? 1 : 0;
            _enemies[idx].Alive = true;
            _enemies[idx].X = 12 + (float)_rng.NextDouble() * (canvasW - 24);
            _enemies[idx].Y = -10;
            _enemies[idx].Vy = EnemySpeedMin + (float)_rng.NextDouble() * (EnemySpeedMax - EnemySpeedMin);
            _enemies[idx].Type = type;
            _enemies[idx].Hp = type == 0 ? 1 : 3;
            _enemies[idx].W = type == 0 ? 14 : 22;
            _enemies[idx].H = type == 0 ? 12 : 18;

            // Color tier mirrors throughput band
            Color c = ColorForTps(tps);
            _enemies[idx].R = c.R;
            _enemies[idx].G = c.G;
            _enemies[idx].B = c.B;
        }

        private void FireLaser(float x, float y)
        {
            int idx = -1;
            for (int i = 0; i < MaxLasers; i++)
                if (!_lasers[i].Alive) { idx = i; break; }
            if (idx < 0) return;
            _lasers[idx].Alive = true;
            _lasers[idx].X = x;
            _lasers[idx].Y = y;
            _lasers[idx].Vy = -LaserSpeed;
        }

        private void SpawnBurst(float x, float y, byte r, byte g, byte b)
        {
            int idx = -1;
            for (int i = 0; i < MaxExplosions; i++)
                if (!_bursts[i].Alive) { idx = i; break; }
            if (idx < 0) return;
            _bursts[idx].Alive = true;
            _bursts[idx].X = x;
            _bursts[idx].Y = y;
            _bursts[idx].Life = ExplosionLife;
            _bursts[idx].R = r;
            _bursts[idx].G = g;
            _bursts[idx].B = b;
        }

        public void Draw(Graphics g, int w, int h, string modelName, double tokensPerSec)
        {
            // Black-blue space background
            using (LinearGradientBrush bg = new LinearGradientBrush(
                new Rectangle(0, 0, w, h),
                Color.FromArgb(8, 10, 20),
                Color.FromArgb(2, 4, 12), 90f))
            {
                g.FillRectangle(bg, 0, 0, w, h);
            }

            // Stars
            for (int i = 0; i < Stars; i++)
            {
                int b = _stars[i].Bright;
                using (Brush br = new SolidBrush(Color.FromArgb(b, 200, 220, 255)))
                {
                    g.FillRectangle(br, _stars[i].X, _stars[i].Y, 1, 1);
                }
            }

            // Enemies
            for (int i = 0; i < MaxEnemies; i++)
            {
                if (!_enemies[i].Alive) continue;
                DrawEnemy(g, _enemies[i]);
            }

            // Lasers
            using (Pen lp = new Pen(Color.FromArgb(255, 255, 255, 200), 1.5f))
            {
                for (int i = 0; i < MaxLasers; i++)
                {
                    if (!_lasers[i].Alive) continue;
                    g.DrawLine(lp, _lasers[i].X, _lasers[i].Y - 6, _lasers[i].X, _lasers[i].Y + 6);
                }
            }

            // Bursts
            for (int i = 0; i < MaxExplosions; i++)
            {
                if (!_bursts[i].Alive) continue;
                float t = _bursts[i].Life / ExplosionLife;
                int alpha = (int)(t * 220);
                float radius = (1 - t) * 18 + 2;
                using (Brush br = new SolidBrush(Color.FromArgb(alpha, _bursts[i].R, _bursts[i].G, _bursts[i].B)))
                {
                    g.FillEllipse(br, _bursts[i].X - radius, _bursts[i].Y - radius, radius * 2, radius * 2);
                }
            }

            // Friendly ship — simple triangle
            DrawShip(g, _shipX, ShipY);

            // HUD overlay (top-left + top-right)
            DrawHud(g, w, h, modelName, tokensPerSec);

            // Entry fade
            if (_entryAnim > 0)
            {
                int a = (int)(_entryAnim * 255);
                using (Brush fade = new SolidBrush(Color.FromArgb(a, 0, 0, 0)))
                {
                    g.FillRectangle(fade, 0, 0, w, h);
                }
            }
        }

        private void DrawEnemy(Graphics g, Enemy e)
        {
            Color body = Color.FromArgb(255, e.R, e.G, e.B);
            if (e.Type == 0)
            {
                // Small diamond
                PointF[] pts = new PointF[4];
                pts[0] = new PointF(e.X, e.Y - e.H / 2);
                pts[1] = new PointF(e.X + e.W / 2, e.Y);
                pts[2] = new PointF(e.X, e.Y + e.H / 2);
                pts[3] = new PointF(e.X - e.W / 2, e.Y);
                using (Brush b = new SolidBrush(body)) g.FillPolygon(b, pts);
                using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), 1)) g.DrawPolygon(p, pts);
            }
            else
            {
                // Tank — boxy
                Rectangle r = new Rectangle((int)(e.X - e.W / 2), (int)(e.Y - e.H / 2), (int)e.W, (int)e.H);
                using (Brush b = new SolidBrush(body)) g.FillRectangle(b, r);
                using (Pen p = new Pen(Color.FromArgb(255, 30, 40, 60), 1.5f)) g.DrawRectangle(p, r);
                // HP pip count
                for (int i = 0; i < e.Hp; i++)
                {
                    using (Brush b = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                    {
                        g.FillRectangle(b, e.X - e.W / 2 + 2 + i * 4, e.Y - e.H / 2 - 4, 2, 2);
                    }
                }
            }
        }

        private void DrawShip(Graphics g, float x, float y)
        {
            PointF[] pts = new PointF[4];
            pts[0] = new PointF(x, y - 12);
            pts[1] = new PointF(x + 9, y + 6);
            pts[2] = new PointF(x, y + 2);
            pts[3] = new PointF(x - 9, y + 6);
            using (Brush b = new SolidBrush(Color.FromArgb(255, 0, 230, 255))) g.FillPolygon(b, pts);
            using (Pen p = new Pen(Color.FromArgb(255, 255, 255, 255), 1)) g.DrawPolygon(p, pts);
            // Engine glow
            using (Brush eg = new SolidBrush(Color.FromArgb(180, 255, 200, 60)))
            {
                g.FillEllipse(eg, x - 3, y + 4, 6, 5);
            }
        }

        private void DrawHud(Graphics g, int w, int h, string modelName, double tokensPerSec)
        {
            using (Font f = new Font("Arial", 10, FontStyle.Bold))
            using (Brush bM = new SolidBrush(Color.FromArgb(180, 200, 220, 255)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                Rectangle nameBox = new Rectangle(8, 8, w / 2 - 16, 16);
                string txt = "AUTOPILOT  " + (string.IsNullOrEmpty(modelName) ? "(idle)" : modelName);
                g.DrawString(txt, f, bM, nameBox, fmt);
            }

            // Score — top right
            using (Font f = new Font("Arial", 14, FontStyle.Bold))
            using (Brush b = new SolidBrush(Color.FromArgb(255, 0, 230, 255)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Far;
                g.DrawString(_score.ToString("D5"), f, b, new RectangleF(w - 90, 6, 80, 18), fmt);
            }
            using (Font f = new Font("Arial", 7))
            using (Brush b = new SolidBrush(Color.FromArgb(140, 160, 200)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Far;
                g.DrawString("KILLS", f, b, new RectangleF(w - 90, 22, 80, 10), fmt);
            }

            // tok/s readout — bottom left
            using (Font f = new Font("Arial", 10, FontStyle.Bold))
            using (Brush b = new SolidBrush(ColorForTps(tokensPerSec)))
            {
                string s = tokensPerSec > 0 ? ((int)tokensPerSec) + " tok/s" : "AWAITING TOKENS";
                g.DrawString(s, f, b, 8, h - 18);
            }

            // Exit hint — bottom right
            using (Font f = new Font("Arial", 8))
            using (Brush b = new SolidBrush(Color.FromArgb(120, 140, 170)))
            {
                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Far;
                g.DrawString("double-tap to exit", f, b, new RectangleF(0, h - 16, w - 6, 12), fmt);
            }
        }

        private static Color ColorForTps(double tps)
        {
            if (tps <= 0) return Color.FromArgb(180, 180, 200);
            if (tps < 15) return Color.FromArgb(255, 200, 60);
            if (tps < 40) return Color.FromArgb(0, 230, 255);
            return Color.FromArgb(80, 255, 140);
        }
    }
}
