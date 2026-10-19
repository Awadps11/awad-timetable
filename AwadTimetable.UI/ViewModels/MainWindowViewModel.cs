using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using AwadTimetable.Core.Constants;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Core.Constraints;
using AwadTimetable.Data.Repositories;
using AwadTimetable.AI.Interfaces;
using AwadTimetable.AI.Tools;
using AwadTimetable.UI.Services;
using AwadTimetable.Printing.Services;
using AwadTimetable.UI.Views;
using AwadTimetable.Solver;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.UI.ViewModels;

/// <summary>
/// نموذج عرض النافذة الرئيسية
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ITimetableGenerator _generator;
    private readonly IGeneratorResolver _generatorResolver;
    private readonly IConstraintValidator _validator;
    private readonly IConflictDetector _detector;

    [ObservableProperty]
    private string _currentTimetableName = "لا يوجد جدول محدد";

    /// <summary>اسم مساحة العمل/المدرسة الحالية — يُعرض في العنوان لمنع الخلط بين المدارس.</summary>
    [ObservableProperty]
    private string _currentSchoolName = "غير محدد";

    public string WindowTitle =>
        string.IsNullOrWhiteSpace(CurrentSchoolName)
            ? $"{Core.Constants.AppConstants.ApplicationName} — {CurrentTimetableName}"
            : $"{Core.Constants.AppConstants.ApplicationName} — {CurrentSchoolName} — {CurrentTimetableName}";

    partial void OnCurrentTimetableNameChanged(string value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnCurrentSchoolNameChanged(string value) => OnPropertyChanged(nameof(WindowTitle));

    [ObservableProperty]
    private string _timetableStatusText = "مسودة";

    [ObservableProperty]
    private int _totalLessonsCount = 0;

    [ObservableProperty]
    private int _conflictsCount = 0;

    [ObservableProperty]
    private string _qualityScoreText = "0%";

    [ObservableProperty]
    private string _currentDateTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm");

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedSidebarTab = 1;

    [ObservableProperty]
    private int _rightPanelTab = 0;

    [ObservableProperty]
    private bool _isAIChatPanelOpen = false;

    [ObservableProperty]
    private object? _selectedItemPropertiesView;

    [ObservableProperty]
    private AIChatPanelViewModel? _aiChatPanelViewModel;

    /// <summary>توليد/تحسين قيد التشغيل الآن (يشغّل شريط التقدّم وزر الإلغاء)</summary>
    [ObservableProperty]
    private bool _isGenerating;

    /// <summary>نص مرحلة التوليد الحالية المعروض في شريط الحالة</summary>
    [ObservableProperty]
    private string _generationStatusText = string.Empty;

    private CancellationTokenSource? _generationCts;

    // نماذج عرض الإدارة (تُسند في المُنشئ)
    public TeacherManagementViewModel Teachers { get; }
    public SubjectManagementViewModel Subjects { get; }
    public RoomManagementViewModel Rooms { get; }
    public ClassManagementViewModel Classes { get; }
    public ConstraintEditorViewModel Constraints { get; }

    // إحصائيات
    [ObservableProperty]
    private int _teachersCount;

    [ObservableProperty]
    private int _subjectsCount;

    [ObservableProperty]
    private int _roomsCount;

    [ObservableProperty]
    private int _classesCount;

    [ObservableProperty]
    private int _lessonsCount;

    [ObservableProperty]
    private ObservableCollection<string> _conflictsList = new();

    /// <summary>تلوين بطاقات الجدول بلون المعلم (بدل لون المادة) للتفرقة أثناء المراجعة</summary>
    [ObservableProperty]
    private bool _colorByTeacher;

    partial void OnColorByTeacherChanged(bool value)
    {
        if (_currentTimetable != null) UpdateTimetableGrid(_currentTimetable);
    }

    // ===== إظهار/إخفاء اللواحل الجانبية + تكبير/تصغير الجدول =====

    /// <summary>هل اللوحة الجانبية اليسرى (الخصائص/المساعد) ظاهرة؟</summary>
    [ObservableProperty]
    private bool _isLeftPanelVisible = true;

    /// <summary>هل اللوحة الجانبية اليمنى (الجدول/التنقل) ظاهرة؟</summary>
    [ObservableProperty]
    private bool _isRightPanelVisible = true;

    public System.Windows.GridLength LeftPanelWidth =>
        IsLeftPanelVisible ? new System.Windows.GridLength(300) : new System.Windows.GridLength(0);

    public System.Windows.GridLength RightPanelWidth =>
        IsRightPanelVisible ? new System.Windows.GridLength(350) : new System.Windows.GridLength(0);

    partial void OnIsLeftPanelVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(LeftPanelWidth));
        PersistUiPreferences();
    }

    partial void OnIsRightPanelVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(RightPanelWidth));
        PersistUiPreferences();
    }

    [RelayCommand]
    private void ToggleLeftPanel() => IsLeftPanelVisible = !IsLeftPanelVisible;

    [RelayCommand]
    private void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

    /// <summary>مستوى تكبير الجدول (1 = 100%).</summary>
    [ObservableProperty]
    private double _zoomLevel = 1.0;

    /// <summary>مُحوِّل القياس المطبّق على شبكة الجدول (يطابق ZoomLevel).</summary>
    public System.Windows.Media.ScaleTransform ZoomTransform { get; } = new(1.0, 1.0);

    /// <summary>عرض مستوى التكبير كنسبة مئوية.</summary>
    public string ZoomPercentText => $"{ZoomLevel * 100:F0}%";

    partial void OnZoomLevelChanged(double value)
    {
        var clamped = Math.Clamp(value, MinZoom, MaxZoom);
        ZoomTransform.ScaleX = clamped;
        ZoomTransform.ScaleY = clamped;
        OnPropertyChanged(nameof(ZoomPercentText));

        if (Math.Abs(clamped - value) > 0.0001)
        {
            ZoomLevel = clamped;
        }

        PersistUiPreferences();
    }

    private const double MinZoom = 0.4;
    private const double MaxZoom = 2.2;
    private const double ZoomStep = 0.1;

    [RelayCommand]
    private void ZoomIn() => ZoomLevel = Math.Round(ZoomLevel + ZoomStep, 2);

    [RelayCommand]
    private void ZoomOut() => ZoomLevel = Math.Round(ZoomLevel - ZoomStep, 2);

    [RelayCommand]
    private void ZoomReset() => ZoomLevel = 1.0;

    private bool _uiPrefsLoaded;

    /// <summary>تحميل تفضيلات العرض (ظهور اللوحتين + مستوى التكبير) من الإعدادات.</summary>
    private void LoadUiPreferences()
    {
        try
        {
            var settings = _services.GetService<ISettingsService>();
            if (settings != null)
            {
                IsLeftPanelVisible = settings.Get(SettingsKeys.LeftPanelVisible, true);
                IsRightPanelVisible = settings.Get(SettingsKeys.RightPanelVisible, true);
                var z = settings.Get(SettingsKeys.ZoomLevel, 1.0);
                if (z < MinZoom || z > MaxZoom) z = 1.0;
                ZoomLevel = z;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر تحميل تفضيلات العرض");
        }
        finally
        {
            _uiPrefsLoaded = true;
        }
    }

    /// <summary>حفظ تفضيلات العرض الحالية في ملف الإعدادات.</summary>
    private void PersistUiPreferences()
    {
        if (!_uiPrefsLoaded) return;
        try
        {
            var settings = _services.GetService<ISettingsService>();
            if (settings == null) return;
            settings.Set(SettingsKeys.LeftPanelVisible, IsLeftPanelVisible);
            settings.Set(SettingsKeys.RightPanelVisible, IsRightPanelVisible);
            settings.Set(SettingsKeys.ZoomLevel, ZoomLevel);
            _ = settings.SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر حفظ تفضيلات العرض");
        }
    }

    // ===== طبقة الإسناد المركزية: (معلم + مادة + فصل + عدد الحصص الأسبوعية) =====

    private readonly ObservableCollection<TeachingAssignmentRow> _assignments = new();
    public ObservableCollection<TeachingAssignmentRow> Assignments => _assignments;

    public int AssignmentsCount => _assignments.Count;

    /// <summary>خيارات الفصول للإسناد الجماعي: يمكن اختيار أكثر من شعبة دفعة واحدة.</summary>
    public ObservableCollection<SelectableClass> AssignmentClassOptions { get; } = new();

    /// <summary>إعادة بناء قائمة اختيار الفصول مع الحفاظ على التحديد الحالي.</summary>
    private void RebuildAssignmentClassOptions()
    {
        var selected = AssignmentClassOptions
            .Where(o => o.IsSelected)
            .Select(o => (Guid)o.Class.Id)
            .ToHashSet();

        AssignmentClassOptions.Clear();
        if (Classes?.Classes == null) return;

        foreach (var c in Classes.Classes)
        {
            var opt = new SelectableClass(c) { IsSelected = selected.Contains((Guid)c.Id) };
            AssignmentClassOptions.Add(opt);
        }
    }

    [ObservableProperty]
    private Teacher? _newAssignmentTeacher;

    [ObservableProperty]
    private Subject? _newAssignmentSubject;

    [ObservableProperty]
    private int _newAssignmentPeriods = 1;

    /// <summary>إعادة بناء قائمة الإسناد المعروضة من الجدول المفتوح.</summary>
    private void RebuildAssignments()
    {
        _assignments.Clear();
        if (_currentTimetable?.Assignments != null)
        {
            foreach (var a in _currentTimetable.Assignments)
            {
                _assignments.Add(new TeachingAssignmentRow(a)
                {
                    Teacher = FindTeacherById(a.TeacherId),
                    Subject = FindSubjectById(a.SubjectId),
                    Class = FindClassById(a.ClassId),
                });
            }
        }
        OnPropertyChanged(nameof(AssignmentsCount));
    }

    private Teacher? FindTeacherById(Guid id) => Teachers?.Teachers?.FirstOrDefault(t => (Guid)t.Id == id);
    private Subject? FindSubjectById(Guid id) => Subjects?.Subjects?.FirstOrDefault(s => (Guid)s.Id == id);
    private Class? FindClassById(Guid id) => Classes?.Classes?.FirstOrDefault(c => (Guid)c.Id == id);

    [RelayCommand]
    private void AddAssignment()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("لا يوجد جدول مفتوح. أنشئ جدولاً أو افتحه أولاً.", "الإسناد",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (NewAssignmentTeacher == null || NewAssignmentSubject == null)
        {
            MessageBox.Show("اختر المعلم والمادة.", "الإسناد",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var classes = AssignmentClassOptions.Where(o => o.IsSelected).Select(o => o.Class).ToList();
        if (classes.Count == 0)
        {
            MessageBox.Show("اختر شعبة واحدة على الأقل (يمكن تحديد أكثر من شعبة).", "الإسناد",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var cls in classes)
        {
            var model = new TeachingAssignment
            {
                TeacherId = (Guid)NewAssignmentTeacher.Id,
                SubjectId = (Guid)NewAssignmentSubject.Id,
                ClassId = (Guid)cls.Id,
                WeeklyPeriods = Math.Max(1, NewAssignmentPeriods),
            };
            _currentTimetable.Assignments.Add(model);
            _assignments.Add(new TeachingAssignmentRow(model)
            {
                Teacher = NewAssignmentTeacher,
                Subject = NewAssignmentSubject,
                Class = cls,
            });
        }

        foreach (var o in AssignmentClassOptions) o.IsSelected = false;
        OnPropertyChanged(nameof(AssignmentsCount));
        NewAssignmentPeriods = 1;
    }

    [RelayCommand]
    private void RemoveAssignment(TeachingAssignmentRow? row)
    {
        if (row == null || _currentTimetable == null) return;
        _currentTimetable.Assignments.Remove(row.Model);
        _assignments.Remove(row);
        OnPropertyChanged(nameof(AssignmentsCount));
    }

    // ===== بنود القواعد (وقت انتهاء، ربط معلمين، …) مصنّفة حسب العلاقة =====

    private readonly ObservableCollection<RuleGroup> _ruleGroups = new();
    public ObservableCollection<RuleGroup> RuleGroups => _ruleGroups;

    public int RulesCount => _currentTimetable?.Rules?.Count ?? 0;

    [ObservableProperty] private RuleKind _newRuleKind = RuleKind.General;
    [ObservableProperty] private RuleScope _newRuleScope = RuleScope.Teacher;
    [ObservableProperty] private RulePriority _newRulePriority = RulePriority.Medium;
    [ObservableProperty] private string _newRuleTitle = string.Empty;
    [ObservableProperty] private string _newRuleTarget = string.Empty;
    [ObservableProperty] private string _newRuleParameters = string.Empty;

    public IReadOnlyList<RuleOption<RulePriority>> RulePriorityOptions => RuleOptions.Priorities;
    public IReadOnlyList<RuleOption<RuleKind>> RuleKindOptions => RuleOptions.Kinds;
    public IReadOnlyList<RuleOption<RuleScope>> RuleScopeOptions => RuleOptions.Scopes;

    /// <summary>يعيد بناء القوائم المصنّفة من بنود الجدول المفتوح.</summary>
    private void RebuildRuleGroups()
    {
        _ruleGroups.Clear();
        var rules = _currentTimetable?.Rules ?? new List<RuleItem>();

        foreach (var scope in new[] { RuleScope.Teacher, RuleScope.Class, RuleScope.Subject, RuleScope.Room, RuleScope.School })
        {
            var items = rules.Where(r => r.Scope == scope)
                .OrderBy(r => r.Kind).ThenBy(r => r.Priority)
                .Select(r => new RuleRow(r) { TargetName = ResolveTargetName(r) })
                .ToList();
            if (items.Count == 0) continue;

            var group = new RuleGroup { Header = $"{RuleTexts.Scope(scope)} ({items.Count})" };
            foreach (var it in items) group.Items.Add(it);
            _ruleGroups.Add(group);
        }
        OnPropertyChanged(nameof(RulesCount));
    }

    private string ResolveTargetName(RuleItem r)
    {
        if (r.TargetId is not Guid id) return r.Scope == RuleScope.School ? "المدرسة" : "—";
        return r.Scope switch
        {
            RuleScope.Teacher => FindTeacherById(id)?.FullName ?? "معلم غير معروف",
            RuleScope.Class => FindClassById(id)?.FullName ?? "فصل غير معروف",
            RuleScope.Subject => FindSubjectById(id)?.Name ?? "مادة غير معروفة",
            RuleScope.Room => Rooms?.Rooms?.FirstOrDefault(x => (Guid)x.Id == id)?.Name ?? "قاعة غير معروفة",
            _ => "—"
        };
    }

    /// <summary>يحوّل نص الهدف (اسم أو معرّف) إلى معرّف حسب العلاقة.</summary>
    private Guid? ResolveTargetId(RuleScope scope, string? key)
    {
        if (scope == RuleScope.School) return null;
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (Guid.TryParse(key, out var g)) return g;

        string k = key.Trim();
        static bool Hit(string? a, string k) => !string.IsNullOrWhiteSpace(a) &&
            (a.Trim().Equals(k, StringComparison.OrdinalIgnoreCase) || a.Contains(k, StringComparison.OrdinalIgnoreCase));

        return scope switch
        {
            RuleScope.Teacher => Teachers?.Teachers?.FirstOrDefault(t => Hit(t.FullName, k) || Hit(t.DisplayShortName, k) || Hit(t.FirstName, k))?.Id is { } tid ? (Guid)tid : null,
            RuleScope.Class => Classes?.Classes?.FirstOrDefault(c => Hit(c.FullName, k) || Hit(c.Name, k))?.Id is { } cid ? (Guid)cid : null,
            RuleScope.Subject => Subjects?.Subjects?.FirstOrDefault(s => Hit(s.Name, k) || Hit(s.ShortName, k))?.Id is { } sid ? (Guid)sid : null,
            RuleScope.Room => Rooms?.Rooms?.FirstOrDefault(r => Hit(r.Name, k) || Hit(r.ShortName, k))?.Id is { } rid ? (Guid)rid : null,
            _ => null
        };
    }

    [RelayCommand]
    private void AddRule()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("لا يوجد جدول مفتوح.", "القواعد", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.RuleEditorDialog(
            Teachers.Teachers.Select(t => ((Guid)t.Id, t.FullName)).ToList(),
            Classes.Classes.Select(c => ((Guid)c.Id, c.FullName)).ToList(),
            Subjects.Subjects.Select(s => ((Guid)s.Id, s.Name)).ToList(),
            Rooms.Rooms.Select(r => ((Guid)r.Id, r.Name)).ToList())
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        _currentTimetable.Rules.Add(dialog.Result);
        RebuildRuleGroups();
    }

    /// <summary>تحرير قاعدة موجودة عبر المحرر الموجّه.</summary>
    [RelayCommand]
    private void EditRule(RuleRow? row)
    {
        if (row is null || _currentTimetable == null) return;

        var dialog = new Views.RuleEditorDialog(
            Teachers.Teachers.Select(t => ((Guid)t.Id, t.FullName)).ToList(),
            Classes.Classes.Select(c => ((Guid)c.Id, c.FullName)).ToList(),
            Subjects.Subjects.Select(s => ((Guid)s.Id, s.Name)).ToList(),
            Rooms.Rooms.Select(r => ((Guid)r.Id, r.Name)).ToList(),
            row.Model)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var index = _currentTimetable.Rules.IndexOf(row.Model);
        if (index >= 0)
            _currentTimetable.Rules[index] = dialog.Result;
        else
            _currentTimetable.Rules.Add(dialog.Result);

        RebuildRuleGroups();
    }

    [RelayCommand]
    private void RemoveRule(RuleRow? row)
    {
        if (row == null || _currentTimetable == null) return;
        var confirm = MessageBox.Show($"حذف البند «{row.Title}»؟", "تأكيد الحذف",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        _currentTimetable.Rules.Remove(row.Model);
        RebuildRuleGroups();
    }

    /// <summary>حفظ التحرير: يعيد ربط الهدف من النص المكتوب (مثلاً نقل القاعدة من معلم إلى معلم آخر) ثم يعيد بناء القوائم.</summary>
    [RelayCommand]
    private void SaveRuleEdit(RuleRow? row)
    {
        if (row == null || _currentTimetable == null) return;
        if (row.Scope == RuleScope.School)
        {
            row.Model.TargetId = null;
        }
        else
        {
            var resolved = ResolveTargetId(row.Scope, row.TargetName);
            if (resolved == null)
            {
                MessageBox.Show($"لم يتم العثور على «{row.TargetName}» ضمن {RuleTexts.Scope(row.Scope)}.",
                    "القواعد", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            row.Model.TargetId = resolved;
        }
        row.Model.UpdatedAt = DateTime.Now;
        RebuildRuleGroups();
    }

    /// <summary>نقل بند إلى معلم آخر بالاسم (مثلاً من أحمد إلى محمد).</summary>
    public bool MoveRuleToTarget(RuleRow row, string targetKey)
    {
        var id = ResolveTargetId(row.Scope, targetKey);
        if (id == null) return false;
        row.Model.TargetId = id;
        row.Model.UpdatedAt = DateTime.Now;
        RebuildRuleGroups();
        return true;
    }

    /// <summary>
    /// يزامن الحصص مع الإسنادات عبر الخدمة المركزية (إنشاء الناقص وحذف الزائد).
    /// </summary>
    private (int Created, int Removed) SyncLessonsFromAssignments()
    {
        if (_currentTimetable == null) return (0, 0);
        var result = TeachingAssignmentLessonSync.Sync(
            _currentTimetable,
            id => FindTeacherById(id),
            id => FindSubjectById(id),
            id => FindClassById(id));
        return (result.Created, result.Removed);
    }

    /// <summary>
    /// يولّد حصصًا غير مُسندة (Period = 0) من كل إسناد بعدد حصصه الأسبوعية، فتظهر في الشريط السفلي.
    /// يستبدل الحصص غير المُسندة السابقة لنفس (المعلم، المادة، الفصل) حتى لا تتكرر.
    /// </summary>
    [RelayCommand]
    private void GenerateFromAssignments()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("لا يوجد جدول مفتوح.", "الإسناد", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentTimetable.Assignments.Count == 0)
        {
            MessageBox.Show("لا توجد إسنادات بعد. أضف إسنادًا أولاً.", "الإسناد",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (created, removed) = SyncLessonsFromAssignments();

        UpdateTimetableGrid(_currentTimetable);
        _logger.LogInformation("توليد الحصص من الإسناد: أُنشئت {Created}، استُبدلت {Removed}", created, removed);
        MessageBox.Show(
            $"أُنشئت {created} حصة غير مُسندة (في الشريط السفلي)." +
            (removed > 0 ? $"\nحُذفت {removed} حصة زائدة عن عدد حصص الإسناد." : "") +
            "\n\nاضغط «توليد الجدول» الآن.",
            "الإسناد", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ===== وضع عرض الجدول (بأسلوب aSc: مصفوفة المدرسة أو جدول كيان واحد) =====

    /// <summary>وضع عرض الجدول الرئيسي</summary>
    [ObservableProperty]
    private TimetableViewMode _viewMode = TimetableViewMode.SchoolMatrix;

    partial void OnViewModeChanged(TimetableViewMode value)
    {
        RebuildEntityOptions();
        OnPropertyChanged(nameof(IsEntityPickerEnabled));
        OnPropertyChanged(nameof(IsMatrixMode));
        if (_currentTimetable != null) UpdateTimetableGrid(_currentTimetable);
    }

    /// <summary>الكيان المحدد في محدد العرض</summary>
    [ObservableProperty]
    private EntityOption? _selectedEntityOption;

    partial void OnSelectedEntityOptionChanged(EntityOption? value)
    {
        if (_suppressEntityRebuild) return;
        UpdateSelectedViewTitle();
        if (_currentTimetable != null) UpdateTimetableGrid(_currentTimetable);
    }

    private bool _suppressEntityRebuild;

    /// <summary>خيارات الكيانات المعروضة في المحدد حسب وضع العرض</summary>
    public ObservableCollection<EntityOption> EntityOptions { get; } = new();

    /// <summary>هل محدد الكيان مفعّل (كل الأوضاع عدا مصفوفة المدرسة)</summary>
    public bool IsEntityPickerEnabled => ViewMode != TimetableViewMode.SchoolMatrix;

    /// <summary>هل عمود "الفصل" ظاهر في الشبكة (مصفوفة المدرسة فقط)</summary>
    public bool IsMatrixMode => ViewMode == TimetableViewMode.SchoolMatrix;

    /// <summary>عنوان محدد الكيان حسب الوضع الحالي</summary>
    public string EntityPickerLabel => ViewMode switch
    {
        TimetableViewMode.ByClass => "الفصل:",
        TimetableViewMode.ByTeacher => "المعلم:",
        TimetableViewMode.ByRoom => "القاعة:",
        TimetableViewMode.BySubject => "المادة:",
        TimetableViewMode.ByStudent => "الطالب:",
        TimetableViewMode.ByGroup => "المجموعة:",
        _ => "الكيان:"
    };

    /// <summary>عنوان العرض الحالي (يظهر فوق الشبكة)</summary>
    [ObservableProperty]
    private string _selectedViewTitle = "مصفوفة المدرسة";

    /// <summary>إعادة بناء قائمة الكيانات في محدد العرض مع الحفاظ على التحديد السابق</summary>
    private void RebuildEntityOptions()
    {
        _suppressEntityRebuild = true;
        try
        {
            var previousId = SelectedEntityOption?.Id;

            EntityOptions.Clear();

            var lessons = _currentTimetable?.Lessons ?? new List<Lesson>();

            IEnumerable<EntityOption> options = ViewMode switch
            {
                TimetableViewMode.ByClass => UnionOptions(
                    Classes.Classes
                        .OrderBy(c => c.GradeLevel).ThenBy(c => c.Section).ThenBy(c => c.Name)
                        .Select(c => new EntityOption(c.Id, c.ShortName ?? c.Name)),
                    lessons.Select(l => new EntityOption(l.ClassId, l.Class?.FullName ?? "فصل غير معروف"))),
                TimetableViewMode.ByTeacher => UnionOptions(
                    Teachers.Teachers
                        .OrderBy(t => t.FullName)
                        .Select(t => new EntityOption(t.Id, t.FullName)),
                    lessons.Select(l => new EntityOption(l.TeacherId, l.Teacher?.FullName ?? "معلم غير معروف"))),
                TimetableViewMode.ByRoom => UnionOptions(
                    Rooms.Rooms
                        .OrderBy(r => r.Name)
                        .Select(r => new EntityOption(r.Id, r.Name)),
                    lessons.Where(l => l.RoomId != null)
                        .Select(l => new EntityOption(l.RoomId!.Value, l.Room?.Name ?? "قاعة غير معروفة"))),
                TimetableViewMode.BySubject => UnionOptions(
                    Subjects.Subjects
                        .OrderBy(s => s.Name)
                        .Select(s => new EntityOption(s.Id, s.Name)),
                    lessons.Select(l => new EntityOption(l.SubjectId, l.Subject?.Name ?? "مادة غير معروفة"))),
                TimetableViewMode.ByStudent => UnionOptions(
                    (_currentTimetable?.Students ?? new List<Student>())
                        .OrderBy(s => s.Name)
                        .Select(s => new EntityOption(s.Id, s.Name)),
                    Enumerable.Empty<EntityOption>()),
                TimetableViewMode.ByGroup => UnionOptions(
                    (_currentTimetable?.Groups ?? new List<Group>())
                        .OrderBy(g => g.Name)
                        .Select(g => new EntityOption(g.Id, g.Name)),
                    lessons.Where(l => l.GroupId != null)
                        .Select(l => new EntityOption(l.GroupId!.Value, l.Group?.Name ?? "مجموعة غير معروفة"))),
                _ => Enumerable.Empty<EntityOption>()
            };

            foreach (var option in options) EntityOptions.Add(option);

            // التحديد: السابق أولاً، ثم أول كيان له حصص في الجدول الحالي، ثم الأول
            var lessonEntityIds = new HashSet<Guid>(ViewMode switch
            {
                TimetableViewMode.ByClass => lessons.Select(l => (Guid)l.ClassId),
                TimetableViewMode.ByTeacher => lessons.Select(l => (Guid)l.TeacherId),
                TimetableViewMode.ByRoom => lessons.Where(l => l.RoomId != null).Select(l => (Guid)l.RoomId!.Value),
                TimetableViewMode.BySubject => lessons.Select(l => (Guid)l.SubjectId),
                TimetableViewMode.ByGroup => lessons.Where(l => l.GroupId != null).Select(l => l.GroupId!.Value),
                TimetableViewMode.ByStudent => (_currentTimetable?.Students ?? new List<Student>()).Select(s => s.Id),
                _ => Enumerable.Empty<Guid>()
            });

            SelectedEntityOption = EntityOptions.FirstOrDefault(o => o.Id == previousId)
                                   ?? EntityOptions.FirstOrDefault(o => lessonEntityIds.Contains(o.Id))
                                   ?? EntityOptions.FirstOrDefault();

            UpdateSelectedViewTitle();
        }
        finally
        {
            _suppressEntityRebuild = false;
        }
    }

    /// <summary>
    /// دمج خيارات الكيانات: كيانات الإدارة أولاً ثم كيانات الجدول غير المسجّلة (بدون تكرار)
    /// (نفس سياسة مصفوفة المدرسة: حصص كيانات غير مسجّلة تبقى ظاهرة)
    /// </summary>
    private static IEnumerable<EntityOption> UnionOptions(IEnumerable<EntityOption> primary, IEnumerable<EntityOption> extra)
    {
        var seen = new HashSet<Guid>();
        foreach (var option in primary)
        {
            if (seen.Add(option.Id)) yield return option;
        }
        foreach (var option in extra)
        {
            if (seen.Add(option.Id)) yield return option;
        }
    }

    private void UpdateSelectedViewTitle()
    {
        var name = SelectedEntityOption?.Name;
        SelectedViewTitle = ViewMode switch
        {
            TimetableViewMode.ByClass => name == null ? "جدول الفصل" : $"جدول الفصل: {name}",
            TimetableViewMode.ByTeacher => name == null ? "جدول المعلم" : $"جدول المعلم: {name}",
            TimetableViewMode.ByRoom => name == null ? "جدول القاعة" : $"جدول القاعة: {name}",
            TimetableViewMode.BySubject => name == null ? "جدول المادة" : $"جدول المادة: {name}",
            TimetableViewMode.ByStudent => name == null ? "جدول الطالب" : $"جدول الطالب: {name}",
            TimetableViewMode.ByGroup => name == null ? "جدول المجموعة" : $"جدول المجموعة: {name}",
            _ => "مصفوفة المدرسة"
        };
    }

    // بيانات الجدول
    private Timetable? _currentTimetable;
    private ObservableCollection<TimetableGridRow> _timetableGridRows = new();
    private Lesson? _selectedLesson;

    public ObservableCollection<TimetableGridRow> TimetableGridRows => _timetableGridRows;

    // ===== عرض aSc الجديد: الأيام صفوفًا، الحصص صفوفًا فرعية، الكيانات أعمدة =====
    private AscGridView _ascGrid = new();
    public AscGridView AscGrid
    {
        get => _ascGrid;
        private set => SetProperty(ref _ascGrid, value);
    }

    private int _ascGridRevision;
    /// <summary>يزداد عند كل إعادة بناء لعرض aSc حتى تُعيد الواجهة بناء الشبكة.</summary>
    public int AscGridRevision
    {
        get => _ascGridRevision;
        private set => SetProperty(ref _ascGridRevision, value);
    }

    // ===== الحصص التي تحتاج انتباهًا (متعارضة/بلا قاعة/غير معبأة) =====
    private readonly ObservableCollection<ProblemLessonItem> _problemLessons = new();
    public ObservableCollection<ProblemLessonItem> ProblemLessons => _problemLessons;

    private int _problemCount;
    public int ProblemCount
    {
        get => _problemCount;
        private set => SetProperty(ref _problemCount, value);
    }

    // ===== بطاقات المعلم: الحصص في انتظار التوليد (غير موضوعة في الشبكة) =====
    private readonly ObservableCollection<ProblemLessonItem> _pendingCards = new();
    public ObservableCollection<ProblemLessonItem> PendingCards => _pendingCards;

    private int _pendingCardsCount;
    public int PendingCardsCount
    {
        get => _pendingCardsCount;
        private set => SetProperty(ref _pendingCardsCount, value);
    }

    private bool _isProblemPanelOpen = true;
    public bool IsProblemPanelOpen
    {
        get => _isProblemPanelOpen;
        set => SetProperty(ref _isProblemPanelOpen, value);
    }

    public Lesson? SelectedLesson
    {
        get => _selectedLesson;
        set
        {
            if (SetProperty(ref _selectedLesson, value))
            {
                UpdateSelectedItemProperties();
            }
        }
    }

    /// <summary>
    /// معالجة تحديد خلية في شبكة الجدول (يُستدعى من الكود الخلفي)
    /// </summary>
    public void SelectCell(int displayIndex, TimetableGridRow row)
    {
        // أعمدة المصفوفة: 0 = الفصل، 1 = الفترة، 2..8 = أيام الأسبوع
        if (_currentTimetable == null || row == null || displayIndex < 2)
        {
            SelectedLesson = null;
            return;
        }

        var days = Enum.GetValues<DomainDayOfWeek>();
        var dayIndex = displayIndex - 2;
        if (dayIndex >= days.Length)
        {
            SelectedLesson = null;
            return;
        }

        var day = days[dayIndex];

        // في أوضاع الكيان الواحد: طابق الكيان المعروض بدلاً من الفصل
        if (ViewMode != TimetableViewMode.SchoolMatrix)
        {
            var entityId = SelectedEntityOption?.Id;
            SelectedLesson = entityId == null
                ? null
                : _currentTimetable.Lessons.FirstOrDefault(l => l.Day == day
                                                                 && l.Period.Value == row.Period.Value
                                                                 && MatchesCurrentEntity(l, entityId.Value));
            return;
        }

        SelectedLesson = _currentTimetable.Lessons
            .FirstOrDefault(l => l.Day == day
                                 && l.Period.Value == row.Period.Value
                                 && l.ClassId == row.ClassId);
    }

    /// <summary>
    /// إيجاد الحصة في خلية محددة دون تغيير التحديد (يُستخدم للسحب والإفلات).
    /// displayIndex: فهرس العمود المعروض (0=الفصل، 1=الفترة، 2..8=الأيام).
    /// </summary>
    public Lesson? GetLessonAtCell(int displayIndex, TimetableGridRow row)
    {
        if (_currentTimetable == null || row == null || displayIndex < 2) return null;

        var days = Enum.GetValues<DomainDayOfWeek>();
        var dayIndex = displayIndex - 2;
        if (dayIndex >= days.Length) return null;
        var day = days[dayIndex];

        if (ViewMode != TimetableViewMode.SchoolMatrix)
        {
            var entityId = SelectedEntityOption?.Id;
            if (entityId == null) return null;
            return _currentTimetable.Lessons.FirstOrDefault(l => l.Day == day
                                                               && l.Period.Value == row.Period.Value
                                                               && MatchesCurrentEntity(l, entityId.Value));
        }

        return _currentTimetable.Lessons.FirstOrDefault(l => l.Day == day
                                                              && l.Period.Value == row.Period.Value
                                                              && l.ClassId == row.ClassId);
    }

    /// <summary>
    /// تحريك حصة إلى يوم/فترة أخرى (يُستدعى من السحب والإفلات) مع التحقق من التضاربات.
    /// سلوك aSc: إن كانت الخلية الهدف تشغلها حصة متعارضة (نفس المعلم/القاعة/الفصل) يتم
    /// التبديل بين موقعَي الحصتين (Swap)، وإلا تُنقل الحصة المصدر ببساطة.
    /// </summary>
    public bool MoveLessonByDrag(Lesson lesson, DomainDayOfWeek targetDay, PeriodNumber targetPeriod, out string? errorMessage)
    {
        errorMessage = null;
        if (_currentTimetable == null || lesson == null)
        {
            errorMessage = "لا يوجد جدول أو حصة.";
            return false;
        }

        // 1) لا تنقل حصة مثبتة (IsFixed) — aSc يمنع النقل اليدوي للحصة المثبتة
        if (lesson.IsFixed)
        {
            errorMessage = "هذه الحصة مثبتة ولا يجوز تحريكها يدوياً.";
            return false;
        }

        // 2) لا حاجة لحركة إن كانت الوجهة مطابقة للموضع الحالي
        var oldDay = lesson.Day;
        var oldPeriod = lesson.Period;
        if (oldDay == targetDay && oldPeriod.Value == targetPeriod.Value)
        {
            return true; // الموضع نفسه — لا شيء للتغيير
        }

        // 3) ابحث عن حصة تحتل الخلية الهدف وتتعارض مع الحصة المصدر
        //    (تضارب = نفس المعلم، أو نفس القاعة، أو نفس الفصل)
        var targetOccupant = _currentTimetable.Lessons.FirstOrDefault(l =>
            (Guid)l.Id != (Guid)lesson.Id
            && l.Day == targetDay
            && l.Period.Value == targetPeriod.Value
            && ((Guid)l.TeacherId == (Guid)lesson.TeacherId
                || (l.RoomId != null && lesson.RoomId != null && (Guid)l.RoomId == (Guid)lesson.RoomId)
                || (Guid)l.ClassId == (Guid)lesson.ClassId));

        PushUndoState(); // احفظ الحالة للتراجع قبل أي تغيير

        if (targetOccupant != null)
        {
            // 4) تبديل (Swap): حصة متعارضة في الخلية الهدف
            if (targetOccupant.IsFixed)
            {
                errorMessage = "الخلية الهدف تشغلها حصة مثبتة ولا يجوز تبديلها.";
                return false;
            }

            targetOccupant.Reschedule(oldDay, oldPeriod);
            lesson.Reschedule(targetDay, targetPeriod);
        }
        else
        {
            // 5) الخلية الهدف فارغة من التعارض — انقل الحصة ببساطة
            lesson.Reschedule(targetDay, targetPeriod);
        }

        // 6) أعد بناء الشبكة لعرض النتيجة
        UpdateTimetableGrid(_currentTimetable);
        SelectedLesson = lesson;
        return true;
    }

    /// <summary>
    /// تحريك حصة إلى خلية في مصفوفة aSc (يوم × فترة × فصل) مع التحقق من التضاربات.
    /// - نفس الفصل: تبديل إن كانت الخلية مشغولة، وإلا نقل بسيط.
    /// - فصل مختلف: يمنع القفز فوق تعارض (معلم/قاعة)، وإلا يعيد إسناد الفصل وينقل.
    /// </summary>
    public bool MoveLessonByDrag(Lesson lesson, DomainDayOfWeek targetDay, PeriodNumber targetPeriod, Guid targetClassId, out string? errorMessage)
    {
        errorMessage = null;
        if (_currentTimetable == null || lesson == null)
        {
            errorMessage = "لا يوجد جدول أو حصة.";
            return false;
        }

        if (lesson.IsFixed)
        {
            errorMessage = "هذه الحصة مثبتة ولا يجوز تحريكها يدوياً.";
            return false;
        }

        var oldDay = lesson.Day;
        var oldPeriod = lesson.Period;
        var sameClass = (Guid)lesson.ClassId == targetClassId;

        if (sameClass && oldDay == targetDay && oldPeriod.Value == targetPeriod.Value)
            return true;

        // القرار من القاعدة المشتركة نفسها التي يستخدمها التلوين والتشخيص
        var (state, reason) = AwadTimetable.Core.Services.LessonPlacementEvaluator.Evaluate(
            _currentTimetable, lesson, targetDay, targetPeriod, targetClassId);
        if (state == AwadTimetable.Core.Services.PlacementState.Forbidden)
        {
            errorMessage = reason;
            return false;
        }

        var targetOccupant = _currentTimetable.Lessons.FirstOrDefault(l =>
            (Guid)l.Id != (Guid)lesson.Id
            && l.Day == targetDay
            && l.Period.Value == targetPeriod.Value
            && ((Guid)l.ClassId == targetClassId
                || (Guid)l.TeacherId == (Guid)lesson.TeacherId
                || (l.RoomId != null && lesson.RoomId != null && (Guid)l.RoomId == (Guid)lesson.RoomId)));

        PushUndoState();

        if (sameClass)
        {
            // state == Acceptable يعني تبديل مع حصة من الفصل نفسه؛ state == Good يعني خلية فارغة
            if (targetOccupant != null)
                targetOccupant.Reschedule(oldDay, oldPeriod);
            lesson.Reschedule(targetDay, targetPeriod);
        }
        else
        {
            // state == Good هنا فقط (المشغول رُفض أعلاه)
            lesson.ChangeClass(new ClassId(targetClassId));
            lesson.Reschedule(targetDay, targetPeriod);
        }

        UpdateTimetableGrid(_currentTimetable);
        SelectedLesson = lesson;
        return true;
    }

    /// <summary>حالة موضع حصة مسحوبة قبل الإفلات: ممنوع / مقبول (بتبديل) / جيد (خلية فارغة).</summary>
    public enum DropPositionState
    {
        Forbidden = 0,
        Acceptable = 1,
        Good = 2
    }

    /// <summary>
    /// يقيّم موضعًا محتملًا لحصة مسحوبة بنفس قواعد MoveLessonByDrag، لتلوين الخلايا أثناء السحب.
    /// يعيد الحالة وسببها (لعرضه كتلميح).
    /// </summary>
    public (DropPositionState State, string Reason) EvaluateDropPosition(
        Lesson lesson, DomainDayOfWeek targetDay, PeriodNumber targetPeriod, Guid targetClassId)
    {
        if (_currentTimetable == null || lesson == null)
            return (DropPositionState.Forbidden, "لا يوجد جدول أو حصة.");

        var (state, reason) = AwadTimetable.Core.Services.LessonPlacementEvaluator.Evaluate(
            _currentTimetable, lesson, targetDay, targetPeriod, targetClassId);
        return ((DropPositionState)(int)state, reason);
    }

    /// <summary>
    /// تشخيص حصة غير مُوضوعة: يفحص كل (يوم × فترة) في الجدول ويجمع سبب الرفض لكل موضع،
    /// ثم يعيد ملخصًا بالأسباب الأكثر تكرارًا وعدد المواضع المتاحة.
    /// </summary>
    public LessonDiagnosis DiagnoseUnscheduledLesson(Lesson lesson)
    {
        var result = new LessonDiagnosis { LessonTitle = lesson.Subject?.Name ?? "حصة" };
        if (_currentTimetable == null)
        {
            result.Summary = "لا يوجد جدول مفتوح.";
            return result;
        }

        var core = AwadTimetable.Core.Services.LessonPlacementEvaluator.Diagnose(_currentTimetable, lesson);
        var goodSlots = core.GoodSlots
            .Select(s => $"{AssistantHelpers.DayName(s.Day)} الفترة {s.Period}")
            .ToList();

        result.FreeSlotsCount = goodSlots.Count;
        result.GoodSlots = goodSlots;
        result.ReasonCounts = core.ReasonCounts.ToList();
        result.Summary = goodSlots.Count > 0
            ? $"يوجد {goodSlots.Count} موضعًا مناسبًا لهذه الحصة."
            : "لا يوجد أي موضع مناسب لهذه الحصة. أكثر الأسباب تكرارًا موضّحة أدناه.";

        return result;
    }

    public IReadOnlyList<object> ImportMenuItems { get; }
    public IReadOnlyList<object> ExportMenuItems { get; }
    public ContextMenu LessonContextMenu { get; }

    public MainWindowViewModel(
        IServiceProvider services,
        ILogger<MainWindowViewModel> logger,
        ITimetableGenerator generator,
        IConstraintValidator validator,
        IConflictDetector detector)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _generatorResolver = _services.GetRequiredService<IGeneratorResolver>();
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));

        // تهيئة القوائم
        ImportMenuItems = CreateImportMenuItems();
        ExportMenuItems = CreateExportMenuItems();
        LessonContextMenu = CreateLessonContextMenu();

        // تهيئة نماذج عرض الإدارة
        Teachers = _services.GetRequiredService<TeacherManagementViewModel>();
        Subjects = _services.GetRequiredService<SubjectManagementViewModel>();
        Rooms = _services.GetRequiredService<RoomManagementViewModel>();
        Classes = _services.GetRequiredService<ClassManagementViewModel>();
        Constraints = _services.GetRequiredService<ConstraintEditorViewModel>();
        _logger.LogInformation("تم تهيئة نماذج الإدارة بنجاح");

        // تهيئة لوحة المحادثة الذكية
        AiChatPanelViewModel = _services.GetService<AIChatPanelViewModel>();

        // تحديث الإحصائيات تلقائياً عند تغيير قوائم الإدارة
        Teachers.Teachers.CollectionChanged += OnManagementCollectionChanged;
        Subjects.Subjects.CollectionChanged += OnManagementCollectionChanged;
        Rooms.Rooms.CollectionChanged += OnManagementCollectionChanged;
        Classes.Classes.CollectionChanged += OnManagementCollectionChanged;

        // قائمة اختيار الفصول للإسناد الجماعي
        Classes.Classes.CollectionChanged += (_, _) => RebuildAssignmentClassOptions();
        RebuildAssignmentClassOptions();

        // إعادة بناء الشبكة عند تغيّر مظهر معلم (اسم مختصر/لون)
        _services.GetRequiredService<TeacherAppearanceService>().Changed += () =>
        {
            var current = _currentTimetable;
            if (current == null) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(() => UpdateTimetableGrid(current));
            else
                UpdateTimetableGrid(current);
        };

        // إعادة بناء الشبكة عند تعديل الجدول بواسطة أدوات المساعد الذكي
        _services.GetRequiredService<AssistantToolContext>().Changed += () =>
        {
            var current = _currentTimetable;
            if (current == null) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(() => UpdateTimetableGrid(current));
            else
                UpdateTimetableGrid(current);
        };

        // إعادة تحميل قوائم الإدارة عند تغيير البيانات المرجعية (معلم/مادة/فصل/قاعة) بواسطة المساعد الذكي
        _services.GetRequiredService<AssistantToolContext>().DataChanged += () =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void Reload()
            {
                _ = Teachers.LoadAsync();
                _ = Subjects.LoadAsync();
                _ = Rooms.LoadAsync();
                _ = Classes.LoadAsync();
            }
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Reload);
            else
                Reload();
        };

        // تحميل تفضيلات العرض (اللوحتان + الزوم)
        LoadUiPreferences();

        // تحديث الوقت الحالي
        _ = Task.Run(UpdateDateTimeLoop);

        // تحميل الإحصائيات بعد الإقلاع
        _ = Task.Run(RefreshStatsAsync);

        // فتح آخر جدول محفوظ تلقائيًا عند الإقلاع (على خيط الواجهة لتفادي سباق القوائم)
        _ = LastTimetableAutoLoadAsync();
    }

    /// <summary>فتح آخر جدول محفوظ من قاعدة البيانات عند بدء التشغيل (لا رسائل إن لم يوجد)</summary>
    private async Task LastTimetableAutoLoadAsync()
    {
        try
        {
            var storage = _services.GetRequiredService<TimetableStorageService>();
            var items = await storage.ListAsync();
            if (items.Count == 0 || _currentTimetable != null) return;

            // جرّب أحدث الجداول بالترتيب: جدول واحد تالف لا يمنع فتح الباقي
            Timetable? loaded = null;
            foreach (var item in items.Take(5))
            {
                try
                {
                    loaded = await storage.LoadAsync(item.Id);
                }
                catch (Exception loadEx)
                {
                    _logger.LogWarning(loadEx, "تعذر فتح الجدول {Id} تلقائيًا — تجربة الجدول التالي", item.Id);
                }

                if (loaded != null) break;
            }

            if (loaded == null || _currentTimetable != null) return;

            _currentTimetable = loaded;
            CurrentTimetableName = _currentTimetable.Name;
            TimetableStatusText = _currentTimetable.Status.ToString();

            // جدول محفوظ ينتمي لمساحة عمل محددة: بدّل السياق ثم حمّل قوائمها
            await SwitchWorkspaceAsync(_currentTimetable);
            ResetHistory();

            await Application.Current.Dispatcher.InvokeAsync(() => UpdateTimetableGrid(_currentTimetable!));
            _logger.LogInformation("تم فتح آخر جدول محفوظ تلقائيًا: {Name} ({Count} حصة)", _currentTimetable.Name, _currentTimetable.Lessons.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر فتح آخر جدول محفوظ تلقائيًا");
        }
    }

    // الأوامر
    [RelayCommand]
    private async Task NewTimetable()
    {
        var dialog = new NewTimetableDialog();
        if (dialog.ShowDialog() == true && dialog.CreatedTimetable != null)
        {
            _currentTimetable = dialog.CreatedTimetable;
            CurrentTimetableName = _currentTimetable.Name;
            TimetableStatusText = _currentTimetable.Status.ToString();

            // جدول جديد = مساحة عمل مستقلة: بدّل سياق المدرسة الحالية قبل تحميل القوائم
            _services.GetRequiredService<CurrentSchoolService>().SetCurrent(_currentTimetable.SchoolId);
            CurrentSchoolName = ResolveSchoolName();

            // أفرغ القوائم فوراً، لا بعد انتهاء الاستعلامات غير المتزامنة، كيلا تظهر
            // فصول المدرسة السابقة مؤقتاً وكأنها جزء من الجدول الجديد.
            ClearReferenceLists();
            RebuildAssignments();
            RebuildRuleGroups();
            UpdateTimetableGrid(_currentTimetable);
            ResetHistory();

            await ReloadReferenceListsAsync();

            _logger.LogInformation("تم إنشاء جدول جديد: {Name} (مساحة عمل: {SchoolId})",
                _currentTimetable.Name, _currentTimetable.SchoolId.Value);
        }
    }

    /// <summary>يمسح بيانات العرض التابعة لمساحة العمل السابقة قبل تحميل المساحة الجديدة.</summary>
    private void ClearReferenceLists()
    {
        Teachers.SelectedTeacher = null;
        Teachers.Teachers.Clear();
        Subjects.SelectedSubject = null;
        Subjects.Subjects.Clear();
        Classes.SelectedClass = null;
        Classes.Classes.Clear();
        Rooms.SelectedRoom = null;
        Rooms.Rooms.Clear();
        Rooms.Teachers.Clear();
        RebuildAssignmentClassOptions();
    }

    /// <summary>إعادة تحميل قوائم المعلمين/المواد/الفصول/القاعات للمدرسة الحالية.</summary>
    private async Task ReloadReferenceListsAsync()
    {
        await Task.WhenAll(Teachers.LoadAsync(), Subjects.LoadAsync(), Classes.LoadAsync(), Rooms.LoadAsync());
        RebuildAssignmentClassOptions();
        RebuildAssignments();
    }

    [RelayCommand]
    private async Task OpenTimetableAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "ملفات الجداول (*.timetable)|*.timetable|جميع الملفات (*.*)|*.*",
            Title = "فتح جدول",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                var json = File.ReadAllText(openFileDialog.FileName);
                var timetable = System.Text.Json.JsonSerializer.Deserialize<Timetable>(json, JsonOpts);
                if (timetable != null)
                {
                    _currentTimetable = timetable;
                    CurrentTimetableName = _currentTimetable.Name;
                    TimetableStatusText = _currentTimetable.Status.ToString();

                    _services.GetRequiredService<CurrentSchoolService>().SetCurrent(_currentTimetable.SchoolId);
                    await ReloadReferenceListsAsync();

                    UpdateTimetableGrid(_currentTimetable);
                    ResetHistory();
                    _logger.LogInformation("تم فتح جدول: {Name}", _currentTimetable.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في فتح الجدول");
                MessageBox.Show($"فشل في فتح الجدول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void SaveTimetable()
    {
        if (_currentTimetable == null)
        {
            SaveAsTimetable();
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "ملفات الجداول (*.timetable)|*.timetable|جميع الملفات (*.*)|*.*",
            Title = "حفظ الجدول",
            FileName = $"{_currentTimetable.Name}.timetable",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_currentTimetable, JsonOpts);
                File.WriteAllText(saveFileDialog.FileName, json);
                _logger.LogInformation("تم حفظ الجدول: {Path}", saveFileDialog.FileName);
                MessageBox.Show("تم حفظ الجدول بنجاح", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في حفظ الجدول");
                MessageBox.Show($"فشل في حفظ الجدول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void SaveAsTimetable()
    {
        if (_currentTimetable == null) return;
        var saveFileDialog = new SaveFileDialog
        {
            Filter = "ملفات الجداول (*.timetable)|*.timetable|جميع الملفات (*.*)|*.*",
            Title = "حفظ الجدول باسم",
            FileName = $"{_currentTimetable.Name}.timetable",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_currentTimetable, JsonOpts);
                File.WriteAllText(saveFileDialog.FileName, json);
                _logger.LogInformation("تم حفظ الجدول باسم: {Path}", saveFileDialog.FileName);
                MessageBox.Show("تم حفظ الجدول بنجاح", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطأ في حفظ الجدول");
                MessageBox.Show($"فشل في حفظ الجدول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>فتح نافذة إعداد توقيت الفترات (يمكن تخصيصه حسب اليوم والمرحلة)</summary>
    [RelayCommand]
    private void OpenPeriodTimes()
    {
        _logger.LogInformation("أمر توقيت الفترات: جدول موجود={HasTimetable}", _currentTimetable != null);
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.PeriodTimesDialog(_currentTimetable.PeriodTimes)
        {
            Owner = Application.Current.MainWindow
        };
        _logger.LogInformation("فتح نافذة توقيت الفترات");
        if (dialog.ShowDialog() == true)
        {
            PushUndoState();
            _currentTimetable.PeriodTimes = dialog.ResultTimes;
            UpdateTimetableGrid(_currentTimetable);
            _logger.LogInformation("تم تحديث توقيت الفترات: {Count} فترة", dialog.ResultTimes.Count);
        }
    }

    [RelayCommand]
    private void OpenStageWorkConfig()
    {
        _logger.LogInformation("أمر أيام العمل: جدول موجود={HasTimetable}", _currentTimetable != null);
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.StageWorkConfigDialog(_currentTimetable.StageConfigs)
        {
            Owner = Application.Current.MainWindow
        };
        _logger.LogInformation("فتح نافذة أيام العمل والحصص اليومية");
        if (dialog.ShowDialog() == true)
        {
            var oldDays = _currentTimetable.StageConfigs.SelectMany(c => c.WorkDays).Distinct().OrderBy(d => d).ToList();
            var newDays = dialog.ResultConfigs.SelectMany(c => c.WorkDays).Distinct().OrderBy(d => d).ToList();

            PushUndoState();
            _currentTimetable.StageConfigs = dialog.ResultConfigs;

            // عند تغيير الأيام: إزاحة الحصص تلقائياً (للخلف أو للأمام)
            if (oldDays.Count > 0 && newDays.Count > 0 && !oldDays.SequenceEqual(newDays))
            {
                var moved = DayShifter.Shift(_currentTimetable, oldDays, newDays);
                UpdateTimetableGrid(_currentTimetable);
                _ = SaveToDatabaseSilentlyAsync();

                MessageBox.Show(
                    $"تم تحديث أيام الدراسة وإزاحة {moved} حصة تلقائياً وفق التغيير.",
                    "أيام العمل", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            _logger.LogInformation("تم تحديث إعدادات المراحل: {Count} مرحلة", dialog.ResultConfigs.Count);
        }
    }

    /// <summary>تعبئة خريطة معرف الفصل → رقم الصف (يستخدمها الحلّال لتحديد مرحلة الفصل وأيام عمله)</summary>
    private void FillClassGradeLevels(Timetable timetable)
    {
        timetable.ClassGradeLevels = Classes.Classes
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().GradeLevel);

        timetable.ClassNames = Classes.Classes
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>ربط المعلم/المادة/الفصل/الغرفة بكل حصة — العلاقات تأتي null من قاعدة البيانات،
    /// ولا تعمل بدونها أسماء العرض وقيود السعة وعبء المعلم</summary>
    private void AttachLessonNavigation(Timetable timetable)
    {
        var subjects = Subjects.Subjects.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var teachers = Teachers.Teachers.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var classes = Classes.Classes.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var rooms = Rooms.Rooms.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var lesson in timetable.Lessons)
        {
            subjects.TryGetValue((Guid)lesson.SubjectId, out var subject);
            teachers.TryGetValue((Guid)lesson.TeacherId, out var teacher);
            classes.TryGetValue((Guid)lesson.ClassId, out var @class);

            Room? room = null;
            if (lesson.RoomId is { } roomId)
                rooms.TryGetValue((Guid)roomId, out room);

            lesson.AttachReferences(subject, teacher, @class, room);
        }
    }

    /// <summary>تهيئة الجدول قبل التوليد/التحسين: مرحلة الفصل + قاعة الشعبة + الكيانات المرجعية</summary>
    private void PrepareForScheduling(Timetable timetable)
    {
        FillClassGradeLevels(timetable);

        // إن وُجدت قاعات مُعرَّفة: تُخصَّص قاعة لكل شعبة (وإلا تبقى الحصص بلا قاعة كما هو مقبول)
        var assigned = ClassRoomAssigner.Assign(timetable, Rooms.Rooms.ToList(), Classes.Classes.ToList());
        if (assigned > 0)
            _logger.LogInformation("تم إسناد قاعات لـ {Count} حصة حسب شعبة الفصل", assigned);

        AttachLessonNavigation(timetable);
    }

    /// <summary>إيقاف التوليد/التحسين الجارٍ</summary>
    [RelayCommand]
    private void CancelGeneration()
    {
        if (_generationCts is { IsCancellationRequested: false })
        {
            GenerationStatusText = "جارٍ الإلغاء…";
            _generationCts.Cancel();
            _logger.LogInformation("طلب المستخدم إيقاف التوليد");
        }
    }

    [RelayCommand]
    private async Task GenerateTimetableAsync()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsGenerating) return;

        _generationCts = new CancellationTokenSource();
        IsGenerating = true;
        GenerationStatusText = "جارٍ تجهيز الجدول…";

        try
        {
            PrepareForScheduling(_currentTimetable);

            // إن كان الجدول فارغاً: أنشئ الحصص تلقائياً من الإسنادات حتى يعمل زر التوليد مباشرة
            if (_currentTimetable.Lessons.Count == 0 && _currentTimetable.Assignments.Count > 0)
            {
                var (autoCreated, _) = SyncLessonsFromAssignments();
                if (autoCreated > 0)
                {
                    PrepareForScheduling(_currentTimetable);
                    GenerationStatusText = $"أُنشئت {autoCreated} حصة من الإسنادات — جارٍ التوليد…";
                    _logger.LogInformation("توليد تلقائي للحصص من الإسنادات: {Count}", autoCreated);
                }
            }

            if (_currentTimetable.Lessons.Count == 0)
            {
                GenerationStatusText = "لا توجد حصص للتوليد";
                MessageBox.Show(
                    "لا توجد حصص في الجدول ولا إسنادات.\n\n" +
                    "أضف إسنادات (معلم + مادة + فصل + عدد الحصص الأسبوعية) من لوحة «الإسناد»، " +
                    "ثم اضغط «توليد» — سيُنشئ التطبيق الحصص تلقائياً ويجدولها.\n\n" +
                    "ملاحظة: إن كنت تريد بداية جديدة تماماً، أنشئ الجدول من «جدول جديد» مع تحديد خيار «بداية جديدة».",
                    "التوليد", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var genSettings = _services.GetService<ISettingsService>();
            var complexityRaw = genSettings?.Get(SettingsKeys.GenerationComplexity, "Normal") ?? "Normal";
            var complexity = Enum.TryParse<GenerationComplexity>(complexityRaw, true, out var cx)
                ? cx : GenerationComplexity.Normal;
            var minutes = Math.Clamp(genSettings?.Get(SettingsKeys.GenerationTimeLimitMinutes, 5) ?? 5, 1, 240);
            var seed = genSettings?.Get(SettingsKeys.GenerationSeed, 0) ?? 0;
            var variants = Math.Clamp(genSettings?.Get(SettingsKeys.GenerationVariants, 1) ?? 1, 1, 16);

            var options = new GenerationOptions
            {
                MaxIterations = 1000,
                TimeLimit = TimeSpan.FromMinutes(minutes),
                UseParallel = true,
                Complexity = complexity,
                VariantCount = variants,
                DraftMode = genSettings?.Get(SettingsKeys.DraftMode, false) ?? false,
                AllowPartialSolution = genSettings?.Get(SettingsKeys.RelaxConstraints, false) ?? false,
                MaxConflictsAllowed = Math.Max(0, genSettings?.Get(SettingsKeys.MaxConflictsAllowed, 0) ?? 0),
                RandomSeed = seed > 0 ? seed : Environment.TickCount
            };

            var progress = new Progress<string>(msg => GenerationStatusText = msg);

            // اختيار المحرك حسب إعداد الجدول (المحرك المختار عند الإنشاء)
            if (_currentTimetable.Engine == GeneratorEngine.Fet)
            {
                var fetPath = genSettings?.Get<string>(FetSettings.FetClPathKey, string.Empty) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(fetPath) || !File.Exists(fetPath))
                {
                    GenerationStatusText = "مسار FET غير مضبوط";
                    MessageBox.Show(
                        "محرك FET يحتاج تحديد مسار fet-cl.exe في الإعدادات ← التوليد.\n" +
                        "أو أنشئ الجدول بمحرك CP-SAT (المحرك الافتراضي العامل).",
                        "محرك FET", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var engineInfo = GeneratorEngineCatalog.Get(_currentTimetable.Engine);
            if (!engineInfo.IsAvailable)
            {
                MessageBox.Show(
                    $"المحرك «{engineInfo.DisplayName}» غير متاح حالياً. اختر CP-SAT من «جدول جديد».",
                    "المحرك غير متاح", MessageBoxButton.OK, MessageBoxImage.Warning);
                GenerationStatusText = "المحرك المختار غير متاح";
                return;
            }

            var engine = _generatorResolver.Resolve(_currentTimetable.Engine);
            var result = await engine.GenerateAsync(
                _currentTimetable, options, _generationCts.Token, progress);

            if (result.Success && result.Timetable != null)
            {
                _currentTimetable = result.Timetable;
                CurrentTimetableName = _currentTimetable.Name;
                TimetableStatusText = _currentTimetable.Status.ToString();
                UpdateTimetableGrid(_currentTimetable);
                PushUndoState();

                // حفظ تلقائي في قاعدة البيانات بعد التوليد
                _ = SaveToDatabaseSilentlyAsync();

                ConflictsCount = result.ConflictsCount;
                UpdateQualityScore(result.QualityScore.Value);

                // تنبيه: حصص انتظار لم تُجدول (تبقى في الشريط السفلي)
                var waitingLeft = _currentTimetable.Lessons.Count(l =>
                    l.Period.Value < 1 &&
                    l.Subject is not null &&
                    l.Subject.Name.StartsWith("انتظار", StringComparison.Ordinal));
                if (waitingLeft > 0)
                {
                    MessageBox.Show(
                        $"لم تُجدول {waitingLeft} حصة انتظار (لا توجد سعة أو معلمون متاحون).\n" +
                        "قلّل عدد الحصص في خطة الانتظار أو أضف معلمين/فترات.",
                        "حصص الانتظار", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                GenerationStatusText =
                    $"تم التوليد: {result.LessonsScheduled} حصة — {result.ConflictsCount} تضارب — الجودة {result.QualityScore.Value:F0}%";

                _logger.LogInformation(
                    "تم توليد الجدول: {Name} ({Scheduled} حصة، {Failed} فاشلة، درجة {Score})",
                    _currentTimetable.Name, result.LessonsScheduled, result.LessonsFailed, result.QualityScore.Value);

                if (result.Warnings.Count > 0)
                {
                    MessageBox.Show(
                        "تم التوليد مع تنبيهات:\n" + string.Join("\n", result.Warnings),
                        "تنبيهات التوليد", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (result.LessonsFailed > 0)
                {
                    MessageBox.Show(
                        $"تم توليد الجدول لكن {result.LessonsFailed} حصة لم تُجدَّل:\n" +
                        string.Join("\n", result.Warnings.Take(10)),
                        "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // تقرير إرخاء القيود: يُعرض عند وجود مخالفات (وضع الإرخاء مُفعّل)
                if (result.Violations.Count > 0)
                {
                    var report = string.Join("\n", result.Violations
                        .GroupBy(v => v.ConstraintName)
                        .OrderByDescending(g => g.Count())
                        .Select(g => $" • {g.Key}: {g.Count()} موضع"));

                    MessageBox.Show(
                        $"الجدول يحتوي {result.ConflictsCount} مخالفة قيود (إرخاء مُفعّل):\n{report}",
                        "تقرير إرخاء القيود", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else if (_currentTimetable.Engine == GeneratorEngine.Asc)
            {
                // محرك aSc: لم يُولَّد الجدول بعد، والرسالة إرشاد للمستخدم وليست فشلاً
                GenerationStatusText = "بانتظار توليد aSc";
                MessageBox.Show(result.ErrorMessage ?? "تم تصدير الجدول إلى aSc.",
                    "محرك aSc", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                GenerationStatusText = "تعذر التوليد";
                MessageBox.Show($"تعذر توليد الجدول:\n{result.ErrorMessage}",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            GenerationStatusText = "تم إلغاء التوليد";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في توليد الجدول");
            GenerationStatusText = "حدث خطأ أثناء التوليد";
            MessageBox.Show($"خطأ في توليد الجدول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    [RelayCommand]
    private async Task OptimizeTimetableAsync()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsGenerating) return;

        _generationCts = new CancellationTokenSource();
        IsGenerating = true;
        GenerationStatusText = "جارٍ تحسين الجدول…";

        try
        {
            PrepareForScheduling(_currentTimetable);
            var progress = new Progress<string>(msg => GenerationStatusText = msg);

            var result = await _generatorResolver.Resolve(_currentTimetable.Engine).OptimizeAsync(
                _currentTimetable, new OptimizationOptions(), _generationCts.Token, progress);

            if (result.Improved)
            {
                PushUndoState();
                UpdateTimetableGrid(_currentTimetable);
                ConflictsCount = _detector.DetectConflicts(_currentTimetable).Count;
                UpdateQualityScore(result.NewScore.Value);
                _ = SaveToDatabaseSilentlyAsync();
                _logger.LogInformation("تم تحسين الجدول: {Pct}%", result.ImprovementPercentage);
            }

            GenerationStatusText = result.Improved
                ? $"الجودة: {result.OldScore.Value:F0}% ← {result.NewScore.Value:F0}%"
                : "لا يوجد تحسين إضافي";

            MessageBox.Show(
                result.Improved
                    ? $"تم تحسين الجدول\n" +
                      $"الجودة: {result.OldScore.Value:F0}% ← {result.NewScore.Value:F0}%\n" +
                      $"التحسين: {result.ImprovementPercentage:0.#}%\n" +
                      $"التضاربات المتبقية: {result.RemainingViolations.Count}"
                    : "لا يوجد تحسين إضافي ممكن",
                "تحسين الجدول", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            GenerationStatusText = "تم إلغاء التحسين";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في تحسين الجدول");
            GenerationStatusText = "حدث خطأ أثناء التحسين";
            MessageBox.Show($"خطأ في تحسين الجدول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    [RelayCommand]
    private void CheckConflicts()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var conflicts = _detector.DetectConflicts(_currentTimetable);
        ConflictsCount = conflicts.Count;
        UpdateQualityScore();
        RightPanelTab = 2; // تبويب التضاربات

        ConflictsList.Clear();
        foreach (var conflict in conflicts)
        {
            ConflictsList.Add($"[{conflict.Severity}] {conflict.Description}");
        }

        if (conflicts.Count == 0)
        {
            MessageBox.Show("لا توجد تضاربات في الجدول ✓", "فحص التضاربات", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var lines = conflicts.Take(10).Select(c => $"• [{c.Severity}] {c.Description}");
            var suffix = conflicts.Count > 10 ? $"\n... و{conflicts.Count - 10} تضارب آخر" : string.Empty;
            MessageBox.Show(
                $"تم العثور على {conflicts.Count} تضارب:\n\n{string.Join("\n", lines)}{suffix}",
                "فحص التضاربات", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void EditTeacherAvailability()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new TeacherAvailabilityDialog(_currentTimetable) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Applied)
        {
            PushUndoState();
            GenerationStatusText = "تم تحديث التفريغ والندب للمعلم";
            _ = SaveToDatabaseSilentlyAsync();
        }
    }

    /// <summary>
    /// إنشاء صفوف الانتظار بطريقة aSc: شعب «انتظار {المرحلة} 1..3» + مادة «انتظار» + حصصها،
    /// بتوزيع المعلمين بعدالة (الأقل نصاباً أولاً) في الفترات التي فيها دروس للمرحلة.
    /// </summary>
    [RelayCommand]
    private async Task CreateWaitingClassesAsync()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_currentTimetable.StageConfigs.Count == 0)
        {
            MessageBox.Show("لا توجد مراحل مُعرَّفة في هذا الجدول.", "الانتظار", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        const int waitingPerStage = 3;
        const string waitingSubjectName = "انتظار";

        try
        {
            using var scope = _services.CreateScope();
            var subjectRepo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var classRepo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
            var teacherRepo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var schoolId = _currentTimetable.SchoolId;

            var subjects = (await subjectRepo.GetBySchoolAsync(schoolId)).ToList();
            var subject = subjects.FirstOrDefault(s => s.Name == waitingSubjectName);
            if (subject is null)
            {
                subject = new Subject(schoolId, waitingSubjectName, "انتظار");
                await subjectRepo.AddAsync(subject);
            }

            var classes = (await classRepo.GetBySchoolAsync(schoolId)).ToList();
            var teachers = (await teacherRepo.GetBySchoolAsync(schoolId)).ToList();

            var waitingClasses = new List<(string Stage, string WaitingClassName, Class Entity)>();

            foreach (var cfg in _currentTimetable.StageConfigs)
            {
                var grade = cfg.Stage == "متوسط" ? 7 : cfg.Stage == "ثانوي" ? 10 : 1;

                for (var i = 1; i <= waitingPerStage; i++)
                {
                    var name = $"انتظار {cfg.Stage} {i}";
                    var entity = classes.FirstOrDefault(c => c.Name == name);
                    if (entity is null)
                    {
                        entity = new Class(schoolId, name, grade, 'أ');
                        entity.SetShortName($"انتظار {i}");
                        await classRepo.AddAsync(entity);
                        classes.Add(entity);
                    }

                    waitingClasses.Add((cfg.Stage, name, entity));
                }
            }

            var plans = WaitingLessonPlanner.Plan(
                _currentTimetable, teachers,
                waitingClasses.Select(w => (w.Stage, w.WaitingClassName)).ToList());

            if (plans.Count == 0)
            {
                MessageBox.Show("لم تُولَّد أي حصة انتظار. تأكد من وجود حصص للمراحل أولًا.", "الانتظار",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // إزالة حصص الانتظار السابقة لهذه المادة
            foreach (var old in _currentTimetable.Lessons.Where(l => l.SubjectId == subject.Id).ToList())
                _currentTimetable.RemoveLesson(old);

            var teacherById = teachers.ToDictionary(t => t.Id, t => t);
            var created = 0;

            foreach (var plan in plans)
            {
                if (!teacherById.TryGetValue(plan.TeacherId, out var teacher)) continue;
                var cls = waitingClasses.FirstOrDefault(w => w.WaitingClassName == plan.WaitingClassName).Entity;
                if (cls is null) continue;

                var lesson = new Lesson((TimetableId)_currentTimetable.Id, (SubjectId)subject.Id,
                    (TeacherId)teacher.Id, (ClassId)cls.Id, plan.Day, PeriodNumber.Create((byte)plan.Period));
                lesson.AttachReferences(subject, teacher, cls);
                lesson.Fix(true); // حصص الانتظار مثبتة في أماكنها
                _currentTimetable.AddLesson(lesson);
                created++;
            }

            UpdateTimetableGrid(_currentTimetable);
            PushUndoState();
            _ = SaveToDatabaseSilentlyAsync();

            MessageBox.Show(
                $"أُنشئت {created} حصة انتظار في {waitingClasses.Count} شعبة انتظار.\n" +
                "ظهرت كصفوف في أسفل الجدول، والأنصبة توزّعت بعدالة.",
                "الانتظار", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في إنشاء صفوف الانتظار");
            MessageBox.Show($"تعذر إنشاء صفوف الانتظار:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// استيراد دروس من CSV/Excel عام (يغطي Untis/WinLD/Vulcan/SchILD عبر مطابقة رؤوس الأعمدة).
    /// </summary>
    [RelayCommand]
    private async Task ImportVendorLessonsAsync()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Excel أو CSV (*.xlsx;*.csv)|*.xlsx;*.csv|جميع الملفات (*.*)|*.*",
            Title = "استيراد دروس (Untis/WinLD/Vulcan/SchILD…)",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IReadOnlyList<LessonImportRow> rows;
            if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                rows = LessonCsvImporter.ParseCsv(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            else
            {
                using var fs = File.OpenRead(dialog.FileName);
                rows = LessonCsvImporter.ParseXlsx(fs);
            }

            if (rows.Count == 0)
            {
                MessageBox.Show("لم يُعثر على دروس صالحة في الملف. تأكد من وجود أعمدة: الفصل، المعلم، المادة، اليوم، الفترة.",
                    "استيراد الدروس", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var scope = _services.CreateScope();
            var subjectRepo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var teacherRepo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var classRepo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
            var roomRepo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            var schoolId = _currentTimetable.SchoolId;

            var subjects = (await subjectRepo.GetBySchoolAsync(schoolId)).ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
            var teachers = (await teacherRepo.GetBySchoolAsync(schoolId)).ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var classes = (await classRepo.GetBySchoolAsync(schoolId)).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var rooms = (await roomRepo.GetBySchoolAsync(schoolId)).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

            async Task<Subject> GetSubjectAsync(string name)
            {
                if (subjects.TryGetValue(name, out var s)) return s;
                s = new Subject(schoolId, name);
                await subjectRepo.AddAsync(s);
                subjects[name] = s;
                return s;
            }

            async Task<Teacher> GetTeacherAsync(string full)
            {
                if (teachers.TryGetValue(full, out var t)) return t;
                var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var first = parts.Length > 0 ? parts[0] : full;
                var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : first;
                t = new Teacher(schoolId, first, last);
                await teacherRepo.AddAsync(t);
                teachers[t.FullName] = t;
                teachers[full] = t;
                return t;
            }

            async Task<Class> GetClassAsync(string name)
            {
                if (classes.TryGetValue(name, out var c)) return c;
                c = new Class(schoolId, name, StageHelper.ParseGrade(name), StageHelper.ParseSection(name));
                await classRepo.AddAsync(c);
                classes[name] = c;
                return c;
            }

            async Task<Room?> GetRoomAsync(string? name)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                if (rooms.TryGetValue(name, out var r)) return r;
                r = new Room(schoolId, name);
                await roomRepo.AddAsync(r);
                rooms[name] = r;
                return r;
            }

            var created = 0;
            foreach (var row in rows)
            {
                var subject = await GetSubjectAsync(row.SubjectName);
                var teacher = await GetTeacherAsync(row.TeacherName);
                var cls = await GetClassAsync(row.ClassName);
                var room = await GetRoomAsync(row.RoomName);

                var lesson = new Lesson(
                    (TimetableId)_currentTimetable.Id,
                    (SubjectId)subject.Id,
                    (TeacherId)teacher.Id,
                    (ClassId)cls.Id,
                    row.Day,
                    PeriodNumber.Create((byte)row.Period));

                if (room is not null) lesson.AssignRoom((RoomId)room.Id);
                lesson.AttachReferences(subject, teacher, cls, room);
                _currentTimetable.AddLesson(lesson);
                created++;
            }

            UpdateTimetableGrid(_currentTimetable);
            PushUndoState();
            _ = SaveToDatabaseSilentlyAsync();

            MessageBox.Show($"تم استيراد {created} حصة من الملف.", "استيراد الدروس",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر استيراد الدروس:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>استيراد دروس من XML لصيغة نظام معين (untis/winld/vulcan/schild أو aSc)</summary>
    [RelayCommand]
    private async Task ImportVendorXmlAsync(string format)
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var label = format switch
        {
            "untis" => "GP-Untis XML",
            "winld" => "WinLD XML",
            "vulcan" => "Vulcan XML",
            "schild" => "SchILD XML",
            _ => "aSc XML",
        };

        var dialog = new OpenFileDialog
        {
            Filter = "XML (*.xml)|*.xml|جميع الملفات (*.*)|*.*",
            Title = $"استيراد {label}",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var xml = ReadXmlWithEncoding(dialog.FileName);
            var rows = VendorXmlImporter.Parse(xml, format);

            if (rows.Count == 0)
            {
                MessageBox.Show("لم يُعثر على دروس في الملف. تأكد من الصيغة أو استخدم استيراد aSc المخصص.",
                    $"استيراد {label}", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var scope = _services.CreateScope();
            var subjectRepo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var teacherRepo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var classRepo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
            var roomRepo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
            var schoolId = _currentTimetable.SchoolId;

            var subjects = (await subjectRepo.GetBySchoolAsync(schoolId)).ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
            var teachers = (await teacherRepo.GetBySchoolAsync(schoolId)).ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var classes = (await classRepo.GetBySchoolAsync(schoolId)).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var rooms = (await roomRepo.GetBySchoolAsync(schoolId)).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

            async Task<Subject> GetSubjectAsync(string name)
            {
                if (subjects.TryGetValue(name, out var s)) return s;
                s = new Subject(schoolId, name);
                await subjectRepo.AddAsync(s);
                subjects[name] = s;
                return s;
            }

            async Task<Teacher> GetTeacherAsync(string full)
            {
                if (teachers.TryGetValue(full, out var t)) return t;
                var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var first = parts.Length > 0 ? parts[0] : full;
                var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : first;
                t = new Teacher(schoolId, first, last);
                await teacherRepo.AddAsync(t);
                teachers[t.FullName] = t;
                teachers[full] = t;
                return t;
            }

            async Task<Class> GetClassAsync(string name)
            {
                if (classes.TryGetValue(name, out var c)) return c;
                c = new Class(schoolId, name, StageHelper.ParseGrade(name), StageHelper.ParseSection(name));
                await classRepo.AddAsync(c);
                classes[name] = c;
                return c;
            }

            async Task<Room?> GetRoomAsync(string? name)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                if (rooms.TryGetValue(name, out var r)) return r;
                r = new Room(schoolId, name);
                await roomRepo.AddAsync(r);
                rooms[name] = r;
                return r;
            }

            int created = 0, skipped = 0;

            foreach (var row in rows)
            {
                var subject = await GetSubjectAsync(row.SubjectName);
                var teacher = await GetTeacherAsync(row.TeacherName);
                var cls = await GetClassAsync(row.ClassName);
                var room = await GetRoomAsync(row.RoomName);

                if (row.Period < 1)
                {
                    // بيانات أساسية فقط: حصة غير موضوعة
                    var unplaced = new Lesson((TimetableId)_currentTimetable.Id, (SubjectId)subject.Id,
                        (TeacherId)teacher.Id, (ClassId)cls.Id, AwadTimetable.Core.Enums.DayOfWeek.Saturday,
                        new PeriodNumber(0));
                    if (room is not null) unplaced.AssignRoom((RoomId)room.Id);
                    unplaced.AttachReferences(subject, teacher, cls, room);
                    _currentTimetable.AddLesson(unplaced);
                    created++;
                    continue;
                }

                var lesson = new Lesson((TimetableId)_currentTimetable.Id, (SubjectId)subject.Id,
                    (TeacherId)teacher.Id, (ClassId)cls.Id, row.Day, PeriodNumber.Create((byte)row.Period));

                if (room is not null) lesson.AssignRoom((RoomId)room.Id);
                lesson.AttachReferences(subject, teacher, cls, room);
                _currentTimetable.AddLesson(lesson);
                created++;
            }

            UpdateTimetableGrid(_currentTimetable);
            PushUndoState();
            _ = SaveToDatabaseSilentlyAsync();

            MessageBox.Show($"تم استيراد {created} حصة من {label}." + (skipped > 0 ? $"\nتم تخطي {skipped}." : ""),
                $"استيراد {label}", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر الاستيراد:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>قراءة XML مع استشعار الترميز من الترويسة (windows-1256/aSc)</summary>
    private static string ReadXmlWithEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 300));
        var m = System.Text.RegularExpressions.Regex.Match(head, "encoding\\s*=\\s*[\"']([\\w-]+)[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            try { return Encoding.GetEncoding(m.Groups[1].Value).GetString(bytes); } catch { /* fallback */ }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    // ===== تصدير/استيراد مكوّن واحد =====

    /// <summary>تصدير مكوّن (teachers/subjects/classes/rooms) إلى CSV</summary>
    [RelayCommand]
    private async Task ExportComponentAsync(string kind)
    {
        var label = ComponentLabel(kind);
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"{label}.csv",
            Title = $"تصدير {label}",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            using var scope = _services.CreateScope();
            var schoolId = _currentTimetable?.SchoolId
                ?? await _services.GetRequiredService<CurrentSchoolService>().GetSchoolIdAsync();

            var rows = new List<ComponentRow>();

            switch (kind)
            {
                case "teachers":
                    foreach (var t in await scope.ServiceProvider.GetRequiredService<ITeacherRepository>().GetBySchoolAsync(schoolId))
                        rows.Add(new ComponentRow(t.FullName, t.DisplayShortName, t.MaxPeriodsPerWeek));
                    break;
                case "subjects":
                    foreach (var s in await scope.ServiceProvider.GetRequiredService<ISubjectRepository>().GetBySchoolAsync(schoolId))
                        rows.Add(new ComponentRow(s.Name, s.ShortName, s.DefaultPeriodsPerWeek));
                    break;
                case "classes":
                    foreach (var c in await scope.ServiceProvider.GetRequiredService<IClassRepository>().GetBySchoolAsync(schoolId))
                        rows.Add(new ComponentRow(c.Name, c.DisplayShortName, c.StudentCount));
                    break;
                case "rooms":
                    foreach (var r in await scope.ServiceProvider.GetRequiredService<IRoomRepository>().GetBySchoolAsync(schoolId))
                        rows.Add(new ComponentRow(r.Name, r.ShortName, r.Capacity));
                    break;
            }

            File.WriteAllText(dialog.FileName, ComponentTransfer.ToCsv(rows), new UTF8Encoding(true));
            MessageBox.Show($"تم تصدير {rows.Count} من {label}.", "تصدير", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر التصدير:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>استيراد مكوّن (teachers/subjects/classes/rooms) من CSV أو Excel</summary>
    /// <summary>استيراد الإسنادات مجمّعةً من CSV/Excel (المعلم، المادة، الفصل، عدد الحصص الأسبوعية).</summary>
    [RelayCommand]
    private async Task ImportAssignmentsAsync()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا أو افتحه أولًا.", "استيراد الإسنادات",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "الإسنادات (*.csv;*.xlsx)|*.csv;*.xlsx|جميع الملفات (*.*)|*.*",
            Title = "استيراد الإسنادات (المعلم، المادة، الفصل، عدد الحصص)",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IReadOnlyList<Core.Services.TeachingAssignmentImportRow> rows;
            if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                rows = Core.Services.TeachingAssignmentImporter.FromCsv(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            else
            {
                using var fs = File.OpenRead(dialog.FileName);
                rows = Core.Services.TeachingAssignmentImporter.FromXlsx(fs);
            }

            if (rows.Count == 0)
            {
                MessageBox.Show("لم يُعثر على صفوف إسناد. الأعمدة المطلوبة: المعلم، المادة، الفصل، عدد الحصص.",
                    "استيراد الإسنادات", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmAssignments = MessageBox.Show(
                $"معاينة الاستيراد: {rows.Count} إسناد (معلم / مادة / فصل / عدد الحصص).\nهل تريد المتابعة؟",
                "معاينة الاستيراد", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmAssignments != MessageBoxResult.Yes) return;

            using var scope = _services.CreateScope();
            var schoolId = _currentTimetable.SchoolId;
            var teacherRepo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
            var subjectRepo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var classRepo = scope.ServiceProvider.GetRequiredService<IClassRepository>();

            var teachers = await teacherRepo.GetBySchoolAsync(schoolId);
            var subjects = await subjectRepo.GetBySchoolAsync(schoolId);
            var classes = await classRepo.GetBySchoolAsync(schoolId);

            static T? Match<T>(IEnumerable<T> items, string key, Func<T, string?[]> fields) where T : class
            {
                var k = key.Trim();
                foreach (var item in items)
                {
                    if (fields(item).Any(f => !string.IsNullOrWhiteSpace(f)
                        && (f.Trim().Equals(k, StringComparison.OrdinalIgnoreCase)
                            || f.Trim().Contains(k, StringComparison.OrdinalIgnoreCase))))
                        return item;
                }
                return null;
            }

            int added = 0, updated = 0, skipped = 0;

            foreach (var row in rows)
            {
                var teacher = Match(teachers, row.TeacherName, t => new[] { t.FullName, t.DisplayShortName, t.FirstName });
                var subject = Match(subjects, row.SubjectName, s => new[] { s.Name, s.ShortName });
                var @class = Match(classes, row.ClassName, c => new[] { c.Name, c.FullName, c.ShortName });

                if (teacher is null || subject is null || @class is null) { skipped++; continue; }

                var tid = (Guid)teacher.Id;
                var sid = (Guid)subject.Id;
                var cid = (Guid)@class.Id;

                var existing = _currentTimetable.Assignments
                    .FirstOrDefault(a => a.TeacherId == tid && a.SubjectId == sid && a.ClassId == cid);

                if (existing is null)
                {
                    _currentTimetable.Assignments.Add(new TeachingAssignment
                    {
                        TeacherId = tid,
                        SubjectId = sid,
                        ClassId = cid,
                        WeeklyPeriods = Math.Max(1, row.WeeklyPeriods),
                    });
                    added++;
                }
                else
                {
                    existing.WeeklyPeriods = Math.Max(1, row.WeeklyPeriods);
                    existing.IsEnabled = true;
                    updated++;
                }
            }

            RebuildAssignments();
            _ = SaveToDatabaseSilentlyAsync();

            MessageBox.Show(
                $"تم استيراد الإسنادات:\nجديد: {added}\nمحدّث: {updated}\nمتخطّى (لم يُطابق معلم/مادة/فصل): {skipped}\n\n" +
                "اضغط «توليد الحصص من الإسناد» ثم «توليد الجدول».",
                "استيراد الإسنادات", MessageBoxButton.OK, MessageBoxImage.Information);

            _logger.LogInformation("استيراد الإسنادات: جديد {Added}، محدّث {Updated}، متخطّى {Skipped}", added, updated, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في استيراد الإسنادات");
            MessageBox.Show($"تعذر استيراد الإسنادات:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ImportComponentAsync(string kind)
    {
        var label = ComponentLabel(kind);
        var dialog = new OpenFileDialog
        {
            Filter = "Excel أو CSV (*.xlsx;*.csv)|*.xlsx;*.csv|جميع الملفات (*.*)|*.*",
            Title = $"استيراد {label}",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IReadOnlyList<ComponentRow> rows;
            if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                rows = ComponentTransfer.FromCsv(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            else
            {
                using var fs = File.OpenRead(dialog.FileName);
                rows = ComponentTransfer.FromXlsx(fs);
            }

            if (rows.Count == 0)
            {
                MessageBox.Show("لم يُعثر على عناصر في الملف.", $"استيراد {label}",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmImport = MessageBox.Show(
                $"معاينة الاستيراد: {rows.Count} عنصر إلى «{label}» في مساحة العمل الحالية.\nهل تريد المتابعة؟",
                "معاينة الاستيراد", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmImport != MessageBoxResult.Yes) return;

            using var scope = _services.CreateScope();
            var schoolId = _currentTimetable?.SchoolId
                ?? await _services.GetRequiredService<CurrentSchoolService>().GetSchoolIdAsync();

            int added = 0, skipped = 0;

            switch (kind)
            {
                case "teachers":
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<ITeacherRepository>();
                        var existing = (await repo.GetBySchoolAsync(schoolId)).Select(t => t.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows)
                        {
                            if (!existing.Add(r.Name)) { skipped++; continue; }
                            var parts = r.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            var first = parts.Length > 0 ? parts[0] : r.Name;
                            var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : first;
                            var t = new Teacher(schoolId, first, last);
                            if (r.ShortName is not null) t.SetAppearance(r.ShortName, null);
                            if (r.Number is > 0) t.UpdateEmploymentInfo(null, null, null, true, 6, r.Number.Value);
                            await repo.AddAsync(t);
                            added++;
                        }
                        break;
                    }
                case "subjects":
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
                        var existing = (await repo.GetBySchoolAsync(schoolId)).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows)
                        {
                            if (!existing.Add(r.Name)) { skipped++; continue; }
                            var s = new Subject(schoolId, r.Name, r.ShortName);
                            var p = r.Number is > 0 ? r.Number.Value : 4;
                            s.UpdateSchedule(p, 1, Math.Max(p, 10));
                            await repo.AddAsync(s);
                            added++;
                        }
                        break;
                    }
                case "classes":
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<IClassRepository>();
                        var existing = (await repo.GetBySchoolAsync(schoolId)).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows)
                        {
                            if (!existing.Add(r.Name)) { skipped++; continue; }
                            var c = new Class(schoolId, r.Name, StageHelper.ParseGrade(r.Name), StageHelper.ParseSection(r.Name));
                            c.SetShortName(r.ShortName);
                            if (r.Number is > 0) c.SetStudentCount(r.Number.Value);
                            await repo.AddAsync(c);
                            added++;
                        }
                        break;
                    }
                case "rooms":
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
                        var existing = (await repo.GetBySchoolAsync(schoolId)).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows)
                        {
                            if (!existing.Add(r.Name)) { skipped++; continue; }
                            var room = new Room(schoolId, r.Name, r.ShortName, r.Number is > 0 ? r.Number.Value : 30);
                            await repo.AddAsync(room);
                            added++;
                        }
                        break;
                    }
            }

            // تحديث الواجهات
            if (_currentTimetable is not null) UpdateTimetableGrid(_currentTimetable);
            await Teachers.RefreshCommand.ExecuteAsync(null);
            await Subjects.RefreshCommand.ExecuteAsync(null);
            await Classes.RefreshCommand.ExecuteAsync(null);
            await Rooms.RefreshCommand.ExecuteAsync(null);

            MessageBox.Show($"تمت إضافة {added} إلى {label}." + (skipped > 0 ? $"\nتم تخطي {skipped} (مكرر)." : ""),
                "استيراد", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر الاستيراد:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ComponentLabel(string kind) => kind switch
    {
        "teachers" => "المعلمين",
        "subjects" => "المواد",
        "classes" => "الفصول",
        "rooms" => "القاعات",
        _ => kind,
    };

    /// <summary>خطة الانتظار: تحديد معلمي كل فئة وعدد حصصهم (طريقة aSc)</summary>
    /// <summary>قوالب الطباعة والمعاينة</summary>
    [RelayCommand]
    private void PrintTemplates()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new PrintTemplatesDialog(_currentTimetable) { Owner = Application.Current?.MainWindow }.ShowDialog();
    }

    /// <summary>بطاقة القاعة PDF</summary>
    [RelayCommand]
    private void PrintRoomCard()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new RoomCardDialog(_currentTimetable) { Owner = Application.Current?.MainWindow }.ShowDialog();
    }

    /// <summary>الإشراف (Dozor): توزيع فترات الاستراحة على القاعات</summary>
    [RelayCommand]
    private void Supervision()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SupervisionDialog(_currentTimetable) { Owner = Application.Current?.MainWindow };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void WaitingPlan()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WaitingPlanDialog(_currentTimetable) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Applied)
        {
            UpdateTimetableGrid(_currentTimetable);
            PushUndoState();
            _ = SaveToDatabaseSilentlyAsync();
        }
    }

    [RelayCommand]
    private void WaitingSchedule()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WaitingScheduleDialog(_currentTimetable) { Owner = Application.Current?.MainWindow };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void AdvisorReport()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var findings = ScheduleAdvisor.Analyze(TimetableContextFactory.Build(_currentTimetable));

        if (findings.Count == 0)
        {
            MessageBox.Show("لا ملاحظات: الجدول سليم بناءً على فحوصات المستشار ✓",
                "تقرير المستشار", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = findings.Take(12).Select(f => $"• [{SeverityLabel(f.Severity)}] {f.Title}: {f.Message}");
        var suffix = findings.Count > 12 ? $"\n... و{findings.Count - 12} ملاحظة أخرى" : string.Empty;

        MessageBox.Show(
            $"وجد المستشار {findings.Count} ملاحظة:\n\n{string.Join("\n", lines)}{suffix}",
            "تقرير المستشار", MessageBoxButton.OK,
            findings.Any(f => f.Severity == AdvisorSeverity.Error) ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private static string SeverityLabel(AdvisorSeverity severity) => severity switch
    {
        AdvisorSeverity.Error => "خطأ",
        AdvisorSeverity.Warning => "تحذير",
        _ => "ملاحظة",
    };

    [RelayCommand]
    private void AddLesson()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new LessonDialog(
            _currentTimetable,
            Subjects.Subjects.ToList(),
            Teachers.Teachers.ToList(),
            Classes.Classes.ToList(),
            Rooms.Rooms.ToList());

        if (dialog.ShowDialog() == true && dialog.CreatedLesson != null)
        {
            PushUndoState();
            _currentTimetable.AddLesson(dialog.CreatedLesson);
            UpdateTimetableGrid(_currentTimetable);
            SelectedLesson = dialog.CreatedLesson;
            _logger.LogInformation("تمت إضافة حصة: {Lesson}", dialog.CreatedLesson.GetDisplayName());
        }
    }

    [RelayCommand]
    private void SplitLesson()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lesson = SelectedLesson ?? _currentTimetable.Lessons.FirstOrDefault();
        if (lesson == null)
        {
            MessageBox.Show("لا توجد حصة لتقسيمها. اختر حصة من الجدول أولًا.", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.SplitLessonDialog(lesson) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true) return;

        try
        {
            PushUndoState();
            var result = LessonSplitter.Split(
                _currentTimetable,
                lesson,
                dialog.Parts,
                minDaysBetween: dialog.MinDaysBetween,
                splitDuration: dialog.SplitDuration);

            UpdateTimetableGrid(_currentTimetable);
            SelectedLesson = lesson;
            _logger.LogInformation("تم تقسيم الحصة إلى {Count} أجزاء", result.Parts.Count);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذّر تقسيم الحصة: {ex.Message}", "خطأ",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CopyLessons()
    {
        if (_currentTimetable == null)
        {
            MessageBox.Show("أنشئ جدولًا جديدًا أولًا (Ctrl+N)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.CopyLessonsDialog(
            Classes.Classes.ToList(),
            Teachers.Teachers.ToList())
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result == null) return;
        var req = dialog.Result;

        if (req.Mode == "class")
            ExecuteCopyClassLessons(req);
        else
            ExecuteMoveTeacherLessons(req);
    }

    [RelayCommand]
    private void OpenSubstitution()
    {
        var window = new Views.SubstitutionWindow(
            Teachers.Teachers.ToList(),
            Classes.Classes.ToList(),
            Subjects.Subjects.ToList(),
            _currentTimetable)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    /// <summary>نسخ حصص فصل كامل إلى فصل آخر (الأقسام المتوازية)</summary>
    private void ExecuteCopyClassLessons(Views.CopyLessonsRequest req)
    {
        var source = _currentTimetable!.Lessons.Where(l => l.ClassId.Value == req.SourceId).ToList();
        if (source.Count == 0)
        {
            MessageBox.Show("الفصل المصدر لا يملك حصصًا في الجدول.", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PushUndoState();

        if (req.ReplaceTarget)
        {
            var old = _currentTimetable.Lessons.Where(l => l.ClassId.Value == req.TargetId).ToList();
            foreach (var lesson in old)
                _currentTimetable.RemoveLesson(lesson);
        }

        var count = 0;
        foreach (var lesson in source)
        {
            var copy = new Lesson(
                (TimetableId)_currentTimetable.Id,
                lesson.SubjectId,
                lesson.TeacherId,
                lesson.ClassId.Value == req.SourceId ? new ClassId(req.TargetId) : lesson.ClassId,
                lesson.Day,
                lesson.Period,
                lesson.Duration);
            copy.SetType(lesson.Type);
            copy.WeekNumber = lesson.WeekNumber;
            copy.SetSortOrder(lesson.SortOrder);
            // لا تُنسخ الغرفة: الهدف قد يستخدم غرفة أخرى في نفس الفترة
            _currentTimetable.AddLesson(copy);
            count++;
        }

        UpdateTimetableGrid(_currentTimetable);
        ConflictsCount = _detector.DetectConflicts(_currentTimetable).Count;
        _logger.LogInformation("تم نسخ {Count} حصة إلى الفصل الهدف", count);
        MessageBox.Show($"تمت إضافة {count} حصة إلى الفصل الهدف.",
            "نسخ الحصص", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>نقل حصص معلم كامل إلى معلم آخر (بديل/استبدال)</summary>
    private void ExecuteMoveTeacherLessons(Views.CopyLessonsRequest req)
    {
        var moved = _currentTimetable!.Lessons.Where(l => l.TeacherId.Value == req.SourceId).ToList();
        if (moved.Count == 0)
        {
            MessageBox.Show("المعلم المصدر لا يملك حصصًا في الجدول.", "تنبيه",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PushUndoState();

        var count = 0;
        foreach (var lesson in moved)
        {
            var copy = new Lesson(
                (TimetableId)_currentTimetable.Id,
                lesson.SubjectId,
                new TeacherId(req.TargetId),
                lesson.ClassId,
                lesson.Day,
                lesson.Period,
                lesson.Duration);
            copy.SetType(lesson.Type);
            copy.WeekNumber = lesson.WeekNumber;
            copy.SetSortOrder(lesson.SortOrder);
            if (lesson.RoomId is { } roomId) copy.AssignRoom(roomId);
            if (lesson.IsFixed) copy.Fix();

            _currentTimetable.RemoveLesson(lesson);
            _currentTimetable.AddLesson(copy);
            count++;
        }

        UpdateTimetableGrid(_currentTimetable);
        ConflictsCount = _detector.DetectConflicts(_currentTimetable).Count;
        _logger.LogInformation("تم نقل {Count} حصة إلى المعلم الهدف", count);
        MessageBox.Show($"تم نقل {count} حصة إلى المعلم الهدف.",
            "نقل الحصص", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void AddConstraint()
    {
        // فتح تبويب القيود في الشريط الجانبي
        SelectedSidebarTab = 6;
        RightPanelTab = 3;
    }

    [RelayCommand]
    private void ShowAIChat()
    {
        IsAIChatPanelOpen = !IsAIChatPanelOpen;
        RightPanelTab = 1; // تبويب المساعد الذكي
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var dialog = new SettingsWindow { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var dialog = new AboutDialog { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        MessageBox.Show(
            "دليل الاستخدام السريع:\n\n" +
            "1. أنشئ جدولًا جديدًا: Ctrl+N\n" +
            "2. أضف المعلمين والمواد والغرف والفصول من الشريط الجانبي\n" +
            "3. أضف القيود المطلوبة\n" +
            "4. ولّد الجدول: Ctrl+G\n" +
            "5. راجع التضاربات: Ctrl+Shift+C\n" +
            "6. حفظ الجدول: Ctrl+S\n\n" +
            "للحصول على مساعدة ذكية، افتح مساعد الجدولة: Ctrl+I",
            "دليل الاستخدام", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ShowShortcuts()
    {
        MessageBox.Show(
            "اختصارات لوحة المفاتيح:\n\n" +
            "Ctrl+N  |  جدول جديد\n" +
            "Ctrl+O  |  فتح جدول\n" +
            "Ctrl+S  |  حفظ الجدول\n" +
            "Ctrl+G  |  توليد الجدول\n" +
            "Ctrl+Shift+G  |  تحسين الجدول\n" +
            "Ctrl+Shift+C  |  فحص التضاربات\n" +
            "Ctrl+L  |  إضافة حصة\n" +
            "Ctrl+I  |  مساعد ذكي\n" +
            "Ctrl+F  |  بحث\n" +
            "Ctrl+P  |  طباعة\n" +
            "Ctrl+،  |  الإعدادات\n" +
            "F1  |  دليل الاستخدام\n" +
            "Esc  |  إغلاق اللوحات",
            "اختصارات لوحة المفاتيح", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }

    // ===== أوامر قائمة "إدارة" =====

    [RelayCommand]
    private void ShowTeachers()
    {
        SelectedSidebarTab = 2;
    }

    [RelayCommand]
    private void ShowSubjects()
    {
        SelectedSidebarTab = 3;
    }

    [RelayCommand]
    private void ShowRooms()
    {
        SelectedSidebarTab = 4;
    }

    [RelayCommand]
    private void ShowClasses()
    {
        SelectedSidebarTab = 5;
    }

    [RelayCommand]
    private void ShowConstraints()
    {
        SelectedSidebarTab = 6;
    }

    // ===== أوامر قائمة "AI" =====

    [RelayCommand]
    private void GetAISuggestions()
    {
        IsAIChatPanelOpen = true;
        RightPanelTab = 1;
        if (AiChatPanelViewModel != null)
        {
            AiChatPanelViewModel.InputText = "اقترح توزيعًا محسّنًا لحصص اليوم";
            AiChatPanelViewModel.SendMessageCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ShowAISettings()
    {
        ShowSettings();
    }

    // ===== أوامر قائمة "ملف" =====

    [RelayCommand]
    private void Print()
    {
        _ = ExportPdfAsync();
    }

    /// <summary>القالب الافتراضي للطباعة من الإعدادات (أو aSc الكلاسيكي)</summary>
    private AwadTimetable.Printing.Templates.PrintTemplate LoadDefaultPrintTemplate()
    {
        try
        {
            var settings = _services.GetService<ISettingsService>();
            var id = settings?.Get<string>("Print.TemplateId", string.Empty);
            if (!string.IsNullOrWhiteSpace(id))
            {
                var custom = AwadTimetable.Printing.Templates.PrintTemplateStore.Load();
                var found = custom.FirstOrDefault(t => t.Id == id);
                if (found is not null) return found;

                var preset = AwadTimetable.Printing.Templates.PrintTemplatePresets.All.FirstOrDefault(t => t.Id == id);
                if (preset is not null) return preset;
            }
        }
        catch
        {
            // تجاهل: نعود للقالب الافتراضي
        }

        return AwadTimetable.Printing.Templates.PrintTemplatePresets.Default;
    }

    [RelayCommand]
    private void PrintPreview()
    {
        _ = OpenPrintPreviewAsync();
    }

    /// <summary>معاينة الطباعة على الشاشة: HTML مُصدَّر داخل WebBrowser + طباعة/حفظ PDF</summary>
    private async Task OpenPrintPreviewAsync()
    {
        if (!EnsureTimetable()) return;

        try
        {
            var printer = _services.GetRequiredService<IPrintService>();
            var options = new PrintOptions
            {
                SchoolName = ResolveSchoolName(),
                FontSize = 9
            };

            _logger.LogInformation("جاري تجهيز معاينة الطباعة: {Name}", _currentTimetable!.Name);

            // استخدام القالب الافتراضي المحفوظ (أو aSc الكلاسيكي)
            var template = LoadDefaultPrintTemplate();
            var report = _services.GetService<IWebReportService>();
            var html = report is not null
                ? report.GenerateHtml(_currentTimetable, new WebReportOptions
                {
                    SchoolName = options.SchoolName,
                    ProductName = "نظام عوض للجداول المدرسية",
                }, GetLessonNames, template)
                : await printer.GenerateHtmlAsync(_currentTimetable, options);

            var window = new Views.PrintPreviewWindow(html)
            {
                Owner = Application.Current.MainWindow,
                SavePdfAction = () => SavePreviewPdfAsync(printer, options)
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في فتح معاينة الطباعة");
            MessageBox.Show($"تعذر فتح المعاينة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>حفظ PDF من داخل المعاينة (حوار حفظ ثم توليد عبر QuestPDF)</summary>
    private async Task SavePreviewPdfAsync(IPrintService printer, PrintOptions options)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ملف PDF (*.pdf)|*.pdf",
            FileName = $"{_currentTimetable!.Name}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return;

        var bytes = await printer.GeneratePdfAsync(_currentTimetable, options);
        await File.WriteAllBytesAsync(dialog.FileName, bytes);
        _logger.LogInformation("تم حفظ PDF من المعاينة: {Path} ({Size} بايت)", dialog.FileName, bytes.Length);

        MessageBox.Show($"تم إنشاء ملف PDF بنجاح:\n{dialog.FileName}",
            "تصدير PDF", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>تصدير الجدول الحالي كملف PDF (QuestPDF) وفتحه في العارض الافتراضي</summary>
    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (!EnsureTimetable()) return;

        var dialog = new SaveFileDialog
        {
            Filter = "ملف PDF (*.pdf)|*.pdf",
            FileName = $"{_currentTimetable!.Name}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var printer = _services.GetRequiredService<IPrintService>();
            var options = new PrintOptions
            {
                SchoolName = ResolveSchoolName(),
                FontSize = 9
            };

            _logger.LogInformation("جاري إنشاء PDF: {Name}", _currentTimetable.Name);
            var bytes = await printer.GeneratePdfAsync(_currentTimetable, options);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            _logger.LogInformation("تم إنشاء PDF: {Path} ({Size} بايت)", dialog.FileName, bytes.Length);

            try
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception openEx)
            {
                _logger.LogWarning(openEx, "تعذر فتح ملف PDF تلقائياً");
            }

            MessageBox.Show($"تم إنشاء ملف PDF بنجاح:\n{dialog.FileName}",
                "تصدير PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في إنشاء PDF");
            MessageBox.Show($"فشل إنشاء PDF:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>اسم المدرسة لترويسة PDF (من قاعدة البيانات إن أمكن)</summary>
    private string ResolveSchoolName()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AwadTimetable.Data.TimetableDbContext>();
            var school = db.Set<School>().FirstOrDefault(s => s.Id == _currentTimetable!.SchoolId);
            if (!string.IsNullOrWhiteSpace(school?.Name)) return school!.Name;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "تعذر جلب اسم المدرسة — سيُستخدم اسم افتراضي");
        }
        return "مدرستي";
    }

    // ===== أوامر قائمة "تحرير" =====

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CloneTimetable(_currentTimetable));
        _currentTimetable = _undoStack.Pop();
        if (_currentTimetable != null)
        {
            CurrentTimetableName = _currentTimetable.Name;
            await SwitchWorkspaceAsync(_currentTimetable);
            UpdateTimetableGrid(_currentTimetable);
        }
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private async Task RedoAsync()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CloneTimetable(_currentTimetable));
        _currentTimetable = _redoStack.Pop();
        if (_currentTimetable != null)
        {
            CurrentTimetableName = _currentTimetable.Name;
            await SwitchWorkspaceAsync(_currentTimetable);
            UpdateTimetableGrid(_currentTimetable);
        }
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>يبدّل مساحة العمل الحالية إلى مدرسة الجدول ثم يعيد تحميل قوائمها المرجعية.</summary>
    private async Task SwitchWorkspaceAsync(Timetable timetable)
    {
        _services.GetRequiredService<CurrentSchoolService>().SetCurrent(timetable.SchoolId);
        CurrentSchoolName = ResolveSchoolName();

        // حدّث سياق المساعد الذكي فوراً، وامسح الحالة القديمة (اختيار/تضاربات) لتفادي خلط المدارس
        _services.GetRequiredService<AssistantToolContext>().SetCurrent(timetable);
        SelectedLesson = null;
        ConflictsList.Clear();
        ConflictsCount = 0;
        RebuildEntityOptions();

        await ReloadReferenceListsAsync();
    }

    [RelayCommand]
    private void Cut()
    {
        Copy();
        if (SelectedLesson != null)
        {
            _currentTimetable?.Lessons.Remove(SelectedLesson);
            SelectedLesson = null;
            if (_currentTimetable != null) UpdateTimetableGrid(_currentTimetable);
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (SelectedLesson == null) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(SelectedLesson, JsonOpts);
            Clipboard.SetText(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في النسخ");
        }
    }

    [RelayCommand]
    private void Paste()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var lesson = System.Text.Json.JsonSerializer.Deserialize<Lesson>(Clipboard.GetText(), JsonOpts);
            if (lesson != null && _currentTimetable != null)
            {
                PushUndoState();
                _currentTimetable.Lessons.Add(lesson);
                UpdateTimetableGrid(_currentTimetable);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في اللصق");
            MessageBox.Show("لا يمكن لصق المحتوى الحالي", "لصق", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void Search()
    {
        RightPanelTab = 0;
        OnPropertyChanged(nameof(SearchText));
    }

    [RelayCommand]
    private void ShowNotifications()
    {
        var message = ConflictsCount > 0
            ? $"لديك {ConflictsCount} تضارب يحتاج إلى مراجعة."
            : "لا توجد إشعارات جديدة.";
        MessageBox.Show(message, "الإشعارات", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ===== الإحصائيات =====

    [RelayCommand]
    private async Task RefreshStatsAsync()
    {
        try
        {
            TeachersCount = Teachers.Teachers.Count;
            SubjectsCount = Subjects.Subjects.Count;
            RoomsCount = Rooms.Rooms.Count;
            ClassesCount = Classes.Classes.Count;
            LessonsCount = _currentTimetable?.Lessons.Count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في تحديث الإحصائيات");
        }
        await Task.CompletedTask;
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    private readonly Stack<Timetable?> _undoStack = new();
    private readonly Stack<Timetable?> _redoStack = new();

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    private void PushUndoState()
    {
        _undoStack.Push(CloneTimetable(_currentTimetable));
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>تفريغ سجل التراجع عند تغيير الجدول/مساحة العمل حتى لا يعود إلى جدول آخر.</summary>
    private void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private static Timetable? CloneTimetable(Timetable? source)
    {
        if (source == null) return null;
        var json = System.Text.Json.JsonSerializer.Serialize(source, JsonOpts);
        return System.Text.Json.JsonSerializer.Deserialize<Timetable>(json, JsonOpts);
    }

    /// <summary>يفرّغ عرض الجدول وكل الحالات المرتبطة (حذف الجدول/مساحة بلا جداول).</summary>
    private void ClearTimetableDisplay()
    {
        _timetableGridRows.Clear();
        _problemLessons.Clear();
        _pendingCards.Clear();
        PendingCardsCount = 0;
        SelectedLesson = null;
        SelectedItemPropertiesView = null;
        ConflictsList.Clear();
        ConflictsCount = 0;
        AscGrid = new AscGridView();
        AscGridRevision++;
        _services.GetRequiredService<AssistantToolContext>().SetCurrent(null);
    }

    private void UpdateTimetableGrid(Timetable timetable)
    {
        AttachLessonNavigation(timetable);

        // إبقاء سياق أدوات المساعد متزامنًا مع الجدول المعروض
        _services.GetRequiredService<AssistantToolContext>().SetCurrent(timetable);

        RebuildAssignments();
        RebuildRuleGroups();

        _timetableGridRows.Clear();

        var days = Enum.GetValues<DomainDayOfWeek>().ToList();

        // فترات المدرسة: من 1 إلى أقصى فترة مستخدمة (افتراضي 8 عند الجدول الفارغ، حد أقصى 12)
        var maxUsedPeriod = timetable.Lessons.Count == 0 ? 0 : timetable.Lessons.Max(l => l.Period.Value);
        var periodCount = maxUsedPeriod switch
        {
            0 => 8,
            > 12 => 12,
            var p => p
        };
        var periods = Enumerable.Range(1, periodCount).ToList();

        // ===== عرض كيان واحد (فصل/معلم/قاعة/مادة) — بأسلوب aSc =====
        if (ViewMode != TimetableViewMode.SchoolMatrix)
        {
            // إن كانت قائمة الكيانات فارغة أو تغيّرت البيانات أعد بناءها
            if (EntityOptions.Count == 0 ||
                (SelectedEntityOption != null && !EntityOptions.Any(o => o.Id == SelectedEntityOption.Id)))
            {
                RebuildEntityOptions();
            }

            var entityId = SelectedEntityOption?.Id;

            if (entityId != null)
            {
                foreach (var period in periods)
                {
                    var row = new TimetableGridRow
                    {
                        Period = PeriodNumber.Create((byte)period),
                        ClassId = ClassId.Empty,
                        ClassName = "",
                        IsClassGroupStart = false
                    };

                    var rowTime = timetable.GetPeriodTimeForDisplay(period);
                    row.PeriodName = rowTime == null
                        ? PeriodNames.GetFullName(period)
                        : $"{PeriodNames.GetFullName(period)} ({PeriodTime.FormatMinutes(rowTime.StartMinutes)}-{PeriodTime.FormatMinutes(rowTime.EndMinutes)})";

                    foreach (var day in days)
                    {
                        var dayLessons = timetable.Lessons
                            .Where(l => l.Day == day && l.Period.Value == period && MatchesCurrentEntity(l, entityId.Value))
                            .ToList();

                        foreach (var lesson in dayLessons)
                        {
                            AddLessonToRow(row, day, CreateLessonCellViewModel(timetable, lesson));
                        }
                    }

                    _timetableGridRows.Add(row);
                }
            }

            TotalLessonsCount = timetable.Lessons.Count;
            LessonsCount = timetable.Lessons.Count;
            BuildAscGrid(timetable);
            return;
        }

        // ===== مصفوفة المدرسة الكاملة: الصفوف = فصل × فترة، والأعمدة = الأيام =====
        // ترتيب الفصول: حسب الصف ثم الشعبة ثم الاسم
        var classGroups = Classes.Classes
            .OrderBy(c => c.GradeLevel)
            .ThenBy(c => c.Section)
            .ThenBy(c => c.Name)
            .Select(c => (Id: c.Id, Name: c.ShortName ?? c.Name))
            .ToList();

        // حصص فصول غير مسجّلة في قاعدة البيانات — تُعرض حتى لا تختفي من الشبكة
        var knownIds = classGroups.Select(c => c.Id).ToHashSet();
        var unknownClassIds = timetable.Lessons
            .Select(l => (Guid)l.ClassId)
            .Distinct()
            .Where(id => !knownIds.Contains(id))
            .ToList();
        foreach (var id in unknownClassIds)
        {
            var name = timetable.Lessons.FirstOrDefault(l => (Guid)l.ClassId == id)?.Class?.FullName
                       ?? "فصل غير معروف";
            classGroups.Add((id, name));
        }

        foreach (var (classId, className) in classGroups)
        {
            var isFirstPeriod = true;

            foreach (var period in periods)
            {
                var row = new TimetableGridRow
                {
                    Period = PeriodNumber.Create((byte)period),
                    ClassId = (ClassId)classId,
                    ClassName = isFirstPeriod ? className : "",
                    IsClassGroupStart = isFirstPeriod
                };

                // اسم الفترة + توقيتها إن وُجد (موحّداً؛ إن اختلفت بين المراحل يُعرض الاسم فقط)
                var rowTime = timetable.GetPeriodTimeForDisplay(period);
                row.PeriodName = rowTime == null
                    ? PeriodNames.GetFullName(period)
                    : $"{PeriodNames.GetFullName(period)} ({PeriodTime.FormatMinutes(rowTime.StartMinutes)}-{PeriodTime.FormatMinutes(rowTime.EndMinutes)})";

                foreach (var day in days)
                {
                    var dayLessons = timetable.Lessons
                        .Where(l => l.ClassId == classId && l.Day == day && l.Period.Value == period)
                        .ToList();

                    foreach (var lesson in dayLessons)
                    {
                        AddLessonToRow(row, day, CreateLessonCellViewModel(timetable, lesson));
                    }
                }

                _timetableGridRows.Add(row);
                isFirstPeriod = false;
            }
        }

        TotalLessonsCount = timetable.Lessons.Count;
        LessonsCount = timetable.Lessons.Count;
        BuildAscGrid(timetable);
    }

    /// <summary>
    /// يبني نموذج عرض aSc: الصفوف = الكيانات (الفصول)، الأعمدة = الأيام × الحصص،
    /// مع رأس أفقي فيه اسم كل يوم ممتدًّا فوق أعمدته.
    /// </summary>
    private void BuildAscGrid(Timetable timetable)
    {
        var view = new AscGridView();

        var maxUsedPeriod = timetable.Lessons.Count == 0 ? 0 : timetable.Lessons.Max(l => l.Period.Value);
        var periodCount = maxUsedPeriod switch
        {
            0 => 8,
            > 12 => 12,
            var p => p
        };
        var periods = Enumerable.Range(1, periodCount).ToList();

        // ترتيب الأيام: أسبوع الدراسة (الأحد..الخميس) ثم أي يوم آخر فيه حصص
        var displayDays = new List<DomainDayOfWeek>
        {
            DomainDayOfWeek.Sunday, DomainDayOfWeek.Monday, DomainDayOfWeek.Tuesday,
            DomainDayOfWeek.Wednesday, DomainDayOfWeek.Thursday
        };
        foreach (var d in timetable.Lessons.Select(l => l.Day).Distinct().OrderBy(d => (int)d))
            if (!displayDays.Contains(d)) displayDays.Add(d);

        // مجموعات الأيام = الأعمدة (كل يوم يحوي أعمدته الفرعية = الحصص)
        foreach (var day in displayDays)
        {
            var group = new AscDayGroup { Day = day, DayName = DayToArabic(day) };
            foreach (var period in periods)
                group.Periods.Add(new AscPeriodColumn
                {
                    Period = period,
                    Label = $"ح{period}",
                    TooltipText = PeriodLabelFor(timetable, period)
                });
            view.DayGroups.Add(group);
        }

        if (ViewMode == TimetableViewMode.SchoolMatrix)
        {
            view.RowHeaderLabel = "الفصل";

            var classGroups = Classes.Classes
                .OrderBy(c => c.GradeLevel).ThenBy(c => c.Section).ThenBy(c => c.Name)
                .Select(c => (Id: c.Id, Name: string.IsNullOrWhiteSpace(c.ShortName) ? c.Name : c.ShortName!))
                .ToList();

            var knownIds = classGroups.Select(c => c.Id).ToHashSet();
            foreach (var id in timetable.Lessons.Select(l => (Guid)l.ClassId).Distinct().Where(id => !knownIds.Contains(id)))
            {
                var name = timetable.Lessons.FirstOrDefault(l => (Guid)l.ClassId == id)?.Class?.FullName ?? "فصل غير معروف";
                classGroups.Add((id, name));
            }

            foreach (var (id, name) in classGroups)
                view.Rows.Add(BuildAscRow(timetable, id, name, l => (Guid)l.ClassId == id));
        }
        else
        {
            view.RowHeaderLabel = ViewMode switch
            {
                TimetableViewMode.ByTeacher => "المعلم",
                TimetableViewMode.ByRoom => "القاعة",
                TimetableViewMode.BySubject => "المادة",
                _ => "الفصل"
            };

            var entityId = SelectedEntityOption?.Id;
            if (entityId != null)
                view.Rows.Add(BuildAscRow(timetable, entityId.Value, SelectedEntityOption?.Name ?? "",
                    l => MatchesCurrentEntity(l, entityId.Value)));
        }

        AscGrid = view;
        AscGridRevision++;
        BuildProblemLessons(timetable);
    }

    /// <summary>يبني قائمة الحصص التي تحتاج انتباهًا: متعارضة، بلا قاعة، وغير معبأة.</summary>
    private void BuildProblemLessons(Timetable timetable)
    {
        _problemLessons.Clear();
        var seen = new HashSet<Guid>();

        // 1) التضاربات: نفس الوقت ويشتركان في المعلم أو الفصل أو القاعة
        foreach (var group in timetable.Lessons.GroupBy(l => ((int)l.Day, l.Period.Value)))
        {
            var list = group.ToList();
            var conflicted = new HashSet<int>();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    var clash = (Guid)a.TeacherId == (Guid)b.TeacherId
                        || (Guid)a.ClassId == (Guid)b.ClassId
                        || (a.RoomId != null && b.RoomId != null && (Guid)a.RoomId == (Guid)b.RoomId);
                    if (clash)
                    {
                        conflicted.Add(i);
                        conflicted.Add(j);
                    }
                }
            }

            foreach (var idx in conflicted)
            {
                var lesson = list[idx];
                if (seen.Add((Guid)lesson.Id))
                    _problemLessons.Add(MakeProblem(timetable, lesson, "متضارب"));
            }
        }

        // 2) غير مُسندة: حصة لها معلم وفصل لكنها لم تُوضع في الجدول (بلا فترة صحيحة)
        foreach (var lesson in timetable.Lessons.Where(l => l.Period.Value < 1))
        {
            if (seen.Add((Guid)lesson.Id))
                _problemLessons.Add(MakeProblem(timetable, lesson, "غير مُسندة"));
        }

        // 3) بلا قاعة
        foreach (var lesson in timetable.Lessons.Where(l => l.RoomId == null))
        {
            if (seen.Add((Guid)lesson.Id))
                _problemLessons.Add(MakeProblem(timetable, lesson, "بلا قاعة"));
        }

        ProblemCount = _problemLessons.Count;

        // بطاقات المعلم: كل حصة غير موضوعة (في انتظار التوليد)، بلون المعلم
        _pendingCards.Clear();
        foreach (var lesson in timetable.Lessons.Where(l => l.Period.Value < 1))
        {
            var vm = CreateLessonCellViewModel(timetable, lesson);
            _pendingCards.Add(new ProblemLessonItem
            {
                Lesson = vm,
                Title = $"{vm.SubjectShortName} - {vm.ClassShortName}".Trim(' ', '-'),
                Reason = vm.TeacherShortName
            });
        }

        PendingCardsCount = _pendingCards.Count;
    }

    private ProblemLessonItem MakeProblem(Timetable timetable, Lesson lesson, string reason)
    {
        var vm = CreateLessonCellViewModel(timetable, lesson);
        var title = $"{vm.SubjectShortName} - {vm.TeacherShortName}".Trim(' ', '-');
        return new ProblemLessonItem { Lesson = vm, Title = title, Reason = reason };
    }

    private AscRow BuildAscRow(Timetable timetable, Guid key, string name, Func<Lesson, bool> match)
    {
        var row = new AscRow { Key = key, Name = name };
        foreach (var lesson in timetable.Lessons.Where(match))
        {
            var cellKey = ((int)lesson.Day, lesson.Period.Value);
            if (!row.Cells.TryGetValue(cellKey, out var list))
            {
                list = new List<ScheduledLessonViewModel>();
                row.Cells[cellKey] = list;
            }
            list.Add(CreateLessonCellViewModel(timetable, lesson));
        }
        return row;
    }

    private static string PeriodLabelFor(Timetable timetable, int period)
    {
        var rowTime = timetable.GetPeriodTimeForDisplay(period);
        return rowTime == null
            ? PeriodNames.GetFullName(period)
            : $"{PeriodNames.GetFullName(period)} ({PeriodTime.FormatMinutes(rowTime.StartMinutes)}-{PeriodTime.FormatMinutes(rowTime.EndMinutes)})";
    }

    /// <summary>هل تنتمي الحصة إلى الكيان المعروض حالياً (حسب وضع العرض)</summary>
    private bool MatchesCurrentEntity(Lesson lesson, Guid entityId) => ViewMode switch
    {
        TimetableViewMode.ByClass => (Guid)lesson.ClassId == entityId,
        TimetableViewMode.ByTeacher => (Guid)lesson.TeacherId == entityId,
        TimetableViewMode.ByRoom => lesson.RoomId is { } rid && (Guid)rid == entityId,
        TimetableViewMode.BySubject => (Guid)lesson.SubjectId == entityId,
        TimetableViewMode.ByGroup => lesson.GroupId == entityId,
        TimetableViewMode.ByStudent => (_currentTimetable?.Students.FirstOrDefault(s => s.Id == entityId)?.ClassId == (Guid)lesson.ClassId) &&
                                       ((_currentTimetable?.Students.FirstOrDefault(s => s.Id == entityId)?.GroupIds.Contains(lesson.GroupId ?? Guid.Empty) ?? false) || lesson.GroupId == null),
        _ => true
    };

    /// <summary>سياق عرض البطاقة حسب وضع العرض الحالي</summary>
    private ChipDisplayContext ChipContextForViewMode() => ViewMode switch
    {
        TimetableViewMode.ByClass => ChipDisplayContext.Class,
        TimetableViewMode.ByTeacher => ChipDisplayContext.Teacher,
        TimetableViewMode.ByRoom => ChipDisplayContext.Room,
        TimetableViewMode.BySubject => ChipDisplayContext.Subject,
        TimetableViewMode.ByStudent => ChipDisplayContext.Student,
        TimetableViewMode.ByGroup => ChipDisplayContext.Group,
        _ => ChipDisplayContext.School
    };

    /// <summary>بناء نموذج عرض حصة واحدة داخل خلية (يحل الكيانات من ذاكرة الإدارة)</summary>
    private ScheduledLessonViewModel CreateLessonCellViewModel(Timetable timetable, Lesson lesson)
    {
        // حل الكيانات من ذاكرة الإدارة (العلاقات قد تكون null في جدول مولّد حديثاً)
        var subject = Subjects.Subjects.FirstOrDefault(s => s.Id == lesson.SubjectId);
        var teacher = Teachers.Teachers.FirstOrDefault(t => t.Id == lesson.TeacherId);
        var classRoom = Classes.Classes.FirstOrDefault(c => c.Id == lesson.ClassId);
        var room = lesson.RoomId == null ? null : Rooms.Rooms.FirstOrDefault(r => r.Id == lesson.RoomId);

        // توقيت الحصة حسب اليوم ومرحلة الصف (مع رجوع للتوقيت العام)
        var stage = classRoom == null ? null : StageHelper.GetStage(classRoom.GradeLevel, classRoom.Name);
        var cellTime = timetable.GetPeriodTime(lesson.Period.Value, (int)lesson.Day, stage)
                       ?? timetable.GetPeriodTime(lesson.Period.Value, -1, stage)
                       ?? timetable.GetPeriodTime(lesson.Period.Value, (int)lesson.Day, null);

        return new ScheduledLessonViewModel
        {
            Lesson = lesson,
            ResolvedSubject = subject,
            ResolvedTeacher = teacher,
            ResolvedClass = classRoom,
            ResolvedRoom = room,
            UseTeacherColor = ColorByTeacher,
            ChipContext = ChipContextForViewMode(),
            PeriodTimeText = cellTime == null
                ? ""
                : $"{PeriodTime.FormatMinutes(cellTime.StartMinutes)}-{PeriodTime.FormatMinutes(cellTime.EndMinutes)}",
            EditCommand = new RelayCommand(() => EditLesson(lesson))
        };
    }

    /// <summary>إضافة حصة إلى مجموعة خلية يومها (تُعرض في عمود ذلك اليوم فقط)</summary>
    private static void AddLessonToRow(TimetableGridRow row, DomainDayOfWeek day, ScheduledLessonViewModel vm)
    {
        switch (day)
        {
            case DomainDayOfWeek.Saturday: row.SaturdayLessons.Add(vm); break;
            case DomainDayOfWeek.Sunday: row.SundayLessons.Add(vm); break;
            case DomainDayOfWeek.Monday: row.MondayLessons.Add(vm); break;
            case DomainDayOfWeek.Tuesday: row.TuesdayLessons.Add(vm); break;
            case DomainDayOfWeek.Wednesday: row.WednesdayLessons.Add(vm); break;
            case DomainDayOfWeek.Thursday: row.ThursdayLessons.Add(vm); break;
            case DomainDayOfWeek.Friday: row.FridayLessons.Add(vm); break;
        }
    }

    /// <summary>
    /// فتح خصائص الحصة في اللوحة الجانبية
    /// </summary>
    private void EditLesson(Lesson lesson)
    {
        SelectedLesson = lesson;
        RightPanelTab = 0; // تبويب الخصائص
    }

    private void EditSelectedLesson()
    {
        if (SelectedLesson == null) return;
        RightPanelTab = 0;
        UpdateSelectedItemProperties();
    }

    private void DeleteSelectedLesson()
    {
        if (SelectedLesson == null || _currentTimetable == null) return;

        var confirm = MessageBox.Show(
            $"هل تريد حذف حصة \"{SelectedLesson.GetDisplayName()}\"؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        PushUndoState();
        _currentTimetable.RemoveLesson(SelectedLesson);
        SelectedLesson = null;
        UpdateTimetableGrid(_currentTimetable);
    }

    private void ToggleSelectedLessonFixed()
    {
        if (SelectedLesson == null || _currentTimetable == null) return;

        PushUndoState();
        SelectedLesson.Fix(!SelectedLesson.IsFixed);
        UpdateTimetableGrid(_currentTimetable);
    }

    /// <summary>يعرض تشخيص الحصة المحددة: عدد المواضع المناسبة وأسباب الرفض الأكثر تكرارًا.</summary>
    private void ShowSelectedLessonDiagnosis()
    {
        if (SelectedLesson == null || _currentTimetable == null)
        {
            MessageBox.Show("اختر حصة أولًا.", "التشخيص", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var d = DiagnoseUnscheduledLesson(SelectedLesson);
        var sb = new StringBuilder();
        sb.AppendLine($"الحصة: {d.LessonTitle}");
        sb.AppendLine(d.Summary);

        if (d.GoodSlots.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("أمثل المواضع:");
            foreach (var s in d.GoodSlots.Take(10)) sb.AppendLine("• " + s);
            if (d.GoodSlots.Count > 10) sb.AppendLine($"… و{d.GoodSlots.Count - 10} موضعًا آخر.");
        }
        else if (d.ReasonCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("أسباب الرفض (عدد المواضع المتأثرة):");
            foreach (var kv in d.ReasonCounts.Take(8)) sb.AppendLine($"• {kv.Key}: {kv.Value}");
        }

        MessageBox.Show(sb.ToString().TrimEnd(), "تشخيص الحصة", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// بناء لوحة خصائص الحصة المحددة
    /// </summary>
    private void UpdateSelectedItemProperties()
    {
        if (SelectedLesson == null)
        {
            SelectedItemPropertiesView = null;
            return;
        }

        var subject = Subjects.Subjects.FirstOrDefault(s => s.Id == SelectedLesson.SubjectId)
                      ?? SelectedLesson.Subject;
        var teacher = Teachers.Teachers.FirstOrDefault(t => t.Id == SelectedLesson.TeacherId)
                      ?? SelectedLesson.Teacher;
        var classRoom = Classes.Classes.FirstOrDefault(c => c.Id == SelectedLesson.ClassId)
                        ?? SelectedLesson.Class;
        var room = SelectedLesson.RoomId == null
            ? SelectedLesson.Room
            : Rooms.Rooms.FirstOrDefault(r => r.Id == SelectedLesson.RoomId) ?? SelectedLesson.Room;

        SelectedItemPropertiesView = new LessonPropertiesView
        {
            DataContext = new LessonPropertiesViewModel
            {
                SubjectName = subject?.Name ?? "غير محدد",
                TeacherName = teacher?.FullName ?? "غير محدد",
                ClassName = classRoom?.FullName ?? "غير محدد",
                RoomName = room?.Name ?? "بدون غرفة",
                DayName = DayToArabic(SelectedLesson.Day),
                PeriodName = PeriodNames.GetFullName(SelectedLesson.Period.Value),
                DurationText = $"{SelectedLesson.Duration.Value} دقيقة",
                TypeText = LessonTypeToArabic(SelectedLesson.Type),
                StatusText = SelectedLesson.IsFixed ? "مثبتة (لا تتغير عند التوليد)" : "حصة عادية",
                Notes = SelectedLesson.Notes ?? "—"
            }
        };
    }

    private static string DayToArabic(DomainDayOfWeek day) => day switch
    {
        DomainDayOfWeek.Saturday => "السبت",
        DomainDayOfWeek.Sunday => "الأحد",
        DomainDayOfWeek.Monday => "الاثنين",
        DomainDayOfWeek.Tuesday => "الثلاثاء",
        DomainDayOfWeek.Wednesday => "الأربعاء",
        DomainDayOfWeek.Thursday => "الخميس",
        DomainDayOfWeek.Friday => "الجمعة",
        _ => day.ToString()
    };

    private static string LessonTypeToArabic(LessonType type) => type switch
    {
        LessonType.Regular => "عادية",
        LessonType.Lab => "مختبر",
        LessonType.Activity => "نشاط",
        LessonType.Exam => "امتحان",
        LessonType.Break => "استراحة",
        _ => type.ToString()
    };

    /// <summary>درجة الجودة الموحّدة: تُحسب بنفس مدقق القيود الذي يستخدمه الحلّال
    /// (وعند توفّر نتيجة جاهزة منه تُعرض مباشرة لئلا تُحسب مرتين)</summary>
    private void UpdateQualityScore(double? score = null)
    {
        if (score is { } provided)
        {
            QualityScoreText = $"{provided:F0}%";
            return;
        }

        if (_currentTimetable == null || _currentTimetable.Lessons.Count == 0)
        {
            QualityScoreText = "0%";
            return;
        }

        try
        {
            AttachLessonNavigation(_currentTimetable);
            var validation = _validator.Validate(TimetableContextFactory.Build(_currentTimetable));
            QualityScoreText = $"{validation.QualityScore.Value:F0}%";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر حساب درجة الجودة من المدقق");
            var fallback = Math.Clamp(
                100 - (ConflictsCount * 100 / Math.Max(1, _currentTimetable.Lessons.Count)), 0, 100);
            QualityScoreText = $"{fallback}%";
        }
    }

    private void OnManagementCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _ = RefreshStatsAsync();
    }

    // ===== عمليات الاستيراد والتصدير =====

    private bool EnsureTimetable()
    {
        if (_currentTimetable != null) return true;
        MessageBox.Show("أنشئ جدولًا أو افتحه أولًا", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void DoExport(string filter, string suggestedFileName, Action<string> write)
    {
        if (!EnsureTimetable()) return;

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = suggestedFileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            write(dialog.FileName);
            _logger.LogInformation("تم التصدير: {Path}", dialog.FileName);
            MessageBox.Show($"تم التصدير بنجاح:\n{dialog.FileName}", "تصدير", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في التصدير");
            MessageBox.Show($"فشل التصدير:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (string Subject, string Teacher, string Class, string Room) GetLessonNames(Lesson lesson)
    {
        var subject = Subjects.Subjects.FirstOrDefault(s => s.Id == lesson.SubjectId)?.Name
                      ?? lesson.Subject?.Name ?? "";
        var teacher = Teachers.Teachers.FirstOrDefault(t => t.Id == lesson.TeacherId)?.FullName
                      ?? lesson.Teacher?.FullName ?? "";
        var classRoom = Classes.Classes.FirstOrDefault(c => c.Id == lesson.ClassId)?.DisplayShortName
                        ?? lesson.Class?.DisplayShortName ?? lesson.Class?.FullName ?? "";
        var room = lesson.RoomId == null
            ? (lesson.Room?.Name ?? "")
            : (Rooms.Rooms.FirstOrDefault(r => r.Id == lesson.RoomId)?.Name ?? lesson.Room?.Name ?? "");
        return (subject, teacher, classRoom, room);
    }

    private IEnumerable<Lesson> OrderedLessons() =>
        _currentTimetable!.Lessons
            .OrderBy(l => l.Day)
            .ThenBy(l => l.Period.Value);

    private static string CsvEscape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    private void ExportCsv(string kind)
    {
        DoExport("CSV (*.csv)|*.csv", $"{_currentTimetable?.Name ?? "جدول"}.csv", path =>
        {
            var sb = new StringBuilder();
            if (kind == "plain")
                sb.AppendLine("اليوم,الفترة,المادة,المعلم,الفصل,الغرفة,المدة");
            else if (kind == "madrasati")
                sb.AppendLine("المادة,المعلم,الفصل,الغرفة,اليوم,الفترة");
            else // noor
                sb.AppendLine("Subject,Teacher,Class,Room,Day,Period");

            foreach (var lesson in OrderedLessons())
            {
                var names = GetLessonNames(lesson);
                if (kind == "plain")
                    sb.AppendLine(string.Join(",",
                        CsvEscape(DayToArabic(lesson.Day)), lesson.Period.Value,
                        CsvEscape(names.Subject), CsvEscape(names.Teacher),
                        CsvEscape(names.Class), CsvEscape(names.Room),
                        lesson.Duration.Value));
                else if (kind == "madrasati")
                    sb.AppendLine(string.Join(",",
                        CsvEscape(names.Subject), CsvEscape(names.Teacher),
                        CsvEscape(names.Class), CsvEscape(names.Room),
                        CsvEscape(DayToArabic(lesson.Day)), lesson.Period.Value));
                else
                    sb.AppendLine(string.Join(",",
                        CsvEscape(names.Subject), CsvEscape(names.Teacher),
                        CsvEscape(names.Class), CsvEscape(names.Room),
                        CsvEscape(lesson.Day.ToString()), lesson.Period.Value));
            }

            // BOM لضمان ظهور العربية بشكل صحيح في Excel
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        });
    }

    private void ExportExcel(string kind)
    {
        DoExport("Excel (*.xlsx)|*.xlsx", $"{_currentTimetable?.Name ?? "جدول"}.xlsx", path =>
        {
            var rows = new List<string[]>();
            if (kind == "madrasati")
            {
                rows.Add(new[] { "المادة", "المعلم", "الفصل", "الغرفة", "اليوم", "الفترة" });
                foreach (var lesson in OrderedLessons())
                {
                    var n = GetLessonNames(lesson);
                    rows.Add(new[] { n.Subject, n.Teacher, n.Class, n.Room, DayToArabic(lesson.Day), lesson.Period.Value.ToString() });
                }
            }
            else if (kind == "noor")
            {
                rows.Add(new[] { "Subject", "Teacher", "Class", "Room", "Day", "Period" });
                foreach (var lesson in OrderedLessons())
                {
                    var n = GetLessonNames(lesson);
                    rows.Add(new[] { n.Subject, n.Teacher, n.Class, n.Room, lesson.Day.ToString(), lesson.Period.Value.ToString() });
                }
            }
            else // pro
            {
                rows.Add(new[] { "اليوم", "الفترة", "المادة", "المعلم", "الفصل", "الغرفة", "المدة", "النوع", "ملاحظات" });
                foreach (var lesson in OrderedLessons())
                {
                    var n = GetLessonNames(lesson);
                    rows.Add(new[]
                    {
                        DayToArabic(lesson.Day), lesson.Period.Value.ToString(),
                        n.Subject, n.Teacher, n.Class, n.Room,
                        $"{lesson.Duration.Value} د", LessonTypeToArabic(lesson.Type),
                        lesson.Notes ?? ""
                    });
                }
            }

            WriteXlsx(path, rows);
        });
    }

    private static void WriteXlsx(string path, IReadOnlyList<string[]> rows)
    {
        static string CellRef(int col, int row)
        {
            var letter = (char)('A' + col);
            return $"{letter}{row}";
        }

        var sheetData = new StringBuilder();
        for (var r = 0; r < rows.Count; r++)
        {
            sheetData.Append("<row r=\"").Append(r + 1).Append("\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var text = System.Security.SecurityElement.Escape(rows[r][c]) ?? "";
                sheetData.Append("<c r=\"").Append(CellRef(c, r + 1)).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                    .Append(text).Append("</t></is></c>");
            }
            sheetData.Append("</row>");
        }

        const string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>";

        const string rootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        const string workbook =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"الجدول\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

        const string workbookRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>";

        var sheet =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<sheetData>" + sheetData + "</sheetData></worksheet>";

        if (File.Exists(path)) File.Delete(path);
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        static void AddEntry(System.IO.Compression.ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        AddEntry(zip, "[Content_Types].xml", contentTypes);
        AddEntry(zip, "_rels/.rels", rootRels);
        AddEntry(zip, "xl/workbook.xml", workbook);
        AddEntry(zip, "xl/_rels/workbook.xml.rels", workbookRels);
        AddEntry(zip, "xl/worksheets/sheet1.xml", sheet);
    }

    private void ExportXml(string kind)
    {
        DoExport("XML (*.xml)|*.xml", $"{_currentTimetable?.Name ?? "جدول"}.xml", path =>
        {
            var rootName = kind == "noor" ? "NoorTimetable" : "Timetable";
            var root = new XElement(rootName,
                new XAttribute("name", _currentTimetable!.Name),
                new XAttribute("academicYearStart", _currentTimetable.AcademicYearStart),
                new XAttribute("academicYearEnd", _currentTimetable.AcademicYearEnd),
                new XAttribute("term", _currentTimetable.TermNumber));

            foreach (var lesson in OrderedLessons())
            {
                var n = GetLessonNames(lesson);
                root.Add(new XElement("Lesson",
                    new XAttribute("day", lesson.Day),
                    new XAttribute("period", lesson.Period.Value),
                    new XAttribute("duration", lesson.Duration.Value),
                    new XElement("Subject", n.Subject),
                    new XElement("Teacher", n.Teacher),
                    new XElement("Class", n.Class),
                    new XElement("Room", n.Room)));
            }

            new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(path);
        });
    }

    private void ExportJson()
    {
        DoExport("JSON (*.json)|*.json", $"{_currentTimetable?.Name ?? "جدول"}.json", path =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_currentTimetable, JsonOpts);
            File.WriteAllText(path, json);
        });
    }

    private void ExportHtml()
    {
        DoExport("HTML (*.html)|*.html", $"{_currentTimetable?.Name ?? "جدول"}.html", path =>
        {
            File.WriteAllText(path, BuildTimetableHtml(), new UTF8Encoding(true));
        });
    }

    /// <summary>بناء صفحة HTML للجدول الحالي عبر تقرير الويب (يُستخدم للتصدير وللمشاركة المحلية)</summary>
    private string BuildTimetableHtml()
    {
        if (_currentTimetable == null)
            return "<!DOCTYPE html><html dir=\"rtl\"><body><p>لا يوجد جدول حالياً</p></body></html>";

        var report = _services.GetRequiredService<IWebReportService>();
        var options = new WebReportOptions { SchoolName = ResolveSchoolName() };
        return report.GenerateHtml(_currentTimetable, options, GetLessonNames);
    }

    private void ExportICal()
    {
        DoExport("iCalendar (*.ics)|*.ics", $"{_currentTimetable?.Name ?? "جدول"}.ics", path =>
        {
            var dayCodes = new Dictionary<DomainDayOfWeek, string>
            {
                [DomainDayOfWeek.Saturday] = "SA",
                [DomainDayOfWeek.Sunday] = "SU",
                [DomainDayOfWeek.Monday] = "MO",
                [DomainDayOfWeek.Tuesday] = "TU",
                [DomainDayOfWeek.Wednesday] = "WE",
                [DomainDayOfWeek.Thursday] = "TH",
                [DomainDayOfWeek.Friday] = "FR"
            };

            // بداية الأسبوع: أول سبتمبر من السنة الدراسية
            var start = new DateTime(_currentTimetable!.AcademicYearStart, 9, 1);
            while (start.DayOfWeek != System.DayOfWeek.Saturday) start = start.AddDays(1);

            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//Awad Timetable//AR");
            sb.AppendLine("CALSCALE:GREGORIAN");

            foreach (var lesson in OrderedLessons())
            {
                var n = GetLessonNames(lesson);
                var lessonStart = start.AddDays((int)lesson.Day).AddMinutes((lesson.Period.Value - 1) * 50 + 8 * 60);

                sb.AppendLine("BEGIN:VEVENT");
                sb.AppendLine($"UID:{Guid.NewGuid()}@awadtimetable");
                sb.AppendLine($"DTSTAMP:{DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")}");
                sb.AppendLine($"DTSTART:{lessonStart.ToString("yyyyMMddTHHmmss")}");
                sb.AppendLine($"RRULE:FREQ=WEEKLY;BYDAY={dayCodes[lesson.Day]}");
                sb.AppendLine($"SUMMARY:{EscapeIcal($"{n.Subject} - {n.Class}")}");
                if (!string.IsNullOrEmpty(n.Room))
                    sb.AppendLine($"LOCATION:{EscapeIcal(n.Room)}");
                sb.AppendLine($"DESCRIPTION:{EscapeIcal($"المعلم: {n.Teacher} | الفترة {lesson.Period.Value} | {lesson.Duration.Value} دقيقة")}");
                sb.AppendLine("END:VEVENT");
            }

            sb.AppendLine("END:VCALENDAR");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        });
    }

    private static string EscapeIcal(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");

    // ===== الاستيراد =====

    private async void ImportJson()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json|جميع الملفات (*.*)|*.*",
            Title = "استيراد جدول JSON",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var timetable = System.Text.Json.JsonSerializer.Deserialize<Timetable>(json, JsonOpts);
            if (timetable == null) throw new InvalidDataException("الملف فارغ أو غير صالح");

            _currentTimetable = timetable;
            CurrentTimetableName = timetable.Name;
            TimetableStatusText = timetable.Status.ToString();
            await SwitchWorkspaceAsync(timetable);
            UpdateTimetableGrid(timetable);
            ResetHistory();
            _logger.LogInformation("تم استيراد جدول JSON: {Name}", timetable.Name);
            MessageBox.Show("تم استيراد الجدول بنجاح", "استيراد", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في استيراد JSON");
            MessageBox.Show($"فشل الاستيراد:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportCsv()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|جميع الملفات (*.*)|*.*",
            Title = "استيراد حصص من CSV",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return;

        if (!EnsureTimetable()) return;

        try
        {
            var lines = File.ReadAllLines(dialog.FileName)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(SplitCsvLine)
                .ToList();

            ImportRows(lines, "CSV");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في استيراد CSV");
            MessageBox.Show($"فشل الاستيراد:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// استيراد الحصص من صفوف جدولية (الصف الأول = رأس أعمدة: اليوم، الفترة، المادة، المعلم، الفصل، الغرفة)
    /// </summary>
    private void ImportRows(IReadOnlyList<string[]> rows, string sourceLabel)
    {
        if (rows.Count < 2)
            throw new InvalidDataException("الملف لا يحتوي على بيانات (يحتاج عنوان أعمدة + صف واحد على الأقل)");

        var header = rows[0].Select(h => (h ?? "").Trim().ToLowerInvariant()).ToList();

        int DayCol() => header.FindIndex(h => h.Contains("يوم") || h == "day");
        int PeriodCol() => header.FindIndex(h => h.Contains("فترة") || h == "period");
        int SubjectCol() => header.FindIndex(h => h.Contains("مادة") || h == "subject");
        int TeacherCol() => header.FindIndex(h => h.Contains("معلم") || h == "teacher" || h.Contains("أستاذ"));
        int ClassCol() => header.FindIndex(h => h.Contains("فصل") || h == "class");
        int RoomCol() => header.FindIndex(h => h.Contains("غرف") || h == "room");

        var dayIdx = DayCol(); var periodIdx = PeriodCol();
        var subjectIdx = SubjectCol(); var teacherIdx = TeacherCol();
        var classIdx = ClassCol(); var roomIdx = RoomCol();

        if (dayIdx < 0 || periodIdx < 0 || subjectIdx < 0 || teacherIdx < 0 || classIdx < 0)
            throw new InvalidDataException("يجب أن يحتوي الرأس على أعمدة: اليوم، الفترة، المادة، المعلم، الفصل");

        var imported = 0;
        var skipped = 0;

        foreach (var raw in rows.Skip(1))
        {
            var cells = raw ?? Array.Empty<string>();
            if (cells.Length <= Math.Max(Math.Max(dayIdx, periodIdx), Math.Max(subjectIdx, teacherIdx)))
            {
                skipped++;
                continue;
            }

            if (!TryParseDay((cells[dayIdx] ?? "").Trim(), out var day)) { skipped++; continue; }
            if (!int.TryParse((cells[periodIdx] ?? "").Trim(), out var period) || period < 1 || period > 15) { skipped++; continue; }

            var subjectName = (cells[subjectIdx] ?? "").Trim();
            var teacherName = (cells[teacherIdx] ?? "").Trim();
            var className = (cells[classIdx] ?? "").Trim();
            var roomName = roomIdx >= 0 && roomIdx < cells.Length ? (cells[roomIdx] ?? "").Trim() : "";

            if (string.IsNullOrEmpty(subjectName) || string.IsNullOrEmpty(teacherName) || string.IsNullOrEmpty(className))
            {
                skipped++;
                continue;
            }

            var subject = Subjects.Subjects.FirstOrDefault(s =>
                string.Equals(s.Name, subjectName, StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains(subjectName, StringComparison.OrdinalIgnoreCase));
            var teacher = Teachers.Teachers.FirstOrDefault(t =>
                t.FullName.Contains(teacherName, StringComparison.OrdinalIgnoreCase));
            var classRoom = Classes.Classes.FirstOrDefault(c =>
                c.FullName.Contains(className, StringComparison.OrdinalIgnoreCase));
            var room = string.IsNullOrEmpty(roomName) ? null : Rooms.Rooms.FirstOrDefault(r =>
                r.Name.Contains(roomName, StringComparison.OrdinalIgnoreCase));

            if (subject == null || teacher == null || classRoom == null)
            {
                skipped++;
                continue;
            }

            var lesson = new Lesson(
                (TimetableId)_currentTimetable!.Id,
                (SubjectId)subject.Id,
                (TeacherId)teacher.Id,
                (ClassId)classRoom.Id,
                day,
                PeriodNumber.Create((byte)period));
            if (room != null) lesson.AssignRoom((RoomId)room.Id);

            _currentTimetable.Lessons.Add(lesson);
            imported++;
        }

        if (imported > 0)
        {
            UpdateTimetableGrid(_currentTimetable!);
            PushUndoState();
        }

        MessageBox.Show(
            $"تم استيراد {imported} حصة من {sourceLabel}.\nتم تخطي {skipped} صف (بيانات ناقصة أو غير مطابقة).",
            $"استيراد {sourceLabel}", MessageBoxButton.OK,
            imported > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        _logger.LogInformation("استيراد {Source}: {Imported} تم، {Skipped} تم تخطيهما", sourceLabel, imported, skipped);
    }

    /// <summary>استيراد الحصص من ملف Excel (.xlsx) — قراءة مباشرة بدون مكتبات خارجية</summary>
    [RelayCommand]
    private Task ImportExcelAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx|جميع الملفات (*.*)|*.*",
            Title = "استيراد حصص من Excel",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        if (!EnsureTimetable()) return Task.CompletedTask;

        try
        {
            var rows = ReadXlsxRows(dialog.FileName);
            ImportRows(rows, "Excel");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في استيراد Excel");
            MessageBox.Show($"فشل الاستيراد:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    /// <summary>قراءة أول ورقة عمل من ملف xlsx (يدعم inlineStr و sharedStrings والأرقام)</summary>
    private static List<string[]> ReadXlsxRows(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        // نصوص مشتركة (إن وجدت)
        var shared = new List<string>();
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry != null)
        {
            using var ssStream = ssEntry.Open();
            var ssDoc = XDocument.Load(ssStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var si in ssDoc.Root!.Elements(ns + "si"))
            {
                shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
            }
        }

        // أول ورقة عمل
        var sheetEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("الملف لا يحتوي على ورقة عمل (ليس ملف Excel صالح)");

        using var sheetStream = sheetEntry.Open();
        var doc = XDocument.Load(sheetStream);
        XNamespace nsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        static int ColumnIndex(string cellRef)
        {
            var col = 0;
            foreach (var ch in cellRef)
            {
                if (char.IsLetter(ch)) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                else break;
            }
            return Math.Max(0, col - 1);
        }

        var rowMap = new SortedDictionary<int, string[]>();

        foreach (var rowEl in doc.Root!.Element(nsMain + "sheetData")!.Elements(nsMain + "row"))
        {
            var rowNumAttr = (int?)rowEl.Attribute("r") ?? (rowMap.Count + 1);
            var cells = new List<(int Col, string Val)>();

            foreach (var c in rowEl.Elements(nsMain + "c"))
            {
                var cellRef = (string?)c.Attribute("r") ?? "A1";
                var col = ColumnIndex(cellRef);
                var type = (string?)c.Attribute("t") ?? "";
                string value;

                if (type == "inlineStr")
                {
                    value = string.Concat((c.Element(nsMain + "is")?.Descendants(nsMain + "t") ?? Enumerable.Empty<XElement>())
                        .Select(t => t?.Value ?? ""));
                }
                else if (type == "s")
                {
                    var vText = c.Element(nsMain + "v")?.Value;
                    value = int.TryParse(vText, out var idx) && idx >= 0 && idx < shared.Count
                        ? shared[idx]
                        : "";
                }
                else
                {
                    value = c.Element(nsMain + "v")?.Value ?? "";
                }

                cells.Add((col, value));
            }

            if (cells.Count == 0) continue;
            var maxCol = cells.Max(x => x.Col);
            var arr = new string[maxCol + 1];
            foreach (var (col, val) in cells) arr[col] = val;
            rowMap[rowNumAttr] = arr;
        }

        if (rowMap.Count == 0)
            throw new InvalidDataException("ورقة العمل فارغة");

        return rowMap.Values.ToList();
    }

    /// <summary>استيراد جدول من صورة عبر نموذج رؤية Ollama (llava وما يماثله)</summary>
    [RelayCommand]
    private async Task ImportFromImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "صور (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|جميع الملفات (*.*)|*.*",
            Title = "استيراد جدول من صورة",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dialog.ShowDialog() != true) return;
        if (!EnsureTimetable()) return;

        try
        {
            var imageB64 = Convert.ToBase64String(File.ReadAllBytes(dialog.FileName));
            var settings = _services.GetRequiredService<ISettingsService>();
            var baseUrl = settings.Get<string>(SettingsKeys.OllamaBaseUrl, "http://localhost:11434") ?? "http://localhost:11434";

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            var models = await GetOllamaModelsAsync(http, baseUrl);
            if (models == null)
            {
                MessageBox.Show(
                    "تعذر الاتصال بـ Ollama.\nشغّل Ollama أولاً ثم أعد المحاولة.",
                    "استيراد صورة", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var visionModels = models.Where(IsVisionModelName).ToList();
            if (visionModels.Count == 0)
            {
                var answer = MessageBox.Show(
                    "لا يوجد نموذج رؤية مثبت في Ollama.\n" +
                    $"النماذج المثبتة: {string.Join("، ", models)}\n\n" +
                    "الاستيراد من صورة يحتاج نموذجاً مثل llava (يُنزّل مرة واحدة، ~1.7 جيجابايت).\n\n" +
                    "هل تريد تنزيل نموذج llava الآن؟",
                    "استيراد صورة", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes) return;

                MessageBox.Show("جاري تنزيل نموذج llava... قد يستغرق دقائق حسب سرعة الإنترنت.",
                    "تنزيل نموذج الرؤية", MessageBoxButton.OK, MessageBoxImage.Information);

                var downloaded = await DownloadVisionModelAsync();
                if (!downloaded)
                {
                    MessageBox.Show(
                        "فشل تنزيل نموذج llava.\nيمكنك تنزيله يدوياً من الطرفية بتنفيذ: ollama pull llava",
                        "استيراد صورة", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                models = await GetOllamaModelsAsync(http, baseUrl);
                visionModels = (models ?? new List<string>()).Where(IsVisionModelName).ToList();
                if (visionModels.Count == 0)
                {
                    MessageBox.Show(
                        "تم التنزيل لكن النموذج لم يُكتشف بعد.\nأعد تشغيل التطبيق ثم أعد المحاولة.",
                        "استيراد صورة", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string? responseText = null;
            string usedModel = "";
            var lastError = "";

            foreach (var model in visionModels)
            {
                try
                {
                    responseText = await OllamaVisionGenerateAsync(http, baseUrl, model, imageB64);
                    usedModel = model;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.LogWarning("نموذج {Model} فشل في قراءة الصورة: {Error}", model, ex.Message);
                }
            }

            if (responseText == null)
            {
                MessageBox.Show(
                    $"تعذر قراءة الصورة بأي نموذج رؤية مثبت.\n{lastError}",
                    "استيراد صورة", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var rows = ParseVisionRows(responseText);
            if (rows.Count < 2)
            {
                MessageBox.Show(
                    "لم يُعثر على حصص واضحة في الصورة.\nجرّب صورة أوضح (جدول ظاهر بالكامل).",
                    "استيراد صورة", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ImportRows(rows, $"صورة ({usedModel})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في استيراد الصورة");
            MessageBox.Show($"فشل الاستيراد من الصورة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>استيراد جدول من ملف aSc Timetables XML (‎.xml) — حصص مجدولة + إنشاء الكيانات الناقصة</summary>
    [RelayCommand]
    private async Task ImportAscAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "aSc XML (*.xml)|*.xml|جميع الملفات (*.*)|*.*",
            Title = "استيراد جدول من aSc XML",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() != true) return;

        // معاينة الاستيراد قبل أي كتابة (عدد العناصر والحصص)
        try
        {
            var preview = Core.Services.AscXmlReader.ParseFile(dialog.FileName);
            var proceed = MessageBox.Show(
                "معاينة ملف aSc:\n" +
                $"• المواد: {preview.Subjects.Count}\n" +
                $"• المعلمون: {preview.Teachers.Count}\n" +
                $"• الفصول: {preview.Classes.Count}\n" +
                $"• القاعات: {preview.Rooms.Count}\n" +
                $"• الحصص (بطاقات): {preview.Cards.Count}\n\n" +
                (_currentTimetable == null
                    ? "لا يوجد جدول مفتوح: سيُنشأ جدول جديد في مساحة عمل فارغة.\n\n"
                    : "ستُضاف الحصص إلى الجدول المفتوح.\n\n") +
                "متابعة الاستيراد؟",
                "معاينة الاستيراد", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (proceed != MessageBoxResult.Yes) return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر قراءة الملف:\n{ex.Message}", "استيراد aSc", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_currentTimetable == null)
        {
            // الاستيراد يبدأ دائماً بمساحة عمل فارغة تماماً باسم الملف — لا يمسّ جداول أخرى
            var schoolService = _services.GetRequiredService<CurrentSchoolService>();
            var now = DateTime.Now;
            var startYear = now.Month >= 8 ? now.Year : now.Year - 1;
            var name = Path.GetFileNameWithoutExtension(dialog.FileName);
            if (string.IsNullOrWhiteSpace(name)) name = "جدول مستورد";

            var schoolId = await schoolService.CreateEmptyWorkspaceAsync(name);
            _currentTimetable = new Timetable(schoolId, name, startYear, startYear + 1, 1)
            {
                PeriodTimes = SchoolTimingPresets.DefaultGeneral(),
                StageConfigs = SchoolTimingPresets.DefaultStageConfigs("default")
            };
            CurrentTimetableName = _currentTimetable.Name;
            TimetableStatusText = _currentTimetable.Status.ToString();
            ResetHistory();
            _logger.LogInformation("إنشاء مساحة عمل وجدول للاستيراد: {Name}", _currentTimetable.Name);
        }

        try
        {
            var svc = _services.GetRequiredService<AscXmlImportService>();
            var result = await svc.ImportAsync(dialog.FileName);

            // تحديث قوائم الإدارة (لإنشاء الكيانات الجديدة في قاعدة البيانات)
            await Task.WhenAll(
                Subjects.LoadAsync(), Teachers.LoadAsync(), Classes.LoadAsync(), Rooms.LoadAsync());

            var subjByName = Subjects.Subjects.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var teachByName = Teachers.Teachers.GroupBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var classByName = Classes.Classes.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var roomByName = Rooms.Rooms.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var existing = _currentTimetable!.Lessons
                .Select(l => (l.ClassId, (byte)l.Day, l.Period.Value))
                .ToHashSet();

            var imported = 0;
            var skippedRows = result.Skipped;
            foreach (var p in result.Placements)
            {
                if (!subjByName.TryGetValue(p.SubjectName, out var subject)
                    || !teachByName.TryGetValue(p.TeacherName, out var teacher)
                    || !classByName.TryGetValue(p.ClassName, out var classRoom)) { skippedRows++; continue; }

                var key = ((ClassId)classRoom.Id, (byte)p.Day, p.Period);
                if (existing.Contains(key)) { skippedRows++; continue; }

                var lesson = new Lesson(
                    (TimetableId)_currentTimetable.Id,
                    (SubjectId)subject.Id,
                    (TeacherId)teacher.Id,
                    (ClassId)classRoom.Id,
                    p.Day,
                    PeriodNumber.Create(p.Period));
                if (p.RoomName != null && roomByName.TryGetValue(p.RoomName, out var room)) lesson.AssignRoom((RoomId)room.Id);

                _currentTimetable.Lessons.Add(lesson);
                existing.Add(key);
                imported++;
            }

            if (imported > 0)
            {
                UpdateTimetableGrid(_currentTimetable!);
                PushUndoState();
                // حفظ تلقائي في قاعدة البيانات حتى لا تضيع الحصص المستوردة عند إغلاق التطبيق
                await SaveToDatabaseSilentlyAsync();
            }

            var createdInfo =
                (result.CreatedSubjects > 0 ? $"\nعناصر جديدة: مواد {result.CreatedSubjects}، معلمون {result.CreatedTeachers}، فصول {result.CreatedClasses}، غرف {result.CreatedRooms}" : "");
            var saveNote = imported > 0 ? "\nتم حفظ الجدول تلقائيًا في قاعدة البيانات." : "";
            MessageBox.Show(
                $"تم استيراد {imported} حصة من aSc XML.{saveNote}\nتم تخطي {skippedRows} حصة (مكررة أو بيانات ناقصة).{createdInfo}",
                "استيراد aSc XML", MessageBoxButton.OK,
                imported > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            _logger.LogInformation("استيراد aSc XML: {Imported} تم، {Skipped} تم تخطيهما، إنشاء: مواد {CS} معلمون {CT} فصول {CC} غرف {CR}",
                imported, skippedRows, result.CreatedSubjects, result.CreatedTeachers, result.CreatedClasses, result.CreatedRooms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في استيراد aSc XML");
            MessageBox.Show($"فشل الاستيراد من aSc XML:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<List<string>?> GetOllamaModelsAsync(HttpClient http, string baseUrl)
    {
        try
        {
            var json = await http.GetStringAsync($"{baseUrl}/api/tags");
            using var doc = JsonDocument.Parse(json);
            var result = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var models))
            {
                foreach (var m in models.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var name))
                    {
                        var n = name.GetString();
                        if (!string.IsNullOrEmpty(n)) result.Add(n);
                    }
                }
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsVisionModelName(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("llava")
            || n.Contains("vision")
            || n.Contains("-vl")
            || n.Contains("moondream")
            || n.Contains("minicpm-v")
            || n.Contains("pixtral")
            || n.Contains("internvl")
            || n.Contains("bakllava");
    }

    private static async Task<bool> DownloadVisionModelAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("ollama", "pull llava")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            // تفريغ المخرجات تفادياً لامتلاء المخزن المؤقت
            _ = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();

            var finished = await Task.Run(() => proc.WaitForExit(20 * 60 * 1000));
            if (!finished)
            {
                try { proc.Kill(true); } catch { /* إغلاق قسري */ }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> OllamaVisionGenerateAsync(HttpClient http, string baseUrl, string model, string imageB64)
    {
        var prompt =
            "هذه صورة جدول دراسي مدرسي. استخرج كل خانة تحتوي على حصة دراسية. " +
            "أخرج JSON فقط بدون أي شرح، بالشكل التالي: " +
            "{\"lessons\":[{\"day\":\"السبت\",\"period\":1,\"subject\":\"اسم المادة\",\"teacher\":\"اسم المعلم\",\"class\":\"اسم الفصل\",\"room\":\"اسم الغرفة\"}]} " +
            "قواعد: day بالعربية أحد: السبت، الأحد، الاثنين، الثلاثاء، الأربعاء، الخميس، الجمعة. " +
            "period رقم صحيح للفترة. room اتركه فارغاً إذا لم يظهر. " +
            "لا تكرر خانة فارغة، ولا تضف تعليقات.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            images = new[] { imageB64 },
            options = new { temperature = 0 }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync($"{baseUrl}/api/generate", content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 300 ? body.Substring(0, 300) : body;
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {snippet}");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("response", out var response))
            return response.GetString() ?? "";

        throw new InvalidOperationException("لا يوجد رد من النموذج");
    }

    private static List<string[]> ParseVisionRows(string responseText)
    {
        var text = (responseText ?? "").Trim();

        // إزالة احتمال غرف ```json ... ```
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text[(firstNewline + 1)..];
            if (text.EndsWith("```")) text = text[..^3];
            text = text.Trim();
        }

        // استخراج أول كتلة JSON {}
        if (!text.StartsWith("{") && !text.StartsWith("["))
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start) text = text[start..(end + 1)];
        }

        using var doc = JsonDocument.Parse(text);

        JsonElement array;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            array = doc.RootElement;
        }
        else if (doc.RootElement.TryGetProperty("lessons", out var lessons) && lessons.ValueKind == JsonValueKind.Array)
        {
            array = lessons;
        }
        else
        {
            throw new InvalidDataException("رد النموذج ليس قائمة حصص (JSON غير متوقع)");
        }

        static string GetProp(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return "";
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.ToString(),
                _ => ""
            };
        }

        var rows = new List<string[]>
        {
            new[] { "اليوم", "الفترة", "المادة", "المعلم", "الفصل", "الغرفة" }
        };

        foreach (var item in array.EnumerateArray())
        {
            var day = GetProp(item, "day").Trim();
            var period = GetProp(item, "period").Trim();
            if (string.IsNullOrEmpty(day) || string.IsNullOrEmpty(period)) continue;
            rows.Add(new[]
            {
                day,
                period,
                GetProp(item, "subject"),
                GetProp(item, "teacher"),
                GetProp(item, "class"),
                GetProp(item, "room")
            });
        }

        return rows;
    }

    private static bool TryParseDay(string value, out DomainDayOfWeek day)
    {
        day = default;
        switch (value)
        {
            case "السبت": case "Saturday": case "Sat": day = DomainDayOfWeek.Saturday; return true;
            case "الأحد": case "الاحد": case "Sunday": case "Sun": day = DomainDayOfWeek.Sunday; return true;
            case "الاثنين": case "الإثنين": case "Monday": case "Mon": day = DomainDayOfWeek.Monday; return true;
            case "الثلاثاء": case "Tuesday": case "Tue": day = DomainDayOfWeek.Tuesday; return true;
            case "الأربعاء": case "الاربعاء": case "Wednesday": case "Wed": day = DomainDayOfWeek.Wednesday; return true;
            case "الخميس": case "Thursday": case "Thu": day = DomainDayOfWeek.Thursday; return true;
            case "الجمعة": case "Friday": case "Fri": day = DomainDayOfWeek.Friday; return true;
            default: return false;
        }
    }

    // ===== قاعدة البيانات والمشاركة =====

    /// <summary>حفظ الجدول الحالي في قاعدة البيانات المحلية</summary>
    [RelayCommand]
    private async Task SaveToDatabaseAsync()
    {
        if (!EnsureTimetable()) return;

        try
        {
            var storage = _services.GetRequiredService<TimetableStorageService>();
            await storage.SaveAsync(_currentTimetable!);

            MessageBox.Show(
                $"تم حفظ «{_currentTimetable!.Name}» في قاعدة البيانات\n({_currentTimetable.Lessons.Count} حصة)",
                "حفظ في قاعدة البيانات", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في حفظ الجدول في قاعدة البيانات");
            MessageBox.Show($"فشل الحفظ:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>حفظ صامت (بعد التوليد/التحسين) — يُسجَّل في السجل فقط</summary>
    private async Task SaveToDatabaseSilentlyAsync()
    {
        try
        {
            var storage = _services.GetRequiredService<TimetableStorageService>();
            await storage.SaveAsync(_currentTimetable!);
            _logger.LogInformation("حفظ تلقائي في قاعدة البيانات: {Name}", _currentTimetable!.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "فشل الحفظ التلقائي في قاعدة البيانات");
        }
    }

    /// <summary>فتح قائمة الجداول المحفوظة واختيار واحد لفتحه أو حذفه</summary>
    [RelayCommand]
    private async Task OpenFromDatabaseAsync()
    {
        try
        {
            var storage = _services.GetRequiredService<TimetableStorageService>();
            var items = await storage.ListAsync();

            if (items.Count == 0)
            {
                MessageBox.Show(
                    "لا توجد جداول محفوظة في قاعدة البيانات بعد.\nاحفظ الجدول الحالي من «ملف ← حفظ في قاعدة البيانات».",
                    "فتح من قاعدة البيانات", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new DbTimetablesDialog(items)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true) return;

            if (dialog.DeleteTargetId is Guid deleteId)
            {
                var deleted = await storage.DeleteAsync(deleteId);

                // إن كان المحذوف هو الجدول المفتوح: أفرغ العرض والذاكرة ومساحة العمل
                if (deleted && _currentTimetable != null && _currentTimetable.Id == deleteId)
                {
                    _currentTimetable = null;
                    CurrentTimetableName = string.Empty;
                    TimetableStatusText = string.Empty;
                    CurrentSchoolName = "غير محدد";
                    ClearTimetableDisplay();
                    ResetHistory();
                    _services.GetRequiredService<CurrentSchoolService>().ClearCurrent();
                }

                MessageBox.Show(
                    deleted ? "تم حذف الجدول من قاعدة البيانات." : "لم يُعثر على الجدول (ربما حُذف مسبقاً).",
                    "حذف", MessageBoxButton.OK,
                    deleted ? MessageBoxImage.Information : MessageBoxImage.Warning);
                return;
            }

            if (dialog.SelectedTimetableId is Guid openId)
            {
                var timetable = await storage.LoadAsync(openId);
                if (timetable == null)
                {
                    MessageBox.Show("تعذر فتح الجدول (ربما حُذف).", "خطأ",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _currentTimetable = timetable;
                CurrentTimetableName = timetable.Name;
                TimetableStatusText = timetable.Status.ToString();

                _services.GetRequiredService<CurrentSchoolService>().SetCurrent(timetable.SchoolId);
                await ReloadReferenceListsAsync();

                UpdateTimetableGrid(timetable);
                ResetHistory();

                _logger.LogInformation("تم فتح جدول من قاعدة البيانات: {Name}", timetable.Name);
                MessageBox.Show(
                    $"تم فتح «{timetable.Name}»\n({timetable.Lessons.Count} حصة، {timetable.PeriodTimes.Count} فترة توقيت)",
                    "فتح من قاعدة البيانات", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في فتح جدول من قاعدة البيانات");
            MessageBox.Show($"تعذر فتح الجداول:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>إدارة المدارس/مساحات العمل: عرض، إعادة تسمية، نسخ، حذف، وفتح أحدث جدول.</summary>
    [RelayCommand]
    private async Task ManageWorkspacesAsync()
    {
        try
        {
            var service = _services.GetRequiredService<WorkspaceService>();
            var dialog = new Views.WorkspacesDialog(service) { Owner = Application.Current?.MainWindow };

            if (dialog.ShowDialog() != true)
            {
                if (_currentTimetable != null) CurrentSchoolName = ResolveSchoolName();
                return;
            }

            if (dialog.OpenWorkspaceId is not Guid schoolId)
            {
                if (_currentTimetable != null) CurrentSchoolName = ResolveSchoolName();
                return;
            }

            var storage = _services.GetRequiredService<TimetableStorageService>();
            var items = (await storage.ListAsync())
                .Where(i => i.SchoolId == schoolId)
                .OrderByDescending(i => i.UpdatedAt ?? DateTime.MinValue)
                .ToList();

            if (items.Count == 0)
            {
                // مساحة بلا جداول محفوظة: ابدّل السياق لعرض بياناتها المرجعية
                _services.GetRequiredService<CurrentSchoolService>().SetCurrent((SchoolId)schoolId);
                _currentTimetable = null;
                CurrentTimetableName = string.Empty;
                TimetableStatusText = string.Empty;
                CurrentSchoolName = "غير محدد";
                ClearTimetableDisplay();
                RebuildAssignments();
                RebuildRuleGroups();
                ResetHistory();
                await ReloadReferenceListsAsync();
                CurrentSchoolName = ResolveSchoolName();

                MessageBox.Show(
                    "هذه المساحة لا تحتوي جداول محفوظة بعد. حُمّلت بياناتها المرجعية؛ أنشئ جدولاً جديداً لبدء العمل.",
                    "مساحة عمل", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var timetable = await storage.LoadAsync(items[0].Id);
            if (timetable is null)
            {
                MessageBox.Show("تعذر فتح أحدث جدول في هذه المساحة.", "مساحة عمل",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentTimetable = timetable;
            CurrentTimetableName = timetable.Name;
            TimetableStatusText = timetable.Status.ToString();
            await SwitchWorkspaceAsync(timetable);
            UpdateTimetableGrid(timetable);
            ResetHistory();

            _logger.LogInformation("فُتحت مساحة العمل وحديث جدولها: {Name}", timetable.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في إدارة مساحات العمل");
            MessageBox.Show($"تعذر إدارة مساحات العمل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>مشاركة الجدول: خادم محلي + رمز QR + رابط الشبكة</summary>
    [RelayCommand]
    private void ShareTimetable()
    {
        if (!EnsureTimetable()) return;

        try
        {
            var server = _services.GetRequiredService<ShareServerService>();
            var window = new ShareWindow(server, BuildTimetableHtml)
            {
                Owner = Application.Current.MainWindow
            };
            _logger.LogInformation("فتح نافذة مشاركة الجدول");
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في فتح نافذة المشاركة");
            MessageBox.Show($"تعذر فتح المشاركة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTimetableStats()
    {
        if (_currentTimetable != null)
        {
            TotalLessonsCount = _currentTimetable.Lessons.Count;
        }
    }

    // تهيئة القوائم
    private IReadOnlyList<object> CreateImportMenuItems()
    {
        return new object[]
        {
            new System.Windows.Controls.MenuItem { Header = "JSON", Command = new RelayCommand(ImportJson) },
            new System.Windows.Controls.MenuItem { Header = "CSV", Command = new RelayCommand(ImportCsv) },
            new Separator(),
            new System.Windows.Controls.MenuItem { Header = "Excel (xlsx)", Command = ImportExcelCommand },
            new System.Windows.Controls.MenuItem { Header = "دروس CSV/Excel (Untis/WinLD/Vulcan/SchILD…)", Command = ImportVendorLessonsCommand },
            new System.Windows.Controls.MenuItem { Header = "Untis XML", Command = ImportVendorXmlCommand, CommandParameter = "untis" },
            new System.Windows.Controls.MenuItem { Header = "WinLD XML", Command = ImportVendorXmlCommand, CommandParameter = "winld" },
            new System.Windows.Controls.MenuItem { Header = "Vulcan XML", Command = ImportVendorXmlCommand, CommandParameter = "vulcan" },
            new System.Windows.Controls.MenuItem { Header = "SchILD XML", Command = ImportVendorXmlCommand, CommandParameter = "schild" },
            new System.Windows.Controls.MenuItem { Header = "صورة (OCR)", Command = ImportFromImageCommand },
            new System.Windows.Controls.MenuItem { Header = "aSc XML", Command = ImportAscCommand }
        };
    }

    private IReadOnlyList<object> CreateExportMenuItems()
    {
        return new object[]
        {
            new System.Windows.Controls.MenuItem { Header = "Madrasati CSV", Command = new RelayCommand(() => ExportCsv("madrasati")) },
            new System.Windows.Controls.MenuItem { Header = "Madrasati Excel", Command = new RelayCommand(() => ExportExcel("madrasati")) },
            new System.Windows.Controls.MenuItem { Header = "Noor XML", Command = new RelayCommand(() => ExportXml("noor")) },
            new System.Windows.Controls.MenuItem { Header = "Noor CSV", Command = new RelayCommand(() => ExportCsv("noor")) },
            new Separator(),
            new System.Windows.Controls.MenuItem { Header = "Excel Pro", Command = new RelayCommand(() => ExportExcel("pro")) },
            new System.Windows.Controls.MenuItem { Header = "CSV", Command = new RelayCommand(() => ExportCsv("plain")) },
            new System.Windows.Controls.MenuItem { Header = "PDF", Command = ExportPdfCommand },
            new System.Windows.Controls.MenuItem { Header = "XML", Command = new RelayCommand(() => ExportXml("plain")) },
            new System.Windows.Controls.MenuItem { Header = "JSON", Command = new RelayCommand(() => ExportJson()) },
            new System.Windows.Controls.MenuItem { Header = "iCal", Command = new RelayCommand(() => ExportICal()) },
            new System.Windows.Controls.MenuItem { Header = "HTML", Command = new RelayCommand(() => ExportHtml()) }
        };
    }

    private ContextMenu CreateLessonContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "تعديل (عرض الخصائص)", Command = new RelayCommand(EditSelectedLesson) });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "نسخ", Command = CopyCommand });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "قص", Command = CutCommand });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "لصق", Command = PasteCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "تثبيت/إلغاء تثبيت", Command = new RelayCommand(ToggleSelectedLessonFixed) });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "تشخيص: لماذا لم تُوضع؟", Command = new RelayCommand(ShowSelectedLessonDiagnosis) });
        menu.Items.Add(new Separator());
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "حذف", Command = new RelayCommand(DeleteSelectedLesson), Foreground = System.Windows.Media.Brushes.Red });

        return menu;
    }

    private async Task UpdateDateTimeLoop()
    {
        while (true)
        {
            await Task.Delay(1000);
            OnPropertyChanged(nameof(CurrentDateTime));
        }
    }

    private CancellationTokenSource? _searchDebounce;

    /// <summary>
    /// البحث يعمل بعد توقّف الكتابة 350 مللي ثانية - بدون ذلك يقفز التبويب مع كل حرف يُكتب
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        _searchDebounce = cts;
        _ = DebouncedSearchAsync(value, cts.Token);
    }

    private async Task DebouncedSearchAsync(string value, CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || string.IsNullOrWhiteSpace(value)) return;
        var query = value.Trim();

        var teacher = Teachers.Teachers.FirstOrDefault(t =>
            t.FullName.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (teacher != null)
        {
            SelectedSidebarTab = 2;
            Teachers.SelectedTeacher = teacher;
            return;
        }

        var subject = Subjects.Subjects.FirstOrDefault(s =>
            s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (subject != null)
        {
            SelectedSidebarTab = 3;
            Subjects.SelectedSubject = subject;
            return;
        }

        var room = Rooms.Rooms.FirstOrDefault(r =>
            r.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (room != null)
        {
            SelectedSidebarTab = 4;
            Rooms.SelectedRoom = room;
            return;
        }

        var classRoom = Classes.Classes.FirstOrDefault(c =>
            c.FullName.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (classRoom != null)
        {
            SelectedSidebarTab = 5;
            Classes.SelectedClass = classRoom;
        }
    }
}
