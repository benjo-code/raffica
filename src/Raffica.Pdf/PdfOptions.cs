using Raffica.Core.Distribution;
using Raffica.Shell;

namespace Raffica.Pdf;

/// <summary>The command line of the PDF app. An invalid switch throws <see cref="ArgumentException"/>; anything that is not a switch is a file to open.</summary>
internal sealed class PdfOptions
{
    /// <summary>The files named on the command line, in order.</summary>
    public IReadOnlyList<string> Files { get; private init; } = [];

    /// <summary>The UI language given with <c>--lang</c> (<c>en</c> or <c>de</c>); null: the stored setting, then the language of the system.</summary>
    public string? Lang { get; private init; }

    public ThemeMode Theme { get; private init; } = ThemeMode.System;

    /// <summary>True if <c>--theme</c> was given; it then wins over the stored theme.</summary>
    public bool ThemeGiven { get; private init; }

    public bool HighContrast { get; private init; }

    /// <summary>The JSON file with the settings instead of the default one.</summary>
    public string? SettingsFile { get; private init; }

    /// <summary>Prints the value of this string key and exits before any window exists (the CI smoke test).</summary>
    public string? PrintString { get; private init; }

    /// <summary>Registers the app for PDF files in this scope (<c>--register-file-types=user|machine</c>, run by the installer) and exits before any window exists.</summary>
    public RegistrationScope? RegisterFileTypes { get; private init; }

    /// <summary>Removes that registration (<c>--unregister-file-types=user|machine</c>, run by the uninstaller) and exits before any window exists.</summary>
    public RegistrationScope? UnregisterFileTypes { get; private init; }

    /// <summary>
    /// The measurement switch of the run (at most one): <c>--measure-startup</c> (the line <c>first-page</c>), <c>--measure-second</c>
    /// (also <c>second-page</c> for a file handed over by a second launch), <c>--measure-scroll</c> (also <c>scroll …</c>) or
    /// <c>--measure-memory</c> (also <c>memory-bytes …</c>); null for a normal run. See <see cref="Measurement"/>.
    /// </summary>
    public string? MeasureSwitch { get; private init; }

    /// <summary>True for a measurement run.</summary>
    public bool IsMeasuring => MeasureSwitch is not null;

    public static PdfOptions Parse(string[] args)
    {
        var files = new List<string>();
        string? measure = null;
        string? lang = null;
        var theme = ThemeMode.System;
        var themeGiven = false;
        var highContrast = false;
        string? settingsFile = null;
        string? printString = null;
        RegistrationScope? register = null;
        RegistrationScope? unregister = null;

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
                    themeGiven = true;
                    break;
                case "--high-contrast":
                    highContrast = true;
                    break;
                case "--settings-file":
                    settingsFile = Value(args, ref i);
                    break;
                case "--print-string":
                    printString = Value(args, ref i);
                    break;
                case var other when other.StartsWith("--register-file-types", StringComparison.Ordinal):
                    register = Scope(other, "--register-file-types");
                    break;
                case var other when other.StartsWith("--unregister-file-types", StringComparison.Ordinal):
                    unregister = Scope(other, "--unregister-file-types");
                    break;
                case "--measure-startup" or "--measure-second" or "--measure-scroll" or "--measure-memory":
                    if (measure is not null)
                    {
                        throw new ArgumentException($"'{args[i]}' and '{measure}' cannot be combined (one measurement per run)");
                    }

                    measure = args[i];
                    break;
                case var other when other.StartsWith("--", StringComparison.Ordinal):
                    throw new ArgumentException($"unknown switch '{other}'");
                default:
                    files.Add(args[i]);
                    break;
            }
        }

        if (register is not null && unregister is not null)
        {
            throw new ArgumentException("'--register-file-types' and '--unregister-file-types' cannot be combined");
        }

        if (measure is not null)
        {
            // A measurement never meets the settings, the recent files or the instance pipe of the user: they all follow --settings-file.
            if (string.IsNullOrWhiteSpace(settingsFile))
            {
                throw new ArgumentException($"'{measure}' needs --settings-file (a measurement never reads or writes the settings of the user)");
            }

            if (files.Count == 0)
            {
                throw new ArgumentException($"'{measure}' needs the file to open");
            }
        }

        return new PdfOptions
        {
            MeasureSwitch = measure,
            Files = files,
            Lang = lang,
            Theme = theme,
            ThemeGiven = themeGiven,
            HighContrast = highContrast,
            SettingsFile = settingsFile,
            PrintString = printString,
            RegisterFileTypes = register,
            UnregisterFileTypes = unregister,
        };
    }

    // The scope is part of the argument (<name>=user|machine), as the installer passes it.
    private static RegistrationScope Scope(string arg, string name)
    {
        var value = arg.StartsWith(name + "=", StringComparison.Ordinal) ? arg[(name.Length + 1)..] : null;
        return value switch
        {
            "user" => RegistrationScope.User,
            "machine" => RegistrationScope.Machine,
            _ => throw new ArgumentException($"'{name}' needs =user or =machine"),
        };
    }

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
