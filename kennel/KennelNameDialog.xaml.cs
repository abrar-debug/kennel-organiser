using System.Windows;

namespace kennel;

public partial class KennelNameDialog : Window
{
    public string KennelName => NameTextBox.Text;

    public KennelNameDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

