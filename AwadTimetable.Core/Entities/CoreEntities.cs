using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;
using AwadTimetable.Core.Common;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Core.Events;
using static AwadTimetable.Core.Events.TimetableEvents;

using SchoolId = AwadTimetable.Core.ValueObjects.SchoolId;
using TeacherId = AwadTimetable.Core.ValueObjects.TeacherId;
using SubjectId = AwadTimetable.Core.ValueObjects.SubjectId;
using RoomId = AwadTimetable.Core.ValueObjects.RoomId;
using ClassId = AwadTimetable.Core.ValueObjects.ClassId;
using LessonId = AwadTimetable.Core.ValueObjects.LessonId;
using TimetableId = AwadTimetable.Core.ValueObjects.TimetableId;
using PeriodNumber = AwadTimetable.Core.ValueObjects.PeriodNumber;
using Duration = AwadTimetable.Core.ValueObjects.Duration;

namespace AwadTimetable.Core.Entities;

/// <summary>
/// المدرسة
/// </summary>
public class School : Entity
{
    [Required, MaxLength(200)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(100)]
    public string? ShortName { get; private set; }

    [MaxLength(500)]
    public string? Address { get; private set; }

    [MaxLength(50)]
    public string? Phone { get; private set; }

    [MaxLength(100)]
    public string? Email { get; private set; }

    [MaxLength(100)]
    public string? PrincipalName { get; private set; }

    public string? LogoPath { get; private set; }

    public string? WatermarkText { get; private set; }

    public string? CopyrightText { get; private set; }

    public int AcademicYearStart { get; private set; } = DateTime.Now.Year;

