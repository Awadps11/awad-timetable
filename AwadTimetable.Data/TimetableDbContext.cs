using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Core.Enums;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Data;

/// <summary>
/// سياق قاعدة البيانات
/// </summary>
public class TimetableDbContext : DbContext
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Class> Classes => Set<Class>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Timetable> Timetables => Set<Timetable>();
    public DbSet<TeacherAvailability> TeacherAvailabilities => Set<TeacherAvailability>();

    public TimetableDbContext(DbContextOptions<TimetableDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AwadTimetable", "awad_timetable.db");
            
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // تجاهل أنواع المعرفات القوية (Strong-typed IDs) - ليست كيانات قاعدة بيانات
        modelBuilder.Ignore<SchoolId>();
        modelBuilder.Ignore<TeacherId>();
        modelBuilder.Ignore<SubjectId>();
        modelBuilder.Ignore<RoomId>();
        modelBuilder.Ignore<ClassId>();
        modelBuilder.Ignore<LessonId>();
        modelBuilder.Ignore<TimetableId>();
        modelBuilder.Ignore<PeriodNumber>();
        modelBuilder.Ignore<Duration>();
        modelBuilder.Ignore<ConstraintId>();
        modelBuilder.Ignore<ConstraintWeight>();
        modelBuilder.Ignore<Score>();
        modelBuilder.Ignore<Percentage>();
        modelBuilder.Ignore<ContactInfo>();

        // كيانات نموذجية غير مخزّنة في قاعدة البيانات (تُحفظ مع ملف الجدول فقط)
        modelBuilder.Ignore<Group>();
        modelBuilder.Ignore<Student>();

        // School
        modelBuilder.Entity<School>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ShortName).HasMaxLength(100);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.HasIndex(e => e.Name);
        });

        // Teacher
        modelBuilder.Entity<Teacher>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.MiddleName).HasMaxLength(100);
            entity.Property(e => e.EmployeeNumber).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.NationalId).HasMaxLength(50);
            entity.Property(e => e.Specialization).HasMaxLength(50);
            entity.HasIndex(e => new { e.SchoolId, e.EmployeeNumber }).IsUnique();
            entity.HasIndex(e => new { e.SchoolId, e.LastName, e.FirstName });
        });

        // Subject
        modelBuilder.Entity<Subject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ShortName).HasMaxLength(20);
            entity.Property(e => e.ColorHex).HasMaxLength(7);
            entity.HasIndex(e => new { e.SchoolId, e.Name }).IsUnique();
        });

        // Room
        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ShortName).HasMaxLength(20);
            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.Building).HasMaxLength(50);
            entity.HasIndex(e => new { e.SchoolId, e.Code }).IsUnique();
        });

        // Class
        modelBuilder.Entity<Class>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ShortName).HasMaxLength(20);
            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.Section).HasMaxLength(1).IsFixedLength();
            entity.HasIndex(e => new { e.SchoolId, e.GradeLevel, e.Section });
        });

        // Lesson
        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Day).HasConversion<byte>();
            entity.Property(e => e.Period).HasConversion(
                v => v.Value,
                v => PeriodNumber.Create(v));
            entity.Property(e => e.Duration).HasConversion(
                v => v.Value,
                v => Duration.FromMinutes(v));
            entity.Property(e => e.Type).HasConversion<byte>();
            entity.HasIndex(e => new { e.TimetableId, e.Day, e.Period, e.ClassId });
            entity.HasIndex(e => new { e.TimetableId, e.Day, e.Period, e.TeacherId });
            entity.HasIndex(e => new { e.TimetableId, e.Day, e.Period, e.RoomId });
        });

        // Timetable
        modelBuilder.Entity<Timetable>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<byte>();
            entity.HasIndex(e => new { e.SchoolId, e.Name, e.AcademicYearStart, e.AcademicYearEnd, e.TermNumber }).IsUnique();
        });

        // TeacherAvailability
        modelBuilder.Entity<TeacherAvailability>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Day).HasConversion<byte>();
            entity.Property(e => e.Period).HasConversion(
                v => v.Value,
                v => PeriodNumber.Create(v));
            entity.HasIndex(e => new { e.TeacherId, e.Day, e.Period }).IsUnique();
        });

