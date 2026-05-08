using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LLMBrainMonitorWidget
{
    /// <summary>
    /// Token particle stream — driven by deltas in tokens_predicted_total.
    /// Each generated token spawns a particle that drifts upward through the
    /// monitor's middle band, fading as it rises. Replaces the dated sin()
    /// waveform with an animation that's actually correlated to model state.
    /// Pool-allocated to keep frame allocations zero.
    /// </summary>
    public class ParticleField
    {
        private struct Particle
        {
            public bool Alive;
            public float X, Y;
            public float Vx, Vy;
            public float Life;     // 0..1 remaining
            public float Size;
            public byte R, G, B;
        }

        private readonly Particle[] _pool;
        private readonly int _capacity;
        private readonly Random _rng;
        private int _nextIdx;

        public ParticleField(int capacity)
        {
            _capacity = capacity;
            _pool = new Particle[capacity];
            _rng = new Random();
            _nextIdx = 0;
        }

        /// <summary>
        /// Spawn N particles distributed across the field width.
        /// Color is mapped from the throughput tier we're currently in.
        /// </summary>
        public void Spawn(int count, Rectangle field, double tokensPerSec)
        {
            if (count <= 0) return;
            count = Math.Min(count, _capacity / 4); // never overflow on a bursty delta

            Color tint = ColorForThroughput(tokensPerSec);

            for (int i = 0; i < count; i++)
            {
                int idx = _nextIdx;
                _nextIdx = (_nextIdx + 1) % _capacity;

                _pool[idx].Alive = true;
                _pool[idx].X = field.X + (float)(_rng.NextDouble() * field.Width);
                _pool[idx].Y = field.Bottom - 2;
                // Slight horizontal drift, mostly upward velocity scaled to throughput
                _pool[idx].Vx = (float)((_rng.NextDouble() - 0.5) * 0.6);
                _pool[idx].Vy = -(float)(0.7 + _rng.NextDouble() * 1.5);
                _pool[idx].Life = 1f;
                _pool[idx].Size = 1.5f + (float)(_rng.NextDouble() * 1.8);
                _pool[idx].R = tint.R;
                _pool[idx].G = tint.G;
                _pool[idx].B = tint.B;
            }
        }

        /// <summary>
        /// Advance physics one frame. Particles fade as they rise, then die.
        /// </summary>
        public void Step(Rectangle field, float dt)
        {
            for (int i = 0; i < _capacity; i++)
            {
                if (!_pool[i].Alive) continue;

                _pool[i].X += _pool[i].Vx * dt * 60f;
                _pool[i].Y += _pool[i].Vy * dt * 60f;
                _pool[i].Life -= dt * 0.55f;

                if (_pool[i].Life <= 0 || _pool[i].Y < field.Y - 4)
                {
                    _pool[i].Alive = false;
                }
            }
        }

        public void Draw(Graphics g, Rectangle field)
        {
            // Soft glow pass first
            for (int i = 0; i < _capacity; i++)
            {
                if (!_pool[i].Alive) continue;
                float alpha = Math.Max(0, Math.Min(1, _pool[i].Life)) * 0.4f;
                int a = (int)(alpha * 255);
                if (a < 8) continue;
                Color glow = Color.FromArgb(a, _pool[i].R, _pool[i].G, _pool[i].B);
                using (Brush b = new SolidBrush(glow))
                {
                    float r = _pool[i].Size * 2.5f;
                    g.FillEllipse(b, _pool[i].X - r, _pool[i].Y - r, r * 2, r * 2);
                }
            }

            // Bright core pass
            for (int i = 0; i < _capacity; i++)
            {
                if (!_pool[i].Alive) continue;
                float alpha = Math.Max(0, Math.Min(1, _pool[i].Life));
                int a = (int)(alpha * 255);
                if (a < 16) continue;
                Color core = Color.FromArgb(a, _pool[i].R, _pool[i].G, _pool[i].B);
                using (Brush b = new SolidBrush(core))
                {
                    g.FillEllipse(b, _pool[i].X - _pool[i].Size, _pool[i].Y - _pool[i].Size,
                                  _pool[i].Size * 2, _pool[i].Size * 2);
                }
            }
        }

        public int LiveCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < _capacity; i++)
                    if (_pool[i].Alive) c++;
                return c;
            }
        }

        private static Color ColorForThroughput(double tps)
        {
            if (tps <= 0) return Color.FromArgb(120, 120, 140);
            if (tps < 15) return Color.FromArgb(255, 200, 60);
            if (tps < 40) return Color.FromArgb(0, 230, 255);
            return Color.FromArgb(80, 255, 140);
        }
    }
}
