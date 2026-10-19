using System.Windows;

namespace AwadTimetable.UI.Views;

/// <summary>
/// جسر ربط (Freezable) لتمرير DataContext إلى عناصر غير بصرية لا ترث السياق، مثل أعمدة DataGrid
/// </summary>
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
