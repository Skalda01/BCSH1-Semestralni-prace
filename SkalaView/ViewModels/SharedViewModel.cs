using System.ComponentModel;
using SkalaView.AppComponent.TickerMenu;

namespace SkalaView.ViewModels;

public sealed class SharedViewModel : INotifyPropertyChanged
{
    private TickerItem? _selectedTicker;
    private string _selectedTimeframe = "1m";

    public TickerItem? SelectedTicker
    {
        get => _selectedTicker;
        set
        {
            if (_selectedTicker == value) return;
            _selectedTicker = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTicker)));
        }
    }

    public string SelectedTimeframe
    {
        get => _selectedTimeframe;
        set
        {
            if (_selectedTimeframe == value) return;
            _selectedTimeframe = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTimeframe)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
