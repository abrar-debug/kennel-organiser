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
        var baseLeft = 60 + (offsetIndex < 0 ? 0 : offsetIndex * 25);
        var baseTop = 60 + (offsetIndex < 0 ? 0 : offsetIndex * 20);

        // If a kennel was added previously, clamp positions into a reasonable range.
        var left = Math.Min(baseLeft, workArea.Width - KennelWindow.ExpandedWidth);
        var top = Math.Min(baseTop, workArea.Height - KennelWindow.ExpandedHeight);

        var kennelWindow = new KennelWindow(kennel, _storage)
        {
            Left = workArea.Left + left,
            Top = workArea.Top + top
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