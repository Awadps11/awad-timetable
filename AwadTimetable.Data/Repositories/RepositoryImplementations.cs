using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Core.Enums;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;
using AwadTimetable.Data.Repositories;

namespace AwadTimetable.Data.Repositories;

/// <summary>
/// تنفيذ مستودع عام
/// </summary>
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly TimetableDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(TimetableDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync(new object[] { id }, cancellationToken);
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
        SyncSchoolShadow(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Update(entity);
        SyncSchoolShadow(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>
    /// ضبط عمود الظل SchoolId1 (المفتاح الأجنبي للمدرسة) من خاصية SchoolId.
    /// EF ينشئ SchoolId1 تلقائياً لأن SchoolId كائن قيمة، وتركه فارغاً يسبب Foreign key constraint failed.
    /// </summary>
    private void SyncSchoolShadow(T entity)
    {
        try
        {
            var entry = _context.Entry(entity);
            var shadow = entry.Metadata.FindProperty("SchoolId1");
            if (shadow is null) return;

            var prop = typeof(T).GetProperty("SchoolId");
            if (prop?.GetValue(entity) is not SchoolId schoolId || schoolId.Value == Guid.Empty) return;

            entry.Property("SchoolId1").CurrentValue = schoolId.Value;
        }
        catch
        {
            // لا نُفشل الحفظ بسبب عدم وجود العمود الظلي
        }
    }

public virtual async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbSet.FindAsync(new object[] { id }, cancellationToken);
        if (entity != null)
        {
            _dbSet.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// مستودع توفر المعلم
    /// </summary>
    public class TeacherAvailabilityRepository : Repository<TeacherAvailability>, ITeacherAvailabilityRepository
    {
        public TeacherAvailabilityRepository(TimetableDbContext context) : base(context) { }

        public async Task<IReadOnlyList<TeacherAvailability>> GetByTeacherAsync(TeacherId teacherId, CancellationToken cancellationToken = default)
        {
            return await _dbSet.Where(ta => ta.TeacherId == teacherId).ToListAsync(cancellationToken);
        }

        public async Task<TeacherAvailability?> GetByTeacherDayPeriodAsync(TeacherId teacherId, DomainDayOfWeek day, PeriodNumber period, CancellationToken cancellationToken = default)
        {
            return await _dbSet.FirstOrDefaultAsync(ta => 
                ta.TeacherId == teacherId && ta.Day == day && ta.Period == period, cancellationToken);
        }

        public async Task<IReadOnlyList<TeacherAvailability>> GetByDayPeriodAsync(DomainDayOfWeek day, PeriodNumber period, CancellationToken cancellationToken = default)
        {
            return await _dbSet.Where(ta => ta.Day == day && ta.Period == period).ToListAsync(cancellationToken);
        }

        public async Task BulkUpsertAsync(IEnumerable<TeacherAvailability> availabilities, CancellationToken cancellationToken = default)
        {
            foreach (var availability in availabilities)
            {
                var existing = await _dbSet.FirstOrDefaultAsync(ta => 
                    ta.TeacherId == availability.TeacherId && 
                    ta.Day == availability.Day && 
                    ta.Period == availability.Period, cancellationToken);
                
                if (existing != null)
                {
                    existing.IsAvailable = availability.IsAvailable;
                }
                else
                {
                    await _dbSet.AddAsync(availability, cancellationToken);
                }
            }
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);
    }

    public virtual async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.CountAsync(cancellationToken);
    }
}

/// <summary>
/// مستودع المدارس
/// </summary>
public class SchoolRepository : Repository<School>, ISchoolRepository
{
    public SchoolRepository(TimetableDbContext context) : base(context) { }

    public async Task<School?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<School>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(s => s.IsActive).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// مستودع المعلمين
/// </summary>
public class TeacherRepository : Repository<Teacher>, ITeacherRepository
{
    public TeacherRepository(TimetableDbContext context) : base(context) { }

    public async Task<Teacher?> GetByEmployeeNumberAsync(string employeeNumber, SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(t => 
            t.EmployeeNumber == employeeNumber && t.SchoolId == schoolId, cancellationToken);
    }

    public async Task<IReadOnlyList<Teacher>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(t => t.SchoolId == schoolId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Teacher>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(t => t.SchoolId == schoolId && t.IsActive).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Teacher>> GetBySubjectAsync(SubjectId subjectId, CancellationToken cancellationToken = default)
    {
        return await _context.Teachers
            .Where(t => t.Subjects.Any(s => s.Id == subjectId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Teacher>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(t => t.IsActive).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// مستودع المواد
/// </summary>
public class SubjectRepository : Repository<Subject>, ISubjectRepository
{
    public SubjectRepository(TimetableDbContext context) : base(context) { }

    public async Task<Subject?> GetByNameAsync(string name, SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(s => 
            s.Name == name && s.SchoolId == schoolId, cancellationToken);
    }

    public async Task<IReadOnlyList<Subject>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(s => s.SchoolId == schoolId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Subject>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(s => s.SchoolId == schoolId && s.IsActive).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Subject>> GetByGradeAsync(int gradeLevel, SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(s => s.SchoolId == schoolId && s.GradeLevel == gradeLevel).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// مستودع الغرف
/// </summary>
public class RoomRepository : Repository<Room>, IRoomRepository
{
    public RoomRepository(TimetableDbContext context) : base(context) { }

    public async Task<Room?> GetByCodeAsync(string code, SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(r => 
            r.Code == code && r.SchoolId == schoolId, cancellationToken);
    }

    public async Task<IReadOnlyList<Room>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(r => r.SchoolId == schoolId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Room>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(r => r.SchoolId == schoolId && r.IsActive).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Room>> GetLabsAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(r => r.SchoolId == schoolId && r.IsLab).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Room>> GetByCapacityAsync(int minCapacity, SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(r => r.SchoolId == schoolId && r.Capacity >= minCapacity).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// مستودع الفصول
/// </summary>
public class ClassRepository : Repository<Class>, IClassRepository
{
    public ClassRepository(TimetableDbContext context) : base(context) { }

    public async Task<Class?> GetByCodeAsync(string code, SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(c => 
            c.Code == code && c.SchoolId == schoolId, cancellationToken);
    }

    public async Task<IReadOnlyList<Class>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(c => c.SchoolId == schoolId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Class>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(c => c.SchoolId == schoolId && c.IsActive).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Class>> GetByGradeAsync(int gradeLevel, SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(c => c.SchoolId == schoolId && c.GradeLevel == gradeLevel).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// مستودع الحصص
/// </summary>
public class LessonRepository : Repository<Lesson>, ILessonRepository
{
    public LessonRepository(TimetableDbContext context) : base(context) { }

    public async Task<IReadOnlyList<Lesson>> GetByTimetableAsync(TimetableId timetableId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(l => l.TimetableId == timetableId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lesson>> GetByTeacherAsync(TeacherId teacherId, TimetableId timetableId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(l => l.TeacherId == teacherId && l.TimetableId == timetableId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lesson>> GetByClassAsync(ClassId classId, TimetableId timetableId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(l => l.ClassId == classId && l.TimetableId == timetableId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lesson>> GetBySubjectAsync(SubjectId subjectId, TimetableId timetableId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(l => l.SubjectId == subjectId && l.TimetableId == timetableId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lesson>> GetByDayAndPeriodAsync(DomainDayOfWeek day, PeriodNumber period, TimetableId timetableId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(l => 
            l.Day == day && l.Period == period && l.TimetableId == timetableId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Lesson>> GetByRoomAsync(RoomId roomId, TimetableId timetableId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(l => l.RoomId == roomId && l.TimetableId == timetableId).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// مستودع الجداول
/// </summary>
public class TimetableRepository : Repository<Timetable>, ITimetableRepository
{
    public TimetableRepository(TimetableDbContext context) : base(context) { }

    public async Task<Timetable?> GetByNameAsync(string name, SchoolId schoolId, int academicYearStart, int termNumber, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(t => 
            t.Name == name && 
            t.SchoolId == schoolId && 
            t.AcademicYearStart == academicYearStart && 
            t.TermNumber == termNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<Timetable>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(t => t.SchoolId == schoolId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Timetable>> GetBySchoolAndYearAsync(SchoolId schoolId, int academicYearStart, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(t => t.SchoolId == schoolId && t.AcademicYearStart == academicYearStart).ToListAsync(cancellationToken);
    }

    public async Task<Timetable?> GetActiveTimetableAsync(SchoolId schoolId, int academicYearStart, int termNumber, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(t => 
            t.SchoolId == schoolId && 
            t.AcademicYearStart == academicYearStart && 
            t.TermNumber == termNumber &&
            t.Status == TimetableStatus.Published, cancellationToken);
    }
}