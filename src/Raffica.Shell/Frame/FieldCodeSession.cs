namespace Raffica.Shell.Frame;

/// <summary>Where the Office-style Alt "field code" mode currently is.</summary>
public enum FieldCodeStage
{
    Off,
    TabLetters,
    CommandCodes,
    MenuItems,
}

/// <summary>
/// The Alt field-code state machine. A bare Alt press and release (or F10) turns the mode on; Alt held
/// with another key is an ordinary shortcut; AltGr (Ctrl+Alt) never enters the mode. It never reads
/// display names, only the declared codes. Pure logic, no UI.
/// </summary>
public sealed class FieldCodeSession(RibbonDefinition ribbon)
{
    private bool _altAlone;
    private bool _altSwallowed;

    public FieldCodeStage Stage { get; private set; }

    public RibbonTabDefinition? ActiveTab { get; private set; }

    public CommandDefinition? OpenMenu { get; private set; }

    public string Typed { get; private set; } = "";

    public event Action? Changed;

    public event Action<CommandDefinition>? CommandRun;

    public event Action<RibbonTabDefinition>? TabSelected;

    /// <summary>Alt went down. Returns whether the key event is consumed.</summary>
    public bool AltDown(bool ctrlHeld)
    {
        if (ctrlHeld)
        {
            _altAlone = false;
            return false;
        }

        if (Stage != FieldCodeStage.Off)
        {
            // Alt again ends the mode; the matching release is swallowed.
            Cancel();
            _altSwallowed = true;
            return true;
        }

        _altAlone = true;
        return false;
    }

    /// <summary>Any other key while Alt is down: Alt becomes an ordinary shortcut modifier. Never consumes.</summary>
    public bool OtherKey()
    {
        _altAlone = false;
        _altSwallowed = false;
        return false;
    }

    /// <summary>Alt was released. Returns whether the key event is consumed.</summary>
    public bool AltUp()
    {
        if (_altSwallowed)
        {
            _altSwallowed = false;
            return true;
        }

        if (!_altAlone)
        {
            return false;
        }

        _altAlone = false;
        if (Stage == FieldCodeStage.Off)
        {
            Enter();
        }

        return true;
    }

    /// <summary>F10 enters the tab-letter stage (or ends the mode when it is already on).</summary>
    public bool F10()
    {
        _altAlone = false;
        _altSwallowed = false;
        if (Stage == FieldCodeStage.Off)
        {
            Enter();
        }
        else
        {
            Cancel();
        }

        return true;
    }

    /// <summary>A typed character. Consumed whenever the mode is on.</summary>
    public bool Type(char c)
    {
        if (Stage == FieldCodeStage.Off)
        {
            return false;
        }

        var key = char.ToUpperInvariant(c);
        switch (Stage)
        {
            case FieldCodeStage.TabLetters:
                SelectTab(key);
                break;
            case FieldCodeStage.CommandCodes:
                TypeCode(ActiveTab is { } tab ? Flatten(tab) : [], key);
                break;
            case FieldCodeStage.MenuItems:
                TypeCode(OpenMenu?.Items ?? [], key);
                break;
        }

        return true;
    }

    /// <summary>One step back: menu items, command codes, tab letters, off.</summary>
    public void Escape()
    {
        switch (Stage)
        {
            case FieldCodeStage.MenuItems:
                OpenMenu = null;
                Typed = "";
                Stage = FieldCodeStage.CommandCodes;
                break;
            case FieldCodeStage.CommandCodes:
                ActiveTab = null;
                Typed = "";
                Stage = FieldCodeStage.TabLetters;
                break;
            case FieldCodeStage.TabLetters:
                Stage = FieldCodeStage.Off;
                break;
            default:
                return;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Ends the mode (click, focus loss, dialog). It also forgets a pending swallowed Alt release: when the window loses the
    /// focus (Alt+Tab) that release never arrives, and the next bare Alt must enter the mode again.
    /// </summary>
    public void Cancel()
    {
        _altAlone = false;
        _altSwallowed = false;
        if (Stage == FieldCodeStage.Off)
        {
            return;
        }

        Reset();
        Changed?.Invoke();
    }

    private static List<CommandDefinition> Flatten(RibbonTabDefinition tab)
    {
        var list = new List<CommandDefinition>();
        foreach (var group in tab.Groups)
        {
            list.AddRange(group.Commands);
        }

        return list;
    }

    private void Enter()
    {
        Reset();
        Stage = FieldCodeStage.TabLetters;
        Changed?.Invoke();
    }

    private void Reset()
    {
        Stage = FieldCodeStage.Off;
        ActiveTab = null;
        OpenMenu = null;
        Typed = "";
    }

    private void SelectTab(char key)
    {
        foreach (var tab in ribbon.Tabs)
        {
            if (tab.Letter.Length == 1 && tab.Letter[0] == key)
            {
                ActiveTab = tab;
                Typed = "";
                Stage = FieldCodeStage.CommandCodes;
                Changed?.Invoke();
                TabSelected?.Invoke(tab);
                return;
            }
        }
    }

    private void TypeCode(IReadOnlyList<CommandDefinition> candidates, char key)
    {
        var typed = Typed + key;
        CommandDefinition? exact = null;
        var prefix = false;
        foreach (var command in candidates)
        {
            if (command.FieldCode == typed)
            {
                exact = command;
                break;
            }

            if (command.FieldCode.StartsWith(typed, StringComparison.Ordinal))
            {
                prefix = true;
            }
        }

        if (exact is not null)
        {
            Complete(exact);
        }
        else if (prefix)
        {
            Typed = typed;
            Changed?.Invoke();
        }
    }

    private void Complete(CommandDefinition command)
    {
        if (Stage == FieldCodeStage.CommandCodes && command.Items is not null)
        {
            OpenMenu = command;
            Typed = "";
            Stage = FieldCodeStage.MenuItems;
            Changed?.Invoke();
            return;
        }

        Reset();
        Changed?.Invoke();
        command.Run?.Invoke();
        CommandRun?.Invoke(command);
    }
}