// تحويل Value Objects للمفاتيح الأجنبية
        var schoolConverter = new ValueConverter<SchoolId, Guid>(v => v.Value, v => new SchoolId(v));
        modelBuilder.Entity<Teacher>().Property(e => e.SchoolId).HasConversion(schoolConverter);
        modelBuilder.Entity<Subject>().Property(e => e.SchoolId).HasConversion(schoolConverter);
        modelBuilder.Entity<Room>().Property(e => e.SchoolId).HasConversion(schoolConverter);
        modelBuilder.Entity<Class>().Property(e => e.SchoolId).HasConversion(schoolConverter);
        modelBuilder.Entity<Timetable>().Property(e => e.SchoolId).HasConversion(schoolConverter);

        var teacherConverter = new ValueConverter<TeacherId, Guid>(v => v.Value, v => new TeacherId(v));
        modelBuilder.Entity<Lesson>().Property(e => e.TeacherId).HasConversion(teacherConverter);
        modelBuilder.Entity<TeacherAvailability>().Property(e => e.TeacherId).HasConversion(teacherConverter);

        var roomConverter = new ValueConverter<RoomId?, Guid?>(v => v != null ? v.Value : null, v => v != null ? new RoomId(v.Value) : null);
        modelBuilder.Entity<Lesson>().Property(e => e.RoomId).HasConversion(roomConverter);

        var classConverter = new ValueConverter<ClassId, Guid>(v => v.Value, v => new ClassId(v));
        modelBuilder.Entity<Lesson>().Property(e => e.ClassId).HasConversion(classConverter);

        var subjectConverter = new ValueConverter<SubjectId, Guid>(v => v.Value, v => new SubjectId(v));
        modelBuilder.Entity<Lesson>().Property(e => e.SubjectId).HasConversion(subjectConverter);

        var timetableConverter = new ValueConverter<TimetableId, Guid>(v => v.Value, v => new TimetableId(v));
        modelBuilder.Entity<Lesson>().Property(e => e.TimetableId).HasConversion(timetableConverter);

        // تحويل PeriodNumber
        var periodConverter = new ValueConverter<PeriodNumber, byte>(v => v.Value, v => PeriodNumber.Create(v));
        modelBuilder.Entity<Lesson>().Property(e => e.Period).HasConversion(periodConverter);
        modelBuilder.Entity<TeacherAvailability>().Property(e => e.Period).HasConversion(periodConverter);

        // تحويل Duration
        var durationConverter = new ValueConverter<Duration, int>(v => v.Value, v => Duration.FromMinutes(v));
        modelBuilder.Entity<Lesson>().Property(e => e.Duration).HasConversion(durationConverter);

        // تحويل DayOfWeek
        var dayConverter = new ValueConverter<DomainDayOfWeek, byte>(v => (byte)v, v => (DomainDayOfWeek)v);
        modelBuilder.Entity<Lesson>().Property(e => e.Day).HasConversion(dayConverter);
        modelBuilder.Entity<TeacherAvailability>().Property(e => e.Day).HasConversion(dayConverter);

        // تحويل LessonType
        var lessonTypeConverter = new ValueConverter<LessonType, byte>(v => (byte)v, v => (LessonType)v);
        modelBuilder.Entity<Lesson>().Property(e => e.Type).HasConversion(lessonTypeConverter);

        // تحويل TimetableStatus
        var statusConverter = new ValueConverter<TimetableStatus, byte>(v => (byte)v, v => (TimetableStatus)v);
        modelBuilder.Entity<Timetable>().Property(e => e.Status).HasConversion(statusConverter);

        // Constraints للكميات
        modelBuilder.Entity<Lesson>().Property(e => e.WeekNumber).HasDefaultValue(1);
        modelBuilder.Entity<Lesson>().Property(e => e.SortOrder).HasDefaultValue(0);
        modelBuilder.Entity<Lesson>().Property(e => e.IsFixed).HasDefaultValue(false);
        
        modelBuilder.Entity<Teacher>().Property(e => e.MaxPeriodsPerDay).HasDefaultValue(6);
        modelBuilder.Entity<Teacher>().Property(e => e.MaxPeriodsPerWeek).HasDefaultValue(30);
        modelBuilder.Entity<Teacher>().Property(e => e.CanTeachMultipleGrades).HasDefaultValue(true);
        modelBuilder.Entity<Teacher>().Property(e => e.IsActive).HasDefaultValue(true);
        
        modelBuilder.Entity<Subject>().Property(e => e.DefaultPeriodsPerWeek).HasDefaultValue(2);
        modelBuilder.Entity<Subject>().Property(e => e.MinPeriodsPerWeek).HasDefaultValue(1);
        modelBuilder.Entity<Subject>().Property(e => e.MaxPeriodsPerWeek).HasDefaultValue(5);
        modelBuilder.Entity<Subject>().Property(e => e.IsActive).HasDefaultValue(true);
        modelBuilder.Entity<Subject>().Property(e => e.SortOrder).HasDefaultValue(0);
        
        modelBuilder.Entity<Room>().Property(e => e.Capacity).HasDefaultValue(30);
        modelBuilder.Entity<Room>().Property(e => e.IsActive).HasDefaultValue(true);
        modelBuilder.Entity<Room>().Property(e => e.Floor).HasDefaultValue(0);
        modelBuilder.Entity<Room>().Property(e => e.IsShared).HasDefaultValue(false);
        
        modelBuilder.Entity<Class>().Property(e => e.StudentCount).HasDefaultValue(0);
        modelBuilder.Entity<Class>().Property(e => e.MaxStudents).HasDefaultValue(30);
        modelBuilder.Entity<Class>().Property(e => e.IsActive).HasDefaultValue(true);
        
        modelBuilder.Entity<Room>().Property(e => e.IsActive).HasDefaultValue(true);
        modelBuilder.Entity<TeacherAvailability>().Property(e => e.IsAvailable).HasDefaultValue(true);
        modelBuilder.Entity<TeacherAvailability>().Property(e => e.Priority).HasDefaultValue(AwadTimetable.Core.Enums.AvailabilityPriority.High);
        
        modelBuilder.Entity<Timetable>().Property(e => e.Status).HasDefaultValue(TimetableStatus.Draft);
        modelBuilder.Entity<Timetable>().Property(e => e.TermNumber).HasDefaultValue(1);
        modelBuilder.Entity<Timetable>().Property(e => e.TotalLessons).HasDefaultValue(0);
        modelBuilder.Entity<Timetable>().Property(e => e.TotalConflicts).HasDefaultValue(0);
    }
}

/// <summary>
/// مصنع سياق قاعدة البيانات وقت التصميم للـ Migrations
/// </summary>
public class TimetableDbContextFactory : IDesignTimeDbContextFactory<TimetableDbContext>
{
    public TimetableDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TimetableDbContext>();
        
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AwadTimetable", "awad_timetable.db");
        
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        
        return new TimetableDbContext(optionsBuilder.Options);
    }
}