    public int AcademicYearEnd { get; private set; } = DateTime.Now.Year + 1;

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; private set; }

    // العلاقات
    [JsonIgnore] public ICollection<Class> Classes { get; private set; } = new List<Class>();
    [JsonIgnore] public ICollection<Teacher> Teachers { get; private set; } = new List<Teacher>();
    [JsonIgnore] public ICollection<Room> Rooms { get; private set; } = new List<Room>();
    [JsonIgnore] public ICollection<Subject> Subjects { get; private set; } = new List<Subject>();
    [JsonIgnore] public ICollection<Timetable> Timetables { get; private set; } = new List<Timetable>();

    // Constructor خاص لـ EF Core
    private School() { }

    public School(string name, string? shortName = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ShortName = shortName;
    }

    public void UpdateBasicInfo(string name, string? shortName, string? address, string? phone, string? email, string? principalName)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ShortName = shortName;
        Address = address;
        Phone = phone;
        Email = email;
        PrincipalName = principalName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateBranding(string? logoPath, string? watermarkText, string? copyrightText)
    {
        LogoPath = logoPath;
        WatermarkText = watermarkText;
        CopyrightText = copyrightText;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAcademicYear(int startYear, int endYear)
    {
        if (endYear <= startYear)
            throw new ArgumentException("سنة النهاية يجب أن تكون أكبر من سنة البداية");

        AcademicYearStart = startYear;
        AcademicYearEnd = endYear;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public override string ToString() => $"{Name} ({AcademicYearStart}-{AcademicYearEnd})";
}

/// <summary>
/// المعلم
/// </summary>
public class Teacher : Entity
{
    [Required, MaxLength(100)]
    public string FirstName { get; private set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; private set; } = string.Empty;

    [MaxLength(100)]
    public string? MiddleName { get; private set; }

    [MaxLength(50)]
    public string? EmployeeNumber { get; private set; }

    [MaxLength(100)]
    public string? Email { get; private set; }

    [MaxLength(20)]
    public string? Phone { get; private set; }

    [MaxLength(500)]
    public string? Address { get; private set; }

    public DateTime? DateOfBirth { get; private set; }

    [MaxLength(50)]
    public string? NationalId { get; private set; }

    public string? PhotoPath { get; private set; }

    [MaxLength(50)]
    public string? Specialization { get; private set; }

    public DateTime? HireDate { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int MaxPeriodsPerDay { get; private set; } = 6;

    public int MaxPeriodsPerWeek { get; private set; } = 30;

    public bool CanTeachMultipleGrades { get; private set; } = true;

    public Guid? Substitute1TeacherId { get; private set; }
    public Guid? Substitute2TeacherId { get; private set; }
    public Guid? Substitute3TeacherId { get; private set; }

    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    // العلاقات
    public SchoolId SchoolId { get; private set; } = null!;
    public School School { get; private set; } = null!;

    [JsonIgnore] public ICollection<Subject> Subjects { get; private set; } = new List<Subject>();
    [JsonIgnore] public ICollection<Lesson> Lessons { get; private set; } = new List<Lesson>();
    [JsonIgnore] public ICollection<TeacherAvailability> Availabilities { get; private set; } = new List<TeacherAvailability>();

    private Teacher() { }

    public Teacher(SchoolId schoolId, string firstName, string lastName, string? middleName = null)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        FirstName = firstName ?? throw new ArgumentNullException(nameof(firstName));
        LastName = lastName ?? throw new ArgumentNullException(nameof(lastName));
        MiddleName = middleName;
    }

    public string FullName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();
    public string ShortName => $"{FirstName[0]}. {LastName}";

    /// <summary>اسم مختصر مخصص (يُحفظ خارج قاعدة البيانات في ملف الإعدادات).</summary>
    [NotMapped]
    public string? CustomShortName { get; private set; }

    /// <summary>لون مخصص للمعلم (يُحفظ خارج قاعدة البيانات في ملف الإعدادات).</summary>
    [NotMapped]
    public string? CustomColorHex { get; private set; }

    /// <summary>الاسم المختصر المعروض: المخصص إن وُجد وإلا المحسوب تلقائيًا.</summary>
    public string DisplayShortName => string.IsNullOrWhiteSpace(CustomShortName) ? ShortName : CustomShortName!;

    /// <summary>يضبط الاسم المختصر واللون المخصصين (لا يُخزَّنان في قاعدة البيانات).</summary>
    public void SetAppearance(string? shortName, string? colorHex)
    {
        CustomShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim();
        CustomColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex.Trim();
    }

    public void UpdatePersonalInfo(string firstName, string lastName, string? middleName, string? email, string? phone, string? address, DateTime? dateOfBirth, string? nationalId)
    {
        FirstName = firstName ?? throw new ArgumentNullException(nameof(firstName));
        LastName = lastName ?? throw new ArgumentNullException(nameof(lastName));
        MiddleName = middleName;
        Email = email;
        Phone = phone;
        Address = address;
        DateOfBirth = dateOfBirth;
        NationalId = nationalId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEmploymentInfo(string? employeeNumber, DateTime? hireDate, string? specialization, bool canTeachMultipleGrades, int maxPeriodsPerDay, int maxPeriodsPerWeek)
    {
        EmployeeNumber = employeeNumber;
        HireDate = hireDate;
        Specialization = specialization;
        CanTeachMultipleGrades = canTeachMultipleGrades;
        MaxPeriodsPerDay = Math.Max(1, maxPeriodsPerDay);
        MaxPeriodsPerWeek = Math.Max(1, maxPeriodsPerWeek);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPhoto(string? photoPath)
    {
        PhotoPath = photoPath;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSubstitutes(Guid? substitute1, Guid? substitute2, Guid? substitute3)
    {
        Substitute1TeacherId = substitute1;
        Substitute2TeacherId = substitute2;
        Substitute3TeacherId = substitute3;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddSubject(Subject subject)
    {
        if (!Subjects.Contains(subject))
            Subjects.Add(subject);
    }

    public void RemoveSubject(Subject subject)
    {
        Subjects.Remove(subject);
    }

    public override string ToString() => FullName;
}

/// <summary>
/// توفر المعلم
/// </summary>
public class TeacherAvailability : Entity
{
    public TeacherId TeacherId { get; private set; } = null!;
    public Teacher Teacher { get; private set; } = null!;

    public AwadTimetable.Core.Enums.DayOfWeek Day { get; private set; }
    public PeriodNumber Period { get; private set; } = null!;
    public bool IsAvailable { get; set; } = true;

    /// <summary>أولوية الإغلاق: Low = قابل للتجاوز (لين)، High = ملزم (صلب)</summary>
    public AvailabilityPriority Priority { get; private set; } = AvailabilityPriority.High;

    private TeacherAvailability() { }

    public TeacherAvailability(TeacherId teacherId, AwadTimetable.Core.Enums.DayOfWeek day, PeriodNumber period, bool isAvailable = true,
        AvailabilityPriority priority = AvailabilityPriority.High)
    {
        TeacherId = teacherId;
        Day = day;
        Period = period;
        IsAvailable = isAvailable;
        Priority = priority;
    }

    public void SetAvailability(bool available)
    {
        IsAvailable = available;
    }

    public void SetPriority(AvailabilityPriority priority)
    {
        Priority = priority;
    }

    /// <summary>إغلاق ملزم (يُحترم دائماً): غير متاح وأولويته عالية</summary>
    public bool IsHardBlock => !IsAvailable && Priority == AvailabilityPriority.High;

    /// <summary>إغلاق لين (يُتجاوز عند الحاجة): غير متاح وأولويته منخفضة</summary>
    public bool IsSoftBlock => !IsAvailable && Priority == AvailabilityPriority.Low;
}

/// <summary>
/// المادة الدراسية
/// </summary>
public class Subject : Entity
{
    [Required, MaxLength(100)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(20)]
    public string? ShortName { get; private set; }

    [MaxLength(500)]
    public string? Description { get; private set; }

    public string? ColorHex { get; private set; }

    public int DefaultPeriodsPerWeek { get; private set; } = 2;

    public int MinPeriodsPerWeek { get; private set; } = 1;

    public int MaxPeriodsPerWeek { get; private set; } = 5;

    public bool RequiresLab { get; private set; } = false;

    public bool RequiresSpecialRoom { get; private set; } = false;

    public int MinStudentsPerClass { get; private set; } = 1;

    public int MaxStudentsPerClass { get; private set; } = 30;

    public int GradeLevel { get; private set; } = 1;

    public bool IsActive { get; private set; } = true;

    public int SortOrder { get; private set; } = 0;

    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    // العلاقات
    public SchoolId SchoolId { get; private set; } = null!;
    public School School { get; private set; } = null!;

    [JsonIgnore] public ICollection<Teacher> Teachers { get; private set; } = new List<Teacher>();
    [JsonIgnore] public ICollection<Lesson> Lessons { get; private set; } = new List<Lesson>();

    private Subject() { }

    public Subject(SchoolId schoolId, string name, string? shortName = null)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ShortName = shortName ?? name.Substring(0, Math.Min(name.Length, 10));
        ColorHex = GenerateRandomColor();
    }

    public void UpdateBasicInfo(string name, string? shortName, string? description, int gradeLevel)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ShortName = shortName ?? name.Substring(0, Math.Min(name.Length, 10));
        Description = description;
        GradeLevel = Math.Max(1, gradeLevel);
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateRequirements(bool requiresLab, bool requiresSpecialRoom, int minStudents, int maxStudents)
    {
        RequiresLab = requiresLab;
        RequiresSpecialRoom = requiresSpecialRoom;
        MinStudentsPerClass = Math.Max(1, minStudents);
        MaxStudentsPerClass = Math.Max(MinStudentsPerClass, maxStudents);
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSchedule(int defaultPeriods, int minPeriods, int maxPeriods)
    {
        DefaultPeriodsPerWeek = Math.Max(1, defaultPeriods);
        MinPeriodsPerWeek = Math.Max(1, minPeriods);
        MaxPeriodsPerWeek = Math.Max(MaxPeriodsPerWeek, maxPeriods);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetColor(string colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex) && !colorHex.StartsWith("#"))
            colorHex = "#" + colorHex;

        ColorHex = colorHex?.Length == 7 ? colorHex : GenerateRandomColor();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSortOrder(int order)
    {
        SortOrder = order;
        UpdatedAt = DateTime.UtcNow;
    }

    private string GenerateRandomColor()
    {
        var colors = new[]
        {
            "#2563EB", "#059669", "#7C3AED", "#D97706", "#EC4899", "#0891B2",
            "#DC2626", "#16A34A", "#9333EA", "#EA580C", "#DB2777", "#0D9488"
        };
        var random = new Random(Id.GetHashCode());
        return colors[random.Next(colors.Length)];
    }

    public override string ToString() => Name;
}

/// <summary>
/// الغرفة/القاعة
/// </summary>
public class Room : Entity
{
    [Required, MaxLength(100)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(20)]
    public string? ShortName { get; private set; }

    [MaxLength(50)]
    public string? Code { get; private set; }

    public int Capacity { get; private set; } = 30;

    public bool HasProjector { get; private set; } = false;
    public bool HasSmartBoard { get; private set; } = false;
    public bool HasComputers { get; private set; } = false;
    public bool IsLab { get; private set; } = false;
    public bool IsGym { get; private set; } = false;
    public bool IsLibrary { get; private set; } = false;
    public bool IsMusicRoom { get; private set; } = false;
    public bool IsArtRoom { get; private set; } = false;

    [MaxLength(500)]
    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int Floor { get; private set; } = 0;
    [MaxLength(50)]
    public string? Building { get; private set; }

    public string? PhotoPath { get; private set; }

    /// <summary>معلم مخصَّص له هذه القاعة (قاعة المعلم) — تُسند حصصه إليها تلقائياً عند التوليد.</summary>
    public Guid? AssignedTeacherId { get; private set; }

    /// <summary>قاعة مشتركة بين أكثر من شعبة/معلم (لا تُخصَّص لشعبة واحدة).</summary>
    public bool IsShared { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    // العلاقات
    public SchoolId SchoolId { get; private set; } = null!;
    public School School { get; private set; } = null!;

    [JsonIgnore] public ICollection<Lesson> Lessons { get; private set; } = new List<Lesson>();

    private Room() { }

    public Room(SchoolId schoolId, string name, string? shortName = null, int capacity = 30)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ShortName = shortName ?? name.Substring(0, Math.Min(name.Length, 10));
        Capacity = Math.Max(1, capacity);
    }

    public void UpdateBasicInfo(string name, string? shortName, string? code, string? description, int capacity)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ShortName = shortName ?? name.Substring(0, Math.Min(name.Length, 10));
        Code = code;
        Description = description;
        Capacity = Math.Max(1, capacity);
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEquipment(bool hasProjector, bool hasSmartBoard, bool hasComputers)
    {
        HasProjector = hasProjector;
        HasSmartBoard = hasSmartBoard;
        HasComputers = hasComputers;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateType(bool isLab, bool isGym, bool isLibrary, bool isMusicRoom, bool isArtRoom)
    {
        IsLab = isLab;
        IsGym = isGym;
        IsLibrary = isLibrary;
        IsMusicRoom = isMusicRoom;
        IsArtRoom = isArtRoom;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateLocation(int floor, string? building)
    {
        Floor = floor;
        Building = building;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPhoto(string? photoPath)
    {
        PhotoPath = photoPath;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>يخصّص القاعة لمعلم (قاعة المعلم) أو يلغي التخصيص بتمرير null.</summary>
    public void SetTeacherAssignment(Guid? teacherId)
    {
        AssignedTeacherId = teacherId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>يجعل القاعة مشتركة (بين عدة شعب/معلمين) أو مخصّصة.</summary>
    public void SetShared(bool shared)
    {
        IsShared = shared;
        UpdatedAt = DateTime.UtcNow;
    }

    public override string ToString() => $"{Name} ({Capacity} طالب)";
}

/// <summary>
/// الفصل/الصف
/// </summary>
public class Class : Entity
{
    [Required, MaxLength(100)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(20)]
    public string? ShortName { get; private set; }

    [MaxLength(20)]
    public string? Code { get; private set; }

    public int GradeLevel { get; private set; } = 1;

    public char Section { get; private set; } = 'أ';

    public int StudentCount { get; private set; } = 0;

    public int MaxStudents { get; private set; } = 30;

    public bool IsActive { get; private set; } = true;

    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    // العلاقات
    public SchoolId SchoolId { get; private set; } = null!;
    public School School { get; private set; } = null!;

    public TeacherId? HomeroomTeacherId { get; private set; }
    public Teacher? HomeroomTeacher { get; private set; }

    public RoomId? DefaultRoomId { get; private set; }
    public Room? DefaultRoom { get; private set; }

    [JsonIgnore] public ICollection<Lesson> Lessons { get; private set; } = new List<Lesson>();

    private Class() { }

    public Class(SchoolId schoolId, string name, int gradeLevel, char section = 'أ')
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        GradeLevel = Math.Max(1, gradeLevel);
        Section = section;
    }

    public string FullName => $"{Name} - {GradeLevel}{Section}";

    /// <summary>الاسم المختصر للعرض في الجدول والطباعة، وإن لم يُضبط يُستخدم الاسم</summary>
    public string DisplayShortName => string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName!;

    /// <summary>ضبط الاسم المختصر للعرض</summary>
    public void SetShortName(string? shortName)
    {
        ShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateBasicInfo(string name, string? shortName, string? code, int studentCount)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ShortName = shortName ?? name.Substring(0, Math.Min(name.Length, 10));
        Code = code;
        StudentCount = Math.Max(0, studentCount);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetHomeroomTeacher(TeacherId? teacherId)
    {
        HomeroomTeacherId = teacherId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDefaultRoom(RoomId? roomId)
    {
        DefaultRoomId = roomId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStudentCount(int count)
    {
        StudentCount = Math.Max(0, count);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }

    public override string ToString() => FullName;
}

/// <summary>
/// مجموعة طلابية (شعبة فرعية داخل الفصل)
/// </summary>
public class Group : Entity
{
    [Required, MaxLength(100)]
    public string Name { get; private set; } = string.Empty;

    public Guid ClassId { get; private set; }
    [JsonIgnore] public Class Class { get; private set; } = null!;

    public int StudentCount { get; private set; }

    public Group(SchoolId schoolId, Guid classId, string name, int studentCount = 0)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        ClassId = classId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        StudentCount = studentCount;
    }

    public SchoolId SchoolId { get; private set; } = null!;
}

/// <summary>
/// طالب
/// </summary>
public class Student : Entity
{
    [Required, MaxLength(100)]
    public string Name { get; private set; } = string.Empty;

    public Guid ClassId { get; private set; }
    [JsonIgnore] public Class Class { get; private set; } = null!;

    [NotMapped] public ICollection<Guid> GroupIds { get; private set; } = new List<Guid>();

    public Student(SchoolId schoolId, Guid classId, string name)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        ClassId = classId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public SchoolId SchoolId { get; private set; } = null!;
}

/// <summary>
/// الحصة/الدرس
/// </summary>
public class Lesson : Entity
{
    public SubjectId SubjectId { get; private set; } = null!;

    /// <summary>العلاقة تُملأ صراحةً عند بناء/تحميل الجدول (EF تملأها أثناء التتبع)؛ لا تُحفظ مع الملف</summary>
    [JsonIgnore] public Subject Subject { get; private set; } = null!;

    public TeacherId TeacherId { get; private set; } = null!;
    [JsonIgnore] public Teacher Teacher { get; private set; } = null!;

    public ClassId ClassId { get; private set; } = null!;
    [JsonIgnore] public Class Class { get; private set; } = null!;

    public RoomId? RoomId { get; set; }
    [JsonIgnore] public Room? Room { get; set; }

    [NotMapped] public Guid? GroupId { get; set; }
    [JsonIgnore, NotMapped] public Group? Group { get; set; }

    public AwadTimetable.Core.Enums.DayOfWeek Day { get; set; }
    public PeriodNumber Period { get; set; } = null!;
    public Duration Duration { get; set; } = Duration.FromMinutes(45);

    public LessonType Type { get; set; } = LessonType.Regular;
    public int WeekNumber { get; set; } = 1; // للفصول الدراسية المتعددة

    public int SortOrder { get; private set; } = 0;
    public bool IsFixed { get; private set; } = false; // حصة ثابتة لا تتغير

    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    // العلاقات
    public TimetableId TimetableId { get; private set; } = null!;
    [JsonIgnore] public Timetable Timetable { get; private set; } = null!;

    private Lesson() { }

    public Lesson(TimetableId timetableId, SubjectId subjectId, TeacherId teacherId, ClassId classId,
                  AwadTimetable.Core.Enums.DayOfWeek day, PeriodNumber period, Duration? duration = null)
    {
        TimetableId = timetableId ?? throw new ArgumentNullException(nameof(timetableId));
        SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
        TeacherId = teacherId ?? throw new ArgumentNullException(nameof(teacherId));
        ClassId = classId ?? throw new ArgumentNullException(nameof(classId));
        Day = day;
        Period = period ?? throw new ArgumentNullException(nameof(period));
        Duration = duration ?? Duration.FromMinutes(45);
    }

    /// <summary>ينشئ حصة بمعرّف محدد (للحفظ المستقر والحفاظ على ربط البدائل بالمعرّف).</summary>
    [JsonConstructor]
    public Lesson(Guid id, TimetableId timetableId, SubjectId subjectId, TeacherId teacherId, ClassId classId,
                  AwadTimetable.Core.Enums.DayOfWeek day, PeriodNumber period, Duration? duration = null)
        : base(id)
    {
        TimetableId = timetableId ?? throw new ArgumentNullException(nameof(timetableId));
        SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
        TeacherId = teacherId ?? throw new ArgumentNullException(nameof(teacherId));
        ClassId = classId ?? throw new ArgumentNullException(nameof(classId));
        Day = day;
        Period = period ?? throw new ArgumentNullException(nameof(period));
        Duration = duration ?? Duration.FromMinutes(45);
    }

    public void AssignRoom(RoomId? roomId)
    {
        RoomId = roomId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>ربط الكيانات المرجعية — تُستدعى بعد التحميل/الإنشاء حتى تعمل رسائل القيود وأسماء العرض</summary>
    public void AttachReferences(Subject? subject, Teacher? teacher, Class? @class, Room? room = null)
    {
        if (subject is not null) Subject = subject;
        if (teacher is not null) Teacher = teacher;
        if (@class is not null) Class = @class;
        if (room is not null) Room = room;
    }

    public void Reschedule(AwadTimetable.Core.Enums.DayOfWeek day, PeriodNumber period)
    {
        Day = day;
        Period = period ?? throw new ArgumentNullException(nameof(period));
        UpdatedAt = DateTime.UtcNow;
    }

    public void ChangeTeacher(TeacherId teacherId)
    {
        TeacherId = teacherId ?? throw new ArgumentNullException(nameof(teacherId));
        UpdatedAt = DateTime.UtcNow;
    }

    public void ChangeSubject(SubjectId subjectId)
    {
        SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
        UpdatedAt = DateTime.UtcNow;
    }

    public void ChangeClass(ClassId classId)
    {
        ClassId = classId ?? throw new ArgumentNullException(nameof(classId));
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDuration(Duration duration)
    {
        Duration = duration;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetType(LessonType type)
    {
        Type = type;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Fix(bool isFixed = true)
    {
        IsFixed = isFixed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSortOrder(int order)
    {
        SortOrder = order;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNotes(string? notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public string GetDisplayName() => $"{Subject?.Name ?? "غير محدد"} - {Class?.FullName ?? "غير محدد"}";
}

/// <summary>
/// الجدول الزمني
/// </summary>
public class Timetable : AggregateRoot
{
    [Required, MaxLength(200)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; private set; }

    public int AcademicYearStart { get; private set; }
    public int AcademicYearEnd { get; private set; }

    public int TermNumber { get; private set; } = 1; // 1 للفصل الأول، 2 للثاني، إلخ

    /// <summary>محرك التوليد المختار لهذا الجدول (CP-SAT افتراضياً)</summary>
    public GeneratorEngine Engine { get; private set; } = GeneratorEngine.CpSat;

    /// <summary>نوع المدرسة (يحدد قالب الفسح وزمن الحصص الافتراضي)</summary>
    public SchoolType SchoolType { get; private set; } = SchoolType.Middle;

    public TimetableStatus Status { get; private set; } = TimetableStatus.Draft;

    public DateTime? GeneratedAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    public int TotalLessons { get; private set; } = 0;
    public int TotalConflicts { get; private set; } = 0;

    public Score QualityScore { get; private set; } = Score.Zero;

    [MaxLength(500)]
    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    // العلاقات
    public SchoolId SchoolId { get; private set; } = null!;
    public School School { get; private set; } = null!;

    public List<Lesson> Lessons { get; set; } = new List<Lesson>();
    [NotMapped] public List<Group> Groups { get; set; } = new List<Group>();
    [NotMapped] public List<Student> Students { get; set; } = new List<Student>();
    public ICollection<TeacherAvailability> TeacherAvailabilities { get; private set; } = new List<TeacherAvailability>();

    /// <summary>توقيت الفترات (غير مخزّن في قاعدة البيانات — يُحفظ مع ملف الجدول)</summary>
    [NotMapped]
    public List<PeriodTime> PeriodTimes { get; set; } = new List<PeriodTime>();

    /// <summary>طبقة الإسناد المركزية (معلم+مادة+فصل+عدد الحصص) — غير مخزّنة في مخطط قاعدة البيانات، تُحفظ مع الجدول</summary>
    [NotMapped]
    public List<TeachingAssignment> Assignments { get; set; } = new List<TeachingAssignment>();

    /// <summary>بنود القواعد/السجل (وقت انتهاء، ربط معلمين، …) — غير مخزّنة في EF، تُحفظ مع الجدول في RulesJson</summary>
    [NotMapped]
    public List<RuleItem> Rules { get; set; } = new List<RuleItem>();

    /// <summary>إعدادات أيام العمل والحصص اليومية لكل مرحلة (غير مخزّنة في EF — تُحفظ مع الجدول)</summary>
    [NotMapped]
    public List<StageScheduleConfig> StageConfigs { get; set; } = new List<StageScheduleConfig>();

    /// <summary>خريطة معرف الفصل → رقم الصف (تُملأ عند التحميل؛ تُستخدم للتعرف على مرحلة الفصل)</summary>
    [NotMapped]
    public Dictionary<Guid, int> ClassGradeLevels { get; set; } = new Dictionary<Guid, int>();

    /// <summary>خريطة معرف الفصل → اسم الفصل (تُملأ عند التحميل؛ تُستخدم للتعرف الدقيق على المرحلة من الاسم)</summary>
    [NotMapped]
    public Dictionary<Guid, string> ClassNames { get; set; } = new Dictionary<Guid, string>();

    /// <summary>يسجّل الفصل (اسمه وصفه) لاستخدامه في تحديد المرحلة لاحقاً</summary>
    public void RegisterClass(Guid classId, string? name, int gradeLevel)
    {
        ClassGradeLevels[classId] = gradeLevel;
        if (!string.IsNullOrWhiteSpace(name))
            ClassNames[classId] = name;
    }

    /// <summary>مرحلة الفصل من الاسم أو رقم الصف؛ null إذا كان الفصل غير معروف</summary>
    public string? GetStageForClass(Guid classId)
    {
        ClassNames.TryGetValue(classId, out var name);
        if (ClassGradeLevels.TryGetValue(classId, out var grade))
            return StageHelper.GetStage(grade, name);
        if (!string.IsNullOrWhiteSpace(name))
            return StageHelper.GetStage(0, name);
        return null;
    }

    /// <summary>المراحل المستخدمة في هذا الجدول (من إعدادات المراحل)</summary>
    public IReadOnlyList<string> StagesInUse()
        => StageConfigs.Select(c => c.Stage)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

    /// <summary>
    /// الفاصل الزمني الفعلي لفترة (دقيقة البداية/النهاية) مع مراعاة اليوم والمرحلة.
    /// ضروري لحساب تضارب المعلم المشترك بين مرحلتين بتوقيتين مختلفين.
    /// </summary>
    public bool TryGetPeriodInterval(int period, int day, string? stage, out int start, out int end)
    {
        var pt = PeriodTimes.FirstOrDefault(p => p.Period == period && p.Day == day && string.Equals(p.Stage, stage, StringComparison.Ordinal))
            ?? PeriodTimes.FirstOrDefault(p => p.Period == period && p.Day == day && string.IsNullOrEmpty(p.Stage))
            ?? PeriodTimes.FirstOrDefault(p => p.Period == period && p.Day == -1 && string.Equals(p.Stage, stage, StringComparison.Ordinal))
            ?? PeriodTimes.FirstOrDefault(p => p.Period == period && p.Day == -1 && string.IsNullOrEmpty(p.Stage))
            ?? PeriodTimes.FirstOrDefault(p => p.Period == period);

        if (pt is null)
        {
            start = end = 0;
            return false;
        }

        start = pt.StartMinutes;
        end = pt.EndMinutes;
        return true;
    }

    /// <summary>هل الفترة استراحة لهذا (اليوم، المرحلة)؟ الاستراحة العامة (Day=-1, Stage=null) تُطابق الجميع.</summary>
    public bool IsBreakPeriod(int period, int day, string? stage)
    {
        foreach (var pt in PeriodTimes)
        {
            if (pt.Period != period || !pt.IsBreak) continue;
            if (pt.Day != -1 && pt.Day != day) continue;
            if (!string.IsNullOrEmpty(pt.Stage) && !string.Equals(pt.Stage, stage, StringComparison.Ordinal)) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// إيجاد توقيت فترة: المطابقة الأدق أولاً (اليوم+المرحلة) ثم (اليوم فقط) ثم (المرحلة فقط) ثم (عام).
    /// day: -1 = كل الأيام، stage: null = كل المراحل
    /// </summary>
    public PeriodTime? GetPeriodTime(int period, int day = -1, string? stage = null)
    {
        var candidates = PeriodTimes.Where(p => p.Period == period).ToList();
        if (candidates.Count == 0) return null;
        return candidates.FirstOrDefault(p => p.Day == day && p.Stage == stage)
            ?? candidates.FirstOrDefault(p => p.Day == day && string.IsNullOrEmpty(p.Stage))
            ?? candidates.FirstOrDefault(p => p.Day == -1 && p.Stage == stage)
            ?? candidates.FirstOrDefault(p => p.Day == -1 && string.IsNullOrEmpty(p.Stage))
            ?? candidates[0];
    }

    /// <summary>
    /// توقيت فترة للعرض العام (الجدول الكامل/الطباعة): يُرجع التوقيت إذا كان موحّداً،
    /// و null إذا اختلفت التوقيات بين المراحل/الأيام (حتى لا يُعرض توقيت مرحلة بعينها كأنه للجميع).
    /// </summary>
    public PeriodTime? GetPeriodTimeForDisplay(int period)
    {
        var candidates = PeriodTimes.Where(p => p.Period == period).ToList();
        if (candidates.Count == 0) return null;
        var distinct = candidates.Select(p => (p.StartMinutes, p.EndMinutes)).Distinct().ToList();
        return distinct.Count == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// هل توجد فترة معرّفة صراحة لهذا (فترة، يوم، مرحلة)؟
    /// يقبل اليوم العام (-1) والمرحلة العامة (null/فارغ)، بدون التراجع لمرحلة أو يوم آخر
    /// (لذلك يختلف عن GetPeriodTime الذي يرجع candidates[0] كملاذ أخير).
    /// </summary>
    public bool HasPeriodTime(int period, int day, string? stage)
    {
        return PeriodTimes.Any(p => p.Period == period
            && (p.Day == day || p.Day == -1)
            && (p.Stage == stage || string.IsNullOrEmpty(p.Stage)));
    }

    public Timetable() { }

    public Timetable(SchoolId schoolId, string name, int academicYearStart, int academicYearEnd, int termNumber = 1)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        AcademicYearStart = academicYearStart;
        AcademicYearEnd = academicYearEnd;
        TermNumber = Math.Max(1, termNumber);
    }

    /// <summary>ينشئ جدولاً بمعرّف محدد (للحفظ المستقر بنفس المعرّف دون تغييره في كل حفظ).</summary>
    public Timetable(Guid id, SchoolId schoolId, string name, int academicYearStart, int academicYearEnd, int termNumber = 1)
        : base(id)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        AcademicYearStart = academicYearStart;
        AcademicYearEnd = academicYearEnd;
        TermNumber = Math.Max(1, termNumber);
    }

    public void UpdateBasicInfo(string name, string? description, int termNumber)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        TermNumber = Math.Max(1, termNumber);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEngine(GeneratorEngine engine)
    {
        Engine = engine;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>إعادة ربط الجدول بمساحة عمل/مدرسة أخرى (يُستخدم عند استيراد ملف يحمل مدرسة غير موجودة).</summary>
    public void ReassignSchool(SchoolId schoolId)
    {
        SchoolId = schoolId ?? throw new ArgumentNullException(nameof(schoolId));
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSchoolType(SchoolType type)
    {
        SchoolType = type;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddLesson(Lesson lesson)
    {
        if (!Lessons.Contains(lesson))
        {
            Lessons.Add(lesson);
            TotalLessons = Lessons.Count;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void RemoveLesson(Lesson lesson)
    {
        Lessons.Remove(lesson);
        TotalLessons = Lessons.Count;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Generate()
    {
        Status = TimetableStatus.Generated;
        GeneratedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new TimetableGenerated(Id, Lessons.Count, TimeSpan.Zero));
    }

    public void Publish()
    {
        if (Status != TimetableStatus.Generated && Status != TimetableStatus.Draft)
            throw new InvalidOperationException("لا يمكن نشر جدول غير مكتمل");

        Status = TimetableStatus.Published;
        PublishedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new TimetablePublished(Id, PublishedAt.Value));
    }

    public void Archive()
    {
        Status = TimetableStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new TimetableArchived(Id, DateTime.UtcNow));
    }

    public void UpdateStatistics(int totalLessons, int totalConflicts, Score qualityScore)
    {
        TotalLessons = totalLessons;
        TotalConflicts = totalConflicts;
        QualityScore = qualityScore;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStatus(TimetableStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNotes(string? notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public override string ToString() => $"{Name} ({AcademicYearStart}-{AcademicYearEnd}) - {Status}";
}

/// <summary>
/// توقيت فترة دراسية: يُحدد بداية ونهاية الحصة، مع إمكانية التخصيص حسب اليوم والمرحلة.
/// Day: -1 = كل الأيام، Stage: null/farigh = كل المراحل.
/// </summary>
public class PeriodTime
{
    public byte Period { get; set; }
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public int Day { get; set; } = -1;
    public string? Stage { get; set; }

    /// <summary>هل هذه الفترة استراحة (لا تُجدَّل فيها حصص)؟ مثال: استراحة الغداء.</summary>
    public bool IsBreak { get; set; }

    public static string FormatMinutes(int minutes)
        => $"{minutes / 60:00}:{minutes % 60:00}";

    public string RangeText => $"{FormatMinutes(StartMinutes)} - {FormatMinutes(EndMinutes)}";

    public static bool TryParseTime(string text, out int minutes)
    {
        minutes = 0;
        var parts = text.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        minutes = h * 60 + m;
        return true;
    }
}

/// <summary>إعداد جدول مرحلة تعليمية: أيام العمل وعدد الحصص اليومي</summary>
public class StageScheduleConfig
{
    /// <summary>اسم المرحلة: ابتدائي / متوسط / ثانوي</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>أيام العمل كقيم enum DayOfWeek (0=السبت … 5=الخميس؛ الجمعة=6 لا تُدرَج)</summary>
    public List<int> WorkDays { get; set; } = new List<int>();

    /// <summary>الحد الأقصى لعدد الحصص في اليوم الواحد لهذه المرحلة</summary>
    public int LessonsPerDay { get; set; } = 7;

    /// <summary>الحد الأدنى لعدد الحصص في اليوم الواحد لهذه المرحلة (0 = بلا حد أدنى)</summary>
    public int MinLessonsPerDay { get; set; } = 0;

    /// <summary>عدد حصص مخصص لأيام معينة (مفتاح = رقم اليوم)؛ ما ليس فيه يستخدم LessonsPerDay</summary>
    public Dictionary<int, int> LessonsPerDayByDay { get; set; } = new();

    /// <summary>عدد الحصص لهذا اليوم: المخصص إن وُجد، وإلا العام</summary>
    public int GetLessonsPerDay(int day)
        => LessonsPerDayByDay.TryGetValue(day, out var v) && v > 0 ? v : LessonsPerDay;
}

/// <summary>مساعد تحديد المرحلة التعليمية من الصف أو من اسم الفصل</summary>
public static class StageHelper
{
    /// <summary>
    /// المرحلة من اسم الفصل أولاً (أسماء الفصول السعودية تحمل المرحلة صراحة مثل «أول متوسط 2»)،
    /// ثم من رقم الصف كاحتياط. هذا ضروري لأن أرقام الصفوف تتكرر بين المراحل (1-3 في الابتدائي والمتوسط والثانوي).
    /// </summary>
    public static string GetStage(int gradeLevel, string? className = null)
    {
        if (!string.IsNullOrWhiteSpace(className))
        {
            if (className.Contains("ثانوي", StringComparison.Ordinal)) return "ثانوي";
            if (className.Contains("متوسط", StringComparison.Ordinal)) return "متوسط";
            if (className.Contains("ابتدائي", StringComparison.Ordinal)) return "ابتدائي";
        }

        return gradeLevel switch
        {
            >= 1 and <= 6 => "ابتدائي",
            >= 7 and <= 9 => "متوسط",
            >= 10 => "ثانوي",
            _ => "غير محدد"
        };
    }

    /// <summary>
    /// يستنتج رقم الصف (1..12) من اسم الفصل العربي: «أول متوسط 1» = 7، «ثاني ثانوي 2» = 11، «ثالث ابتدائي» = 3.
    /// </summary>
    public static int ParseGrade(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return 1;

        var name = className!;
        var ordinal = name.Contains("أول") ? 1
            : name.Contains("ثاني") ? 2
            : name.Contains("ثالث") ? 3
            : name.Contains("رابع") ? 4
            : name.Contains("خامس") ? 5
            : name.Contains("سادس") ? 6
            : 0;

        var baseGrade = name.Contains("ثانوي") ? 10
            : name.Contains("متوسط") ? 7
            : name.Contains("ابتدائي") ? 1
            : 1;

        return ordinal == 0 ? baseGrade : baseGrade + (ordinal - 1);
    }

    /// <summary>يستنتج الشعبة من آخر مقطع في اسم الفصل إن كان حرفاً عربياً واحداً، وإلا «أ».</summary>
    public static char ParseSection(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return 'أ';

        var tokens = className!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return 'أ';

        var last = tokens[^1];
        return last.Length == 1 && char.IsLetter(last[0]) ? last[0] : 'أ';
    }
}