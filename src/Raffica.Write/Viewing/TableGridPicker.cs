using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Write.Commands;

namespace Raffica.Write.Viewing;

/// <summary>
/// The grid of Insert table: ten columns by eight rows of cells, the cells from the top left corner to the pointer (or to the arrow keys' choice) lit, the size in words under it ("3 by 4 table", the same words a
/// screen reader gets for each cell) and a "More…" button for the card of any size up to 63 by 500. It is the content of the popup the window opens under the Insert button. A click on a cell or Enter chooses the lit
/// size, Esc closes, the arrow keys move the choice (never beyond the grid). The shell's picker (<c>RibbonPicker</c>) is a list and cannot show a hover grid, so this is the smallest control that does, kept in the app;
/// it is made the first time the popup opens and never at start (<see cref="WriteWindow.TableGridBuilds"/>).
/// </summary>
internal sealed class TableGridPicker : Border
{
    /// <summary>The columns the grid offers.</summary>
    internal const int GridColumns = 10;

    /// <summary>The rows the grid offers.</summary>
    internal const int GridRows = 8;

    private const double CellSize = 20;
    private const double Gap = 2;

    private readonly Border[,] _cells = new Border[GridRows, GridColumns];
    private readonly Grid _grid;
    private readonly TextBlock _status;
    private readonly Button _more;
    private readonly Action<TableSize> _chosen;
    private readonly Action _close;
    private int _column = 1;
    private int _row = 1;

    /// <param name="chosen">Called with the size of the lit cells when a cell is clicked or Enter is pressed.</param>
    /// <param name="more">Called when "More…" is pressed (the window opens the card).</param>
    /// <param name="close">Called when Esc is pressed (the window closes the popup).</param>
    public TableGridPicker(Action<TableSize> chosen, Action more, Action close)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(more);
        ArgumentNullException.ThrowIfNull(close);
        _chosen = chosen;
        _close = close;
        Focusable = true;
        Padding = new Thickness(8);
        AutomationProperties.SetName(this, WriteStrings.TableGridPopupName);
        AutomationProperties.SetHelpText(this, WriteStrings.TableGridHint);

        _grid = new Grid { Name = "PART_Grid", ColumnSpacing = Gap, RowSpacing = Gap, HorizontalAlignment = HorizontalAlignment.Left };
        for (var c = 0; c < GridColumns; c++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(CellSize, GridUnitType.Pixel));
        }

        for (var r = 0; r < GridRows; r++)
        {
            _grid.RowDefinitions.Add(new RowDefinition(CellSize, GridUnitType.Pixel));
            for (var c = 0; c < GridColumns; c++)
            {
                var cell = new Border { Name = $"PART_Cell_{c + 1}_{r + 1}", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), IsHitTestVisible = true };
                AutomationProperties.SetName(cell, WriteStrings.Fill(WriteStrings.TableGridName, c + 1, r + 1));
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                _cells[r, c] = cell;
                _grid.Children.Add(cell);
            }
        }

        _status = new TextBlock { Name = "PART_Status", Margin = new Thickness(0, 6, 0, 6) };
        _more = new Button { Name = "PART_More", Content = WriteStrings.TableGridMore, HorizontalAlignment = HorizontalAlignment.Left };
        _more.Classes.Add("boxed");
        _more.Click += (_, _) => more();
        Child = new StackPanel { Children = { _grid, _status, _more } };

        _grid.PointerMoved += OnPointerMoved;
        _grid.PointerPressed += OnPointerPressed;
        AttachedToVisualTree += (_, _) => Repaint();
        ActualThemeVariantChanged += (_, _) => Repaint();
        Repaint();
    }

    /// <summary>The columns of the lit size (1 to <see cref="GridColumns"/>).</summary>
    public int Columns => _column;

    /// <summary>The rows of the lit size (1 to <see cref="GridRows"/>).</summary>
    public int Rows => _row;

    /// <summary>The words of the lit size ("3 by 4 table"), what the status line and a screen reader say.</summary>
    public string SizeText => WriteStrings.Fill(WriteStrings.TableGridName, _column, _row);

    /// <summary>The "More…" button (tests).</summary>
    internal Button MoreButton => _more;

    /// <summary>Lights a size (clamped into the grid) and says it.</summary>
    public void Light(int columns, int rows)
    {
        _column = Math.Clamp(columns, 1, GridColumns);
        _row = Math.Clamp(rows, 1, GridRows);
        Repaint();
    }

    /// <summary>Puts the choice back to the smallest size and takes the focus (the popup calls it each time it opens).</summary>
    public void Reset()
    {
        Light(1, 1);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e.Key)
        {
            case Key.Right:
                Light(_column + 1, _row);
                break;
            case Key.Left:
                Light(_column - 1, _row);
                break;
            case Key.Down:
                Light(_column, _row + 1);
                break;
            case Key.Up:
                Light(_column, _row - 1);
                break;
            case Key.Enter or Key.Space:
                _chosen(new TableSize(_column, _row));
                break;
            case Key.Escape:
                _close();
                break;
            default:
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var (column, row) = CellAt(e.GetPosition(_grid));
        Light(column, row);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_grid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var (column, row) = CellAt(e.GetPosition(_grid));
        Light(column, row);
        _chosen(new TableSize(_column, _row));
        e.Handled = true;
    }

    // The cell under a point of the grid (the gap counts to the cell before it), at least the first and at most the last.
    private static (int Column, int Row) CellAt(Point point) =>
        (Math.Clamp((int)Math.Floor(point.X / (CellSize + Gap)) + 1, 1, GridColumns), Math.Clamp((int)Math.Floor(point.Y / (CellSize + Gap)) + 1, 1, GridRows));

    private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

    private void Repaint()
    {
        var lit = Brush("Raffica.Brush.Selection.Tint", Brushes.LightSteelBlue);
        var plain = Brush("Raffica.Brush.Surface.Page", Brushes.White);
        var line = Brush("Raffica.Brush.Border.Control", Brushes.Gray);
        var ring = Brush("Raffica.Brush.Focus.Ring", Brushes.DodgerBlue);
        for (var r = 0; r < GridRows; r++)
        {
            for (var c = 0; c < GridColumns; c++)
            {
                var inside = c < _column && r < _row;
                _cells[r, c].Background = inside ? lit : plain;
                _cells[r, c].BorderBrush = inside ? ring : line;
            }
        }

        _status.Text = SizeText;
        AutomationProperties.SetItemStatus(this, SizeText);
    }
}
