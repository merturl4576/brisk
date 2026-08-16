using System.Windows.Controls;
using Brisk.ViewModels;

namespace Brisk.Views;

public partial class PerfPage : UserControl
{
    public PerfPage() { InitializeComponent(); }

    public void Bind(HealthViewModel performance, StartupViewModel startup)
    {
        DataContext = performance;
        StartupSection.DataContext = startup;
    }
}
