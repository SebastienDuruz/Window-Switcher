namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;

/// <summary>
/// Defines supported preview refresh-rate bounds for Linux previews.
/// </summary>
public static class PreviewRefreshRateSettings
{
    public const double MinFps = 0.2; // 1 frame / 5 seconds
    public const double MaxFps = 60.0;
    public const double DefaultLinuxFps = 20.0;

    public static double Clamp(double fps)
    {
        return Math.Clamp(fps, MinFps, MaxFps);
    }

    public static int GetDelayMs(double fps)
    {
        double clampedFps = Clamp(fps);
        return Math.Max(1, (int)Math.Round(1000d / clampedFps, MidpointRounding.AwayFromZero));
    }

    public static void ToFraction(double fps, out int numerator, out int denominator)
    {
        const int baseDenominator = 10; // Matches UI precision (0.1 FPS steps).
        double clampedFps = Clamp(fps);
        int scaledNumerator = Math.Max(
            1,
            (int)Math.Round(clampedFps * baseDenominator, MidpointRounding.AwayFromZero)
        );
        int gcd = GreatestCommonDivisor(scaledNumerator, baseDenominator);

        numerator = scaledNumerator / gcd;
        denominator = baseDenominator / gcd;
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);

        while (b != 0)
        {
            int remainder = a % b;
            a = b;
            b = remainder;
        }

        return a == 0 ? 1 : a;
    }
}
