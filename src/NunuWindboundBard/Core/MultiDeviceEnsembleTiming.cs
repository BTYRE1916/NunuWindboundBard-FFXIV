using System.Diagnostics;

namespace NunuWindboundBard.Core;

internal static class MultiDeviceEnsembleTiming
{
    public static long ApplyTrim(long sharedTargetQpc, int trimMs) =>
        checked(sharedTargetQpc + (long)Math.Round(Math.Clamp(trimMs, -1500, 1500) * (double)Stopwatch.Frequency / 1000));

    public static bool ReportDue(long targetQpc, long nowQpc, int delayMs) =>
        targetQpc != 0 && Stopwatch.GetElapsedTime(targetQpc, nowQpc).TotalMilliseconds >= delayMs;

    public static string DescribeTrim(int? appliedTrimMs, double localErrorMs) =>
        appliedTrimMs is int trim
            ? $"Applied trim {trim:+0;-0;0}ms, first callback vs shared note {trim + localErrorMs:+0.0;-0.0;0.0}ms. "
            : "";
}
