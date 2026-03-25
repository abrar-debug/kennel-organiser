using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace kennel;

public partial class MainWindow : Window
{
    private readonly KennelStorage _storage = new();
    private readonly Dictionary<string, KennelWindow> _kennelWindows = new();
    private List<KennelDefinition> _kennels = new();

    public MainWindow()
    {
        InitializeComponent();

        _kennels = _storage.LoadKennels();
        for (var i = 0; i < _kennels.Count; i++)
            CreateKennelWindowIfNeeded(_kennels[i], i);

        RefreshList();
    }

    // ── list item view model ──────────────────────────────────────────────────

    private sealed class KennelRow
    {
        public required string Id               { get; init; }
        public required string Name             { get; init; }
        public required string ShortcutCountLabel { get; init; }
    }

    private void RefreshList()
    {
        var rows = new List<KennelRow>();
        foreach (var k in _kennels)
        {
            var count = k.Shortcuts.Count;
            rows.Add(new KennelRow
            {
                Id   = k.Id,
                Name = k.Name,
                ShortcutCountLabel = count == 0 ? "empty"
                                   : count == 1 ? "1 item"
                                   : $"{count} items"
            });
        }

        KennelListView.ItemsSource = rows;
        EmptyLabel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── add ──────────────────────────────────────────────────────────────────

    private void AddKennelButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new KennelNameDialog { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        var name = dlg.KennelName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var kennel = new KennelDefinition
        {
            Id   = Guid.NewGuid().ToString("N"),
            Name = name
        };

        _kennels.Add(kennel);
        _storage.SaveKennels(_kennels);

        CreateKennelWindowIfNeeded(kennel, _kennels.Count - 1);
        RefreshList();
    }

    // ── delete ───────────────────────────────────────────────────────────────

    private void DeleteKennel_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag as string;
        if (string.IsNullOrEmpty(id))
            return;

        var kennel = _kennels.Find(k => k.Id == id);
        if (kennel is null)
            return;

        var result = MessageBox.Show(
            $"Delete kennel \"{kennel.Name}\"?\n\nThis will remove it from your desktop and clear its saved shortcuts.",
            "Delete Kennel",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        // Close the desktop window if it's open.
        if (_kennelWindows.TryGetValue(id, out var window))
        {
            window.Closed -= null; // prevent the Closed handler re-running cleanup
            window.Close();
            _kennelWindows.Remove(id);
        }

        _kennels.Remove(kennel);
        _storage.SaveKennels(_kennels);
        RefreshList();
    }

    // ── kennel window lifecycle ───────────────────────────────────────────────

    private void CreateKennelWindowIfNeeded(KennelDefinition kennel, int offsetIndex = -1)
    {
        if (_kennelWindows.TryGetValue(kennel.Id, out var existing))
        {
            existing.Activate();
            return;
        }

        var workArea = SystemParameters.WorkArea;

        double left, top;
        if (kennel.Left.HasValue && kennel.Top.HasValue)
        {
            left = Math.Max(workArea.Left, Math.Min(kennel.Left.Value, workArea.Left + workArea.Width  - KennelWindow.CollapsedWidth));
            top  = Math.Max(workArea.Top,  Math.Min(kennel.Top.Value,  workArea.Top  + workArea.Height - KennelWindow.CollapsedHeight));
        }
        else
        {
            var baseLeft = 60 + (offsetIndex < 0 ? 0 : offsetIndex * 25);
            var baseTop  = 60 + (offsetIndex < 0 ? 0 : offsetIndex * 20);
            left = workArea.Left + Math.Min(baseLeft, workArea.Width  - KennelWindow.ExpandedWidth);
            top  = workArea.Top  + Math.Min(baseTop,  workArea.Height - KennelWindow.ExpandedHeight);
        }

        var kennelWindow = new KennelWindow(kennel, _storage)
        {
            Left = left,
            Top  = top
        };

        kennelWindow.KennelUpdated += (_, _) =>
        {
            _storage.SaveKennels(_kennels);
            RefreshList();
        };

        kennelWindow.Closed += (_, _) =>
        {
            _kennelWindows.Remove(kennel.Id);
        };

        _kennelWindows[kennel.Id] = kennelWindow;
        kennelWindow.Show();
    }
}
