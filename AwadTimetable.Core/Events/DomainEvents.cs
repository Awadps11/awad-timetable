using System;

namespace AwadTimetable.Core.Events;

/// <summary>
/// واجهة الأحداث النطاقية (Domain Events)
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredOn { get; }
    Guid EventId { get; }
}

/// <summary>
/// حدث نطاق أساسي
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>
/// أحداث الكيانات
/// </summary>
public static class EntityEvents
{
    public sealed record EntityCreated<T>(T Entity) : DomainEvent where T : class;
    public sealed record EntityUpdated<T>(T Entity) : DomainEvent where T : class;
    public sealed record EntityDeleted<T>(T Entity) : DomainEvent where T : class;
}

/// <summary>
/// أحداث الجدولة
/// </summary>
public static class TimetableEvents
{
    public sealed record TimetableGenerated(Guid TimetableId, int LessonsCount, TimeSpan Duration) : DomainEvent;
    public sealed record TimetablePublished(Guid TimetableId, DateTime PublishedAt) : DomainEvent;
    public sealed record TimetableArchived(Guid TimetableId, DateTime ArchivedAt) : DomainEvent;
    public sealed record ConflictDetected(Guid TimetableId, string ConflictType, string Description) : DomainEvent;
    public sealed record ConflictResolved(Guid TimetableId, string ConflictType, string Resolution) : DomainEvent;
}

/// <summary>
/// أحداث الكيانات الأساسية
/// </summary>
public static class EntitySpecificEvents
{
    public sealed record TeacherAdded(Guid TeacherId, string Name) : DomainEvent;
    public sealed record TeacherUpdated(Guid TeacherId, string Name) : DomainEvent;
    public sealed record TeacherRemoved(Guid TeacherId) : DomainEvent;

    public sealed record RoomAdded(Guid RoomId, string Name) : DomainEvent;
    public sealed record RoomUpdated(Guid RoomId, string Name) : DomainEvent;
    public sealed record RoomRemoved(Guid RoomId) : DomainEvent;

    public sealed record ClassAdded(Guid ClassId, string Name) : DomainEvent;
    public sealed record ClassUpdated(Guid ClassId, string Name) : DomainEvent;
    public sealed record ClassRemoved(Guid ClassId) : DomainEvent;

    public sealed record SubjectAdded(Guid SubjectId, string Name) : DomainEvent;
    public sealed record SubjectUpdated(Guid SubjectId, string Name) : DomainEvent;
    public sealed record SubjectRemoved(Guid SubjectId) : DomainEvent;

    public sealed record LessonAdded(Guid LessonId, string Description) : DomainEvent;
    public sealed record LessonUpdated(Guid LessonId, string Description) : DomainEvent;
    public sealed record LessonRemoved(Guid LessonId) : DomainEvent;

    public sealed record ConstraintAdded(Guid ConstraintId, string Type) : DomainEvent;
    public sealed record ConstraintUpdated(Guid ConstraintId, string Type) : DomainEvent;
    public sealed record ConstraintRemoved(Guid ConstraintId) : DomainEvent;
}