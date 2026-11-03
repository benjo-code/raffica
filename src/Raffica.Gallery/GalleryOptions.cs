using System.Globalization;
using Raffica.Gallery.Sections;
using Raffica.Shell;

namespace Raffica.Gallery;

/// <summary>The command line of the gallery. An invalid switch throws <see cref="ArgumentException"/>.</summary>
public sealed class GalleryOptions
{
    /// <summary>The UI language, <c>en</c> or <c>de</c>. Default: German if the OS UI language is German, else English.</summary>
    public string Lang { get; private init; } = DefaultLang();

    public ThemeMode Theme { get; private init; } = ThemeMode.System;

    public bool HighContrast { get; private init; }

    public AppInk Ink { get; private init; } = AppInk.Write;

    /// <summary>Renders the window once into this PNG file and exits.</summary>
    public string? Screenshot { get; private init; }

    /// <summary>Shows only this section of the gallery (a screenshot of one section at a time). Null: all sections.</summary>
    public string? Section { get; private init; }

    /// <summary>Prints the value of this string key and exits before any window exists.</summary>
    public string? PrintString { get; private init; }

    public static GalleryOptions Parse(string[] args)
    {
        var lang = DefaultLang();
        var theme = ThemeMode.System;
        var highContrast = false;
        var ink = AppInk.Write;
        string? screenshot = null;
        string? printString = null;
        string? section = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lang":
                    lang = Value(args, ref i) switch
                    {
                        "en" => "en",
                        "de" => "de",
                        var other => throw new ArgumentException($"unknown language '{other}' (expected en or de)"),
                    };
                    break;
                case "--theme":
                    theme = Value(args, ref i) switch
                    {
                        "light" => ThemeMode.Light,
                        "dark" => ThemeMode.Dark,
                        "system" => ThemeMode.System,
                        var other => throw new ArgumentException($"unknown theme '{other}' (expected light, dark or system)"),
                    };
                    break;
                case "--high-contrast":
                    highContrast = true;
                    break;
                case "--ink":
                    ink = Value(args, ref i) switch
                    {
                        "write" => AppInk.Write,
                        "sheets" => AppInk.Sheets,
                        "slides" => AppInk.Slides,
                        "pdf" => AppInk.Pdf,
                        var other => throw new ArgumentException($"unknown ink '{other}' (expected write, sheets, slides or pdf)"),
                    };
                    break;
                case "--screenshot":
                    screenshot = Value(args, ref i);
                    break;
                case "--section":
                    section = Value(args, ref i);
                    if (!GallerySections.Names.Contains(section))
                    {
                        throw new ArgumentException($"unknown section '{section}' (expected {string.Join(", ", GallerySections.Names)})");
                    }

                    break;
                case "--print-string":
                    printString = Value(args, ref i);
                    break;
                default:
                    throw new ArgumentException($"unknown switch '{args[i]}'");
            }
        }

        return new GalleryOptions
        {
            Lang = lang,
            Theme = theme,
            HighContrast = highContrast,
            Ink = ink,
            Screenshot = screenshot,
            PrintString = printString,
            Section = section,
        };
    }

    private static string DefaultLang() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";

    private static string Value(string[] args, ref int i)
    {
        var name = args[i];
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"the switch '{name}' needs a value");
        }

        return args[++i];
    }
}
