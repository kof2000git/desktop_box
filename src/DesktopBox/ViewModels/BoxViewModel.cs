using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBox.Models;

namespace DesktopBox.ViewModels;

public partial class BoxViewModel : ObservableObject
{
    public Guid Id { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private string? _accentColor;

    public ObservableCollection<BoxItem> Items { get; } = new();

    public BoxViewModel(Box model)
    {
        Id = model.Id;
        _name = model.Name;
        _x = model.X; _y = model.Y;
        _width = model.Width; _height = model.Height;
        _accentColor = model.AccentColor;
        foreach (var i in model.Items) Items.Add(i);
    }

    public Box ToModel() => new()
    {
        Id = Id,
        Name = Name,
        X = X, Y = Y,
        Width = Width, Height = Height,
        AccentColor = AccentColor,
        Items = new(Items)
    };
}
