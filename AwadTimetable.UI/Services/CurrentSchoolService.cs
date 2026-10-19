using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Data.Repositories;

namespace AwadTimetable.UI.Services;

/// <summary>
/// خدمة المدرسة الحالية - تضمن وجود مدرسة واحدة وتعطي معرّفها لكل الكيانات
/// </summary>
public sealed class CurrentSchoolService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CurrentSchoolService> _logger;
    private SchoolId? _cachedId;
    private readonly object _lock = new();

    /// <summary>مساحة العمل الحالية (null إن لم تُحدَّد بعد).</summary>
    public SchoolId? Current
    {
        get { lock (_lock) { return _cachedId; } }
    }

    public CurrentSchoolService(IServiceScopeFactory scopeFactory, ILogger<CurrentSchoolService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SchoolId> GetSchoolIdAsync()
    {
        lock (_lock)
        {
            if (_cachedId is not null) return _cachedId;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISchoolRepository>();
            var schools = await repo.GetActiveAsync();
            _logger.LogInformation("تم العثور على {Count} مدرسة نشطة", schools.Count);

            var school = schools.Count > 0 ? schools[0] : null;
            if (school == null)
            {
                school = await repo.AddAsync(new School("مدرستي", "مدرستي"));
                _logger.LogInformation("تم إنشاء مدرسة جديدة: {Id}", school.Id);
            }

            var id = (SchoolId)school.Id;
            lock (_lock)
            {
                _cachedId = id;
            }
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في الحصول على معرّف المدرسة");
            throw;
        }
    }

    /// <summary>يعيّن مساحة العمل الحالية (مدرسة الجدول المفتوح/المُنشأ).</summary>
    public void SetCurrent(SchoolId schoolId)
    {
        lock (_lock)
        {
            _cachedId = schoolId;
        }
        _logger.LogInformation("مساحة العمل الحالية: {SchoolId}", schoolId.Value);
    }

    /// <summary>يمسح مساحة العمل الحالية (عند حذف الجدول المفتوح).</summary>
    public void ClearCurrent()
    {
        lock (_lock)
        {
            _cachedId = null;
        }
    }

    /// <summary>
    /// ينشئ مساحة عمل جديدة فارغة تماماً باسم المدرسة المطلوب (بدون أي معلمين/مواد/فصول).
    /// تُستخدم عند إنشاء جدول جديد حتى لا تظهر بيانات جداول أخرى.
    /// </summary>
    public async Task<SchoolId> CreateEmptyWorkspaceAsync(string name)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "مدرستي" : name.Trim();

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchoolRepository>();
        var school = await repo.AddAsync(new School(safeName, safeName));

        var id = (SchoolId)school.Id;
        SetCurrent(id);
        _logger.LogInformation("أُنشئت مساحة عمل فارغة: {Name} ({Id})", safeName, id.Value);
        return id;
    }

    /// <summary>يعيد مساحة عمل موجودة بالاسم (لاستخدام بياناتها) أو ينشئها إن لم توجد.</summary>
    public async Task<SchoolId> GetOrCreateWorkspaceByNameAsync(string name)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "مدرستي" : name.Trim();

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchoolRepository>();
        var school = await repo.GetByNameAsync(safeName);
        if (school is null)
        {
            school = await repo.AddAsync(new School(safeName, safeName));
            _logger.LogInformation("أُنشئت مساحة عمل جديدة: {Name} ({Id})", safeName, school.Id);
        }

        var id = (SchoolId)school.Id;
        SetCurrent(id);
        return id;
    }
}
