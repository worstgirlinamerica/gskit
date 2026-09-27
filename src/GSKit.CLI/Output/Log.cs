using Spectre.Console;

namespace GSKit.CLI.Output;

/// <summary>
/// Log output modeled after N_m3u8DL-RE style:
///   - timestamp dim
///   - tag label colored, fixed-width
///   - message plain — color only on meaningful fragments
///   - quiet=true suppresses everything except ERR (used when --format json)
/// </summary>
public static class Log
{
    public static bool Quiet { get; set; } = false;

    public static void Info(string msg) => Write("grey85",      "INFO", msg);
    public static void Http(string msg) => Write("cyan",        "HTTP", msg);
    public static void Geo(string msg)  => Write("dodgerblue2", "GEO",  msg);
    public static void Ok(string msg)   => Write("green",       "OK",   msg);
    public static void Warn(string msg) => Write("yellow",      "WARN", msg);

    /// <summary>Always printed — even in quiet/json mode. Goes to stderr so it doesn't corrupt JSON stdout.</summary>
    public static void Err(string msg)
    {
        var ts  = $"[dim]{Timestamp()}[/]";
        var tag = $"[red][[ERR ]][/]";
        AnsiConsole.MarkupLine($"{ts} {tag} {Markup.Escape(msg)}");
    }

    public static void Dbg(string msg, bool debug)
    {
        if (!debug || Quiet) return;
        var ts = $"[dim]{Timestamp()}[/]";
        AnsiConsole.MarkupLine($"{ts} [dim][[DBG ]][/] [dim]{Markup.Escape(msg)}[/]");
    }

    public static string Hl(string s, string color = "white bold") =>
        $"[{color}]{Markup.Escape(s)}[/]";

    private static void Write(string tagColor, string label, string msg)
    {
        if (Quiet) return;
        var padded = label.PadRight(4);
        var ts     = $"[dim]{Timestamp()}[/]";
        var tag    = $"[{tagColor}][[{Markup.Escape(padded)}]][/]";
        AnsiConsole.MarkupLine($"{ts} {tag} {msg}");
    }

    private static string Timestamp() =>
        DateTime.Now.ToString("HH:mm:ss");
}
