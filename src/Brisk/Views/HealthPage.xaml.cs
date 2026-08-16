using System.Windows.Controls;
using Brisk.ViewModels;

namespace Brisk.Views;

public partial class HealthPage : UserControl
{
    public HealthPage() { InitializeComponent(); }

    public void Bind(HealthViewModel health) => DataContext = health;
}
