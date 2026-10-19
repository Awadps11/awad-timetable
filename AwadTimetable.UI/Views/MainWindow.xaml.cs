using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.UI.ViewModels;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.UI.Views;

/// <summary>
/// النافذة الرئيسية للتطبيق
/// </summary>
public partial class MainWindow : Window
{
    // ===== حالة السحب والإفلات لحصص الجدول (بأسلوب aSc) =====
    private Point _dragStart;
    private Lesson? _draggedLesson;
    private AscCellRef? _pressedAscCell;

    /// <summary>خلايا مصفوفة aSc المبنية حاليًا، لتلوينها أثناء السحب ثم استعادة ألوانها.</summary>
    private readonly List<Border> _ascCellBorders = new();
    private readonly Dictionary<Border, Brush> _ascCellOriginalBrush = new();
    private ScheduledLessonViewModel? _draggedScheduled;
    private Border? _ghost;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    public MainWindow() : this(App.Services.GetRequiredService<MainWindowViewModel>())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // إعداد النافذة
        Loaded += OnLoaded;
        Closing += OnClosing;

        // اختصارات لوحة المفاتيح العالمية
        KeyDown += OnKeyDown;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.AscGridRevision))
        {
            Dispatcher.BeginInvoke(new Action(RebuildAscMatrix));
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // تهيئة إضافية عند تحميل النافذة
    }

    private void OnTimetableSelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (TimetableGrid.SelectedCells.Count == 0) return;
        if (DataContext is not MainWindowViewModel viewModel) return;

        var cell = TimetableGrid.SelectedCells[0];
        if (cell.Item is not TimetableGridRow row) return;

        viewModel.SelectCell(cell.Column.DisplayIndex, row);
    }

    // ===== السحب والإفلات: نقل/تبديل الحصص كما في aSc =====

    /// <summary>يرفع كائن الخلية (DataGridCell) من عنصر بصري داخل الشبكة.</summary>
    private static DataGridCell? FindCell(DependencyObject? source)
    {
        while (source != null && source is not DataGridCell)
        {
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return source as DataGridCell;
    }

    private void TimetableGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _draggedLesson = null;

        if (DataContext is not MainWindowViewModel viewModel) return;

        var cell = FindCell(e.OriginalSource as DependencyObject);
        if (cell?.DataContext is not TimetableGridRow row) return;

        _draggedLesson = viewModel.GetLessonAtCell(cell.Column.DisplayIndex, row);
    }

    private void TimetableGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedLesson == null) return;

        var current = e.GetPosition(null);
        if (System.Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var lesson = _draggedLesson;
        _draggedLesson = null;
        DragDrop.DoDragDrop(TimetableGrid, lesson, DragDropEffects.Move);
    }

    private void TimetableGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Lesson)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void TimetableGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Lesson)) is not Lesson lesson) return;
        if (DataContext is not MainWindowViewModel viewModel) return;

        var cell = FindCell(e.OriginalSource as DependencyObject);
        if (cell?.DataContext is not TimetableGridRow row) return;

        // أعمدة الأيام تبدأ من الفهرس 2
        var dayIndex = cell.Column.DisplayIndex - 2;
        var days = System.Enum.GetValues<DomainDayOfWeek>();
        if (dayIndex < 0 || dayIndex >= days.Length) return;

        var targetDay = days[dayIndex];
        var targetPeriod = row.Period;

        if (viewModel.MoveLessonByDrag(lesson, targetDay, targetPeriod, out var error))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (!string.IsNullOrEmpty(error))
        {
            MessageBox.Show(error, "نقل الحصة", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ===== عرض aSc: بناء شبكة Grid برمجياً (الصفوف = الفصول، الأعمدة = الأيام × الحصص) =====

    private void RebuildAscMatrix()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        BuildAscMatrixCore(vm);
    }

    private readonly HashSet<int> _dayStartColumns = new();

    private void AscGridScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            BuildAscMatrixCore(viewModel);
    }

    private void BuildAscMatrixCore(MainWindowViewModel vm)
    {
        var grid = vm.AscGrid;
        AscMatrixGrid.Children.Clear();
        AscMatrixGrid.ColumnDefinitions.Clear();
        AscMatrixGrid.RowDefinitions.Clear();
        _ascCellBorders.Clear();
        _ascCellOriginalBrush.Clear();
        _dayStartColumns.Clear();

        // أعمدة بداية كل يوم: تُرسم بخط عمودي واضح يفصل الأيام بصرياً
        var boundary = 1;
        foreach (var g in grid.DayGroups)
        {
            _dayStartColumns.Add(boundary);
            boundary += g.Periods.Count;
        }

        var totalPeriodColumns = grid.DayGroups.Sum(g => g.Periods.Count);
        var showAllDays = grid.Rows.Count > 0 && totalPeriodColumns > 0 &&
                          !double.IsNaN(AscGridScrollViewer.ActualWidth) && AscGridScrollViewer.ActualWidth > 0;
        var rowHeaderWidth = showAllDays
            ? Math.Clamp(AscGridScrollViewer.ActualWidth * 0.11, 84, 132)
            : 150;
        var periodColumnWidth = showAllDays
            ? Math.Max(18, (AscGridScrollViewer.ActualWidth - rowHeaderWidth - 4) / totalPeriodColumns)
            : 64;

        // وزّع الأيام والفترات على عرض النافذة بدل فرض عرض كبير لكل حصة.
        AscMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(rowHeaderWidth),
            MinWidth = rowHeaderWidth,
            MaxWidth = rowHeaderWidth
        });
        foreach (var g in grid.DayGroups)
            foreach (var _ in g.Periods)
                AscMatrixGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(periodColumnWidth),
                    MinWidth = periodColumnWidth,
                    MaxWidth = periodColumnWidth
                });

        var template = TryFindResource("LessonItemTemplate") as DataTemplate;

        // الصف 0: أسماء الأيام (كل يوم يمتدّ فوق أعمدته)
        AscMatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddHeader(grid.RowHeaderLabel, 0, 0, 1, 1, true);
        int col = 1;
        foreach (var g in grid.DayGroups)
        {
            AddHeader(g.DayName, col, 0, g.Periods.Count, 1, true);
            col += g.Periods.Count;
        }

        // الصف 1: أسماء الحصص
        AscMatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddHeader("", 0, 1, 1, 1, false);
        col = 1;
        foreach (var g in grid.DayGroups)
            foreach (var p in g.Periods)
            {
                AddHeader(p.Label, col, 1, 1, 1, false, p.TooltipText);
                col++;
            }

        // صفوف الفصول
        int row = 2;
        foreach (var r in grid.Rows)
        {
            AscMatrixGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
                MinHeight = 30
            });
            AddRowHeader(r.Name, row);

            col = 1;
            foreach (var g in grid.DayGroups)
                foreach (var p in g.Periods)
                {
                    r.Cells.TryGetValue(((int)g.Day, p.Period), out var lessons);
                    var cellRef = new AscCellRef
                    {
                        Day = g.Day,
                        Period = p.Period,
                        RowKey = r.Key,
                        IsMatrix = vm.IsMatrixMode,
                        Lessons = lessons ?? new List<ScheduledLessonViewModel>()
                    };
                    AddLessonCell(cellRef, col, row, template);
                    col++;
                }
            row++;
        }
    }

    private void AddHeader(string text, int col, int row, int colSpan, int rowSpan, bool group, string? toolTip = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = group ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = group ? 12 : 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            FontFamily = (FontFamily)FindResource("ArabicFont")
        };
        var isDayStart = _dayStartColumns.Contains(col);
        var border = new Border
        {
            Background = new SolidColorBrush(group ? Color.FromRgb(0xE4, 0xE4, 0xE4) : Color.FromRgb(0xEF, 0xEF, 0xEF)),
            BorderBrush = new SolidColorBrush(isDayStart ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = isDayStart ? new Thickness(2, 0, 1, 1) : new Thickness(0, 0, 1, 1),
            Padding = new Thickness(3, 5, 3, 5),
            ToolTip = string.IsNullOrWhiteSpace(toolTip) ? text : toolTip,
            Child = tb
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        if (colSpan > 1) Grid.SetColumnSpan(border, colSpan);
        if (rowSpan > 1) Grid.SetRowSpan(border, rowSpan);
        AscMatrixGrid.Children.Add(border);
    }

    private void AddRowHeader(string text, int row)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = text,
            Margin = new Thickness(6, 0, 6, 0),
            FontFamily = (FontFamily)FindResource("ArabicFont")
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xEF, 0xEF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(2),
            Child = tb
        };
        Grid.SetColumn(border, 0);
        Grid.SetRow(border, row);
        AscMatrixGrid.Children.Add(border);
    }

    private void AddLessonCell(AscCellRef cell, int col, int row, DataTemplate? template)
    {
        var items = new ItemsControl
        {
            ItemsSource = cell.Lessons,
            ItemTemplate = template,
            Margin = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        var isDayStart = _dayStartColumns.Contains(col);
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(isDayStart ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = isDayStart ? new Thickness(2, 0, 1, 1) : new Thickness(0, 0, 1, 1),
            Padding = new Thickness(1),
            Tag = cell,
            Child = items,
            Cursor = cell.Lessons.Count > 0 ? Cursors.Hand : Cursors.Arrow
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        AscMatrixGrid.Children.Add(border);
        _ascCellBorders.Add(border);
    }

    /// <summary>يلوّن خلايا الشبكة حسب حالة موضع الحصة المسحوبة (ممنوع/مقبول/جيد) — يعرض الحالة قبل الإفلات.</summary>
    private void PaintDropPositions(Lesson lesson)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        foreach (var border in _ascCellBorders)
        {
            if (border.Tag is not AscCellRef cell) continue;

            if (!_ascCellOriginalBrush.ContainsKey(border))
                _ascCellOriginalBrush[border] = border.Background ?? Brushes.White;

            // في وضع المصفوفة الصف هو الفصل؛ وفي أوضاع أخرى الفصل هو الحصة الحالية
            var classId = cell.IsMatrix ? cell.RowKey : (Guid)lesson.ClassId;
            var (state, _) = vm.EvaluateDropPosition(lesson, cell.Day, new PeriodNumber((byte)cell.Period), classId);

            border.Background = state switch
            {
                MainWindowViewModel.DropPositionState.Good => new SolidColorBrush(Color.FromRgb(0xC8, 0xF0, 0xC8)),
                MainWindowViewModel.DropPositionState.Acceptable => new SolidColorBrush(Color.FromRgb(0xC8, 0xDC, 0xF5)),
                _ => new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8))
            };
        }
    }

    /// <summary>يستعيد الألوان الأصلية لخلايا الشبكة بعد انتهاء السحب.</summary>
    private void RestoreDropPositions()
    {
        foreach (var (border, brush) in _ascCellOriginalBrush)
            border.Background = brush;
        _ascCellOriginalBrush.Clear();
    }

    private static AscCellRef? FindAscCell(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Border { Tag: AscCellRef cell }) return cell;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    // ===== السحب/الإفلات والتحديد لعرض aSc =====

    private void AscGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _draggedLesson = null;
        _pressedAscCell = FindAscCell(e.OriginalSource as DependencyObject);

        if (DataContext is not MainWindowViewModel vm) return;

        if (_pressedAscCell is { Lessons.Count: > 0 } pressed)
        {
            var first = pressed.Lessons[0];
            _draggedLesson = first.Lesson;
            _draggedScheduled = first;
            vm.SelectedLesson = first.Lesson;
        }
        else
        {
            vm.SelectedLesson = null;
        }
    }

    private void AscGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedLesson == null) return;

        var current = e.GetPosition(null);
        if (System.Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var lesson = _draggedLesson;
        _draggedLesson = null;
        ShowGhost();
        PaintDropPositions(lesson);
        try
        {
            DragDrop.DoDragDrop(AscGridHost, lesson, DragDropEffects.Move);
        }
        finally
        {
            RestoreDropPositions();
            HideGhost();
        }
    }

    // ===== شبح السحب (يتبع المؤشر) =====

    private void ShowGhost()
    {
        if (_draggedScheduled == null) return;
        HideGhost();

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = _draggedScheduled.SubjectShortName,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = _draggedScheduled.CardForegroundBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = _draggedScheduled.TeacherShortName,
            FontSize = 9,
            Foreground = _draggedScheduled.CardForegroundBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _ghost = new Border
        {
            Background = _draggedScheduled.CardBackgroundBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3, 6, 3),
            Opacity = 0.9,
            IsHitTestVisible = false,
            Child = stack
        };

        if (GetCursorPos(out var p))
        {
            var pt = AscGridHost.PointFromScreen(new Point(p.X, p.Y));
            Canvas.SetLeft(_ghost, pt.X + 14);
            Canvas.SetTop(_ghost, pt.Y + 10);
        }
        AscGhostLayer.Children.Add(_ghost);
    }

    private void HideGhost()
    {
        if (_ghost != null)
        {
            AscGhostLayer.Children.Remove(_ghost);
            _ghost = null;
        }
    }

    private void AscGrid_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_ghost != null && GetCursorPos(out var p))
        {
            var pt = AscGridHost.PointFromScreen(new Point(p.X, p.Y));
            Canvas.SetLeft(_ghost, pt.X + 14);
            Canvas.SetTop(_ghost, pt.Y + 10);
        }
        e.Handled = true;
    }

    // ===== سحب الحصص من لوحة "تحتاج انتباهًا" =====

    private void ProblemTray_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _draggedLesson = null;
        _draggedScheduled = null;

        var item = FindDataContext<ProblemLessonItem>(e.OriginalSource as DependencyObject);
        if (item != null)
        {
            _draggedLesson = item.Lesson.Lesson;
            _draggedScheduled = item.Lesson;
        }
    }

    private void ProblemTray_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedLesson == null) return;

        var current = e.GetPosition(null);
        if (System.Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var lesson = _draggedLesson;
        _draggedLesson = null;
        ShowGhost();
        try
        {
            DragDrop.DoDragDrop(ProblemTray, lesson, DragDropEffects.Move);
        }
        finally
        {
            HideGhost();
        }
    }

    private static T? FindDataContext<T>(DependencyObject? source) where T : class
    {
        while (source != null)
        {
            if (source is FrameworkElement { DataContext: T dc }) return dc;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private void AscGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Lesson)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void AscGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Lesson)) is not Lesson lesson) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var cell = FindAscCell(e.OriginalSource as DependencyObject);
        if (cell == null) return;

        var targetPeriod = new PeriodNumber((byte)cell.Period);
        bool ok;
        string? error;

        if (cell.IsMatrix)
        {
            ok = vm.MoveLessonByDrag(lesson, cell.Day, targetPeriod, cell.RowKey, out error);
        }
        else
        {
            ok = vm.MoveLessonByDrag(lesson, cell.Day, targetPeriod, out error);
        }

        if (ok)
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (!string.IsNullOrEmpty(error))
        {
            MessageBox.Show(error, "نقل الحصة", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // يمكن إضافة تأكيد الإغلاق هنا لاحقًا
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var viewModel = DataContext as MainWindowViewModel;
        if (viewModel == null) return;

        var modifiers = System.Windows.Input.Keyboard.Modifiers;

        // اختصارات Ctrl+Shift
        if (modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.C:
                    viewModel.CheckConflictsCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.G:
                    viewModel.OptimizeTimetableCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        // اختصارات Ctrl
        if (modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.N:
                    viewModel.NewTimetableCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.O:
                    viewModel.OpenTimetableCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.S:
                    viewModel.SaveTimetableCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.G:
                    viewModel.GenerateTimetableCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.F:
                    viewModel.SearchCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.I:
                    viewModel.ShowAIChatCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.L:
                    viewModel.AddLessonCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.P:
                    viewModel.PrintCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.Z:
                    if (viewModel.CanUndo) viewModel.UndoCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.Y:
                    if (viewModel.CanRedo) viewModel.RedoCommand.Execute(null);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.OemComma:
                    viewModel.ShowSettingsCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == System.Windows.Input.Key.F1)
        {
            viewModel.ShowHelpCommand.Execute(null);
            e.Handled = true;
        }

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (viewModel.IsAIChatPanelOpen)
            {
                viewModel.ShowAIChatCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}