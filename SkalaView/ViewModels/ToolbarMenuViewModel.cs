using System;
using System.ComponentModel;
using System.Windows.Input;

namespace SkalaView.ViewModels;

public sealed class ToolbarMenuViewModel : INotifyPropertyChanged
{
    private readonly SharedViewModel _shared;

    public ToolbarMenuViewModel()
        : this(new SharedViewModel())
    {
    }

    public ToolbarMenuViewModel(SharedViewModel shared)
    {
        _shared = shared;
        _shared.PropertyChanged += OnSharedPropertyChanged;

        SelectTimeframeCommand = new RelayCommand(parameter =>
        {
            if (parameter is string timeframe)
                SelectedTimeframe = timeframe;
        });
    }

    public ICommand SelectTimeframeCommand { get; }

    public string SelectedTimeframe
    {
        get => _shared.SelectedTimeframe;
        set
        {
            if (_shared.SelectedTimeframe == value) return;
            _shared.SelectedTimeframe = value;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnSharedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedViewModel.SelectedTimeframe))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTimeframe)));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public RelayCommand(Action<object?> execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
