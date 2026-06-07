using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using System;
using System.Collections.Generic;

namespace AssetMap.Avalonia.Services;

public enum AppTheme { Dark, Light }
public enum AccentColor { Blue, Purple, Green, Orange, DarkGreen, DarkBlue, Red }

public static class ThemeService
{
    private const string AssemblyName = "AssetMap.Avalonia";

    private static readonly Dictionary<AccentColor, string> AccentUris = new()
    {
        [AccentColor.Blue]      = $"avares://{AssemblyName}/Themes/Accents/Blue.axaml",
        [AccentColor.Purple]    = $"avares://{AssemblyName}/Themes/Accents/Purple.axaml",
        [AccentColor.Green]     = $"avares://{AssemblyName}/Themes/Accents/Green.axaml",
        [AccentColor.Orange]    = $"avares://{AssemblyName}/Themes/Accents/Orange.axaml",
        [AccentColor.DarkGreen] = $"avares://{AssemblyName}/Themes/Accents/DarkGreen.axaml",
        [AccentColor.DarkBlue]  = $"avares://{AssemblyName}/Themes/Accents/DarkBlue.axaml",
        [AccentColor.Red]       = $"avares://{AssemblyName}/Themes/Accents/Red.axaml",
    };

    private static AppTheme _currentTheme = AppTheme.Dark;
    private static AccentColor _currentAccent = AccentColor.Blue;

    public static AppTheme CurrentTheme => _currentTheme;
    public static AccentColor CurrentAccent => _currentAccent;

    /// <summary>
    /// Switch light / dark. Updates Application.RequestedThemeVariant
    /// which triggers ThemeDictionaries in ThemeColors.axaml automatically.
    /// </summary>
    public static void SetTheme(AppTheme theme)
    {
        _currentTheme = theme;
        Application.Current!.RequestedThemeVariant =
            theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    /// <summary>
    /// Swap accent ResourceDictionary at runtime.
    /// Replaces the currently loaded accent entry in MergedDictionaries.
    /// </summary>
    public static void SetAccent(AccentColor accent)
    {
        _currentAccent = accent;

        var app = Application.Current!;
        var merged = app.Resources.MergedDictionaries;

        // Remove existing accent (any ResourceInclude pointing to Accents/)
        // Avoid LINQ OfType — Avalonia.Styling defines its own OfType that conflicts.
        ResourceInclude? existing = null;
        foreach (var item in merged)
        {
            if (item is ResourceInclude ri &&
                ri.Source?.ToString().Contains("/Themes/Accents/") == true)
            {
                existing = ri;
                break;
            }
        }

        if (existing is not null)
            merged.Remove(existing);

        merged.Add(new ResourceInclude(new Uri($"avares://{AssemblyName}/App.axaml"))
        {
            Source = new Uri(AccentUris[accent])
        });
    }

    /// <summary>
    /// Toggle between Dark and Light.
    /// </summary>
    public static void ToggleTheme() =>
        SetTheme(_currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
