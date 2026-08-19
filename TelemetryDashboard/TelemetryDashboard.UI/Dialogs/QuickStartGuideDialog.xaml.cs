using System.Windows;

namespace TelemetryDashboard.UI.Dialogs;

public partial class QuickStartGuideDialog : Window
{
    public QuickStartGuideDialog()
    {
        InitializeComponent();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
