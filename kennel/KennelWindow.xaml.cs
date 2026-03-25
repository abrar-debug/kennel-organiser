using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Specialized;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace kennel;

public partial class KennelWindow : Window
{
    public static double CollapsedWidth { get; } = 96;
    public static double CollapsedHeight { get; } = 96;
    public static double ExpandedWidth { get; } = 260;
    // Keep it rectangular/side-ish: fixed height, grow width only as needed.
    // Higher than before so we don't force everything into a single long row.
    public static double ExpandedHeight { get; } = 170;

    private readonly KennelDefinition _kennel;
    private readonly KennelStorage _storage;

    public event EventHandler<KennelDefinition>? KennelUpdated;

    private bool _isCollapsed = true;
    private bool _headerMouseDown;
    private Point _headerDownPos;
    private bool _headerIsDragging;

    private readonly Dictionary<string, System.Windows.Media.ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public KennelWindow(KennelDefinition kennel, KennelStorage storage)
    {
        _kennel = kennel;
        _storage = storage;

        InitializeComponent();

        KennelNameText.Text = _kennel.Name;
        RefreshList();
        SetCollapsed(true);
    }

    private sealed class ShortcutItem
    {
        public required string DisplayName { get; init; }
        public ImageSource? Icon { get; init; }
        public required ShortcutDefinition Definition { get; init; }
    }

    private void RefreshList()
    {
        ShortcutsList.ItemsSource = BuildShortcutItems();

        EmptyHintText.Visibility = _isCollapsed
            ? Visibility.Collapsed
            : (_kennel.Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed);

        if (!_isCollapsed)
            AdjustExpandedWidthToContent();
    }

    private IEnumerable<ShortcutItem> BuildShortcutItems()
    {
        foreach (var shortcut in _kennel.Shortcuts)
        {
            var icon = GetIconImageSource(shortcut.ShortcutPath);
            yield return new ShortcutItem
            {
                DisplayName = shortcut.DisplayName,
                Definition = shortcut,
                // If extraction fails, show a placeholder so we can verify rendering/binding.
                Icon = icon ?? CreatePlaceholderIcon()
            };
        }
    }

    private bool TryGetShortcutFiles(DragEventArgs e, out string[] filePaths)
    {
        filePaths = Array.Empty<string>();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;

        var raw = e.Data.GetData(DataFormats.FileDrop);

        // Explorer commonly provides `StringCollection`, but we support common shapes.
        IEnumerable<string>? strings = raw switch
        {
            string[] arr => arr,
            StringCollection sc => sc.Cast<string>(),
            IEnumerable<string> enumerable => enumerable,
            object[] objArr => objArr.OfType<string>(),
            _ => null
        };

        if (strings == null)
            return false;

        filePaths = strings
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return filePaths.Length > 0;
    }

    private void KennelWindow_DragEnter(object sender, DragEventArgs e)
    {
        if (!TryGetShortcutFiles(e, out var filePaths))
            return;

        // Always accept `FileDrop` as a drag target so the `Drop` event fires.
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        // If it includes any `.lnk` we show the kennel as an active drop target.
        if (filePaths.Any(p => string.Equals(Path.GetExtension(p), ".lnk", StringComparison.OrdinalIgnoreCase)))
        {
            // Expand so users can see the contents while dropping.
            if (_isCollapsed)
                SetCollapsed(false);

            // Show drop hint while hovering.
            DropHintText.Visibility = Visibility.Visible;

            RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
        }
    }

