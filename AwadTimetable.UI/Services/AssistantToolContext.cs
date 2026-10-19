using System;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AwadTimetable.UI.Services;

/// <summary>
/// سياق أدوات المساعد الذكي: يحمل الجدول المفتوح حاليًا ويوفّر الوصول إلى
/// محرّك الجدولة وكاشف التضاربات مع إمكانية التأكيد من المستخدم قبل التنفيذ.
/// يُحدَّث الجدول الحالي من MainWindowViewModel في كل مرة تُبنى الشبكة.
/// </summary>
public sealed class AssistantToolContext
{
    private readonly IServiceProvider _services;

    public AssistantToolContext(IServiceProvider services)
        => _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>الجدول المفتوح حاليًا (null إذا لم يُفتح جدول بعد).</summary>
    public Timetable? CurrentTimetable { get; private set; }

    /// <summary>يُطلَق عند تعديل الجدول الحالي بواسطة أداة، لتعيد الواجهة بناء الشبكة.</summary>
    public event Action? Changed;

    /// <summary>يُطلَق عند تغيير بيانات مرجعية في قاعدة البيانات (معلم/مادة/فصل/قاعة) بواسطة أداة، لإعادة تحميل القوائم.</summary>
    public event Action? DataChanged;

    public void SetCurrent(Timetable? timetable) => CurrentTimetable = timetable;

    public void RaiseChanged() => Changed?.Invoke();

    public void RaiseDataChanged() => DataChanged?.Invoke();

    /// <summary>خدمة المدرسة الحالية (للحصول على معرّف المدرسة عند إنشاء الكيانات).</summary>
    public CurrentSchoolService School => _services.GetRequiredService<CurrentSchoolService>();

    public IConflictDetector Detector => _services.GetRequiredService<IConflictDetector>();

    public ITimetableGenerator Generator => _services.GetRequiredService<ITimetableGenerator>();

    /// <summary>مولّد تقارير HTML (للطباعة والقوالب)</summary>
    public AwadTimetable.Printing.Services.IWebReportService WebReport =>
        _services.GetRequiredService<AwadTimetable.Printing.Services.IWebReportService>();

    public IServiceProvider Services => _services;

    public IServiceScopeFactory ScopeFactory => _services.GetRequiredService<IServiceScopeFactory>();

    /// <summary>يعرض نافذة تأكيد (نعم/لا) على خيط الواجهة، ويعيد true إذا وافق المستخدم.</summary>
    public bool Confirm(string title, string message)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher == null) return false;

        return app.Dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(
                message, title,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes);
    }
}
