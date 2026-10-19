using System;
using System.Text.RegularExpressions;
using AwadTimetable.Core.Common;

namespace AwadTimetable.Core.ValueObjects;

/// <summary>
/// معرف المدرسة
/// </summary>
public sealed record SchoolId(Guid Value) : ValueObject<Guid>(Value)
{
    public static SchoolId New() => new(Guid.NewGuid());
    public static SchoolId Empty => new(Guid.Empty);
    public static implicit operator Guid(SchoolId id) => id.Value;
    public static explicit operator SchoolId(Guid value) => new(value);
}

/// <summary>
/// معرف المعلم
/// </summary>
public sealed record TeacherId(Guid Value) : ValueObject<Guid>(Value)
{
    public static TeacherId New() => new(Guid.NewGuid());
    public static TeacherId Empty => new(Guid.Empty);
    public static implicit operator Guid(TeacherId id) => id.Value;
    public static explicit operator TeacherId(Guid value) => new(value);
}

/// <summary>
/// معرف الغرفة
/// </summary>
public sealed record RoomId(Guid Value) : ValueObject<Guid>(Value)
{
    public static RoomId New() => new(Guid.NewGuid());
    public static RoomId Empty => new(Guid.Empty);
    public static implicit operator Guid(RoomId id) => id.Value;
    public static explicit operator RoomId(Guid value) => new(value);
}

/// <summary>
/// معرف الفصل
/// </summary>
public sealed record ClassId(Guid Value) : ValueObject<Guid>(Value)
{
    public static ClassId New() => new(Guid.NewGuid());
    public static ClassId Empty => new(Guid.Empty);
    public static implicit operator Guid(ClassId id) => id.Value;
    public static explicit operator ClassId(Guid value) => new(value);
}

/// <summary>
/// معرف المادة
/// </summary>
public sealed record SubjectId(Guid Value) : ValueObject<Guid>(Value)
{
    public static SubjectId New() => new(Guid.NewGuid());
    public static SubjectId Empty => new(Guid.Empty);
    public static implicit operator Guid(SubjectId id) => id.Value;
    public static explicit operator SubjectId(Guid value) => new(value);
}

/// <summary>
/// معرف الحصة
/// </summary>
public sealed record LessonId(Guid Value) : ValueObject<Guid>(Value)
{
    public static LessonId New() => new(Guid.NewGuid());
    public static LessonId Empty => new(Guid.Empty);
    public static implicit operator Guid(LessonId id) => id.Value;
    public static explicit operator LessonId(Guid value) => new(value);
}

/// <summary>
/// معرف القيد
/// </summary>
public sealed record ConstraintId(Guid Value) : ValueObject<Guid>(Value)
{
    public static ConstraintId New() => new(Guid.NewGuid());
    public static ConstraintId Empty => new(Guid.Empty);
    public static implicit operator Guid(ConstraintId id) => id.Value;
    public static explicit operator ConstraintId(Guid value) => new(value);
}

/// <summary>
/// معرف الجدول
/// </summary>
public sealed record TimetableId(Guid Value) : ValueObject<Guid>(Value)
{
    public static TimetableId New() => new(Guid.NewGuid());
    public static TimetableId Empty => new(Guid.Empty);
    public static implicit operator Guid(TimetableId id) => id.Value;
    public static explicit operator TimetableId(Guid value) => new(value);
}

/// <summary>
/// رقم الفترة (0-12). القيمة 0 تعني «غير مُسندة» (حصة لم تُوضع في الجدول بعد).
/// </summary>
public sealed record PeriodNumber(byte Value) : ValueObject<byte>(Value)
{
    /// <summary>الفترة «غير مُسندة» (لم تُوضع في الجدول بعد).</summary>
    public static PeriodNumber Unscheduled => new(0);

    public static new PeriodNumber Create(byte value)
    {
        if (value > 12)
            throw new ArgumentOutOfRangeException(nameof(value), "رقم الفترة يجب أن يكون بين 0 و 12");
        return new PeriodNumber(value);
    }

    public static implicit operator byte(PeriodNumber pn) => pn.Value;
    public static explicit operator PeriodNumber(byte value) => Create(value);

    public override string ToString() => $"الفترة {Value}";
}

/// <summary>
/// المدة بالدقائق
/// </summary>
public sealed record Duration(int Value) : ValueObject<int>(Value)
{
    public static new Duration Create(int minutes)
    {
        if (minutes < 30 || minutes > 120)
            throw new ArgumentOutOfRangeException(nameof(minutes), "المدة يجب أن تكون بين 30 و 120 دقيقة");
        if (minutes % 5 != 0)
            throw new ArgumentException("المدة يجب أن تكون مضاعفاً لـ 5 دقائق", nameof(minutes));
        return new Duration(minutes);
    }

    public static Duration FromHours(double hours) => Create((int)(hours * 60));
    public static Duration FromMinutes(int minutes) => Create(minutes);

    public static implicit operator int(Duration d) => d.Value;
    public static explicit operator Duration(int minutes) => Create(minutes);

