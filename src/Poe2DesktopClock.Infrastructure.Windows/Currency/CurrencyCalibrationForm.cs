using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Poe2DeskTracker.Interop;

namespace Poe2DeskTracker.Currency;

internal sealed class CurrencyCalibrationForm : Form
{
    private const int HeaderHeight = 40;
    private const int LabelLineHeight = 16;
    private const int MaximumWrappedLabelHeight = LabelLineHeight * 2;
    private const int LabelScrollIntervalMilliseconds = 40;
    private const float LabelScrollPixelsPerTick = 0.7F;
    private const int LabelScrollPauseTicks = 25;
    private readonly Bitmap _preview;
    private readonly List<SlotState> _slots;
    private readonly System.Windows.Forms.Timer _labelScrollTimer;
    private readonly StringFormat _wrappedLabelFormat = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.LineLimit,
    };
    private readonly StringFormat _marqueeLabelFormat = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit,
    };
    private int _hoveredSlotIndex = -1;
    private int _draggedSlotIndex = -1;
    private Point _dragOffset;

    private CurrencyCalibrationForm(string previewPath, IReadOnlyList<DetectedCurrencySlot> detectedSlots, CurrencyLayout? previousLayout)
    {
        _preview = new Bitmap(previewPath);
        _slots = detectedSlots.Select(slot => new SlotState(slot)).ToList();
        ApplySavedNames(previousLayout);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
        UpdateStyles();
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(_preview.Width, _preview.Height + HeaderHeight);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        KeyPreview = true;
        MaximizeBox = false;
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Калибровка валютной вкладки";
        TopMost = true;

        UpdateLabelWidths();
        _labelScrollTimer = new System.Windows.Forms.Timer
        {
            Interval = LabelScrollIntervalMilliseconds,
        };
        _labelScrollTimer.Tick += OnLabelScrollTimerTick;
        _labelScrollTimer.Start();
    }

    internal CurrencyLayout? CalibrationLayout { get; private set; }

    internal static Task<CurrencyLayout?> CalibrateAsync(
        string previewPath,
        string regionName,
        IReadOnlyList<DetectedCurrencySlot> detectedSlots,
        CurrencyLayout? previousLayout)
    {
        var completion = new TaskCompletionSource<CurrencyLayout?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calibrationThread = new Thread(() =>
        {
            try
            {
                WindowsFormsRuntime.EnsureHighDpiMode();
                using var form = new CurrencyCalibrationForm(previewPath, detectedSlots, previousLayout);
                form.ShowDialog();
                completion.TrySetResult(form.CalibrationLayout is null ? null : form.CalibrationLayout with { RegionName = regionName });
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "PoE 2 currency calibration",
        };

        calibrationThread.SetApartmentState(ApartmentState.STA);
        calibrationThread.Start();
        return completion.Task;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);

        eventArgs.Graphics.FillRectangle(Brushes.Black, ClientRectangle);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            "ЛКМ: перетащить рамку · Enter: сохранить · Esc: отмена",
            Font,
            new Point(10, 12),
            Color.White);
        eventArgs.Graphics.DrawImageUnscaled(_preview, 0, HeaderHeight);

        for (var index = 0; index < _slots.Count; index++)
        {
            var slot = _slots[index];
            var bounds = slot.Bounds;
            bounds.Offset(0, HeaderHeight);
            var color = Color.Lime;
            if (index == _hoveredSlotIndex)
            {
                color = Color.Yellow;
            }

            using var border = new Pen(color, index == _hoveredSlotIndex ? 3 : 2);
            using var fill = new SolidBrush(Color.FromArgb(45, color));
            eventArgs.Graphics.FillRectangle(fill, bounds);
            eventArgs.Graphics.DrawRectangle(border, bounds);

            if (!string.IsNullOrWhiteSpace(slot.Name))
            {
                var labelBounds = new Rectangle(
                    bounds.Left + 2,
                    bounds.Top + 2,
                    Math.Max(1, bounds.Width - 4),
                    Math.Min(slot.LabelHeight, bounds.Height - 4));
                using var labelBackground = new SolidBrush(Color.FromArgb(210, Color.Black));
                eventArgs.Graphics.FillRectangle(labelBackground, labelBounds);
                var graphicsState = eventArgs.Graphics.Save();
                // This is the mask for the marquee: glyphs beyond the frame are
                // never painted over its neighbours or the captured game image.
                eventArgs.Graphics.SetClip(labelBounds, CombineMode.Replace);
                using var labelBrush = new SolidBrush(color);
                if (slot.WrapsInTwoLines)
                {
                    eventArgs.Graphics.DrawString(
                        slot.Name,
                        Font,
                        labelBrush,
                        labelBounds,
                        _wrappedLabelFormat);
                }
                else
                {
                    var textBounds = new RectangleF(
                        labelBounds.Left - slot.LabelScrollOffset,
                        labelBounds.Top + 1,
                        Math.Max(slot.LabelWidth, labelBounds.Width),
                        labelBounds.Height);
                    eventArgs.Graphics.DrawString(slot.Name, Font, labelBrush, textBounds, _marqueeLabelFormat);
                }

                eventArgs.Graphics.Restore(graphicsState);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        if (_draggedSlotIndex >= 0)
        {
            MoveSlot(_draggedSlotIndex, ToImagePosition(eventArgs.Location));
            _hoveredSlotIndex = _draggedSlotIndex;
            Invalidate();
            return;
        }

        var newHoveredSlotIndex = FindSlot(eventArgs.Location);
        if (newHoveredSlotIndex == _hoveredSlotIndex)
        {
            return;
        }

        _hoveredSlotIndex = newHoveredSlotIndex;
        Cursor = _hoveredSlotIndex >= 0 ? Cursors.SizeAll : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        if (_hoveredSlotIndex >= 0)
        {
            _hoveredSlotIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            var slotIndex = FindSlot(eventArgs.Location);
            if (slotIndex >= 0)
            {
                _draggedSlotIndex = slotIndex;
                var imagePosition = ToImagePosition(eventArgs.Location);
                var bounds = _slots[slotIndex].Bounds;
                _dragOffset = new Point(imagePosition.X - bounds.Left, imagePosition.Y - bounds.Top);
                Capture = true;
                return;
            }
        }

        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left && _draggedSlotIndex >= 0)
        {
            _draggedSlotIndex = -1;
            Capture = false;
            return;
        }

        base.OnMouseUp(eventArgs);
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            Close();
            return;
        }

        if (eventArgs.KeyCode == Keys.Enter)
        {
            CalibrationLayout = new CurrencyLayout(
                RegionName: string.Empty,
                ReferenceWidth: _preview.Width,
                ReferenceHeight: _preview.Height,
                Slots: _slots
                    .OrderBy(slot => slot.Bounds.Top)
                    .ThenBy(slot => slot.Bounds.Left)
                    .Select((slot, index) => new CurrencySlotDefinition(
                    Id: $"slot-{index + 1:D2}",
                    X: (double)slot.Bounds.Left / _preview.Width,
                    Y: (double)slot.Bounds.Top / _preview.Height,
                    Width: (double)slot.Bounds.Width / _preview.Width,
                    Height: (double)slot.Bounds.Height / _preview.Height,
                    Confidence: slot.Confidence,
                    Name: string.IsNullOrWhiteSpace(slot.Name) ? null : slot.Name)).ToList());
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelScrollTimer.Dispose();
            _wrappedLabelFormat.Dispose();
            _marqueeLabelFormat.Dispose();
            _preview.Dispose();
        }

        base.Dispose(disposing);
    }

    private int FindSlot(Point cursorPosition)
    {
        var imagePosition = ToImagePosition(cursorPosition);
        return _slots.FindIndex(slot => slot.Bounds.Contains(imagePosition));
    }

    private Point ToImagePosition(Point windowPosition) => new(windowPosition.X, windowPosition.Y - HeaderHeight);

    private void MoveSlot(int slotIndex, Point imagePosition)
    {
        var slot = _slots[slotIndex];
        var maximumX = Math.Max(0, _preview.Width - slot.Bounds.Width);
        var maximumY = Math.Max(0, _preview.Height - slot.Bounds.Height);
        var left = Math.Clamp(imagePosition.X - _dragOffset.X, 0, maximumX);
        var top = Math.Clamp(imagePosition.Y - _dragOffset.Y, 0, maximumY);
        slot.Bounds = new Rectangle(left, top, slot.Bounds.Width, slot.Bounds.Height);
    }

    private void ApplySavedNames(CurrencyLayout? previousLayout)
    {
        if (previousLayout is null)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            var centerX = slot.Bounds.Left + slot.Bounds.Width / 2.0;
            var centerY = slot.Bounds.Top + slot.Bounds.Height / 2.0;
            var closest = previousLayout.Slots
                .Where(saved => !string.IsNullOrWhiteSpace(saved.Name) && !CurrencyTabProfile.IsAutomaticName(saved.Name))
                .Select(saved => new
                {
                    Saved = saved,
                    Distance = Math.Sqrt(
                        Math.Pow(saved.X * _preview.Width + saved.Width * _preview.Width / 2 - centerX, 2) +
                        Math.Pow(saved.Y * _preview.Height + saved.Height * _preview.Height / 2 - centerY, 2)),
                })
                .OrderBy(match => match.Distance)
                .FirstOrDefault();

            if (closest is not null && closest.Distance <= Math.Max(slot.Bounds.Width, slot.Bounds.Height) * 0.75)
            {
                slot.Name = closest.Saved.Name;
            }
        }
    }

    private void UpdateLabelWidths()
    {
        using var measuringSurface = new Bitmap(1, 1);
        using var measuringGraphics = Graphics.FromImage(measuringSurface);
        foreach (var slot in _slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Name))
            {
                slot.LabelWidth = 0;
                slot.LabelHeight = LabelLineHeight;
                slot.WrapsInTwoLines = true;
                continue;
            }

            var availableWidth = Math.Max(1, slot.Bounds.Width - 4);
            slot.LabelWidth = (int)Math.Ceiling(measuringGraphics.MeasureString(
                slot.Name,
                Font,
                int.MaxValue,
                _marqueeLabelFormat).Width);
            var wrappedSize = measuringGraphics.MeasureString(
                slot.Name,
                Font,
                new SizeF(availableWidth, 1000),
                _wrappedLabelFormat);
            slot.WrapsInTwoLines = wrappedSize.Height <= MaximumWrappedLabelHeight;
            slot.LabelHeight = slot.WrapsInTwoLines
                ? Math.Min(MaximumWrappedLabelHeight, Math.Max(LabelLineHeight, (int)Math.Ceiling(wrappedSize.Height)))
                : LabelLineHeight;
        }
    }

    private void OnLabelScrollTimerTick(object? sender, EventArgs eventArgs)
    {
        var changed = false;
        foreach (var slot in _slots)
        {
            var availableWidth = Math.Max(1, slot.Bounds.Width - 4);
            var maximumOffset = Math.Max(0, slot.LabelWidth - availableWidth);
            if (slot.WrapsInTwoLines || maximumOffset == 0)
            {
                continue;
            }

            if (slot.LabelPauseTicks > 0)
            {
                slot.LabelPauseTicks--;
                if (slot.ResetScrollAfterPause && slot.LabelPauseTicks == 0)
                {
                    slot.LabelScrollOffset = 0;
                    slot.ResetScrollAfterPause = false;
                    slot.LabelPauseTicks = LabelScrollPauseTicks;
                    changed = true;
                }

                continue;
            }

            var nextOffset = Math.Min(maximumOffset, slot.LabelScrollOffset + LabelScrollPixelsPerTick);
            if (nextOffset == slot.LabelScrollOffset)
            {
                slot.LabelPauseTicks = LabelScrollPauseTicks;
                slot.ResetScrollAfterPause = true;
                continue;
            }

            slot.LabelScrollOffset = nextOffset;
            changed = true;
            if (slot.LabelScrollOffset >= maximumOffset)
            {
                slot.LabelPauseTicks = LabelScrollPauseTicks;
                slot.ResetScrollAfterPause = true;
            }
        }

        if (changed)
        {
            Invalidate();
        }
    }

    private sealed class SlotState(DetectedCurrencySlot detected)
    {
        internal Rectangle Bounds { get; set; } = detected.Bounds;
        internal double Confidence { get; } = detected.Confidence;
        internal string? Name { get; set; } = detected.Name;
        internal int LabelWidth { get; set; }
        internal int LabelHeight { get; set; }
        internal bool WrapsInTwoLines { get; set; }
        internal float LabelScrollOffset { get; set; }
        internal int LabelPauseTicks { get; set; } = LabelScrollPauseTicks;
        internal bool ResetScrollAfterPause { get; set; }
    }
}
