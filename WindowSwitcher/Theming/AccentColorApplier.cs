using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace WindowSwitcher.Theming;

internal static class AccentColorApplier
{
    public static void Apply(Color accentColor)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(accentColor));
            return;
        }

        if (Application.Current is null)
            return;

        // FluentTheme primarily uses these keys to color "accent" UI parts.
        // We set both the "System*" and non-system variants to cover more styles/templates.
        Color opaqueAccent = Color.FromArgb(0xFF, accentColor.R, accentColor.G, accentColor.B);

        SetAccentPalette(Application.Current.Resources, opaqueAccent, prefix: "SystemAccentColor");
        SetAccentPalette(Application.Current.Resources, opaqueAccent, prefix: "AccentColor");
    }

    private static void SetAccentPalette(
        IResourceDictionary resources,
        Color baseColor,
        string prefix
    )
    {
        resources[prefix] = baseColor;

        // Heuristic palette: progressively lighter/darker variants.
        // (Fluent uses multiple shades for pressed/hovered states.)
        resources[prefix + "Light1"] = Mix(baseColor, Colors.White, amount: 0.25);
        resources[prefix + "Light2"] = Mix(baseColor, Colors.White, amount: 0.45);
        resources[prefix + "Light3"] = Mix(baseColor, Colors.White, amount: 0.65);

        resources[prefix + "Dark1"] = Mix(baseColor, Colors.Black, amount: 0.15);
        resources[prefix + "Dark2"] = Mix(baseColor, Colors.Black, amount: 0.30);
        resources[prefix + "Dark3"] = Mix(baseColor, Colors.Black, amount: 0.45);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);

        byte r = (byte)Math.Clamp(Math.Round(from.R + (to.R - from.R) * amount), 0, 255);
        byte g = (byte)Math.Clamp(Math.Round(from.G + (to.G - from.G) * amount), 0, 255);
        byte b = (byte)Math.Clamp(Math.Round(from.B + (to.B - from.B) * amount), 0, 255);

        return Color.FromArgb(0xFF, r, g, b);
    }
}
