using System;
using System.IO;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// Locates KiCad's per-user settings directory, the one holding the global <c>sym-lib-table</c> and
/// <c>fp-lib-table</c>, by the same rule KiCad uses.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>PATHS::CalculateUserSettingsPath</c> in KiCad 10.0.6 (<c>common/paths.cpp</c>):
/// <c>$KICAD_CONFIG_HOME/&lt;version&gt;</c> when that variable is set, otherwise
/// <c>&lt;user config dir&gt;/kicad/&lt;version&gt;</c>. The user config dir is
/// <c>g_get_user_config_dir()</c> on Linux (<c>$XDG_CONFIG_HOME</c>, else <c>~/.config</c>),
/// <c>%APPDATA%</c> on Windows and <c>~/Library/Preferences</c> on macOS. The version is KiCad's
/// major.minor: <c>10.0</c> for a 10.0.x release, <c>10.99</c> for a nightly.
/// </para>
/// <para>
/// The KiCad Flatpak runs with <c>XDG_CONFIG_HOME=~/.var/app/org.kicad.KiCad/config</c>, and the
/// importer inherits that variable because KiCad launches it, so the same rule lands in the
/// sandboxed directory without a Flatpak special case. Started any other way, the importer sees the
/// host's <c>XDG_CONFIG_HOME</c> and resolves the host KiCad's directory instead.
/// </para>
/// <para>
/// Not covered: a KiCad built with a non-default <c>KICAD_CONFIG_DIR</c> (the <c>kicad</c> path
/// segment is a build-time setting).
/// </para>
/// </remarks>
public static class KiCadSettingsDirectory
{
    /// <summary>
    /// The directory that holds one settings directory per KiCad version.
    /// </summary>
    public static string GetRoot()
    {
        var configHome = Environment.GetEnvironmentVariable("KICAD_CONFIG_HOME");
        if (!string.IsNullOrEmpty(configHome))
        {
            return configHome;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(SpecialFolders.GetPath(Environment.SpecialFolder.ApplicationData), "kicad");
        }

        var home = SpecialFolders.GetPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Preferences", "kicad");
        }

        // The XDG base-directory spec says a relative XDG_CONFIG_HOME is invalid and must be ignored.
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var userConfig = !string.IsNullOrEmpty(xdgConfigHome) && Path.IsPathFullyQualified(xdgConfigHome)
            ? xdgConfigHome
            : Path.Combine(home, ".config");

        return Path.Combine(userConfig, "kicad");
    }

    /// <summary>
    /// The settings directory of one KiCad version. It is not checked for existence.
    /// </summary>
    public static string ForVersion(uint major, uint minor) => Path.Combine(GetRoot(), $"{major}.{minor}");

    /// <summary>
    /// The settings directory of the newest KiCad version that already has <paramref name="fileName"/>
    /// in it, or <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// This is a guess, used only when the running KiCad cannot be asked for its version: with 9.0,
    /// 10.0 and a 10.99 nightly side by side, it picks the nightly. Requiring the file to exist keeps
    /// it from picking a directory some other tool left behind.
    /// </remarks>
    public static string? FindNewestContaining(string fileName)
    {
        var root = GetRoot();
        if (!Directory.Exists(root))
        {
            return null;
        }

        string? newest = null;
        Version? newestVersion = null;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Version.TryParse(Path.GetFileName(directory), out Version? version)
                || version.Build != -1
                || !File.Exists(Path.Combine(directory, fileName)))
            {
                continue;
            }

            if (newestVersion is null || version > newestVersion)
            {
                newest = directory;
                newestVersion = version;
            }
        }

        return newest;
    }
}
