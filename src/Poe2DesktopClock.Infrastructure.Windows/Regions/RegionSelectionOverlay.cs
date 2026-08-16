using System.Drawing;
using System.Windows.Forms;
using Poe2DeskTracker.Interop;

namespace Poe2DeskTracker.Regions;

internal sealed class RegionSelectionOverlay : Form
{
    private const int WmEraseBkgnd = 0x0014;

    private readonly nint _gameWindowHandle;
    private readonly string _regionName;
    private readonly System.Windows.Forms.Timer _windowTracker;
    private Point? _selectionStart;
    private Rectangle _selection;

    private RegionSelectionOverlay(nint gameWindowHandle, string regionName)
    {
        _gameWindowHandle = gameWindowHandle;
        _regionName = regionName;
        _windowTracker = new System.Windows.Forms.Timer { Interval = 50 };
        _windowTracker.Tick += (_, _) => FollowGameWindow();

        AutoScaleMode = AutoScaleMode.None;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        UpdateStyles();
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        Opacity = 0.35;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        if (!TryGetGameBounds(out var bounds))
        {
            throw new InvalidOperationException("The Path of Exile 2 client area is no longer available.");
        }

        Bounds = bounds;
    }

    internal RegionDefinition? SelectedRegion { get; private set; }

    internal static Task<RegionDefinition?> SelectAsync(nint gameWindowHandle, string regionName)
    {
        var completion = new TaskCompletionSource<RegionDefinition?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectionThread = new Thread(() =>
        {
            try
            {
                WindowsFormsRuntime.EnsureHighDpiMode();
                using var overlay = new RegionSelectionOverlay(gameWindowHandle, regionName);
                overlay.ShowDialog();
                completion.TrySetResult(overlay.SelectedRegion);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "PoE 2 region selector",
        };

        selectionThread.SetApartmentState(ApartmentState.STA);
        selectionThread.Start();
        return completion.Task;
    }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        _windowTracker.Start();
        Activate();
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _windowTracker.Stop();
        _windowTracker.Dispose();
        base.OnFormClosed(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            _selectionStart = eventArgs.Location;
            UpdateSelection(Rectangle.Empty);
        }
        else if (eventArgs.Button == MouseButtons.Right)
        {
            _selectionStart = null;
            UpdateSelection(Rectangle.Empty);
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        if (_selectionStart is not { } start || (eventArgs.Button & MouseButtons.Left) == 0)
        {
            return;
        }

        UpdateSelection(CreateRectangle(start, eventArgs.Location));
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left && _selectionStart is { } start)
        {
            UpdateSelection(CreateRectangle(start, eventArgs.Location));
            _selectionStart = null;
        }
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            Close();
            return;
        }

        if (eventArgs.KeyCode == Keys.Enter && _selection.Width >= 4 && _selection.Height >= 4)
        {
            SelectedRegion = new RegionDefinition(
                _regionName,
                (double)_selection.Left / ClientSize.Width,
                (double)_selection.Top / ClientSize.Height,
                (double)_selection.Width / ClientSize.Width,
                (double)_selection.Height / ClientSize.Height,
                ClientSize.Width,
                ClientSize.Height);
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);

        const string instructions = "Drag to select a region · Enter saves · Esc cancels · Right-click resets";
        using var instructionBackground = new SolidBrush(Color.FromArgb(180, Color.Black));
        eventArgs.Graphics.FillRectangle(instructionBackground, 0, 0, ClientSize.Width, 38);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            $"{_regionName}: {instructions}",
            Font,
            new Point(12, 10),
            Color.White);

        if (_selection.IsEmpty)
        {
            return;
        }

        using var fill = new SolidBrush(Color.FromArgb(70, Color.Lime));
        using var border = new Pen(Color.Lime, 3);
        eventArgs.Graphics.FillRectangle(fill, _selection);
        eventArgs.Graphics.DrawRectangle(border, _selection);

        var sizeLabel = $"{_selection.Width} × {_selection.Height}";
        var labelSize = TextRenderer.MeasureText(sizeLabel, Font);
        using var labelBackground = new SolidBrush(Color.FromArgb(220, Color.Black));
        eventArgs.Graphics.FillRectangle(labelBackground, _selection.Left, _selection.Top - labelSize.Height, labelSize.Width + 8, labelSize.Height);
        TextRenderer.DrawText(eventArgs.Graphics, sizeLabel, Font, new Point(_selection.Left + 4, _selection.Top - labelSize.Height), Color.Lime);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmEraseBkgnd)
        {
            message.Result = 1;
            return;
        }

        base.WndProc(ref message);
    }

    private void FollowGameWindow()
    {
        if (!TryGetGameBounds(out var bounds))
        {
            Close();
            return;
        }

        if (bounds.Location != Bounds.Location)
        {
            Location = bounds.Location;
        }

        if (bounds.Size != ClientSize)
        {
            ScaleSelection(ClientSize, bounds.Size);
            Size = bounds.Size;
            Invalidate();
        }
    }

    private bool TryGetGameBounds(out Rectangle bounds)
    {
        if (Win32Native.IsIconic(_gameWindowHandle) || !Win32Native.TryGetClientBoundsOnScreen(_gameWindowHandle, out var left, out var top, out var width, out var height))
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = new Rectangle(left, top, width, height);
        return true;
    }

    private void ScaleSelection(Size oldSize, Size newSize)
    {
        if (_selection.IsEmpty || oldSize.Width == 0 || oldSize.Height == 0)
        {
            return;
        }

        _selection = Rectangle.FromLTRB(
            (int)Math.Round((double)_selection.Left * newSize.Width / oldSize.Width),
            (int)Math.Round((double)_selection.Top * newSize.Height / oldSize.Height),
            (int)Math.Round((double)_selection.Right * newSize.Width / oldSize.Width),
            (int)Math.Round((double)_selection.Bottom * newSize.Height / oldSize.Height));
    }

    private void UpdateSelection(Rectangle selection)
    {
        if (_selection == selection)
        {
            return;
        }

        var invalidatedArea = Rectangle.Union(GetSelectionPaintBounds(_selection), GetSelectionPaintBounds(selection));
        _selection = selection;

        if (invalidatedArea.IsEmpty)
        {
            return;
        }

        invalidatedArea.Inflate(5, 5);
        Invalidate(Rectangle.Intersect(ClientRectangle, invalidatedArea));
    }

    private Rectangle GetSelectionPaintBounds(Rectangle selection)
    {
        if (selection.IsEmpty)
        {
            return Rectangle.Empty;
        }

        var sizeLabel = $"{selection.Width} × {selection.Height}";
        var labelHeight = TextRenderer.MeasureText(sizeLabel, Font).Height;
        return Rectangle.FromLTRB(
            selection.Left,
            Math.Max(0, selection.Top - labelHeight),
            selection.Right,
            selection.Bottom);
    }

    private static Rectangle CreateRectangle(Point start, Point end)
    {
        return Rectangle.FromLTRB(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y));
    }
}
