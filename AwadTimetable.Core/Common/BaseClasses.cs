using System;
using System.Collections.Generic;
using AwadTimetable.Core.Events;

namespace AwadTimetable.Core.Common;

/// <summary>
/// الفئة الأساسية للكيانات مع معرف قوي النوع
/// </summary>
/// <typeparam name="TId">نوع المعرف</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity() { }

    protected Entity(TId id)
    {
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Entity<TId>);
    }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode()
    {
        return EqualityComparer<TId>.Default.GetHashCode(Id);
    }

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        return left?.Equals(right) ?? right is null;
    }

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right)
    {
        return !(left == right);
    }

    public override string ToString()
    {
        return $"{GetType().Name} [Id={Id}]";
    }
}

/// <summary>
/// الفئة الأساسية للكيانات ذات المعرف من نوع Guid
/// </summary>
public abstract class Entity : Entity<Guid>
{
    protected Entity() : base(Guid.NewGuid()) { }

    protected Entity(Guid id) : base(id) { }
}

/// <summary>
/// الفئة الأساسية للقيم (Value Objects) - غير قابلة للتغيير
/// </summary>
/// <typeparam name="T">نوع القيمة</typeparam>
public record ValueObject<T>(T Value) where T : notnull
{
    public static implicit operator T(ValueObject<T> vo) => vo.Value;
    public static explicit operator ValueObject<T>(T value) => Create(value);

    public static ValueObject<T> Create(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ValueObject<T>(value);
    }
}

/// <summary>
/// الفئة الأساسية للتجمعات (Aggregates)
/// </summary>
/// <typeparam name="TId">نوع المعرف</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected AggregateRoot() { }
    protected AggregateRoot(TId id) : base(id) { }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

/// <summary>
/// الفئة الأساسية للتجمعات ذات المعرف Guid
/// </summary>
public abstract class AggregateRoot : AggregateRoot<Guid>
{
    protected AggregateRoot() : base() { }
    protected AggregateRoot(Guid id) : base(id) { }
}