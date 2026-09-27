using Spectre.Console;

namespace GSKit.CLI.Output;

/// <summary>
/// Log output modeled after N_m3u8DL-RE / gallery-dl style:
///   - timestamp is dim
///   - tag label has a color, fixed-width, no bold
///   - message text is plain — color only on the meaningful fragment when passed in
///   - debug lines are the whole line dim
/// </summary>
public static class Log
{
    // Call signatures mirror the intent:
    //   Log.Info("doing thing")                  → plain message
    //   Log.Info($"fetched {Hl(url)}")           → highlight one fragment inline
    //   Log.Ok($"4 stores · {Hl("3 in stock")}") → green tag, highlighted count

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>Neutral progress — tag is dim white, message is plain.</summary>
    public static void Info(string msg) => Write("grey85", "INFO", msg);

    /// <summary>Outbound HTTP request — tag cyan.</summary>
    public static void Http(string msg) => Write("cyan", "HTTP", msg);

    /// <summary>Geo / resolve step — tag blue.</summary>
    public static void Geo(string msg)  => Write("dodgerblue2", "GEO", msg);

    /// <summary>Success — tag green.</summary>
    public static void Ok(string msg)   => Write("green", "OK", msg);

    /// <summary>Non-fatal advisory — tag yellow, message plain.</summary>
    public static void Warn(string msg) => Write("yellow", "WARN", msg);

    /// <summary>Fatal error — tag red, message plain.</summary>
    public static void Err(string msg)  => Write("red", "ERR", msg);

    /// <summary>Debug detail — entire line dim, only shown when debug=true.</summary>
    public static void Dbg(string msg, bool debug)
    {
        if (!debug) return;
        var ts = $"[dim]{Timestamp()}[/]";
        AnsiConsole.MarkupLine($"{ts} [dim][[DBG]][/] [dim]{Markup.Escape(msg)}[/]");
    }

    /// <summary>
    /// Wrap a fragment of a message in a highlight color.
    /// Use inline: Log.Ok($"done — {Log.Hl("3 in stock")} found")
    /// </summary>
    public static string Hl(string s, string color = "white bold") =>
        $"[{color}]{Markup.Escape(s)}[/]";

    // ── Internal ──────────────────────────────────────────────────────────────

    private static void Write(string tagColor, string label, string msg)
    {
        // Pad label to 4 chars so columns line up: HTTP, INFO, WARN, ERR·, OK··, GEO·
        var padded = label.PadRight(4);
        var ts     = $"[dim]{Timestamp()}[/]";
        var tag    = $"[{tagColor}][[{Markup.Escape(padded)}]][/]";
        AnsiConsole.MarkupLine($"{ts} {tag} {msg}");
    }

    private static string Timestamp() =>
        DateTime.Now.ToString("HH:mm:ss");
}
