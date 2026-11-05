using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Core.Signatures;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Signing;

/// <summary>What the signature picker answered.</summary>
internal abstract record PickerResult
{
    private PickerResult()
    {
    }

    /// <summary>The user picked a saved signature to place.</summary>
    /// <param name="Signature">The signature.</param>
    internal sealed record Picked(SavedSignature Signature) : PickerResult;

    /// <summary>The user wants to make a new signature.</summary>
    internal sealed record New : PickerResult;

    /// <summary>The picker was cancelled (Cancel, Esc).</summary>
    internal sealed record Cancelled : PickerResult;
}

/// <summary>
/// The card of the Sign command when signatures are saved: one row per signature with a picture, its name and a Delete button, "New
/// signature…", Cancel and the two sentences of the hint. A click on a row picks it. Delete asks first (a short text with Delete and Keep;
/// no arming delay: nothing in a document is touched) and removes the signature from the library only. While a delete runs nothing else
/// answers: Cancel is disabled and Esc, the close button, a pick and New do nothing, so the result is always given exactly once and never
/// while the library is being changed. A delete that fails says so without a path and leaves the list as it was. The pictures are made off
/// the UI thread and freed when the card closes. Used on the UI thread only; every library call is awaited.
/// </summary>
internal static class SignaturePickerCard
{
    /// <param name="items">The saved signatures in the order they were added.</param>
    /// <param name="library">The library the signatures live in (Delete changes it; reading the pictures needs it).</param>
    /// <param name="done">The answer, given exactly once.</param>
    public static Dialog Create(IReadOnlyList<SavedSignature> items, SignatureLibrary library, Action<PickerResult> done)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(done);
        return new Card(items, library, done).Dialog;
    }

    private sealed class Card
    {
        private readonly SignatureLibrary _library;
        private readonly Action<PickerResult> _done;
        private readonly List<SavedSignature> _items;
        private readonly List<SignatureThumbnail> _thumbnails = [];
        private readonly StackPanel _list = new() { Name = "PART_List", Spacing = 6 };
        private readonly TextBlock _empty;
        private readonly StackPanel _confirm;
        private readonly TextBlock _message;
        private readonly Button _yes;
        private readonly Button _keep;
        private readonly Button _new;
        private readonly Button _cancel;
        private SavedSignature? _pending;
        private bool _busy;
        private bool _finished;

        public Card(IReadOnlyList<SavedSignature> items, SignatureLibrary library, Action<PickerResult> done)
        {
            _library = library;
            _done = done;
            _items = [.. items];
            AutomationProperties.SetName(_list, PdfStrings.SignaturePickerList);

            _empty = new TextBlock { Name = "PART_Empty", Text = PdfStrings.SignaturePickerEmpty, TextWrapping = TextWrapping.Wrap };
            _empty.Bind(TextBlock.ForegroundProperty, _empty.GetResourceObservable("Raffica.Brush.Text.Secondary"));

            var confirmText = new TextBlock { Name = "PART_ConfirmText", Text = PdfStrings.SignaturePickerDeleteConfirm, TextWrapping = TextWrapping.Wrap };
            _yes = TextButton("PART_DeleteYes", PdfStrings.SignaturePickerDeleteYes, "boxed");
            _keep = TextButton("PART_DeleteKeep", PdfStrings.SignaturePickerDeleteKeep, "boxed");
            _confirm = new StackPanel
            {
                IsVisible = false,
                Spacing = 8,
                Children = { confirmText, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _yes, _keep } } },
            };
            AutomationProperties.SetLiveSetting(_confirm, AutomationLiveSetting.Polite);

            _message = new TextBlock { Name = "PART_Message", IsVisible = false, TextWrapping = TextWrapping.Wrap };
            _message.Bind(TextBlock.ForegroundProperty, _message.GetResourceObservable("Raffica.Brush.Danger"));
            AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);

            var hint = new TextBlock { Name = "PART_Hint", Text = PdfStrings.SignatureHintText(), TextWrapping = TextWrapping.Wrap };
            hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Text.Secondary"));

            _new = TextButton("PART_New", PdfStrings.SignaturePickerNew, "boxed");
            _cancel = TextButton("PART_Cancel", ShellStrings.Cancel, "boxed");

            Dialog = new Dialog
            {
                Title = PdfStrings.SignaturePickerTitle,
                Content = new StackPanel { Spacing = 10, MinWidth = 360, Children = { _list, _empty, _confirm, _message, hint } },
                Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _new, _cancel } },
            };

            _yes.Click += async (_, _) => await DeleteAsync();
            _keep.Click += (_, _) =>
            {
                if (!_busy)
                {
                    _pending = null;
                    Refresh();
                    FocusStart();
                }
            };
            _new.Click += (_, _) => Finish(new PickerResult.New());
            _cancel.Click += (_, _) => Finish(new PickerResult.Cancelled());
            Dialog.CancelRequested += (_, _) => Finish(new PickerResult.Cancelled());
            Dialog.DetachedFromVisualTree += (_, _) => DisposeThumbnails();

            Rebuild();
            Refresh();
        }

        public Dialog Dialog { get; }

        private static Button TextButton(string name, string text, string cssClass)
        {
            var button = new Button { Name = name, Content = text };
            button.Classes.Add(cssClass);
            AutomationProperties.SetName(button, text);
            return button;
        }

        /// <summary>Makes the rows from the list (the pictures of the rows before are freed).</summary>
        private void Rebuild()
        {
            DisposeThumbnails();
            _list.Children.Clear();
            foreach (var item in _items)
            {
                var thumbnail = new SignatureThumbnail(item, _library) { Name = "PART_Thumb_" + item.Id };
                _thumbnails.Add(thumbnail);
                var name = new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var pick = new Button
                {
                    Name = "PART_Pick_" + item.Id,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { thumbnail, name } },
                };
                pick.Classes.Add("boxed");
                AutomationProperties.SetName(pick, item.Name);
                var delete = TextButton("PART_Delete_" + item.Id, PdfStrings.SignaturePickerDelete, "boxed");
                ToolTip.SetTip(delete, PdfStrings.SignaturePickerDeleteTip);
                AutomationProperties.SetHelpText(delete, PdfStrings.SignaturePickerDeleteTip);
                var chosen = item;
                pick.Click += (_, _) =>
                {
                    if (!_busy)
                    {
                        Finish(new PickerResult.Picked(chosen));
                    }
                };
                delete.Click += (_, _) =>
                {
                    if (!_busy)
                    {
                        _pending = chosen;
                        _message.IsVisible = false;
                        Refresh();
                        _keep.Focus(); // the safe answer first
                    }
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                row.Children.Add(pick);
                Grid.SetColumn(delete, 1);
                row.Children.Add(delete);
                _list.Children.Add(row);
            }
        }

        private void Refresh()
        {
            _empty.IsVisible = _items.Count == 0;
            _confirm.IsVisible = _pending is not null;
            var idle = !_busy;
            _yes.IsEnabled = _keep.IsEnabled = idle;
            _new.IsEnabled = idle;
            _cancel.IsEnabled = idle;
            foreach (var row in _list.Children.OfType<Grid>())
            {
                foreach (var button in row.Children.OfType<Button>())
                {
                    button.IsEnabled = idle;
                }
            }
        }

        private async Task DeleteAsync()
        {
            if (_busy || _finished || _pending is not { } target)
            {
                return;
            }

            _busy = true;
            _message.IsVisible = false;
            Refresh();
            var removed = false;
            try
            {
                await _library.RemoveAsync(target.Id);
                removed = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The disk or the folder; the text says nothing was changed and names no path.
                _message.Text = PdfStrings.SignaturePickerDeleteFailed;
                _message.IsVisible = true;
            }
            finally
            {
                _busy = false;
            }

            if (_finished)
            {
                return;
            }

            if (removed)
            {
                _items.RemoveAll(i => i.Id == target.Id);
                Rebuild();
            }

            _pending = null;
            Refresh();
            FocusStart();
        }

        // A row that was removed, a button that was disabled or a confirmation that went away takes the focus with it, and Esc only reaches the card from inside it.
        private void FocusStart()
        {
            var first = _list.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEnabled && b.Name?.StartsWith("PART_Pick_", StringComparison.Ordinal) == true);
            (first ?? _new).Focus();
        }

        private void Finish(PickerResult result)
        {
            if (_finished || _busy)
            {
                return;
            }

            _finished = true;
            DisposeThumbnails();
            _done(result);
        }

        private void DisposeThumbnails()
        {
            foreach (var thumbnail in _thumbnails)
            {
                thumbnail.Dispose();
            }

            _thumbnails.Clear();
        }
    }
}
