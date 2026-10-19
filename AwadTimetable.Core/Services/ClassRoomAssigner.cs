using System;
using System.Collections.Generic;
using System.Linq;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;

namespace AwadTimetable.Core.Services;

/// <summary>
/// إسناد القاعات عند التوليد (فقط إذا كانت هناك قاعات مُعرَّفة):
/// 1) قاعة المعلم (Room.AssignedTeacherId) لحصص ذلك المعلم.
/// 2) قاعة الفصل المخصّصة (Class.DefaultRoom ثم إسناد جشع لقاعة مخصّصة لكل شعبة).
/// 3) القاعات المشتركة (Room.IsShared) كاحتياط للشعب التي لم تحصل على قاعة، وبشرط كفاية عددها
///    (قاعة مختلفة لكل شعبة حتى لا يُنشئ إسنادٌ متعارض استحالة في المحرك).
/// لا يمسّ الحصص التي لها قاعة محددة مسبقاً.
/// </summary>
public static class ClassRoomAssigner
{
    public static int Assign(Timetable timetable, IReadOnlyCollection<Room> rooms, IReadOnlyCollection<Class> classes)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        if (rooms is null || rooms.Count == 0) return 0;

        var roomsById = rooms.GroupBy(r => (Guid)r.Id).ToDictionary(g => g.Key, g => g.First());
        var classById = (classes ?? Array.Empty<Class>()).GroupBy(c => (Guid)c.Id).ToDictionary(g => g.Key, g => g.First());

        // قاعة كل معلم (إن حُدّدت)
        var teacherRoom = new Dictionary<Guid, Guid>();
        foreach (var room in roomsById.Values.Where(r => r.AssignedTeacherId is not null))
        {
            var teacherId = room.AssignedTeacherId!.Value;
            if (!teacherRoom.ContainsKey(teacherId)) teacherRoom[teacherId] = (Guid)room.Id;
        }

        var dedicated = roomsById.Values.Where(r => !r.IsShared && r.AssignedTeacherId is null).ToList();
        var shared = roomsById.Values.Where(r => r.IsShared).OrderBy(r => r.Capacity).ToList();

        var assigned = 0;

        // (1) قاعة المعلم
        foreach (var lesson in timetable.Lessons)
        {
            if (HasRoom(lesson)) continue;
            if (teacherRoom.TryGetValue((Guid)lesson.TeacherId, out var rid))
            {
                lesson.AssignRoom((RoomId)rid);
                assigned++;
            }
        }

        // (2) قاعة الفصل: القاعات المخصّصة الصريحة ثم الجشع
        var usedDedicated = new HashSet<Guid>();
        var classRoom = new Dictionary<Guid, Guid>();

        foreach (var cls in classById.Values)
        {
            if (cls.DefaultRoomId is { } def && roomsById.ContainsKey(def.Value) && usedDedicated.Add(def.Value))
                classRoom[cls.Id] = def.Value;
        }

        var needingClass = timetable.Lessons
            .Select(l => (Guid)l.ClassId)
            .Distinct()
            .Where(id => !classRoom.ContainsKey(id) && classById.ContainsKey(id))
            .ToList();

        foreach (var classId in needingClass)
        {
            var studentCount = classById[classId].StudentCount;
            var candidate = dedicated
                .Where(r => !usedDedicated.Contains((Guid)r.Id) && r.Capacity >= studentCount)
                .OrderBy(r => IsSpecial(r) ? 1 : 0)
                .ThenBy(r => r.Capacity)
                .FirstOrDefault();

            if (candidate is null) continue;
            usedDedicated.Add((Guid)candidate.Id);
            classRoom[classId] = (Guid)candidate.Id;
        }

        // (3) القاعات المشتركة كاحتياط (قاعة مختلفة لكل شعبة عند كفايتها فقط)
        var stillNeedingClass = needingClass.Where(id => !classRoom.ContainsKey(id)).ToList();
        if (stillNeedingClass.Count > 0 && stillNeedingClass.Count <= shared.Count)
        {
            for (var i = 0; i < stillNeedingClass.Count; i++)
                classRoom[stillNeedingClass[i]] = (Guid)shared[i].Id;
        }

        foreach (var lesson in timetable.Lessons)
        {
            if (HasRoom(lesson)) continue;
            if (classRoom.TryGetValue((Guid)lesson.ClassId, out var rid))
            {
                lesson.AssignRoom((RoomId)rid);
                assigned++;
            }
        }

        return assigned;
    }

    private static bool HasRoom(Lesson lesson)
        => lesson.RoomId is { } room && room != RoomId.Empty;

    private static bool IsSpecial(Room room)
        => room.IsLab || room.IsLibrary || room.IsGym || room.IsArtRoom || room.IsMusicRoom;
}
