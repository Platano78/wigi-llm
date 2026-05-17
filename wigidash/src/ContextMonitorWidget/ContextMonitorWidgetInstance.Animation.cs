using System;
using System.Drawing;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public enum AnimationIntensity { Subtle, Bold, Adaptive }
    public enum UsageTier { Calm, Busy, Alarm }

    public partial class ContextMonitorWidgetInstance
    {
        private const int FramesPerSecond = 30;
        private const int FrameIntervalMs = 33;

        private int _frameCount = 0;
        private AnimationIntensity _animIntensity = AnimationIntensity.Adaptive;

        private static AnimationIntensity ResolveAnimationIntensity()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("CC_ANIMATION_INTENSITY");
                if (!string.IsNullOrEmpty(env))
                {
                    switch (env.Trim().ToLowerInvariant())
                    {
                        case "subtle": return AnimationIntensity.Subtle;
                        case "bold": return AnimationIntensity.Bold;
                        case "adaptive": return AnimationIntensity.Adaptive;
                        case "off": return AnimationIntensity.Subtle;
                        case "none": return AnimationIntensity.Subtle;
                    }
                }
            }
            catch { }
            return AnimationIntensity.Adaptive;
        }

        private static UsageTier TierFromPercent(float pct)
        {
            if (pct >= 85) return UsageTier.Alarm;
            if (pct >= 60) return UsageTier.Busy;
            return UsageTier.Calm;
        }

        private AnimationIntensity EffectiveIntensity(UsageTier tier)
        {
            if (_animIntensity != AnimationIntensity.Adaptive) return _animIntensity;
            if (tier == UsageTier.Alarm) return AnimationIntensity.Bold;
            if (tier == UsageTier.Busy) return AnimationIntensity.Bold;
            return AnimationIntensity.Subtle;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private Color ComputeBackgroundColor(UsageTier tier, AnimationIntensity eff)
        {
            Color baseColor = Color.FromArgb(18, 18, 28);
            if (eff == AnimationIntensity.Subtle && tier == UsageTier.Calm) return baseColor;

            double freq, amp;
            int r, g, b;
            if (tier == UsageTier.Alarm)
            {
                freq = eff == AnimationIntensity.Bold ? 6.0 : 3.0;
                amp = eff == AnimationIntensity.Bold ? 0.55 : 0.30;
                r = 90; g = 18; b = 28;
            }
            else if (tier == UsageTier.Busy)
            {
                freq = eff == AnimationIntensity.Bold ? 2.0 : 1.0;
                amp = eff == AnimationIntensity.Bold ? 0.32 : 0.15;
                r = 60; g = 48; b = 16;
            }
            else
            {
                freq = 0.5; amp = 0.06;
                r = 24; g = 28; b = 42;
            }

            double phase = (_frameCount / (double)FramesPerSecond) * Math.PI * 2 * freq;
            double pulse = (Math.Sin(phase) + 1.0) / 2.0;
            double mix = amp * pulse;
            int fr = Clamp((int)(baseColor.R + (r - baseColor.R) * mix), 0, 255);
            int fg = Clamp((int)(baseColor.G + (g - baseColor.G) * mix), 0, 255);
            int fb = Clamp((int)(baseColor.B + (b - baseColor.B) * mix), 0, 255);
            return Color.FromArgb(fr, fg, fb);
        }

        private int ShimmerOffsetPx(int barWidth)
        {
            if (barWidth <= 0) return 0;
            int cycleFrames = FramesPerSecond * 2;
            int phase = _frameCount % cycleFrames;
            return (int)((phase / (double)cycleFrames) * barWidth);
        }

        private int StripeOffsetPx()
        {
            return _frameCount % 16;
        }

        private float BannerPulseScale(UsageTier tier, AnimationIntensity eff)
        {
            if (tier == UsageTier.Calm) return 1.0f;
            if (eff == AnimationIntensity.Subtle && tier != UsageTier.Alarm) return 1.0f;
            double freq = tier == UsageTier.Alarm ? 3.0 : 1.5;
            double amp = tier == UsageTier.Alarm ? 0.18 : 0.09;
            if (eff == AnimationIntensity.Bold) amp *= 1.8;
            double phase = (_frameCount / (double)FramesPerSecond) * Math.PI * 2 * freq;
            return (float)(1.0 + amp * Math.Sin(phase));
        }

        private int BannerFlashAlpha(UsageTier tier, AnimationIntensity eff)
        {
            if (tier != UsageTier.Alarm) return 255;
            if (eff == AnimationIntensity.Subtle) return 255;
            double freq = eff == AnimationIntensity.Bold ? 4.0 : 2.0;
            double phase = (_frameCount / (double)FramesPerSecond) * Math.PI * 2 * freq;
            double pulse = (Math.Sin(phase) + 1.0) / 2.0;
            return Clamp((int)(180 + pulse * 75), 0, 255);
        }

        private MultiSessionData GetSessionsCached()
        {
            try
            {
                if (_statusLineReader == null) return null;
                if (_sessionCache == null || DateTime.Now - _lastSessionRefresh > SessionRefreshInterval)
                {
                    _sessionCache = _statusLineReader.ReadAllSessions();
                    _lastSessionRefresh = DateTime.Now;
                }
                return _sessionCache;
            }
            catch (Exception ex) { LogDebug("Sessions: " + ex.Message); return _sessionCache; }
        }

        private UsageCacheData GetUsageCached()
        {
            try
            {
                if (_usageReader == null) return null;
                if (_usageCache == null || DateTime.Now - _lastUsageRefresh > UsageRefreshInterval)
                {
                    _usageCache = _usageReader.Read();
                    _lastUsageRefresh = DateTime.Now;
                }
                return _usageCache;
            }
            catch (Exception ex) { LogDebug("Usage: " + ex.Message); return _usageCache; }
        }

        private UsageTier ComputeAggregateTier(MultiSessionData multi)
        {
            if (multi == null || multi.Sessions == null) return UsageTier.Calm;
            float topPct = 0f;
            foreach (StatusLineData sd in multi.Sessions)
            {
                if (sd != null && sd.IsValid && sd.Session != null && sd.Session.UsagePercentage > topPct)
                    topPct = sd.Session.UsagePercentage;
            }
            return TierFromPercent(topPct);
        }
    }
}