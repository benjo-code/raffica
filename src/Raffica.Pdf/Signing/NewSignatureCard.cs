using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Annotations;
using Raffica.Core.Signatures;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Signing;

/// <summary>
/// The card that makes a new signature for the library: a "Draw" tab with a <see cref="SignaturePad"/> and an "Image" tab with a file picker
/// and a cutout slider, a preview on a checkerboard, a name and Save / Cancel. The two sentences of the hint (a signature image replaces no
/// handwritten signature where written form is required, and is no digital signature) sit under the tabs and are visible in both. Every
/// refusal (the library is full, a file that is too large, too big in pixels, unreadable or empty, a cutout of an extreme aspect ratio, a
/// failing picker or disk) ends in a live reason inside the card, stores nothing and leaves the card usable; no text shows the path of the
/// picked file. The drawn aspect ratio never needs a refusal: Core pads a degenerate drawing (a single straight line) to its extreme aspect.
/// Esc and Cancel store nothing. Used on the UI thread only; every library call is awaited.
/// </summary>
internal static class NewSignatureCard
{
    private const double MinAspect = 0.05;
    private const double MaxAspect = 20;

    /// <param name="library">Where the signature is saved.</param>
    /// <param name="pickImage">Lets the user pick an image file and gives its stream (<see langword="null"/> when nothing was picked); the card disposes the stream.</param>
    /// <param name="done">The saved signature, or <see langword="null"/> when the card was cancelled.</param>
    public static Dialog Create(SignatureLibrary library, Func<Task<Stream?>> pickImage, Action<SavedSignature?> done)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(pickImage);
        ArgumentNullException.ThrowIfNull(done);
        return new Card(library, pickImage, done).Dialog;
    }

    private sealed class Card
    {
        private readonly SignatureLibrary _library;
        private readonly Func<Task<Stream?>> _pickImage;
        private readonly Action<SavedSignature?> _done;
        private readonly SegmentedControl _tabs;
        private readonly SignaturePad _pad;
        private readonly SignaturePreview _preview;
        private readonly TextBox _name;
        private readonly TextBlock _message;
        private readonly TextBlock _imageStatus;
        private readonly Slider _cutout;
        private readonly StackPanel _drawPanel;
        private readonly StackPanel _imagePanel;
        private readonly Button _back;
        private readonly Button _clear;
        private readonly Button _choose;
        private readonly Button _save;
        private readonly Button _cancel;
        private readonly List<(ToggleButton Button, DrawColor Color)> _swatches = [];
        private SignatureImage? _source;
        private SignatureImage? _trimmed;
        private string? _actionRefusal;
        private string? _imageRefusal;
        private int _version;
        private bool _finished;
        private bool _saving;
        private bool _recomputeRunning;
        private bool _recomputeAgain;
        private Task _recompute = Task.CompletedTask;
        private bool _picking;
        private bool _nameTouched;
        private bool _settingName;
        private bool _settingCutout;

        public Card(SignatureLibrary library, Func<Task<Stream?>> pickImage, Action<SavedSignature?> done)
        {
            _library = library;
            _pickImage = pickImage;
            _done = done;

            _tabs = new SegmentedControl
            {
                Name = "PART_Tabs",
                ItemsSource = new[] { PdfStrings.SignatureNewTabDraw, PdfStrings.SignatureNewTabImage },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(_tabs, PdfStrings.SignatureNewTabs);

            _pad = new SignaturePad { Name = "PART_Pad", Height = 112, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(_pad, PdfStrings.SignatureNewPadName);
            _back = TextButton("PART_Back", PdfStrings.SignatureNewBack, "boxed");
            ToolTip.SetTip(_back, PdfStrings.SignatureNewBackTip);
            AutomationProperties.SetHelpText(_back, PdfStrings.SignatureNewBackTip);
            _clear = TextButton("PART_Clear", PdfStrings.SignatureNewClear, "boxed");

            var ink = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var color in DrawPalette.All)
            {
                ink.Children.Add(Swatch(color));
            }

            var inkLabel = new TextBlock { Text = PdfStrings.SignatureNewInk, VerticalAlignment = VerticalAlignment.Center };
            inkLabel.Bind(TextBlock.ForegroundProperty, inkLabel.GetResourceObservable("Raffica.Brush.Text.Secondary"));
            var padButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _back, _clear } };
            var padRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
            padRow.Children.Add(inkLabel);
            Grid.SetColumn(ink, 1);
            padRow.Children.Add(ink);
            Grid.SetColumn(padButtons, 2);
            padRow.Children.Add(padButtons);
            _drawPanel = new StackPanel { Spacing = 8, Children = { _pad, padRow } };

            _choose = TextButton("PART_Choose", PdfStrings.SignatureNewChoose, "boxed");
            _imageStatus = new TextBlock { Name = "PART_ImageStatus", Text = PdfStrings.SignatureNewImageNone, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            _imageStatus.Bind(TextBlock.ForegroundProperty, _imageStatus.GetResourceObservable("Raffica.Brush.Text.Secondary"));
            var chooseRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            chooseRow.Children.Add(_choose);
            Grid.SetColumn(_imageStatus, 1);
            chooseRow.Children.Add(_imageStatus);

            _cutout = new Slider { Name = "PART_Cutout", Minimum = 0, Maximum = 255, Value = 128, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(_cutout, PdfStrings.SignatureNewCutout);
            ToolTip.SetTip(_cutout, PdfStrings.SignatureNewCutoutTip);
            var cutoutLabel = new TextBlock { Text = PdfStrings.SignatureNewCutout, VerticalAlignment = VerticalAlignment.Center };
            var cutoutRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            cutoutRow.Children.Add(cutoutLabel);
            Grid.SetColumn(_cutout, 1);
            cutoutRow.Children.Add(_cutout);
            _imagePanel = new StackPanel { Spacing = 8, IsVisible = false, Children = { chooseRow, cutoutRow } };

            _preview = new SignaturePreview { Name = "PART_Preview", Height = 64, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(_preview, PdfStrings.SignatureNewPreview);

            _name = new TextBox { Name = "PART_Name", Text = Numbered(1), MaxLength = SignatureStore.MaxNameLength, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(_name, PdfStrings.SignatureNewName);
            var nameLabel = new TextBlock { Text = PdfStrings.SignatureNewName, VerticalAlignment = VerticalAlignment.Center };
            var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            nameRow.Children.Add(nameLabel);
            Grid.SetColumn(_name, 1);
            nameRow.Children.Add(_name);

            _message = new TextBlock { Name = "PART_Message", IsVisible = false, TextWrapping = TextWrapping.Wrap };
            ValidationHint.Mark(_message);

            var hint = new TextBlock { Name = "PART_Hint", Text = PdfStrings.SignatureHintText(), TextWrapping = TextWrapping.Wrap };
            hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("Raffica.Brush.Text.Secondary"));

            _cancel = TextButton("PART_Cancel", ShellStrings.Cancel, "boxed");
            _save = TextButton("PART_Save", PdfStrings.SignatureNewSave, "primary");
            _save.IsEnabled = false;

            Dialog = new Dialog
            {
                Title = PdfStrings.SignatureNewTitle,
                Content = new StackPanel { Spacing = 10, Children = { _tabs, _drawPanel, _imagePanel, _preview, nameRow, _message, hint } },
                Footer = CardFooter.Build(_save, null, _cancel),
            };

            _tabs.SelectionChanged += (_, _) => OnTabChanged();
            _pad.Changed += OnPadChanged;
            _back.Click += (_, _) => _pad.Back();
            _clear.Click += (_, _) => _pad.Clear();
            _choose.Click += async (_, _) => await ChooseAsync();
            _cutout.ValueChanged += (_, _) =>
            {
                if (!_settingCutout)
                {
                    _actionRefusal = null;
                    _ = RecomputeAsync();
                }
            };
            _name.TextChanged += (_, _) => _nameTouched |= !_settingName;
            _save.Click += async (_, _) => await SaveAsync();
            _cancel.Click += (_, _) => Cancel();
            Dialog.CancelRequested += (_, _) => Cancel();

            Refresh();
            _ = SuggestNameAsync();
        }

        public Dialog Dialog { get; }

        private bool IsDrawTab => _tabs.SelectedIndex == 0;

        private static string Numbered(int number) => PdfStrings.Fill(PdfStrings.SignatureNewDefaultNameFormat, number);

        private static Button TextButton(string name, string text, string cssClass)
        {
            var button = new Button { Name = name, Content = text };
            button.Classes.Add(cssClass);
            AutomationProperties.SetName(button, text);
            return button;
        }

        private ToggleButton Swatch(DrawColor color)
        {
            var rgb = DrawPalette.ColorOf(color);
            var name = color switch
            {
                DrawColor.Red => PdfStrings.DrawColorRed,
                DrawColor.Black => PdfStrings.DrawColorBlack,
                DrawColor.Blue => PdfStrings.DrawColorBlue,
                DrawColor.Green => PdfStrings.DrawColorGreen,
                _ => PdfStrings.DrawColorOrange,
            };
            var chip = new Border { Width = 16, Height = 16, Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B)), Classes = { "mark-swatch" } };
            var button = new ToggleButton { Name = "PART_Ink_" + color, Content = chip, Padding = new Thickness(0), Width = 26, IsChecked = color == _pad.Color };
            AutomationProperties.SetName(button, name);
            ToolTip.SetTip(button, name);
            button.Click += (_, _) =>
            {
                _pad.Color = color;
                foreach (var (other, otherColor) in _swatches)
                {
                    other.IsChecked = otherColor == color;
                }
            };
            _swatches.Add((button, color));
            return button;
        }

        /// <summary>Numbers the default name after the signatures already saved; a name the user typed meanwhile stays.</summary>
        private async Task SuggestNameAsync()
        {
            try
            {
                var items = await _library.LoadAsync();
                if (_finished || _nameTouched)
                {
                    return;
                }

                var taken = items.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
                var number = 1;
                while (taken.Contains(Numbered(number)))
                {
                    number++;
                }

                _settingName = true;
                _name.Text = Numbered(number);
                _settingName = false;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The default name stays; a library that cannot be read shows its trouble when saving.
                _settingName = false;
            }
        }

        private void OnTabChanged()
        {
            _actionRefusal = null;
            Refresh();
        }

        private void OnPadChanged()
        {
            _actionRefusal = null;
            Refresh();
        }

        /// <summary>The Choose button: picks, checks and decodes the file; the first cutout starts at the suggested threshold.</summary>
        private async Task ChooseAsync()
        {
            if (_picking || _saving)
            {
                return;
            }

            _picking = true;
            try
            {
                _actionRefusal = null;
                Stream? stream;
                try
                {
                    stream = await _pickImage();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    RefusePick(PdfStrings.SignatureNewRefusalUnreadable);
                    return;
                }

                if (stream is null)
                {
                    return;
                }

                if (_finished)
                {
                    await stream.DisposeAsync(); // the card closed while the file dialog was open
                    return;
                }

                SignatureImageResult result;
                try
                {
                    result = await SignatureImageCodec.DecodeAsync(stream);
                }
                finally
                {
                    await stream.DisposeAsync();
                }

                if (_finished)
                {
                    return;
                }

                if (result is SignatureImageResult.Problem problem)
                {
                    RefusePick(problem.Reason switch
                    {
                        SignatureImageProblem.TooLarge => PdfStrings.SignatureNewRefusalTooLarge,
                        SignatureImageProblem.TooManyPixels => PdfStrings.SignatureNewRefusalTooManyPixels,
                        SignatureImageProblem.Empty => PdfStrings.SignatureNewRefusalEmpty,
                        _ => PdfStrings.SignatureNewRefusalUnreadable,
                    });
                    return;
                }

                _source = ((SignatureImageResult.Ok)result).Image;
                _imageStatus.Text = PdfStrings.SignatureNewImageLoaded;
                _settingCutout = true;
                _cutout.Value = SignatureCutout.AutoThreshold(_source);
                _settingCutout = false;
                _cutout.IsEnabled = true;
                await RecomputeAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Never lets a failure of the picker or the decoder escape a click handler.
                RefusePick(PdfStrings.SignatureNewRefusalUnreadable);
            }
            finally
            {
                _picking = false;
            }
        }

        /// <summary>Makes the cutout of the loaded image at the slider's threshold off the UI thread; only the newest request is shown.</summary>
        private Task RecomputeAsync()
        {
            // One running task: a request that comes while it works only asks it to go round once more with the newest slider value.
            _recomputeAgain = true;
            if (!_recomputeRunning)
            {
                _recomputeRunning = true;
                _recompute = RunRecomputeAsync();
            }

            return _recompute;
        }

        private async Task RunRecomputeAsync()
        {
            try
            {
                while (_recomputeAgain && !_finished)
                {
                    _recomputeAgain = false;
                    var source = _source;
                    if (source is null)
                    {
                        continue;
                    }

                    var version = _version;
                    var threshold = (int)Math.Round(_cutout.Value);
                    var trimmed = await Task.Run(() => SignatureCutout.TrimToContent(SignatureCutout.Apply(source, threshold)));
                    if (_recomputeAgain || version != _version || _finished)
                    {
                        continue; // a newer request, a new or cleared image, or a closed card: this result is stale
                    }

                    _trimmed = trimmed;
                    Refresh();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The cutout could not be made: say so (nothing is stored) rather than leave a failed task unobserved.
                RefusePick(PdfStrings.SignatureNewRefusalUnreadable);
            }
            finally
            {
                _recomputeRunning = false;
            }
        }

        /// <summary>A refused file: its reason is shown and the image of an earlier pick is dropped, so the reason matches what Save would store.</summary>
        private void RefusePick(string reason)
        {
            _version++;
            _source = null;
            _trimmed = null;
            _imageRefusal = null;
            _cutout.IsEnabled = false;
            _imageStatus.Text = PdfStrings.SignatureNewImageNone;
            Refuse(reason);
        }

        /// <summary>Cancel, Esc and the close button: while a save runs they do nothing (the save is reported when it ends), so no stored signature is ever left unreported.</summary>
        private void Cancel()
        {
            if (!_saving)
            {
                Finish(null);
            }
        }

        /// <summary>The reason the current cutout cannot be saved, or <see langword="null"/> when it can.</summary>
        private string? CutoutRefusal()
        {
            if (_trimmed is null)
            {
                return null;
            }

            if (_trimmed.IsEmpty)
            {
                return PdfStrings.SignatureNewRefusalEmpty;
            }

            var aspect = (double)_trimmed.Width / _trimmed.Height;
            return aspect is < MinAspect or > MaxAspect ? PdfStrings.SignatureNewRefusalAspect : null;
        }

        private bool CanSave() => !_saving && (IsDrawTab ? _pad.HasInk : _trimmed is not null && CutoutRefusal() is null);

        private void Refuse(string reason)
        {
            _actionRefusal = reason;
            Refresh();
        }

        /// <summary>Brings panels, buttons, preview and the live reason in line with the state.</summary>
        private void Refresh()
        {
            var draw = IsDrawTab;
            _drawPanel.IsVisible = draw;
            _imagePanel.IsVisible = !draw;
            if (draw)
            {
                _preview.ShowStrokes(_pad.Strokes, _pad.Color);
            }
            else
            {
                _imageRefusal = CutoutRefusal();
                _preview.ShowImage(_trimmed);
            }

            _back.IsEnabled = _clear.IsEnabled = _pad.HasInk && !_saving;
            _choose.IsEnabled = !_saving;
            _cancel.IsEnabled = !_saving;
            _save.IsEnabled = CanSave();
            var reason = _actionRefusal ?? (draw ? null : _imageRefusal);
            _message.Text = reason ?? string.Empty;
            _message.IsVisible = reason is not null;
        }

        private async Task SaveAsync()
        {
            if (!CanSave())
            {
                return;
            }

            _saving = true;
            _actionRefusal = null;
            Refresh();
            SavedSignature? saved = null;
            try
            {
                var name = _name.Text ?? string.Empty;
                saved = IsDrawTab
                    ? await _library.AddDrawnAsync(name, _pad.Strokes, _pad.Color)
                    : await _library.AddImageAsync(name, _trimmed!);
            }
            catch (SignatureLimitException)
            {
                _actionRefusal = PdfStrings.SignatureNewRefusalLimit;
            }
            catch (ArgumentException)
            {
                _actionRefusal = IsDrawTab ? PdfStrings.SignatureNewRefusalUnusable : PdfStrings.SignatureNewRefusalAspect;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The disk or the folder; the text says nothing was changed and carries no path.
                _actionRefusal = PdfStrings.SignatureNewRefusalWriteFailed;
            }
            finally
            {
                _saving = false;
            }

            if (saved is not null)
            {
                Finish(saved);
                return;
            }

            Refresh();
        }

        private void Finish(SavedSignature? saved)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _preview.ShowStrokes(null, DrawColor.Black); // frees the bitmap of the preview
            _version++;
            _done(saved);
        }
    }
}
