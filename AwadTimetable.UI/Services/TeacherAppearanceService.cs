using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwadTimetable.Core.Entities;

namespace AwadTimetable.UI.Services;

/// <summary>مظهر مخصص لمعلم واحد: اسم مختصر ولون.</summary>
public sealed class TeacherAppearanceEntry
{
    public string? ShortName { get; set; }
    public string? ColorHex { get; set; }
}

/// <summary>
/// خدمة تحفظ الأسماء المختصرة والألوان المخصصة للمعلمين في ملف JSON
/// تحت مجلد بيانات التطبيق، دون أي تغيير في مخطط قاعدة البيانات.
/// </summary>
public sealed class TeacherAppearanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<Guid, TeacherAppearanceEntry> _map = new();

    /// <summary>يُطلَق عند تغيّر مظهر معلم حتى تعيد الواجهة بناء الجدول.</summary>
    public event Action? Changed;

    public TeacherAppearanceService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AwadTimetable");
        _path = Path.Combine(dir, "teacher_appearance.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<Dictionary<Guid, TeacherAppearanceEntry>>(json);
            if (parsed != null) _map = parsed;
        }
        catch
        {
            _map = new Dictionary<Guid, TeacherAppearanceEntry>();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_map, JsonOptions));
        }
        catch
        {
            // تجاهل أخطاء الكتابة: المظهر تحسين تجميلي فقط.
        }
    }

    public TeacherAppearanceEntry? Get(Guid teacherId)
        => _map.TryGetValue(teacherId, out var entry) ? entry : null;

    public void Set(Guid teacherId, string? shortName, string? colorHex)
    {
        lock (_gate)
        {
            var empty = string.IsNullOrWhiteSpace(shortName) && string.IsNullOrWhiteSpace(colorHex);
            if (empty)
            {
                if (_map.ContainsKey(teacherId)) _map.Remove(teacherId);
            }
            else
            {
                _map[teacherId] = new TeacherAppearanceEntry
                {
                    ShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName!.Trim(),
                    ColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex!.Trim()
                };
            }
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>يطبّق المظهر المحفوظ على كائن المعلم (خصائص غير مُخزَّنة).</summary>
    public void Apply(Teacher teacher)
    {
        var entry = Get(teacher.Id);
        teacher.SetAppearance(entry?.ShortName, entry?.ColorHex);
    }

    public void ApplyAll(IEnumerable<Teacher> teachers)
    {
        foreach (var t in teachers) Apply(t);
    }
}
