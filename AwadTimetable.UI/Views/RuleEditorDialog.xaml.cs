using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AwadTimetable.Core.Entities;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>
/// محرر قاعدة موجّه: يختار النوع والنطاق والهدف، ويعرض حقول المعاملات المناسبة فقط
/// ثم يبني معاملات JSON المتوافقة مع RuleCompiler.
/// </summary>
public partial class RuleEditorDialog : Window
{
    public sealed record NamedOption(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly IReadOnlyList<NamedOption> _teachers;
    private readonly IReadOnlyList<NamedOption> _classes;
    private readonly IReadOnlyList<NamedOption> _subjects;
    private readonly IReadOnlyList<NamedOption> _rooms;
    private readonly RuleItem? _existing;

    private readonly Dictionary<string, TextBox> _texts = new();
    private readonly Dictionary<string, CheckBox> _checks = new();
    private readonly List<(int Day, CheckBox Box)> _dayChecks = new();
    private readonly Dictionary<string, JsonElement> _existingParams = new();

    private static readonly string[] DayNames = { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };

    /// <summary>القاعدة الناتجة (null عند الإلغاء).</summary>
    public RuleItem? Result { get; private set; }

    public RuleEditorDialog(
        IReadOnlyList<(Guid Id, string Name)> teachers,
        IReadOnlyList<(Guid Id, string Name)> classes,
        IReadOnlyList<(Guid Id, string Name)> subjects,
        IReadOnlyList<(Guid Id, string Name)> rooms,
        RuleItem? existing = null)
    {
        InitializeComponent();

        _teachers = teachers.Select(t => new NamedOption(t.Id, t.Name)).ToList();
        _classes = classes.Select(t => new NamedOption(t.Id, t.Name)).ToList();
        _subjects = subjects.Select(t => new NamedOption(t.Id, t.Name)).ToList();
        _rooms = rooms.Select(t => new NamedOption(t.Id, t.Name)).ToList();
        _existing = existing;

        KindCombo.ItemsSource = RuleOptions.Kinds;
        ScopeCombo.ItemsSource = RuleOptions.Scopes;
        PriorityCombo.ItemsSource = RuleOptions.Priorities;

        if (existing is not null)
        {
            HeaderText.Text = "تحرير قاعدة";
            KindCombo.SelectedValue = existing.Kind;
            ScopeCombo.SelectedValue = existing.Scope;
            PriorityCombo.SelectedValue = existing.Priority;
            TitleBox.Text = existing.Title;
            EnabledCheck.IsChecked = existing.IsEnabled;

            ParseExistingParameters(existing.Parameters);
        }
        else
        {
            KindCombo.SelectedValue = RuleKind.Availability;
            ScopeCombo.SelectedValue = RuleScope.Teacher;
            PriorityCombo.SelectedValue = RulePriority.High;
        }

        OnScopeChanged(this, null!);
        OnKindChanged(this, null!);
        PreselectTargets();
    }

    private void ParseExistingParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var p in doc.RootElement.EnumerateObject())
                _existingParams[p.Name] = p.Value.Clone();
        }
        catch
        {
            // معاملات قديمة غير صالحة: تُتجاهل
        }
    }

    private void PreselectTargets()
    {
        if (_existing?.TargetId is { } targetId)
            TargetCombo.SelectedValue = targetId;

        if (_existing is not null
            && _existingParams.TryGetValue("secondTargetId", out var second)
            && second.ValueKind == JsonValueKind.String
            && Guid.TryParse(second.GetString(), out var secondId))
        {
            SecondTargetCombo.SelectedValue = secondId;
        }
    }

    private RuleKind SelectedKind => KindCombo.SelectedValue is RuleKind k ? k : RuleKind.General;
    private RuleScope SelectedScope => ScopeCombo.SelectedValue is RuleScope s ? s : RuleScope.School;

    private IReadOnlyList<NamedOption> OptionsFor(RuleScope scope) => scope switch
    {
        RuleScope.Teacher => _teachers,
        RuleScope.Class => _classes,
        RuleScope.Subject => _subjects,
        RuleScope.Room => _rooms,
        _ => Array.Empty<NamedOption>(),
    };

    private void OnScopeChanged(object? sender, SelectionChangedEventArgs? e)
    {
        var scope = SelectedScope;
        var options = OptionsFor(scope);

        TargetCombo.ItemsSource = options;
        TargetCombo.IsEnabled = options.Count > 0;
        TargetLabel.Text = scope == RuleScope.School ? "الهدف: عام (كل المدرسة)" : "الهدف";
    }

    private void OnKindChanged(object? sender, SelectionChangedEventArgs? e)
    {
        var kind = SelectedKind;

        // علاقة مادتين تعني نطاق مادة
        if (kind is RuleKind.SubjectPair or RuleKind.SubjectTwicePerDay)
        {
            ScopeCombo.SelectedValue = RuleScope.Subject;
            ScopeCombo.IsEnabled = false;
        }
        else
        {
            ScopeCombo.IsEnabled = true;
        }

        if (kind is RuleKind.PreferredRoom)
        {
            if (SelectedScope is not (RuleScope.Subject or RuleScope.Class))
                ScopeCombo.SelectedValue = RuleScope.Subject;
            OnScopeChanged(this, null!);
        }

        var showSecond = kind is RuleKind.SubjectPair or RuleKind.PreferredRoom;
        SecondTargetPanel.Visibility = showSecond ? Visibility.Visible : Visibility.Collapsed;

        if (kind == RuleKind.SubjectPair)
        {
            SecondTargetLabel.Text = "المادة الثانية";
            SecondTargetCombo.ItemsSource = _subjects;
        }
        else if (kind == RuleKind.PreferredRoom)
        {
            SecondTargetLabel.Text = "القاعة";
            SecondTargetCombo.ItemsSource = _rooms;
        }

        RebuildParameters(kind);
    }

    private void RebuildParameters(RuleKind kind)
    {
        ParamsPanel.Children.Clear();
        _texts.Clear();
        _checks.Clear();
        _dayChecks.Clear();

        switch (kind)
        {
            case RuleKind.Availability:
            case RuleKind.TeacherLink:
                AddDays();
                AddText("periods", "الفترات", "1,3,5 (اتركه فارغاً = كل الفترات)", ExistingListText("periods"));
                AddHint("تُحجب الأوقات المختارة على الهدف: لا تُجدَّل له حصص فيها.");
                break;

            case RuleKind.EndTime:
                AddText("afterPeriod", "بعد الفترة", "مثال: 6 يعني لا حصص بعد الحصة السادسة", ExistingInt("afterPeriod"));
                break;

            case RuleKind.SubjectDistribution:
                AddText("maxPerDay", "أقصى حصص المادة يومياً", "مثال: 2", ExistingInt("maxPerDay"));
                break;

            case RuleKind.MaxConsecutive:
                AddText("maxConsecutive", "أقصى حصص متتالية", "مثال: 3", ExistingInt("maxConsecutive"));
                break;

            case RuleKind.MaxGapsPerDay:
                AddText("maxGapsPerDay", "أقصى فجوات في اليوم", "مثال: 1 (0 = بلا فجوات)", ExistingInt("maxGapsPerDay"));
                break;

            case RuleKind.DailyLoad:
                AddText("minPerDay", "أدنى حصص يومياً", "مثال: 3", ExistingInt("minPerDay"));
                AddText("maxPerDay", "أقصى حصص يومياً", "مثال: 6", ExistingInt("maxPerDay"));
                break;

            case RuleKind.MaxDaysPerWeek:
                AddText("maxDaysPerWeek", "أقصى أيام في الأسبوع", "1..7", ExistingInt("maxDaysPerWeek"));
                break;

            case RuleKind.MinDaysPerWeek:
                AddText("minDaysPerWeek", "أدنى أيام في الأسبوع", "1..7", ExistingInt("minDaysPerWeek"));
                break;

            case RuleKind.MaxGapsPerWeek:
                AddText("maxGapsPerWeek", "أقصى فجوات في الأسبوع", "مثال: 4", ExistingInt("maxGapsPerWeek"));
                break;

            case RuleKind.MaxSpanPerDay:
                AddText("maxSpanPerDay", "أقصى امتداد لليوم (فترات)", "مثال: 7", ExistingInt("maxSpanPerDay"));
                break;

            case RuleKind.BeginsEarly:
                AddText("maxBeginningsSecondHour", "أقصى أيام تبدأ من الحصة الثانية", "مثال: 1", ExistingInt("maxBeginningsSecondHour"));
                break;

            case RuleKind.SubjectPair:
                AddCheck("noSameDay", "لا تُدرَّس المادتان في نفس اليوم", ExistingBool("noSameDay", true));
                AddCheck("noConsecutive", "لا تكونان متتاليتين", ExistingBool("noConsecutive", true));
                AddHint("اختر المادة الأولى كهدف، والمادة الثانية في الحقل أعلاه.");
                break;

            case RuleKind.SubjectTwicePerDay:
                AddCheck("mustBeConsecutive", "يجب أن تكون الحصتان متتاليتين", ExistingBool("mustBeConsecutive", false));
                break;

            case RuleKind.MinDaysBetweenActivities:
                AddText("minDaysBetween", "أدنى أيام بين التكرارات", "1..7", ExistingInt("minDaysBetween"));
                break;

            case RuleKind.PreferredRoom:
                AddHint("اختر القاعة في الحقل أعلاه. تُفضَّل/تُلزم لكل حصص الهدف.");
                break;

            case RuleKind.RoomChangeLimit:
                AddText("maxRoomChangesPerDay", "أقصى تغييرات قاعة يومياً", "0..12", ExistingInt("maxRoomChangesPerDay"));
                AddText("maxRoomChangesPerWeek", "أقصى تغييرات قاعة أسبوعياً", "0..60", ExistingInt("maxRoomChangesPerWeek"));
                break;

            default:
                AddHint("هذه القاعدة لا تحتاج معاملات إضافية؛ يكفي النوع والنطاق والهدف.");
                break;
        }
    }

    private void AddText(string key, string label, string hint, string? value)
    {
        var tb = new TextBox
        {
            Text = value ?? string.Empty,
            ToolTip = hint,
            Margin = new Thickness(0, 0, 0, 6),
            FlowDirection = FlowDirection.LeftToRight,
        };
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(tb);
        ParamsPanel.Children.Add(panel);
        _texts[key] = tb;
    }

    private void AddCheck(string key, string label, bool value)
    {
        var cb = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 0, 6) };
        ParamsPanel.Children.Add(cb);
        _checks[key] = cb;
    }

    private void AddDays()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        for (var d = 0; d < DayNames.Length; d++)
        {
            var cb = new CheckBox
            {
                Content = DayNames[d],
                Margin = new Thickness(0, 0, 12, 4),
                IsChecked = ExistingDaySelected(d),
            };
            _dayChecks.Add((d, cb));
            wrap.Children.Add(cb);
        }
        ParamsPanel.Children.Add(new TextBlock { Text = "الأيام (اتركها فارغة = كل الأيام)", FontWeight = FontWeights.SemiBold });
        ParamsPanel.Children.Add(wrap);
    }

    private void AddHint(string text)
        => ParamsPanel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(0, 2, 0, 0),
        });

    private bool ExistingDaySelected(int day)
    {
        if (!_existingParams.TryGetValue("days", out var el) || el.ValueKind != JsonValueKind.Array) return false;
        return el.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Number && x.GetInt32() == day);
    }

    private string? ExistingInt(string key)
        => _existingParams.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetInt32().ToString(CultureInfo.InvariantCulture)
            : null;

    private bool ExistingBool(string key, bool fallback)
        => _existingParams.TryGetValue(key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : fallback;

    private string? ExistingListText(string key)
    {
        if (!_existingParams.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array) return null;
        return string.Join(",", el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Number)
            .Select(x => x.GetInt32().ToString(CultureInfo.InvariantCulture)));
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var kind = SelectedKind;
        var scope = SelectedScope;

        Guid? targetId = TargetCombo.SelectedValue is Guid t ? t : null;
        if (scope != RuleScope.School && targetId is null)
        {
            MessageBox.Show("اختر الهدف (معلم/فصل/مادة/قاعة).", "القاعدة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var p = new Dictionary<string, object?>();

        switch (kind)
        {
            case RuleKind.Availability:
            case RuleKind.TeacherLink:
            {
                var days = _dayChecks.Where(x => x.Box.IsChecked == true).Select(x => x.Day).ToList();
                var periods = ParseIntList(_texts.GetValueOrDefault("periods")?.Text);
                if (days.Count == 0) days = Enumerable.Range(0, 7).ToList();
                if (periods.Count > 0) p["periods"] = periods;
                p["days"] = days;
                break;
            }
            case RuleKind.EndTime: PutInt(p, "afterPeriod"); break;
            case RuleKind.SubjectDistribution: PutInt(p, "maxPerDay"); break;
            case RuleKind.MaxConsecutive: PutInt(p, "maxConsecutive"); break;
            case RuleKind.MaxGapsPerDay: PutInt(p, "maxGapsPerDay"); break;
            case RuleKind.DailyLoad:
                PutInt(p, "minPerDay");
                PutInt(p, "maxPerDay");
                break;
            case RuleKind.MaxDaysPerWeek: PutInt(p, "maxDaysPerWeek"); break;
            case RuleKind.MinDaysPerWeek: PutInt(p, "minDaysPerWeek"); break;
            case RuleKind.MaxGapsPerWeek: PutInt(p, "maxGapsPerWeek"); break;
            case RuleKind.MaxSpanPerDay: PutInt(p, "maxSpanPerDay"); break;
            case RuleKind.BeginsEarly: PutInt(p, "maxBeginningsSecondHour"); break;
            case RuleKind.MinDaysBetweenActivities: PutInt(p, "minDaysBetween"); break;
            case RuleKind.RoomChangeLimit:
                PutInt(p, "maxRoomChangesPerDay");
                PutInt(p, "maxRoomChangesPerWeek");
                break;
            case RuleKind.SubjectPair:
            {
                if (SecondTargetCombo.SelectedValue is not Guid second || targetId is null || second == targetId)
                {
                    MessageBox.Show("اختر مادتين مختلفتين.", "القاعدة", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                p["secondTargetId"] = second;
                p["noSameDay"] = _checks.GetValueOrDefault("noSameDay")?.IsChecked == true;
                p["noConsecutive"] = _checks.GetValueOrDefault("noConsecutive")?.IsChecked == true;
                break;
            }
            case RuleKind.SubjectTwicePerDay:
                p["mustBeConsecutive"] = _checks.GetValueOrDefault("mustBeConsecutive")?.IsChecked == true;
                break;
            case RuleKind.PreferredRoom:
            {
                if (SecondTargetCombo.SelectedValue is not Guid room)
                {
                    MessageBox.Show("اختر القاعة.", "القاعدة", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                p["secondTargetId"] = room;
                break;
            }
        }

        if (kind is RuleKind.EndTime or RuleKind.SubjectDistribution or RuleKind.MaxConsecutive
            or RuleKind.MaxGapsPerDay or RuleKind.DailyLoad or RuleKind.MaxDaysPerWeek
            or RuleKind.MinDaysPerWeek or RuleKind.MaxGapsPerWeek or RuleKind.MaxSpanPerDay
            or RuleKind.BeginsEarly or RuleKind.MinDaysBetweenActivities or RuleKind.RoomChangeLimit
            && p.Count == 0)
        {
            MessageBox.Show("أدخل قيمة رقمية واحدة على الأقل.", "القاعدة", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parameters = p.Count == 0 ? null : JsonSerializer.Serialize(p);
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            var targetName = (TargetCombo.SelectedItem as NamedOption)?.Name;
            title = RuleTexts.Kind(kind) + (targetName is null ? string.Empty : " — " + targetName);
        }

        Result = new RuleItem
        {
            Id = _existing?.Id ?? Guid.NewGuid(),
            Kind = kind,
            Scope = scope,
            TargetId = targetId,
            Title = title,
            Priority = PriorityCombo.SelectedValue is RulePriority pr ? pr : RulePriority.Medium,
            Parameters = parameters,
            IsEnabled = EnabledCheck.IsChecked == true,
            Source = _existing?.Source ?? "user",
            CreatedAt = _existing?.CreatedAt ?? DateTime.Now,
            UpdatedAt = DateTime.Now,
        };

        DialogResult = true;
        Close();
    }

    private void PutInt(Dictionary<string, object?> p, string key)
    {
        if (_texts.TryGetValue(key, out var tb)
            && int.TryParse(tb.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            p[key] = value;
        }
    }

    private static List<int> ParseIntList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<int>();
        return text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null)
            .Where(v => v is > 0)
            .Select(v => v!.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
    }
}
