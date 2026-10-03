using System;
using System.Threading;

namespace HMoeWebCrawler;

internal static class ConsoleLogger
{
    private const string Reset = "\e[0m";
    private const string Cyan = "\e[36m";
    private const string Green = "\e[32m";
    private const string Yellow = "\e[33m";
    private const string Red = "\e[31m";
    private const string Gray = "\e[90m";

    private static readonly Lock _SyncRoot = new();
    private static readonly bool _UseColors = !Console.IsOutputRedirected;

    public static void Header(string title)
    {
        lock (_SyncRoot)
        {
            var rule = new string('=', Math.Clamp(title.Length + 12, 32, 72));
            Console.WriteLine();
            Console.WriteLine(rule);
            Console.WriteLine($"  {title}");
            Console.WriteLine(rule);
        }
    }

    public static void Info(string message) => Write("INFO", Cyan, message);

    public static void Success(string message) => Write("SUCC", Green, message);

    public static void Warning(string message) => Write("WARN", Yellow, message);

    public static void Error(string message) => Write("ERR!", Red, message);

    public static void Skip(string message) => Write("SKIP", Gray, message);

    public static void Exception(Exception exception, string? context = null)
    {
        var prefix = string.IsNullOrWhiteSpace(context) ? string.Empty : context + ": ";
        Error($"{prefix}{exception.GetType().Name}: {exception.Message}");
    }

    public static void Prompt(string message) => Write("INPT", Yellow, message, newline: false);

    private static void Write(string level, string color, string message, bool newline = true)
    {
        lock (_SyncRoot)
        {
            var prefix = $"[{DateTime.Now:HH:mm:ss}] [{level}]";
            var formatted = $"{prefix} {message}";
            if (_UseColors)
            {
                var coloredLevel = $"{prefix[..^(level.Length + 2)]}{color}[{level}]{Reset}";
                formatted = $"{coloredLevel} {message}";
            }

            if (newline)
                Console.WriteLine(formatted);
            else
                Console.Write(formatted);
        }
    }
}
