using System;
using System.Collections.Generic;
using System.Windows;

namespace kennel;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
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
        {
            CreateKennelWindowIfNeeded(_kennels[i], i);
        }
    }

    private void AddKennelButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new KennelNameDialog
        {
            Owner = this
        };

        if (dlg.ShowDialog() != true)
            return;

        var kennelName = dlg.KennelName?.Trim();
        if (string.IsNullOrWhiteSpace(kennelName))
            return;

        // Keep windows manageable by offsetting new kennels slightly.
        var offsetIndex = _kennels.Count;
        var kennel = new KennelDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = kennelName,
        };

        _kennels.Add(kennel);
        _storage.SaveKennels(_kennels);

        CreateKennelWindowIfNeeded(kennel, offsetIndex);
    }

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
            // Restore saved position, clamping back onto the screen in case resolution changed.
            left = Math.Max(workArea.Left, Math.Min(kennel.Left.Value, workArea.Left + workArea.Width  - KennelWindow.CollapsedWidth));
            top  = Math.Max(workArea.Top,  Math.Min(kennel.Top.Value,  workArea.Top  + workArea.Height - KennelWindow.CollapsedHeight));
        }
        else
        {
            // First-time placement: stagger new kennels so they don't stack on top of each other.
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

        kennelWindow.KennelUpdated += (_, updated) =>
        {
            // `updated` is the same instance as `kennel` for our in-memory list.
            _storage.SaveKennels(_kennels);
        };

        kennelWindow.Closed += (_, _) =>
        {
            _kennelWindows.Remove(kennel.Id);
        };

        _kennelWindows[kennel.Id] = kennelWindow;
        kennelWindow.Show();
    }
}