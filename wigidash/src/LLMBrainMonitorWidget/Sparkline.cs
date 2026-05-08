using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LLMBrainMonitorWidget
{
    /// <summary>
    /// Live tok/s history — rolling ring buffer rendered as a filled curve.
    /// Resolution: one sample per poll tick (typically 1-2s).
    /// Capacity: configurable; 60-120 samples covers 1-2 minutes at 1s polls.
    /// </summary>
    public class Sparkline
    {
        private readonly double[] _buf;
        private int _head;
        private int _count;
        private readonly int _capacity;

        public Sparkline(int capacity)
        {
            _capacity = capacity;
            _buf = new double[capacity];
            _head = 0;
            _count = 0;
        }

        public void Push(double value)
        {
            _buf[_head] = value;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public double Max
        {
            get
            {
                double m = 0;
                for (int i = 0; i < _count; i++)
                    if (_buf[i] > m) m = _buf[i];
                return m;
            }
        }

        public void Draw(Graphics g, Rectangle bounds, double yScaleMax)
        {
            // Background — slightly lighter than canvas
            using (Brush bg = new SolidBrush(Color.FromArgb(14, 18, 28)))
            {
                g.FillRectangle(bg, bounds);
            }

            // Top/bottom edge lines
            using (Pen edge = new Pen(Color.FromArgb(40, 50, 70), 1))
            {
                g.DrawLine(edge, bounds.X, bounds.Y, bounds.Right, bounds.Y);
                g.DrawLine(edge, bounds.X, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            }

            if (_count < 2 || yScaleMax <= 0) return;

            // Build points walking the buffer in chronological order
            int n = _count;
            PointF[] line = new PointF[n + 2];
            float xStep = (float)bounds.Width / (_capacity - 1);

            int start = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < n; i++)
            {
                double v = _buf[(start + i) % _capacity];
                double clamped = Math.Max(0, Math.Min(yScaleMax, v));
                float x = bounds.X + i * xStep;
                float y = bounds.Bottom - 2 - (float)((bounds.Height - 4) * (clamped / yScaleMax));
                line[i] = new PointF(x, y);
            }
            line[n] = new PointF(line[n - 1].X, bounds.Bottom);
            line[n + 1] = new PointF(line[0].X, bounds.Bottom);

            // Filled area — translucent cyan gradient
            using (LinearGradientBrush fill = new LinearGradientBrush(
                bounds, Color.FromArgb(140, 0, 230, 255), Color.FromArgb(20, 0, 230, 255),
                LinearGradientMode.Vertical))
            {
                g.FillPolygon(fill, line);
            }

            // Top stroke
            PointF[] strokeLine = new PointF[n];
            for (int i = 0; i < n; i++) strokeLine[i] = line[i];
            using (Pen stroke = new Pen(Color.FromArgb(220, 0, 230, 255), 1.5f))
            {
                stroke.LineJoin = LineJoin.Round;
                if (n > 1) g.DrawLines(stroke, strokeLine);
            }

            // Latest sample dot
            using (Brush dotBrush = new SolidBrush(Color.FromArgb(255, 255, 255)))
            {
                g.FillEllipse(dotBrush, line[n - 1].X - 2, line[n - 1].Y - 2, 4, 4);
            }
        }
    }
}