    private void KennelWindow_DragLeave(object sender, DragEventArgs e)
    {
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00));
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
        DropHintText.Visibility = Visibility.Collapsed;
    }

    private void KennelWindow_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetShortcutFiles(e, out var filePaths))
            return;

        var changed = false;

        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
                continue;

            var ext = Path.GetExtension(filePath);
            if (!string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
                continue;

            // De-dupe by original dropped path.
            var originalPath = Path.GetFullPath(filePath);
            if (_kennel.Shortcuts.Any(s =>
                    string.Equals(s.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                var copiedPath = _storage.CopyShortcutToKennel(_kennel.Id, originalPath);
                var displayName = Path.GetFileNameWithoutExtension(originalPath);

                _kennel.Shortcuts.Add(new ShortcutDefinition
                {
                    DisplayName = displayName,
                    ShortcutPath = copiedPath,
                    OriginalPath = originalPath
                });

                changed = true;
            }
            catch
            {
                // Ignore unsupported/corrupt shortcuts or file IO errors.
            }
        }

        if (changed)
        {
            RefreshList();
            KennelUpdated?.Invoke(this, _kennel);
        }

        KennelWindow_DragLeave(sender, e);
        e.Handled = true;
    }

    private void SetCollapsed(bool collapsed)
    {
        _isCollapsed = collapsed;

        Width = collapsed ? CollapsedWidth : ExpandedWidth;
        Height = collapsed ? CollapsedHeight : ExpandedHeight;

        // Collapse the content row entirely.
        if (KennelLayoutGrid.RowDefinitions.Count >= 2)
        {
            KennelLayoutGrid.RowDefinitions[1].Height = collapsed
                ? new GridLength(0)
                : GridLength.Auto;
        }

        ShortcutsList.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        DropHintText.Visibility = Visibility.Collapsed;

        // Make the header fully rounded when collapsed like a desktop icon.
        HeaderBorder.CornerRadius = collapsed ? new CornerRadius(12) : new CornerRadius(12);

        RefreshList();
        if (!collapsed)
            AdjustExpandedWidthToContent();
    }

    private void AdjustExpandedWidthToContent()
    {
        try
        {
            // We keep the expanded height fixed. To avoid excessive width,
            // we find the *minimal* width where the unbounded desired height fits inside `ExpandedHeight`.
            var maxHeight = ExpandedHeight;
            var minWidth = Math.Max(ExpandedWidth, Width);

            var workArea = SystemParameters.WorkArea;
            var maxAllowedWidth = (workArea.Left + workArea.Width) - workArea.Left - 40; // leave a small margin
            maxAllowedWidth = Math.Max(maxAllowedWidth, minWidth);

            // Hard cap to prevent taking up too much horizontal screen space.
            var hardMaxWidth = Math.Min(maxAllowedWidth, 640);

            double MeasureDesiredHeight(double candidateWidth)
            {
                Width = candidateWidth;
                Height = 10_000; // unbounded measurement
                UpdateLayout();
                var desired = RootBorder.DesiredSize.Height;
                return desired;
            }

            // If default width already fits, we're done.
            var desiredAtMin = MeasureDesiredHeight(minWidth);
            if (!double.IsNaN(desiredAtMin) && desiredAtMin <= maxHeight + 1)
            {
                Width = minWidth;
                Height = ExpandedHeight;
                UpdateLayout();
            }
            else
            {
                // Find an upper bound where it fits (or we hit hard max).
                var low = minWidth;
                var high = minWidth;

                var step = 60.0;
                while (high < hardMaxWidth)
                {
                    var next = Math.Min(hardMaxWidth, high + step);
                    var desired = MeasureDesiredHeight(next);
                    if (!double.IsNaN(desired) && desired <= maxHeight + 1)
                    {
                        high = next;
                        break;
                    }
                    high = next;
                }

                // Binary search between `low` and `high` for the minimal width that fits.
                // If it never fits, `high` will be hardMaxWidth.
                for (var i = 0; i < 14; i++)
                {
                    var mid = (low + high) / 2.0;
                    var desired = MeasureDesiredHeight(mid);
                    if (!double.IsNaN(desired) && desired <= maxHeight + 1)
                        high = mid;
                    else
                        low = mid;
                }

                Width = high;

                // If it still doesn't fit even at the hard max width,
                // allow height to grow a bit (no scrolling) so content is still visible.
                var finalDesiredHeight = MeasureDesiredHeight(Width);
                var appliedHeight = ExpandedHeight;
                if (!double.IsNaN(finalDesiredHeight) && finalDesiredHeight > ExpandedHeight + 1)
                    appliedHeight = Math.Min(finalDesiredHeight, ExpandedHeight + 120);

                Height = appliedHeight;
                UpdateLayout();
            }

            // Keep within the visible work area as best as possible.
            var maxLeft = workArea.Left + workArea.Width - Width;
            if (Left < workArea.Left)
                Left = workArea.Left;
            else if (Left > maxLeft)
                Left = maxLeft;

            if (Top < workArea.Top)
                Top = workArea.Top;
            else if (Top + Height > workArea.Top + workArea.Height)
                Top = workArea.Top + workArea.Height - Height;
        }
        catch
        {
            // If measurement fails for any reason, keep default expanded size.
            Width = ExpandedWidth;
            Height = ExpandedHeight;
        }
    }

    private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _headerMouseDown = true;
        _headerIsDragging = false;
        _headerDownPos = e.GetPosition(this);
    }

    private void HeaderBorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_headerMouseDown)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(this);
        var movedX = Math.Abs(pos.X - _headerDownPos.X);
        var movedY = Math.Abs(pos.Y - _headerDownPos.Y);

        // Only initiate a move if the user actually drags (prevents click-to-toggle confusion).
        if (!_headerIsDragging && (movedX > 4 || movedY > 4))
        {
            _headerIsDragging = true;
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw if the drag is interrupted; ignore.
            }
        }
    }

    private void HeaderBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_headerMouseDown)
            return;

        _headerMouseDown = false;

        // If we started a drag, don't toggle.
        if (_headerIsDragging)
            return;

        SetCollapsed(!_isCollapsed);
    }

    private void ShortcutsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ShortcutsList.SelectedItem is not ShortcutItem selected)
            return;

        var shortcutPath = selected.Definition.ShortcutPath;
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = shortcutPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Launch failures are ignored for now.
        }
    }

    private ImageSource? GetIconImageSource(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath))
            return null;

        if (_iconCache.TryGetValue(shortcutPath, out var cached))
            return cached;

        var icon = ExtractAssociatedIconImage(shortcutPath);
        _iconCache[shortcutPath] = icon;
        return icon;
    }

    private static ImageSource CreatePlaceholderIcon()
    {
        // Simple rounded-square placeholder (used only when shell icon extraction fails).
        var brush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), 1);
        var rect = new Rect(0, 0, 26, 26);
        var geometry = new RectangleGeometry(rect, 5, 5);

        var drawing = new GeometryDrawing(brush, pen, geometry);
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static ImageSource? ExtractAssociatedIconImage(string filePath)
    {
        // Use SHGetFileInfo so Windows returns the icon for the `.lnk` (usually resolves to the target app).
        var flags = SHGFI.SHGFI_ICON | SHGFI.SHGFI_SMALLICON;

        var shfi = new SHFILEINFO();
        var result = SHGetFileInfo(
            filePath,
            0,
            out shfi,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            (uint)flags);
        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            // Important: CreateBitmapSourceFromHIcon can defer internal work until later.
            // To avoid invalidating the unmanaged handle too early, copy into a managed bitmap.
            var hiconBitmap = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(26, 26));

            var writeable = new System.Windows.Media.Imaging.WriteableBitmap(hiconBitmap);
            writeable.Freeze();
            return writeable;
        }
        catch (Exception ex)
        {
            // Optional debugging: write failures to a log for easier diagnosis.
            try
            {
                var appRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Kennel Organiser");
                Directory.CreateDirectory(appRoot);
                var logPath = Path.Combine(appRoot, "icon-errors.log");
                File.AppendAllText(logPath, $"{DateTime.Now:O} {filePath}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Never crash the app for logging issues.
            }
            // If icon extraction fails for any reason (unusual .lnk targets, DPI edge-cases, etc.),
            // don't crash the whole app. Just show text.
            return null;
        }
        finally
        {
            if (shfi.hIcon != IntPtr.Zero)
                DestroyIcon(shfi.hIcon);
        }
    }

    [Flags]
    private enum SHGFI : uint
    {
        SHGFI_ICON = 0x100,
        SHGFI_SMALLICON = 0x1,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

