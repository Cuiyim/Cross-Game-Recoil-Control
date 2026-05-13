using System.Windows;

namespace LegendaryCSharp;

public partial class UsageInstructionsWindow : Window
{
    public UsageInstructionsWindow()
    {
        InitializeComponent();
        UsageText.Text = UsageGuide.Text;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
