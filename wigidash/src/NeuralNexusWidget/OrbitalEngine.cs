using System;
using System.Drawing;

namespace NeuralNexusWidget
{
    /// <summary>
    /// Static helper for orbital positioning math.
    /// Distributes nodes evenly around a circle, rotates the entire ring.
    /// </summary>
    public static class OrbitalEngine
    {
        private const float DegreesPerSecond = 15f; // slow, cinematic rotation
        private const float RadiansPerSecond = DegreesPerSecond * (float)Math.PI / 180f;

        /// <summary>
        /// Initialize node positions: distribute peripheral nodes evenly
        /// around a circle centered at (cx, cy) with given radius.
        /// Node 0 (index 0) is the center gateway node.
        /// Nodes 1..N are distributed around the ring.
        /// </summary>
        public static void InitializeLayout(GraphModel model, int cx, int cy, float orbitRadius)
        {
            if (model == null) return;

            // Center node stays at center
            GraphNode center = model.GetCenterNode();
            if (center != null)
            {
                center.OrbitRadius = 0f;
                center.Radius = 24f;
            }

            // Distribute peripheral nodes evenly
            var peripherals = model.GetPeripheralNodes();
            int count = peripherals.Count;
            for (int i = 0; i < count; i++)
            {
                GraphNode node = peripherals[i];
                node.OrbitRadius = orbitRadius;
                // Evenly space around circle, starting from top (-PI/2)
                float angle = (2f * (float)Math.PI * i / count) - (float)Math.PI / 2f;
                node.OrbitAngle = angle;
                node.Radius = 16f;
            }
        }

        /// <summary>
        /// Advance orbital rotation by elapsed seconds.
        /// All nodes rotate around the center by the same delta.
        /// </summary>
        public static void StepRotation(GraphModel model, float elapsedSeconds)
        {
            if (model == null) return;

            float delta = RadiansPerSecond * elapsedSeconds;
            var peripherals = model.GetPeripheralNodes();
            for (int i = 0; i < peripherals.Count; i++)
            {
                peripherals[i].OrbitAngle += delta;
                // Keep angle normalized to 0..2PI
                while (peripherals[i].OrbitAngle < 0f)
                    peripherals[i].OrbitAngle += 2f * (float)Math.PI;
                while (peripherals[i].OrbitAngle >= 2f * (float)Math.PI)
                    peripherals[i].OrbitAngle -= 2f * (float)Math.PI;
            }
        }

        /// <summary>
        /// Compute the screen position of a node given the center point
        /// and the node's orbital parameters.
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
        /// Pulse phase goes 0..1 each second for healthy nodes.
        /// </summary>
        public static void DrawGlow(Graphics g, PointF pos, float radius, float pulsePhase, Color glowColor)
        {
            // Outer glow ring — draw 3 concentric circles with decreasing alpha
            for (int ring = 3; ring >= 1; ring--)
            {
                float r = radius + ring * 4f;
                int alpha = glowColor.A / (ring + 1);
                using (Pen p = new Pen(Color.FromArgb(alpha, glowColor.R, glowColor.G, glowColor.B), ring * 2f))
                {
                    g.DrawEllipse(p, pos.X - r, pos.Y - r, r * 2f, r * 2f);
                }
            }

            // Pulsing inner ring for healthy nodes
            if (pulsePhase > 0f && pulsePhase < 0.3f)
            {
                float pulseR = radius + 8f + pulsePhase * 20f;
                float alpha = (float)((1f - pulsePhase / 0.3f) * 80);
                using (Pen p = new Pen(Color.FromArgb((int)alpha, glowColor.R, glowColor.G, glowColor.B), 2f))
                {
                    g.DrawEllipse(p, pos.X - pulseR, pos.Y - pulseR, pulseR * 2f, pulseR * 2f);
                }
            }
        }

        /// <summary>
        /// Draw a radar grid background — concentric circles and cross-hairs.
        /// </summary>
        public static void DrawRadarGrid(Graphics g, Rectangle bounds)
        {
            int cx = bounds.Left + bounds.Width / 2;
            int cy = bounds.Top + bounds.Height / 2;

            // Concentric circles
            for (int i = 1; i <= 5; i++)
            {
                float r = i * (Math.Min(bounds.Width, bounds.Height) / 12f);
                using (Pen p = new Pen(Color.FromArgb(15, 40, 60, 80), 1f))
                {
                    g.DrawEllipse(p, cx - r, cy - r, r * 2f, r * 2f);
                }
            }

            // Cross-hair lines
            using (Pen p = new Pen(Color.FromArgb(12, 40, 60, 80), 1f))
            {
                g.DrawLine(p, cx, bounds.Top, cx, bounds.Bottom);
                g.DrawLine(p, bounds.Left, cy, bounds.Right, cy);
            }

            // Diagonal lines
            float diagR = (float)(Math.Min(bounds.Width, bounds.Height) / 2) * 0.9f;
            using (Pen p = new Pen(Color.FromArgb(8, 30, 50, 70), 1f))
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
            // Dashed line for energy conduit effect
            using (Pen p = new Pen(Color.FromArgb(40, healthColor.R, healthColor.G, healthColor.B), 1f))
            {
                p.DashPattern = new float[] { 4f, 6f };
                g.DrawLine(p, source.X, source.Y, target.X, target.Y);
            }
        }
    }
}
