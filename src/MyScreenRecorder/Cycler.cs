using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MyScreenRecorder;

public sealed class Cycler : Grid
{
    private readonly Button _prev = new() { Content = "<", Width = 28 };
    private readonly Button _next = new() { Content = ">", Width = 28 };
    private readonly TextBlock _text = new()
    {
        Margin = new Thickness(8, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private IReadOnlyList<string> _items = Array.Empty<string>();
    private int _index;

    public event Action? Changed;

    public int Index => _index;

    public Cycler()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(_prev, 0);
        SetColumn(_text, 1);
        SetColumn(_next, 2);
        Children.Add(_prev);
        Children.Add(_text);
        Children.Add(_next);
        Tips.ForTrimmed(_text);
        _prev.Click += (_, _) => Step(-1);
        _next.Click += (_, _) => Step(1);
    }

    public void SetItems(IReadOnlyList<string> items, int index)
    {
        _items = items;
        _index = items.Count == 0 ? 0 : Math.Clamp(index, 0, items.Count - 1);
        Refresh();
    }

    private void Step(int delta)
    {
        if (_items.Count < 2) return;
        _index = (_index + delta + _items.Count) % _items.Count;
        Refresh();
        Changed?.Invoke();
    }

    private void Refresh()
    {
        string text = _items.Count == 0 ? string.Empty : _items[_index];
        Tips.Set(_text, text);
    }
}
