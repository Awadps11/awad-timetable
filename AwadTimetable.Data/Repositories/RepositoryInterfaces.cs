using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Data.Repositories;

/// <summary>
/// واجهة مستودع عام
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع المدارس
/// </summary>
public interface ISchoolRepository : IRepository<School>
{
    Task<School?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<School>> GetActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع المعلمين
/// </summary>
public interface ITeacherRepository : IRepository<Teacher>
{
    Task<Teacher?> GetByEmployeeNumberAsync(string employeeNumber, SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Teacher>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Teacher>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Teacher>> GetBySubjectAsync(SubjectId subjectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Teacher>> GetActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع المواد
/// </summary>
public interface ISubjectRepository : IRepository<Subject>
{
    Task<Subject?> GetByNameAsync(string name, SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subject>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subject>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subject>> GetByGradeAsync(int gradeLevel, SchoolId schoolId, CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع الغرف
/// </summary>
public interface IRoomRepository : IRepository<Room>
{
    Task<Room?> GetByCodeAsync(string code, SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Room>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Room>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Room>> GetLabsAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Room>> GetByCapacityAsync(int minCapacity, SchoolId schoolId, CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع الفصول
/// </summary>
public interface IClassRepository : IRepository<Class>
{
    Task<Class?> GetByCodeAsync(string code, SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Class>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Class>> GetActiveBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Class>> GetByGradeAsync(int gradeLevel, SchoolId schoolId, CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع الحصص
/// </summary>
public interface ILessonRepository : IRepository<Lesson>
{
    Task<IReadOnlyList<Lesson>> GetByTimetableAsync(TimetableId timetableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lesson>> GetByTeacherAsync(TeacherId teacherId, TimetableId timetableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lesson>> GetByClassAsync(ClassId classId, TimetableId timetableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lesson>> GetBySubjectAsync(SubjectId subjectId, TimetableId timetableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lesson>> GetByDayAndPeriodAsync(DomainDayOfWeek day, PeriodNumber period, TimetableId timetableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lesson>> GetByRoomAsync(RoomId roomId, TimetableId timetableId, CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع الجداول
/// </summary>
public interface ITimetableRepository : IRepository<Timetable>
{
    Task<Timetable?> GetByNameAsync(string name, SchoolId schoolId, int academicYearStart, int termNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Timetable>> GetBySchoolAsync(SchoolId schoolId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Timetable>> GetBySchoolAndYearAsync(SchoolId schoolId, int academicYearStart, CancellationToken cancellationToken = default);
    Task<Timetable?> GetActiveTimetableAsync(SchoolId schoolId, int academicYearStart, int termNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// مستودع توفر المعلم
/// </summary>
public interface ITeacherAvailabilityRepository : IRepository<TeacherAvailability>
{
    Task<IReadOnlyList<TeacherAvailability>> GetByTeacherAsync(TeacherId teacherId, CancellationToken cancellationToken = default);
    Task<TeacherAvailability?> GetByTeacherDayPeriodAsync(TeacherId teacherId, DomainDayOfWeek day, PeriodNumber period, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeacherAvailability>> GetByDayPeriodAsync(DomainDayOfWeek day, PeriodNumber period, CancellationToken cancellationToken = default);
    Task BulkUpsertAsync(IEnumerable<TeacherAvailability> availabilities, CancellationToken cancellationToken = default);
}