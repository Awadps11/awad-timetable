using System;
using System.Windows;
using AwadTimetable.UI.ViewModels;

namespace AwadTimetable.UI.Views;

/// <summary>معالج غير حاجب لتنسيق إعداد البيانات والتوليد وعرض الجدول.</summary>
public partial class SetupWizardWindow : Window
{
    private sealed record WizardStep(
        string Title,
        string Description,
        string Summary,
        int? SidebarTab,
        bool OpensTiming);

    private static readonly WizardStep[] Steps =
    {
        new("إعداد التوقيت وأيام الدراسة",
            "راجع أيام الدراسة وعدد الحصص وأزمنتها لكل مرحلة. يمكن تعديل توقيت الفترات من أدوات التطبيق.",
            "حدّد أيام العمل والحصص اليومية، ثم راجع بداية ونهاية الفترات والفسح.", null, true),
        new("تعريف المعلمين",
            "أضف المعلمين وبياناتهم من شاشة المعلمين. يمكنك الرجوع إلى هذه الخطوة في أي وقت.",
            "تأكد من تسجيل جميع المعلمين الذين سيظهرون في الإسنادات.", 2, false),
        new("تعريف المواد",
            "أضف المواد الدراسية وأسماءها المختصرة عند الحاجة.",
            "كل مادة مستخدمة في إسناد يجب أن تكون معرفة هنا.", 3, false),
        new("تعريف الفصول والشعب",
            "أنشئ الفصول والشعب وحدد المرحلة والصف والشعبة كما يلزم.",
            "ستستخدم الفصول لاحقاً لاختيار وجهة كل إسناد.", 5, false),
        new("تعريف القاعات (اختياري)",
            "أضف القاعات إذا كنت تحتاج إسناد الأماكن أو قيود السعة. يمكن تخطي هذه الخطوة.",
            "القاعات اختيارية، ويمكن استكمالها لاحقاً.", 4, false),
        new("الإسنادات والحصص الأسبوعية",
            "أضف لكل إسناد معلماً ومادة وفصلاً وعدد الحصص الأسبوعية. اختر الفصول من تبويب الإسناد.",
            "بعد اكتمال الإسنادات، أنشئ منها حصصاً غير مجدولة تمهيداً للتوليد.", 8, false),
        new("القيود والتفريغ والندب",
            "أضف القواعد اللازمة من تبويب القيود، واضبط الأوقات المغلقة للمعلمين. القواعد القوية تؤثر في إمكانية الحل.",
            "أضف القيود التي تحتاجها فقط؛ ستُطبق القواعد المفعلة أثناء التوليد.", 6, false),
        new("توليد الجدول ومراجعته",
            "شغّل التوليد بعد مراجعة الإعدادات. يمكنك بعدها مراجعة الجدول والتضاربات ثم حفظه أو طباعته.",
            "سيُنشأ تلقائياً ما ينقص من الحصص إذا كانت الإسنادات موجودة والجدول بلا حصص.", 1, false),
    };

    private readonly Action<int> _navigate;
    private readonly Action _editAvailability;
    private readonly Action _generateLessons;
    private readonly Action _generateTimetable;

    public event Action<int>? StepChanged;

    public SetupWizardWindow(
        int initialStep,
        Action<int> navigate,
        Action editAvailability,
        Action generateLessons,
        Action generateTimetable)
    {
        InitializeComponent();
        _navigate = navigate;
        _editAvailability = editAvailability;
        _generateLessons = generateLessons;
        _generateTimetable = generateTimetable;

        SetStep(Math.Clamp(initialStep, 0, Steps.Length - 1), notify: false);
    }

    private int _stepIndex;

    private void SetStep(int index, bool notify = true)
    {
        _stepIndex = Math.Clamp(index, 0, Steps.Length - 1);
        var step = Steps[_stepIndex];

        StepProgress.Value = _stepIndex;
        StepNumberText.Text = $"الخطوة {_stepIndex + 1} من {Steps.Length}";
        StepTitleText.Text = step.Title;
        StepDescriptionText.Text = step.Description;
        StepSummaryText.Text = step.Summary;
        ActionStatusText.Text = string.Empty;

        BackButton.IsEnabled = _stepIndex > 0;
        NextButton.Content = _stepIndex == Steps.Length - 1 ? "توليد الجدول" : "التالي";
        OpenStepButton.Visibility = step.SidebarTab.HasValue || step.OpensTiming
            ? Visibility.Visible
            : Visibility.Collapsed;
        AvailabilityButton.Visibility = _stepIndex == 6 ? Visibility.Visible : Visibility.Collapsed;
        AssignmentsLessonsButton.Visibility = _stepIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

        if (notify) StepChanged?.Invoke(_stepIndex);
    }

    private void OnOpenStepClick(object sender, RoutedEventArgs e)
    {
        var step = Steps[_stepIndex];
        _navigate(step.SidebarTab == 1 ? 7 : _stepIndex);
        if (_stepIndex == 0) ActionStatusText.Text = "افتح نافذتي أيام العمل والتوقيت لمراجعة التفاصيل.";
        else ActionStatusText.Text = "تم فتح القسم المناسب في النافذة الرئيسية؛ يمكنك العودة إلى المعالج متى شئت.";
    }

    private void OnAvailabilityClick(object sender, RoutedEventArgs e)
    {
        _editAvailability();
        ActionStatusText.Text = "أُغلقت نافذة التفريغ؟ عُد إلى هذه الخطوة ثم تابع.";
    }

    private void OnAssignmentsLessonsClick(object sender, RoutedEventArgs e)
    {
        _generateLessons();
        ActionStatusText.Text = "تم تشغيل إنشاء الحصص من الإسنادات. يمكنك الانتقال إلى خطوة التوليد.";
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => SetStep(_stepIndex - 1);

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_stepIndex < Steps.Length - 1)
        {
            SetStep(_stepIndex + 1);
            _navigate(_stepIndex);
            return;
        }

        var answer = MessageBox.Show(
            "هل تريد بدء توليد الجدول الآن؟ سيستخدم التطبيق الإسنادات والقيود والإعدادات الحالية.",
            "تأكيد التوليد", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        ActionStatusText.Text = "بدأ التوليد في النافذة الرئيسية…";
        _generateTimetable();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}