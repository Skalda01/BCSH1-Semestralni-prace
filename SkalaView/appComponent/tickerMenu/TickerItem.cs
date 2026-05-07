using System;
using Avalonia.Media;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SkalaView.AppComponent.TickerMenu;

public class TickerItem : INotifyPropertyChanged
{
    private static readonly IBrush PositiveBrush = new SolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush NegativeBrush = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private string _symbol = string.Empty;
    private string _displayName = string.Empty;
    private double _price;
    private double _change;
    private string _exchange = "NASDAQ";
    private double? _dayLow;
    private double? _dayHigh;
    private long? _volume;
    private double? _marketCap;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Symbol
    {
        get => _symbol;
        set => SetField(ref _symbol, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public double Price
    {
        get => _price;
        set
        {
            if (!SetField(ref _price, value)) return;
            OnPropertyChanged(nameof(PriceText));
        }
    }

    public double Change
    {
        get => _change;
        set
        {
            if (!SetField(ref _change, value)) return;
            OnPropertyChanged(nameof(ChangeText));
            OnPropertyChanged(nameof(ChangeBrush));
        }
    }

    // Percentualni zmena za den.

    public string Exchange
    {
        get => _exchange;
        set => SetField(ref _exchange, value);
    }

    public double? DayLow
    {
        get => _dayLow;
        set
        {
            if (!SetField(ref _dayLow, value)) return;
            OnPropertyChanged(nameof(DayRangeText));
        }
    }

    public double? DayHigh
    {
        get => _dayHigh;
        set
        {
            if (!SetField(ref _dayHigh, value)) return;
            OnPropertyChanged(nameof(DayRangeText));
        }
    }

    public long? Volume
    {
        get => _volume;
        set
        {
            if (!SetField(ref _volume, value)) return;
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    // USD
    public double? MarketCap
    {
        get => _marketCap;
        set
        {
            if (!SetField(ref _marketCap, value)) return;
            OnPropertyChanged(nameof(MarketCapText));
        }
    }

    public string PriceText => Price > 0 ? $"${Price:N2}" : "USDT";

    public string ChangeText
    {
        get
        {
            if (Math.Abs(Change) < 0.000001) return "Live";
            var sign = Change > 0 ? "+" : string.Empty;
            return $"{sign}{Change:0.##}%";
        }
    }

    public IBrush ChangeBrush => Change > 0 ? PositiveBrush : Change < 0 ? NegativeBrush : NeutralBrush;

    public string DayRangeText =>
        DayLow.HasValue && DayHigh.HasValue
            ? $"Range {DayLow.Value:N2} - {DayHigh.Value:N2}"
            : "Range --";

    public string VolumeText => Volume.HasValue ? $"Vol {FormatCompactNumber(Volume.Value)}" : "Vol --";

    public string MarketCapText => MarketCap.HasValue ? $"MCap ${FormatCompactNumber(MarketCap.Value)}" : "MCap --";

    private static string FormatCompactNumber(double value)
    {
        var abs = Math.Abs(value);
        if (abs >= 1_000_000_000_000d) return (value / 1_000_000_000_000d).ToString("0.##T", CultureInfo.InvariantCulture);
        if (abs >= 1_000_000_000d) return (value / 1_000_000_000d).ToString("0.##B", CultureInfo.InvariantCulture);
        if (abs >= 1_000_000d) return (value / 1_000_000d).ToString("0.##M", CultureInfo.InvariantCulture);
        if (abs >= 1_000d) return (value / 1_000d).ToString("0.##K", CultureInfo.InvariantCulture);
        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
