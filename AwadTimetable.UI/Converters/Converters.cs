using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.ValueObjects;
using CoreDuration = AwadTimetable.Core.ValueObjects.Duration;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.UI.Converters;

/// <summary>
/// محول نوع المادة إلى فرشاة لون
/// </summary>
public class SubjectColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Subject subject && !string.IsNullOrEmpty(subject.ColorHex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(subject.ColorHex));
            }
            catch
            {
                return new SolidColorBrush(Colors.Blue);
            }
        }

        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch { }
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول نوع الحصة إلى نص عربي
/// </summary>
public class LessonTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LessonType type)
        {
            return type switch
            {
                LessonType.Regular => "عادية",
                LessonType.Lab => "مختبر",
                LessonType.Activity => "نشاط",
                LessonType.Exam => "امتحان",
                LessonType.Break => "استراحة",
                _ => "غير محدد"
            };
        }
        return "غير محدد";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول يوم الأسبوع إلى نص عربي
/// </summary>
public class DayOfWeekToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DomainDayOfWeek day)
        {
            return day switch
            {
                DomainDayOfWeek.Saturday => "السبت",
                DomainDayOfWeek.Sunday => "الأحد",
                DomainDayOfWeek.Monday => "الاثنين",
                DomainDayOfWeek.Tuesday => "الثلاثاء",
                DomainDayOfWeek.Wednesday => "الأربعاء",
                DomainDayOfWeek.Thursday => "الخميس",
                DomainDayOfWeek.Friday => "الجمعة",
                _ => "غير محدد"
            };
        }
        return "غير محدد";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول حالة التضارب إلى ألوان
/// </summary>
public class ConflictSeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConflictSeverity severity)
        {
            return severity switch
            {
                ConflictSeverity.Critical => new SolidColorBrush(Colors.Red),
                ConflictSeverity.High => new SolidColorBrush(Colors.OrangeRed),
                ConflictSeverity.Medium => new SolidColorBrush(Colors.Orange),
                ConflictSeverity.Low => new SolidColorBrush(Colors.Yellow),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول قيمة منطقية إلى رؤية
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var invert = parameter?.ToString()?.ToLower() == "invert";
            return (boolValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول عدد إلى رؤية (صفر = مخفي)
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString()?.ToLower() == "invert";
        if (value is int count)
        {
            var visible = count > 0;
            return (visible ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }
        return invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول دور المستخدم إلى ألوان
/// </summary>
public class RoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return role switch
            {
                "user" => new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD)), // أزرق فاتح
                "assistant" => new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)), // أخضر فاتح
                "tool" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)), // برتقالي فاتح
                _ => new SolidColorBrush(Colors.White)
            };
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول منطقي إلى فرشاة
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string param)
        {
            var parts = param.Split(';');
            if (parts.Length == 2)
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(boolValue ? parts[0] : parts[1]));
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول منطقي معكوس (true ↔ false)
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>
/// محول منطقي إلى محاذاة أفقية
/// </summary>
public class BoolToHorizontalAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var alignRight = parameter?.ToString()?.ToLower() == "right";
            return (boolValue ^ alignRight) ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول مدة إلى نص
/// </summary>
public class DurationToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CoreDuration d)
        {
            return $"{d.Value} د";
        }
        if (value is int minutes)
        {
            return $"{minutes} د";
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول DayOfWeek إلى فهرس عمود (لـ DataGrid)
/// </summary>
public class DayOfWeekToColumnIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DomainDayOfWeek day)
        {
            return (int)day + 1;
        }
        return 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول تعداد إلى وصف عربي
/// </summary>
public class EnumToDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "";

        var fieldName = value.ToString() ?? string.Empty;
        var field = value.GetType().GetField(fieldName);
        if (field != null)
        {
            var attr = field.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault() as DescriptionAttribute;
            if (attr != null) return attr.Description;
        }
        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول قيمة فارغة إلى رؤية
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString()?.ToLower() == "invert";
        var hasValue = value != null;
        return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول نسبة مئوية إلى فرشاة لون
/// </summary>
public class PercentageToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double pct = 0;
        if (value is Percentage p) pct = p.Value;
        else if (value is double d) pct = d;
        else if (value is int i) pct = i;
        else if (value is float f) pct = f;

        if (pct >= 90) return new SolidColorBrush(Colors.Green);
        if (pct >= 70) return new SolidColorBrush(Color.FromRgb(0x8B, 0xC3, 0x4A));
        if (pct >= 50) return new SolidColorBrush(Colors.Orange);
        if (pct >= 30) return new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
        return new SolidColorBrush(Colors.Red);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// محول تعداد إلى Boolean — لربط مفاتيح الراديو بوضع العرض (ConverterParameter = اسم القيمة)
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null && parameter != null &&
           string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (enumType.IsEnum)
            {
                try { return Enum.Parse(enumType, parameter.ToString()!, ignoreCase: true); }
                catch { return Binding.DoNothing; }
            }
        }
        return Binding.DoNothing;
    }
}