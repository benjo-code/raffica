using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;

namespace Raffica.Write.Dialogs;

/// <summary>
/// The Bookmarks card: the list of the bookmarks the user may see, a name box, and Go to, Add and Delete. A name that breaks Word's rule shows its reason in the hint and keeps the card open; Go to and Delete need a bookmark
/// (the one the name box names, or the selected row while the box is empty). Every answer closes the card; Close and Esc answer null. The list holds names only, never text of the document.
/// </summary>
internal static class BookmarkCard
{
    /// <param name="context">The names the card lists and the names a new one must not repeat.</param>
    /// <param name="done">What the user chose, or null when the card was closed.</param>
    public static Dialog Create(BookmarkContext context, Action<BookmarkRequest?> done)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);

        var name = CardParts.Text("PART_Name", WriteStrings.BookmarkNameLabel, null, minWidth: 280);
        var hint = CardParts.Hint();

        var list = new ListBox { Name = "PART_List", ItemsSource = context.Names.ToArray(), MinHeight = 120, MaxHeight = 220, MinWidth = 280, IsVisible = context.Names.Count > 0 };
        AutomationProperties.SetName(list, WriteStrings.BookmarkListLabel);
        var empty = new TextBlock { Name = "PART_Empty", Text = WriteStrings.BookmarkNone, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = context.Names.Count == 0 };
        empty.Classes.Add("secondary");

        var goTo = new Button { Name = "PART_GoTo", Content = WriteStrings.BookmarkGoTo };
        goTo.Classes.Add("boxed");
        var delete = new Button { Name = "PART_Delete", Content = WriteStrings.BookmarkDelete };
        delete.Classes.Add("boxed");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = CardFooter.Spacing, Children = { goTo, delete } };

        var (add, close) = CardParts.Buttons(WriteStrings.BookmarkAdd, ShellStrings.Close);
        var body = new StackPanel { Spacing = 10, MinWidth = 340 };
        body.Children.Add(CardParts.Field(WriteStrings.BookmarkNameLabel, name));
        body.Children.Add(hint);
        body.Children.Add(new TextBlock { Text = WriteStrings.BookmarkListLabel });
        body.Children.Add(list);
        body.Children.Add(empty);
        body.Children.Add(actions);
        var dialog = new Dialog { Title = WriteStrings.BookmarkTitle, Content = body, Footer = CardFooter.Build(add, null, close) };

        var finish = CardParts.Once(done);

        // The bookmark that Go to and Delete act on: the one the name box names (any case), else the selected row while the box is empty.
        string? Target()
        {
            var typed = name.Text?.Trim();
            if (!string.IsNullOrEmpty(typed))
            {
                return context.Names.FirstOrDefault(n => string.Equals(n, typed, StringComparison.OrdinalIgnoreCase));
            }

            return list.SelectedItem as string;
        }

        void Refresh()
        {
            var target = Target() is not null;
            goTo.IsEnabled = target;
            delete.IsEnabled = target;
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string selected)
            {
                name.Text = selected;
            }

            Refresh();
        };
        name.TextChanged += (_, _) =>
        {
            hint.IsVisible = false;
            Refresh();
        };
        Refresh();

        void Add()
        {
            var text = name.Text?.Trim() ?? string.Empty;
            if (BookmarkNames.Problem(text, context.AllNames) is { } problem)
            {
                hint.Text = ProblemText(problem);
                hint.IsVisible = true;
                name.Focus();
                name.SelectAll();
                return;
            }

            finish(new BookmarkRequest(BookmarkAction.Add, text));
        }

        CardParts.ConfirmOnEnter(Add, [name]);
        add.Click += (_, _) => Add();
        goTo.Click += (_, _) =>
        {
            if (Target() is { } target)
            {
                finish(new BookmarkRequest(BookmarkAction.GoTo, target));
            }
        };
        delete.Click += (_, _) =>
        {
            if (Target() is { } target)
            {
                finish(new BookmarkRequest(BookmarkAction.Delete, target));
            }
        };
        close.Click += (_, _) => finish(null);
        dialog.CancelRequested += (_, _) => finish(null);
        dialog.AttachedToVisualTree += (_, _) => name.Focus();
        return dialog;
    }

    /// <summary>The words for a refused name in the UI language.</summary>
    internal static string ProblemText(BookmarkNameProblem problem) => problem switch
    {
        BookmarkNameProblem.Empty => WriteStrings.BookmarkProblemEmpty,
        BookmarkNameProblem.TooLong => WriteStrings.BookmarkProblemTooLong,
        BookmarkNameProblem.BadStart => WriteStrings.BookmarkProblemBadStart,
        BookmarkNameProblem.BadCharacter => WriteStrings.BookmarkProblemBadCharacter,
        _ => WriteStrings.BookmarkProblemDuplicate,
    };
}
