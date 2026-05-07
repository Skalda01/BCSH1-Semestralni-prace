using Avalonia.Controls;
using SkalaView.ViewModels;

namespace SkalaView;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
