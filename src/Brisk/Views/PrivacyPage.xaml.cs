using System.Windows.Controls;
using Brisk.ViewModels;

namespace Brisk.Views;

public partial class PrivacyPage : UserControl
{
    public PrivacyPage() { InitializeComponent(); }

    public void Bind(PrivacyViewModel privacy) => DataContext = privacy;
}
