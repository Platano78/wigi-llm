using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MCPPulseWidget
{
    /// <summary>
    /// Static helpers for orbital positioning, radar grid, glow rings,
    /// conduit lines, and beam highlight animations.
    /// </summary>
    public static class OrbitalEngine
    {
        private const float DegreesPerSecond = 3.0f; // slow, cinematic rotation (~0.05 rad/s)
        private const float RadiansPerSecond = DegreesPerSecond * (float)Math.PI / 180f;

        /// <summary>
        /// Initialize node positions: distribute peripheral nodes evenly
        /// around one or two concentric rings centered at (cx, cy).
        /// Node 0 is the center gateway node.
        /// </summary>
        public static void InitializeLayout(GraphModel model, int cx, int cy, float orbitRadius)
        {
            if (model == null) return;

            GraphNode center = model.GetCenterNode();
            if (center != null)
            {
                center.OrbitRadius = 0f;
                center.Radius = 28f; // slightly larger than peripheral
            }

            var peripherals = model.GetPeripheralNodes();
            int count = peripherals.Count;
            if (count == 0) return;

            // If more than 12 servers, use two concentric rings with INTERLEAVED
            // placement (even-indexed → inner, odd-indexed → outer). Keeps the
            // overall shape circular. The earlier "first half on inner, second
            // half on outer" packed all small nodes onto one side and all large
            // ones onto the other — looked lopsided.
            if (count > 12)
            {
                float innerR = orbitRadius * 0.6f;
                float outerR = orbitRadius;

                for (int i = 0; i < count; i++)
                {
                    GraphNode node = peripherals[i];
                    node.OrbitRadius = (i % 2 == 0) ? innerR : outerR;
                    float angle = (2f * (float)Math.PI * i / count) - (float)Math.PI / 2f;
                    node.OrbitAngle = angle;
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    GraphNode node = peripherals[i];
                    node.OrbitRadius = orbitRadius;
                    float angle = (2f * (float)Math.PI * i / count) - (float)Math.PI / 2f;
                    node.OrbitAngle = angle;
                }
            }
        }

        /// <summary>
        /// Advance orbital rotation by elapsed seconds.
        /// </summary>
        public static void StepRotation(GraphModel model, float elapsedSeconds)
        {
            if (model == null) return;

            float delta = RadiansPerSecond * elapsedSeconds;
            var peripherals = model.GetPeripheralNodes();
            for (int i = 0; i < peripherals.Count; i++)
            {
                peripherals[i].OrbitAngle += delta;
                while (peripherals[i].OrbitAngle < 0f)
                    peripherals[i].OrbitAngle += 2f * (float)Math.PI;
                while (peripherals[i].OrbitAngle >= 2f * (float)Math.PI)
                    peripherals[i].OrbitAngle -= 2f * (float)Math.PI;
            }
        }

        /// <summary>
        /// Compute the screen position of a node given the center point.
        /// </summary>
        public static PointF ComputePosition(GraphNode node, int cx, int cy)
        {
            if (node.OrbitRadius == 0f)
                return new PointF((float)cx, (float)cy);

            float x = cx + node.OrbitRadius * (float)Math.Cos(node.OrbitAngle);
            float y = cy + node.OrbitRadius * (float)Math.Sin(node.OrbitAngle);
            return new PointF(x, y);
        }

        /// <summary>
        /// Draw a pulsing glow ring around a node.
        /// Pulse intensity scales with callsPerMinute parameter.
        /// </summary>
        public static void DrawGlow(Graphics g, PointF pos, float radius, float pulsePhase,
            Color glowColor, int callsPerMinute)
        {
            // Scale pulse intensity with activity
            float activityScale = 1f;
            if (callsPerMinute > 0)
            {
                activityScale = 1f + (float)Math.Min(callsPerMinute, 60) / 30f;
            }

            // Outer glow ring — 3 concentric circles with decreasing alpha
            for (int ring = 3; ring >= 1; ring--)
            {
                float r = radius + ring * 4f * activityScale;
                int alpha = glowColor.A / (ring + 1);
                using (Pen p = new Pen(Color.FromArgb(alpha, glowColor.R, glowColor.G, glowColor.B), ring * 2f))
                {
                    g.DrawEllipse(p, pos.X - r, pos.Y - r, r * 2f, r * 2f);
                }
            }

            // Pulsing inner ring for healthy nodes
            if (pulsePhase > 0f && pulsePhase < 0.3f)
            {
                float pulseR = radius + 8f + pulsePhase * 20f * activityScale;
                float alpha = (float)((1f - pulsePhase / 0.3f) * 80);
                using (Pen p = new Pen(Color.FromArgb((int)alpha, glowColor.R, glowColor.G, glowColor.B), 2f))
                {
                    g.DrawEllipse(p, pos.X - pulseR, pos.Y - pulseR, pulseR * 2f, pulseR * 2f);
                }
            }
        }

        /// <summary>
        /// Draw a radar grid background: concentric circles + cross-hairs centered on the LLM node.
        /// </summary>
        public static void DrawRadarGrid(Graphics g, Rectangle bounds)
        {
            int cx = bounds.Left + bounds.Width / 2;
            int cy = bounds.Top + bounds.Height / 2;

            // Concentric circles
            for (int i = 1; i <= 5; i++)
            {
                float r = i * (float)(Math.Min(bounds.Width, bounds.Height) / 12);
                using (Pen p = new Pen(Color.FromArgb(12, 30, 50, 70), 1f))
                {
                    g.DrawEllipse(p, cx - r, cy - r, r * 2f, r * 2f);
                }
            }

            // Cross-hair lines
            using (Pen p = new Pen(Color.FromArgb(10, 30, 50, 70), 1f))
            {
                g.DrawLine(p, cx, bounds.Top, cx, bounds.Bottom);
                g.DrawLine(p, bounds.Left, cy, bounds.Right, cy);
            }

            // Diagonal lines
            float diagR = (float)(Math.Min(bounds.Width, bounds.Height) / 2) * 0.9f;
            using (Pen p = new Pen(Color.FromArgb(6, 20, 40, 60), 1f))
            {
                g.DrawLine(p, cx - diagR, cy - diagR, cx + diagR, cy + diagR);
                g.DrawLine(p, cx + diagR, cy - diagR, cx - diagR, cy + diagR);
            }
        }

        /// <summary>
        /// Draw an energy conduit line between two nodes.
        /// Line opacity reflects the target node's health state.
        /// </summary>
        public static void DrawConduit(Graphics g, PointF source, PointF target, Color healthColor)
        {
            using (Pen p = new Pen(Color.FromArgb(35, healthColor.R, healthColor.G, healthColor.B), 1f))
            {
                p.DashPattern = new float[] { 4f, 6f };
                g.DrawLine(p, source.X, source.Y, target.X, target.Y);
            }
        }

        /// <summary>
        /// Draw a beam highlight on a conduit: moving gradient highlight that brightens
        /// the conduit for 2 seconds. Called when an activity event fires.
        /// </summary>
        public static void DrawBeamHighlight(Graphics g, PointF source, PointF target,
            double spawnTime, bool success, int frameIndex)
        {
            double now = DateTime.UtcNow.ToOADate();
            double ageSec = (now - spawnTime) * 86400.0; // convert to seconds

            if (ageSec > 2.0) return;

            // Progress 0..1 over 2 seconds
            float t = (float)(ageSec / 2.0);
            float alpha = (float)((1.0 - t * t) * 200); // quadratic decay

            if (alpha < 1) return;

            Color beamColor = success
                ? Color.FromArgb((int)alpha, 0, 200, 255)
                : Color.FromArgb((int)alpha, 255, 80, 80);

            // Moving dash along the beam
            float dashOffset = t * 20f;
            using (Pen p = new Pen(beamColor, 2.5f))
            {
                p.DashPattern = new float[] { 6f, 8f };
                p.DashOffset = dashOffset;
                g.DrawLine(p, source.X, source.Y, target.X, target.Y);
            }

            // Glow halo around the beam
            using (Pen glowP = new Pen(Color.FromArgb((int)(alpha * 0.3f), beamColor.R, beamColor.G, beamColor.B), 8f))
            {
                g.DrawLine(glowP, source.X, source.Y, target.X, target.Y);
            }
        }

        /// <summary>
        /// Draw a red flash frame on a node that had a failed call.
        /// 3-frame red flash on the source node.
        /// </summary>
        public static void DrawNodeFlash(Graphics g, PointF pos, float radius, int flashFrames)
        {
            if (flashFrames <= 0) return;

            float flashAlpha = 80 + flashFrames * 50; // gets brighter as frames decrease
            using (SolidBrush flash = new SolidBrush(Color.FromArgb((int)flashAlpha, 255, 40, 40)))
            {
                g.FillEllipse(flash,
                    pos.X - radius * 1.5f, pos.Y - radius * 1.5f,
                    radius * 3f, radius * 3f);
            }
        }
    }
}
