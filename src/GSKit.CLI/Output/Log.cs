using Spectre.Console;

namespace GSKit.CLI.Output;

/// <summary>
/// Unified log output with colored bracketed tags — matches the style of tools like
/// yt-dlp, spotDL, and gallery-dl.  Tag colors are consistent across every command.
/// </summary>
public static class Log
{
    // ─── Tag styles ────────────────────────────────────────────────────────────
    //   [HTTP]   cyan        — outbound request fired
    //   [GEO]    blue        — geocoding / coordinate resolution
    //   [OK]     green       — success result
    //   [WARN]   yellow      — non-fatal advisory
    //   [ERR]    red bold    — fatal error
    //   [DBG]    dark gray   — debug/verbose, only shown with --debug

    public static void Http(string msg)     => Tag("cyan",    "HTTP", msg);
    public static void Geo(string msg)      => Tag("blue",    "GEO",  msg);
    public static void Ok(string msg)       => Tag("green",   "OK",   msg);
    public static void Warn(string msg)     => Tag("yellow",  "WARN", msg);
    public static void Err(string msg)      => Tag("red bold","ERR",  msg, stderr: true);
    public static void Dbg(string msg, bool debug)
    {
        if (debug) Tag("grey50",  "DBG",  msg);
    }

    private static void Tag(string color, string label, string msg, bool stderr = false)
    {
        var line = $"[{color}][[{label}]][/] {Markup.Escape(msg)}";
        if (stderr)
            AnsiConsole.MarkupLine(line); // no stderr distinction in Spectre; red is enough
        else
            AnsiConsole.MarkupLine(line);
    }
}
