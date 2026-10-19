using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AwadTimetable.UI.Services;

/// <summary>
/// واجهة خدمة الإعدادات
/// </summary>
public interface ISettingsService
{
    T Get<T>(string key, T defaultValue = default!);
    void Set<T>(string key, T value);
    Task SaveAsync();
    Task LoadAsync();
    void Reset();
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
}

/// <summary>
/// وسيط تغيير الإعدادات
/// </summary>
public sealed class SettingsChangedEventArgs : EventArgs
{
    public string Key { get; init; } = string.Empty;
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
}