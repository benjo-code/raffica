using System.Globalization;
using Raffica.Gallery.Sections;
using Raffica.Shell;

namespace Raffica.Gallery;

/// <summary>The two views of the gallery app.</summary>
public enum GalleryView
{
    /// <summary>The control gallery (default).</summary>
    Gallery,

    /// <summary>The demo window of the app frame, with the measurement switches.</summary>
    Frame,
}

/// <summary>The command line of the gallery. An invalid switch throws <see cref="ArgumentException"/>.</summary>
public sealed class GalleryOptions
{
    /// <summary>The UI language, <c>en</c> or <c>de</c>. Default: German if the OS UI language is German, else English.</summary>
    public string Lang { get; private init; } = DefaultLang();

    /// <summary>True if <c>--lang</c> was given; the frame view then ignores the stored language.</summary>
    public bool LangGiven { get; private init; }

    public ThemeMode Theme { get; private init; } = ThemeMode.System;

    /// <summary>True if <c>--theme</c> was given; the frame view then ignores the stored theme.</summary>
    public bool ThemeGiven { get; private init; }

    public bool HighContrast { get; private init; }

    public AppInk Ink { get; private init; } = AppInk.Write;

    /// <summary>Renders the window once into this PNG file and exits.</summary>
    public string? Screenshot { get; private init; }

    /// <summary>Shows only this section of the gallery (a screenshot of one section at a time). Null: all sections.</summary>
    public string? Section { get; private init; }

    /// <summary>
    /// Opens one menu (with its submenu) of the menus section when the window is shown, so a <c>--screenshot</c> can capture a popup.
    /// Without the menus section on screen the switch does nothing.
    /// </summary>
    public bool OpenMenu { get; private init; }

    /// <summary>
    /// Opens the sample dialog of the dialogs section when the window is shown, so a <c>--screenshot</c> can capture the scrim and the
    /// dialog. Without the dialogs section on screen the switch does nothing.
    /// </summary>
    public bool OpenDialog { get; private init; }

    /// <summary>Prints the value of this string key and exits before any window exists.</summary>
    public string? PrintString { get; private init; }

    /// <summary>The window to show: the control gallery (default) or the app frame demo.</summary>
    public GalleryView View { get; private init; }

    /// <summary>Frame view: writes <c>first-frame</c> after the second animation frame and exits (see <see cref="Measurement"/>).</summary>
    public bool MeasureStartup { get; private init; }

    /// <summary>Frame view: types 100 keys into the input target and writes the <c>input</c> line.</summary>
    public bool MeasureInput { get; private init; }

    /// <summary>Frame view: writes <c>memory-bytes</c> two seconds after the first frame.</summary>
    public bool MeasureMemory { get; private init; }

    /// <summary>Whether one of the measurement switches is set.</summary>
    public bool IsMeasuring => MeasureStartup || MeasureInput || MeasureMemory;

    /// <summary>Frame view: the JSON file with the settings; a run with this switch never reads or writes the settings of the user.</summary>
    public string? SettingsFile { get; private init; }

    /// <summary>Frame view: starts with the compact command bar (for screenshots and the layout test).</summary>
    public bool Compact { get; private init; }

    public static GalleryOptions Parse(string[] args)
    {
        var lang = DefaultLang();
        var theme = ThemeMode.System;
        var highContrast = false;
        var ink = AppInk.Write;
        string? screenshot = null;
        string? printString = null;
        string? section = null;
        var openMenu = false;
        var openDialog = false;
        var langGiven = false;
        var themeGiven = false;
        var view = GalleryView.Gallery;
        var measureStartup = false;
        var measureInput = false;
        var measureMemory = false;
        string? settingsFile = null;
        var compact = false;

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
                    langGiven = true;
                    break;
                case "--theme":
                    theme = Value(args, ref i) switch
                    {
                        "light" => ThemeMode.Light,
                        "dark" => ThemeMode.Dark,
                        "system" => ThemeMode.System,
                        var other => throw new ArgumentException($"unknown theme '{other}' (expected light, dark or system)"),
                    };
                    themeGiven = true;
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
                case "--open-menu":
                    openMenu = true;
                    break;
                case "--open-dialog":
                    openDialog = true;
                    break;
                case "--print-string":
                    printString = Value(args, ref i);
                    break;
                case "--view":
                    view = Value(args, ref i) switch
                    {
                        "gallery" => GalleryView.Gallery,
                        "frame" => GalleryView.Frame,
                        var other => throw new ArgumentException($"unknown view '{other}' (expected gallery or frame)"),
                    };
                    break;
                case "--measure-startup":
                    measureStartup = true;
                    break;
                case "--measure-input":
                    measureInput = true;
                    break;
                case "--measure-memory":
                    measureMemory = true;
                    break;
                case "--settings-file":
                    settingsFile = Value(args, ref i);
                    break;
                case "--compact":
                    compact = true;
                    break;
                default:
                    throw new ArgumentException($"unknown switch '{args[i]}'");
            }
        }

        if ((measureStartup || measureInput || measureMemory) && view != GalleryView.Frame)
        {
            throw new ArgumentException("the measurement switches (--measure-startup, --measure-input, --measure-memory) need --view frame");
        }

        return new GalleryOptions
        {
            Lang = lang,
            LangGiven = langGiven,
            Theme = theme,
            ThemeGiven = themeGiven,
            View = view,
            MeasureStartup = measureStartup,
            MeasureInput = measureInput,
            MeasureMemory = measureMemory,
            SettingsFile = settingsFile,
            Compact = compact,
            HighContrast = highContrast,
            Ink = ink,
            Screenshot = screenshot,
            PrintString = printString,
            Section = section,
            OpenMenu = openMenu,
            OpenDialog = openDialog,
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
