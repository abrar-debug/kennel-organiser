using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Specialized;

namespace kennel;

public partial class KennelWindow : Window
{
    private readonly KennelDefinition _kennel;
    private readonly KennelStorage _storage;

    public event EventHandler<KennelDefinition>? KennelUpdated;

    public KennelWindow(KennelDefinition kennel, KennelStorage storage)
    {
        _kennel = kennel;
        _storage = storage;

        InitializeComponent();

        KennelNameText.Text = _kennel.Name;
        RefreshList();
    }

    private void RefreshList()
    {
        ShortcutsList.ItemsSource = null;
        ShortcutsList.ItemsSource = _kennel.Shortcuts;

        EmptyHintText.Visibility = _kennel.Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
        }
    }

    private void KennelWindow_DragLeave(object sender, DragEventArgs e)
    {
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00));
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
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

    private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ShortcutsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ShortcutsList.SelectedItem is not ShortcutDefinition selected)
            return;

        if (string.IsNullOrWhiteSpace(selected.ShortcutPath) || !File.Exists(selected.ShortcutPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = selected.ShortcutPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Launch failures are ignored for now.
        }
    }
}

