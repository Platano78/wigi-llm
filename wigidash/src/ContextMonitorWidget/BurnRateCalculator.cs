using System;

namespace ClaudeCodeWidgets.ContextMonitor
{
    public enum LimitingFactor
    {
        TOKENS,
        TIME
    }

    public enum HealthStatus
    {
        Healthy,
        Warning,
        Critical
    }

    public class BurnRateCalculator
    {
        private const int TOKEN_LIMIT = 200000;
        private const double TIME_LIMIT_HOURS = 5.0;

        public void CalculateBurnRate(SessionMetrics metrics)
        {
            if (metrics == null)
                throw new ArgumentNullException("metrics");

            double elapsedHours = (metrics.LastActivity - metrics.SessionStart).TotalHours;

            double burnRate = elapsedHours > 0 ? metrics.TotalTokens / elapsedHours : 0;

            long tokensRemaining = TOKEN_LIMIT - metrics.TotalTokens;
            double hoursToTokenLimit = burnRate > 0 ? tokensRemaining / burnRate : double.MaxValue;
            double timeRemainingHours = TIME_LIMIT_HOURS - elapsedHours;

            hoursToTokenLimit = Math.Max(0, hoursToTokenLimit);
            timeRemainingHours = Math.Max(0, timeRemainingHours);

            LimitingFactor limitingFactor = hoursToTokenLimit < timeRemainingHours ?
                LimitingFactor.TOKENS : LimitingFactor.TIME;

            double minimumRemainingHours = Math.Min(hoursToTokenLimit, timeRemainingHours);

            double minutesRemaining = minimumRemainingHours * 60;
            HealthStatus healthStatus = minutesRemaining > 60 ?
                HealthStatus.Healthy :
                minutesRemaining >= 15 ?
                    HealthStatus.Warning :
                    HealthStatus.Critical;

            metrics.BurnRate = burnRate;
            metrics.HoursToTokenLimit = hoursToTokenLimit;
            metrics.TimeRemainingHours = timeRemainingHours;
            metrics.LimitingFactor = limitingFactor;
            metrics.MinimumRemainingHours = minimumRemainingHours;
            metrics.HealthStatus = healthStatus;
        }
    }
}
