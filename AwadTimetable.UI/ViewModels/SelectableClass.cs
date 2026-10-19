using AwadTimetable.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AwadTimetable.UI.ViewModels;

/// <summary>عنصر فصل قابل للتحديد لتمكين الإسناد الجماعي لعدة شعب في عملية واحدة.</summary>
public partial class SelectableClass : ObservableObject
{
    public Class Class { get; }

    public SelectableClass(Class cls) => Class = cls;

    [ObservableProperty]
    private bool _isSelected;

    public string Label => Class.FullName;
}
