using System;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// محدد موقع نماذج العرض (ViewModel Locator)
/// </summary>
public class ViewModelLocator
{
    private static IServiceProvider? _serviceProvider;

    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public static T GetService<T>() where T : notnull
    {
        return _serviceProvider!.GetRequiredService<T>();
    }

    public static T? GetServiceOrNull<T>() where T : class
    {
        return _serviceProvider!.GetService<T>();
    }

    // ViewModels خصائص للوصول السهل في XAML
    public MainWindowViewModel MainWindowViewModel => GetService<MainWindowViewModel>();
    public TimetableViewModel TimetableViewModel => GetService<TimetableViewModel>();
    public TeacherManagementViewModel TeacherManagementViewModel => GetService<TeacherManagementViewModel>();
    public SubjectManagementViewModel SubjectManagementViewModel => GetService<SubjectManagementViewModel>();
    public RoomManagementViewModel RoomManagementViewModel => GetService<RoomManagementViewModel>();
    public ClassManagementViewModel ClassManagementViewModel => GetService<ClassManagementViewModel>();
    public ConstraintEditorViewModel ConstraintEditorViewModel => GetService<ConstraintEditorViewModel>();
    public AIChatViewModel AIChatViewModel => GetService<AIChatViewModel>();
    public SettingsViewModel SettingsViewModel => GetService<SettingsViewModel>();
    public AIChatPanelViewModel AIChatPanelViewModel => GetService<AIChatPanelViewModel>();
}