    public TimeSpan ToTimeSpan() => TimeSpan.FromMinutes(Value);

    public override string ToString() => $"{Value} دقيقة";
}

/// <summary>
/// وزن القيد (للقيود اللينة)
/// </summary>
public sealed record ConstraintWeight(double Value) : ValueObject<double>(Value)
{
    public static new ConstraintWeight Create(double value)
    {
        if (value < 0 || value > 1000)
            throw new ArgumentOutOfRangeException(nameof(value), "الوزن يجب أن يكون بين 0 و 1000");
        return new ConstraintWeight(value);
    }

    public static ConstraintWeight Zero => new(0);
    public static ConstraintWeight Low => new(10);
    public static ConstraintWeight Normal => new(50);
    public static ConstraintWeight High => new(200);
    public static ConstraintWeight Critical => new(1000);

    public static implicit operator double(ConstraintWeight cw) => cw.Value;
    public static explicit operator ConstraintWeight(double value) => Create(value);

    public override string ToString() => $"وزن: {Value}";
}

/// <summary>
/// النقاط (للترتيب/الترقيم)
/// </summary>
public sealed record Score(double Value) : ValueObject<double>(Value)
{
    public static Score Zero => new(0);
    public static Score Max => new(1000000);

    public static implicit operator double(Score s) => s.Value;
    public static explicit operator Score(double value) => new(value);

    public static Score operator +(Score a, Score b) => new(a.Value + b.Value);
    public static Score operator -(Score a, Score b) => new(a.Value - b.Value);
    public static Score operator *(Score a, double factor) => new(a.Value * factor);
}

/// <summary>
/// النسبة المئوية (0-100)
/// </summary>
public sealed record Percentage(double Value) : ValueObject<double>(Value)
{
    public static new Percentage Create(double value)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "النسبة يجب أن تكون بين 0 و 100");
        return new Percentage(value);
    }

    public static Percentage Zero => new(0);
    public static Percentage Hundred => new(100);

    public static implicit operator double(Percentage p) => p.Value;
    public static explicit operator Percentage(double value) => Create(value);

    public override string ToString() => $"{Value:F1}%";
}

/// <summary>
/// الوقت (الساعة والدقيقة)
/// </summary>
public readonly record struct TimeOfDay(byte Hour, byte Minute)
{
    public byte Hour { get; } = Hour;
    public byte Minute { get; } = Minute;

    public static TimeOfDay Create(byte hour, byte minute)
    {
        if (hour > 23) throw new ArgumentOutOfRangeException(nameof(hour), "الساعة يجب أن تكون بين 0 و 23");
        if (minute > 59) throw new ArgumentOutOfRangeException(nameof(minute), "الدقيقة يجب أن تكون بين 0 و 59");
        return new TimeOfDay(hour, minute);
    }

    public static TimeOfDay FromTimeSpan(TimeSpan ts) => Create((byte)ts.Hours, (byte)ts.Minutes);

    public TimeSpan ToTimeSpan() => new TimeSpan(Hour, Minute, 0);

    public int TotalMinutes => Hour * 60 + Minute;

    public static bool operator <(TimeOfDay left, TimeOfDay right) => left.TotalMinutes < right.TotalMinutes;
    public static bool operator >(TimeOfDay left, TimeOfDay right) => left.TotalMinutes > right.TotalMinutes;
    public static bool operator <=(TimeOfDay left, TimeOfDay right) => left.TotalMinutes <= right.TotalMinutes;
    public static bool operator >=(TimeOfDay left, TimeOfDay right) => left.TotalMinutes >= right.TotalMinutes;

    public static TimeOfDay operator +(TimeOfDay left, Duration right)
    {
        var total = left.TotalMinutes + right.Value;
        return Create((byte)(total / 60), (byte)(total % 60));
    }

    public static TimeOfDay operator -(TimeOfDay left, Duration right)
    {
        var total = left.TotalMinutes - right.Value;
        if (total < 0) total += 24 * 60;
        return Create((byte)(total / 60), (byte)(total % 60));
    }

    public static Duration operator -(TimeOfDay left, TimeOfDay right)
    {
        var diff = left.TotalMinutes - right.TotalMinutes;
        if (diff < 0) diff += 24 * 60;
        return Duration.FromMinutes(diff);
    }

    public override string ToString() => $"{Hour:D2}:{Minute:D2}";
}

/// <summary>
/// نطاق زمني (بداية ونهاية)
/// </summary>
public readonly record struct TimeRange(TimeOfDay Start, TimeOfDay End)
{
    public TimeOfDay Start { get; } = Start;
    public TimeOfDay End { get; } = End;

    public Duration Duration => End - Start;

    public bool Contains(TimeOfDay time) => time >= Start && time <= End;

    public bool Overlaps(TimeRange other) => Start < other.End && End > other.Start;

    public override string ToString() => $"{Start} - {End}";
}

/// <summary>
/// معلومات الاتصال
/// </summary>
public sealed record ContactInfo(
    string? Phone,
    string? Email,
    string? Address)
{
    public static ContactInfo Empty => new(null, null, null);

    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);
}