using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Google.OrTools.Sat;
using AwadTimetable.Core.Entities;
using AwadTimetable.Core.ValueObjects;
using AwadTimetable.Core.Services;
using AwadTimetable.Core.Constraints;

using DomainDayOfWeek = AwadTimetable.Core.Enums.DayOfWeek;

namespace AwadTimetable.Solver;

/// <summary>
/// محول CP-SAT من OR-Tools: يبني نموذجًا حقيقيًا، يطبّق الحل على الجدول نفسه،
/// ويقيس النتيجة بالتحقق الفعلي من القيود.
/// </summary>
public class CpSatTimetableSolver : ITimetableGenerator
{
    private const int MaxPeriod = 12;
    private const int ConsecutiveWindowSize = 3;
    private const int DefaultMinDaysPerWeek = 3;

    private readonly ILogger<CpSatTimetableSolver> _logger;
    private readonly IConstraintValidator _validator;

    /// <summary>بوابة تمنع تشغيل توليدين/تحسينين متزامنين على نفس الجدول (الواجهة + أدوات المساعد).</summary>
    private static readonly SemaphoreSlim GenerationGate = new(1, 1);

    public CpSatTimetableSolver(ILogger<CpSatTimetableSolver> logger, IConstraintValidator validator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<GenerationResult> GenerateAsync(
        Timetable timetable,
        GenerationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            await GenerationGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return GenerationResult.Failure("تم إلغاء توليد الجدول من قبل المستخدم.");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("بدء توليد الجدول: {TimetableName}", timetable.Name);

            EnsureGradeLevels(timetable);

            if (timetable.Lessons.Count == 0)
                return GenerationResult.Failure("لا توجد حصص في الجدول — أضف الحصص أولًا ثم أعد التوليد.");

            // حصة مثبّتة بلا فترة صالحة تُفسد المتغيّرات لاحقاً
            var badFixed = timetable.Lessons.FirstOrDefault(l => l.IsFixed && l.Period.Value < 1);
            if (badFixed is not null)
                return GenerationResult.Failure(
                    $"حصة مثبّتة بلا فترة صالحة: {badFixed.GetDisplayName()}. حدّد لها فترة أو ألغِ تثبيتها.");

            if (options.RespectFixedLessons)
            {
                var fixedConflicts = FindFixedLessonConflicts(timetable);
                if (fixedConflicts.Count > 0)
                    return GenerationResult.Failure(
                        "تعذر التوليد لأن الحصص الثابتة المحددة متعارضة:\n" + string.Join("\n", fixedConflicts));
            }

            var rules = RuleCompiler.Compile(timetable);
            var data = BuildVariables(timetable, options, rules);
            var warnings = CollectWarnings(timetable);

            if (data.Unplaceable.Count > 0)
            {
                _logger.LogWarning("حصص لا تملك أي فترة متاحة: {Count}", data.Unplaceable.Count);

                if (!options.AllowPartialSolution)
                    return GenerationResult.Failure(BuildUnplaceableMessage(data));

                warnings.AddRange(data.Unplaceable.Select(l =>
                    $"تعذر جدولة: {l.GetDisplayName()} — {data.UnplaceableReasons[l.Id]}"));
            }

            // فحص الجدوى: تجاوز سعة الفصل أو المعلم بوضوح يمنع الحل ويستنفد المهلة بلا فائدة
            if (!options.AllowPartialSolution)
            {
                var capacityIssues = DiagnoseCapacity(timetable, data);
                if (capacityIssues.Count > 0)
                {
                    _logger.LogWarning("فشل فحص الجدوى: {Count} مشكلة سعة", capacityIssues.Count);
                    return GenerationResult.Failure(
                        "تعذر التوليد — السعة غير كافية:\n" + string.Join("\n", capacityIssues.Take(15)));
                }
            }

            var model = data.Model;
            AddHardConstraints(model, data, timetable, rules);

            var objective = BuildObjective(model, data, timetable, rules);
            if (objective is not null)
                model.Minimize(objective);

            progress?.Report($"تم بناء النموذج: {data.Vars.Count} متغير، {data.LessonVars.Count} حصة قابلة للجدولة");

            // بحث أفضل-من-ن: نجرّب عدة بذور داخل المهلة ونختار الأفضل بمقياس المدقق الفعلي
            var attempt = await SolveBestByScoreAsync(
                model, data, timetable, options, cancellationToken, progress);

            {
                if (attempt is null)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("تم إلغاء توليد الجدول من قبل المستخدم");
                        return GenerationResult.Failure("تم إلغاء توليد الجدول من قبل المستخدم.");
                    }

                    stopwatch.Stop();
                    _logger.LogWarning("لم يُرجِع المحلل أي محاولة (attempt = null)");
                    return GenerationResult.Failure(BuildFailureMessage(timetable, data, CpSolverStatus.Unknown, warnings));
                }

                if (attempt.Validation is null)
                {
                    stopwatch.Stop();
                    _logger.LogWarning("فشل في إيجاد حل: {Status}", attempt.Status);
                    return GenerationResult.Failure(BuildFailureMessage(timetable, data, attempt.Status, warnings));
                }

                var validation = attempt.Validation;

                // لا نُعلن النجاح مع مخالفات صلبة (إلا في وضع الإرخاء الصريح)
                if (validation.ConflictsCount > 0 && !options.AllowPartialSolution)
                {
                    stopwatch.Stop();
                    _logger.LogWarning("حل غير صالح: {Conflicts} مخالفة صلبة", validation.ConflictsCount);
                    return GenerationResult.Failure(
                        $"تعذر توليد جدول صالح: {validation.ConflictsCount} مخالفة قيود صلبة.\n" +
                        string.Join("\n", validation.Violations
                            .Where(v => v.Type == AwadTimetable.Core.Enums.ConstraintType.Hard)
                            .Take(10)
                            .Select(v => "• " + v.Message)));
                }

                timetable.UpdateStatistics(
                    timetable.Lessons.Count, validation.ConflictsCount, validation.QualityScore);
                timetable.Generate();

                stopwatch.Stop();
                _logger.LogInformation(
                    "تم توليد الجدول في {Duration}ms — مجدولة: {Scheduled}، فاشلة: {Failed}، انتهاكات: {Conflicts}، الدرجة: {Score}، محاولات: {Attempts}",
                    stopwatch.ElapsedMilliseconds,
                    data.LessonVars.Count,
                    data.Unplaceable.Count,
                    validation.ConflictsCount,
                    validation.QualityScore.Value,
                    attempt.Attempts);

                return new GenerationResult
                {
                    Success = true,
                    Timetable = timetable,
                    LessonsScheduled = data.LessonVars.Count,
                    LessonsFailed = data.Unplaceable.Count,
                    ConflictsCount = validation.ConflictsCount,
                    QualityScore = validation.QualityScore,
                    Duration = stopwatch.Elapsed,
                    Iterations = attempt.Solutions,
                    Violations = validation.Violations,
                    Warnings = warnings
                };
            }
        }
        catch (OperationCanceledException)
        {
            return GenerationResult.Failure("تم إلغاء توليد الجدول من قبل المستخدم.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في توليد الجدول");
            return GenerationResult.Failure($"خطأ غير متوقع: {ex.Message}");
        }
        finally
        {
            GenerationGate.Release();
        }
    }

    public async Task<OptimizationResult> OptimizeAsync(
        Timetable timetable,
        OptimizationOptions options,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            await GenerationGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OptimizationResult.NoImprovement(Score.Zero, TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("بدء تحسين الجدول: {TimetableName}", timetable.Name);
            EnsureGradeLevels(timetable);

            if (timetable.Lessons.Count == 0)
                return OptimizationResult.NoImprovement(Score.Zero, stopwatch.Elapsed);

            var before = _validator.Validate(TimetableContextFactory.Build(timetable));

            var generationOptions = new GenerationOptions
            {
                TimeLimit = options.TimeLimit,
                RandomSeed = Environment.TickCount,
                UseParallel = true,
                RespectFixedLessons = true,
                AllowPartialSolution = false,
                MaxIterations = options.MaxIterations
            };

            var rules = RuleCompiler.Compile(timetable);
            var data = BuildVariables(timetable, generationOptions, rules);
            if (data.Unplaceable.Count > 0 || data.LessonVars.Count == 0)
            {
                stopwatch.Stop();
                return OptimizationResult.NoImprovement(before.QualityScore, stopwatch.Elapsed);
            }

            var model = data.Model;
            AddHardConstraints(model, data, timetable, rules);

            // نقطة الانطلاق: الحل الحالي كتلميح لـ CP-SAT حتى يحسّنه بدل إعادة بنائه من الصفر
            HintCurrentAssignment(model, data, timetable);

            var objective = BuildObjective(model, data, timetable, rules);
            if (objective is not null)
                model.Minimize(objective);

            progress?.Report($"تم بناء نموذج التحسين: {data.Vars.Count} متغير");

            var outcome = await SolveAsync(
                model, options.TimeLimit, generationOptions.RandomSeed, true, cancellationToken, progress);

            {
                stopwatch.Stop();

                if (outcome.Cancelled ||
                    outcome.Status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
                {
                    return OptimizationResult.NoImprovement(before.QualityScore, stopwatch.Elapsed);
                }

                // لقطة للحل الحالي: إن كان الجديد أسوأ نرجع الجدول كما كان
                var snapshot = timetable.Lessons
                    .Select(l => (lesson: l, day: l.Day, period: l.Period))
                    .ToList();

                ApplySolution(outcome.Solver!, data, timetable);

                var after = _validator.Validate(TimetableContextFactory.Build(timetable));
                var oldScore = before.QualityScore.Value;
                var newScore = after.QualityScore.Value;
                var isBetter = newScore > oldScore || after.ConflictsCount < before.ConflictsCount;
                var isWorse = newScore < oldScore || after.ConflictsCount > before.ConflictsCount;

                if (isWorse && !isBetter)
                {
                    foreach (var item in snapshot)
                        item.lesson.Reschedule(item.day, item.period);

                    _logger.LogInformation(
                        "النتيجة الجديدة أسوأ ({New:F1} مقابل {Old:F1}) — أُلغي التحسين",
                        newScore, oldScore);
                    return OptimizationResult.NoImprovement(before.QualityScore, stopwatch.Elapsed);
                }

                timetable.UpdateStatistics(
                    timetable.Lessons.Count, after.ConflictsCount, after.QualityScore);

                return new OptimizationResult
                {
                    Improved = isBetter,
                    OldScore = before.QualityScore,
                    NewScore = after.QualityScore,
                    ImprovementPercentage = oldScore > 0 ? (newScore - oldScore) / oldScore * 100.0 : 0,
                    ConflictsReduced = before.ConflictsCount - after.ConflictsCount,
                    Duration = stopwatch.Elapsed,
                    Iterations = outcome.Solutions,
                    RemainingViolations = after.Violations
                };
            }
        }
        catch (OperationCanceledException)
        {
            return OptimizationResult.NoImprovement(Score.Zero, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطأ في تحسين الجدول");
            return OptimizationResult.NoImprovement(Score.Zero, stopwatch.Elapsed);
        }
        finally
        {
            GenerationGate.Release();
        }
    }

    public async Task<ValidationResult> ValidateAsync(
        Timetable timetable,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(timetable);

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        EnsureGradeLevels(timetable);

        var context = TimetableContextFactory.Build(timetable);
        progress?.Report($"جارٍ التحقق من {context.Lessons.Count} حصة…");

        var result = await _validator.ValidateAsync(context, null, cancellationToken);

        progress?.Report(result.IsValid && result.ConflictsCount == 0
            ? "لا توجد انتهاكات في الجدول"
            : $"{result.ConflictsCount} انتهاك — الدرجة {result.QualityScore.Value:F0}/100");

        return result;
    }

    // --------------------------------------------------------------------
    // بناء المتغيرات
    // --------------------------------------------------------------------

    private static void EnsureGradeLevels(Timetable timetable)
    {
        foreach (var lesson in timetable.Lessons)
        {
            if (lesson.Class is { } cls)
                timetable.RegisterClass((Guid)lesson.ClassId, cls.Name, cls.GradeLevel);
        }
    }

    /// <summary>هل يمكن جدولة حصة في (اليوم، الفترة)؟ أيام العمل من StageConfigs والفترات المفتوحة من PeriodTimes</summary>
    private static bool IsSlotAllowed(Timetable timetable, string? stage, DomainDayOfWeek day, int period)
    {
        var cfg = FindStageConfig(timetable, stage);

        if (cfg is { WorkDays.Count: > 0 })
        {
            if (!cfg.WorkDays.Contains((int)day)) return false;
        }
        else if (day == DomainDayOfWeek.Friday)
        {
            // لا جمعة ما لم تُحدَّد صراحة في أيام العمل أو في توقيت الفترات
            if (timetable.PeriodTimes.Count == 0 ||
                !timetable.PeriodTimes.Any(pt => pt.Period == period && pt.Day == (int)day))
                return false;
        }

        if (timetable.PeriodTimes.Count > 0 && !timetable.HasPeriodTime(period, (int)day, stage))
            return false;

        // فترات الاستراحة (مثل الغداء) لا تُجدَّل فيها حصص
        if (timetable.IsBreakPeriod(period, (int)day, stage))
            return false;

        return true;
    }

    private static StageScheduleConfig? FindStageConfig(Timetable timetable, string? stage)
        => stage is null ? null : timetable.StageConfigs.FirstOrDefault(c => c.Stage == stage);

    private static List<DomainDayOfWeek> ActiveDays(Timetable timetable, string? stage)
    {
        var cfg = FindStageConfig(timetable, stage);
        var all = Enum.GetValues<DomainDayOfWeek>();

        if (cfg is { WorkDays.Count: > 0 })
            return all.Where(d => cfg.WorkDays.Contains((int)d)).ToList();

        return all.Where(d => d != DomainDayOfWeek.Friday).ToList();
    }

    private static int CountAllowedPeriods(Timetable timetable, string? stage, DomainDayOfWeek day)
    {
        var n = 0;
        for (var p = 1; p <= MaxPeriod; p++)
            if (IsSlotAllowed(timetable, stage, day, p))
                n++;
        return n;
    }

    private static int CountGenericPeriods(Timetable timetable, DomainDayOfWeek day)
    {
        if (timetable.PeriodTimes.Count == 0) return MaxPeriod;

        var n = 0;
        for (var p = 1; p <= MaxPeriod; p++)
            if (timetable.PeriodTimes.Any(pt => pt.Period == p && (pt.Day == (int)day || pt.Day == -1)))
                n++;
        return n;
    }

    private ModelData BuildVariables(Timetable timetable, GenerationOptions options, RuleProgram rules)
    {
        var data = new ModelData();
        var model = data.Model;

        foreach (var avail in timetable.TeacherAvailabilities)
        {
            if (avail.IsAvailable)
            {
                data.Declared.Add((avail.TeacherId, avail.Day, avail.Period.Value));
                data.DeclaredTeachers.Add(avail.TeacherId);
            }
            else if (avail.IsHardBlock)
            {
                // إغلاق ملزم: يُستبعد من المواضع المسموحة
                data.Unavailable.Add((avail.TeacherId, avail.Day, avail.Period.Value));
            }
            else if (avail.IsSoftBlock)
            {
                // إغلاق منخفض الأولوية: مسموح لكن يُعاقَب في دالة الهدف
                data.SoftBlocked.Add((avail.TeacherId, avail.Day, avail.Period.Value));
            }
        }

        foreach (var lesson in timetable.Lessons)
        {
            data.LessonsByClass.GetOrAdd(lesson.ClassId).Add(lesson);
            data.LessonsByTeacher.GetOrAdd(lesson.TeacherId).Add(lesson);
            data.LessonsByClassSubject.GetOrAdd(((Guid)lesson.ClassId, (Guid)lesson.SubjectId)).Add(lesson);

            if (lesson.RoomId is not null && lesson.RoomId != RoomId.Empty)
                data.LessonsByRoom.GetOrAdd(lesson.RoomId).Add(lesson);

            var stage = timetable.GetStageForClass(lesson.ClassId);
            var allowed = BuildAllowedSlots(timetable, options, rules, data, lesson, stage);

            // السلامة: إن لم تُعرَّف أي فترة لمرحلة الفصل (خلط مراحل أو أرقام صفوف متكررة) نجرّب باقي مراحل الجدول
            if (allowed.Count == 0 && !(options.RespectFixedLessons && lesson.IsFixed))
            {
                foreach (var alt in timetable.StagesInUse())
                {
                    if (string.Equals(alt, stage, StringComparison.Ordinal)) continue;
                    var altAllowed = BuildAllowedSlots(timetable, options, rules, data, lesson, alt);
                    if (altAllowed.Count > 0)
                    {
                        allowed = altAllowed;
                        stage = alt;
                        break;
                    }
                }
            }

            if (allowed.Count == 0)
            {
                data.Unplaceable.Add(lesson);
                data.UnplaceableReasons[lesson.Id] = ExplainUnplaceable(timetable, data, lesson, stage);
                continue;
            }

            var lessonVars = data.LessonVars.GetOrAdd(lesson.Id);

            foreach (var (day, period) in allowed)
            {
                var v = model.NewBoolVar($"l_{lesson.Id:N}_{(int)day}_{period}");

                data.Vars[(lesson.Id, day, period)] = v;
                lessonVars.Add((day, period, v));
                data.ClassDayVars.GetOrAdd(((Guid)lesson.ClassId, day)).Add((period, v));
                data.TeacherDayVars.GetOrAdd((lesson.TeacherId, day)).Add((period, v));

                // زمن الفترة الفعلي لحساب تضارب المعلم المشترك بين المراحل بتوقيتات مختلفة
                if (timetable.TryGetPeriodInterval(period, (int)day, stage, out var slotStart, out var slotEnd))
                    data.TeacherSlotTimes.GetOrAdd(lesson.TeacherId.Value).Add((day, slotStart, slotEnd, v));
            }
        }

        return data;
    }

    /// <summary>المواضع (يوم، فترة) المسموحة لحصة في مرحلة معيّنة.</summary>
    private static List<(DomainDayOfWeek day, int period)> BuildAllowedSlots(
        Timetable timetable, GenerationOptions options, RuleProgram rules, ModelData data, Lesson lesson, string? stage)
    {
        var allowed = new List<(DomainDayOfWeek day, int period)>();

        if (options.RespectFixedLessons && lesson.IsFixed)
        {
            allowed.Add((lesson.Day, lesson.Period.Value));
            return allowed;
        }

        foreach (var day in ActiveDays(timetable, stage))
        {
            for (var p = 1; p <= MaxPeriod; p++)
            {
                if (!IsSlotAllowed(timetable, stage, day, p)) continue;
                if (data.Unavailable.Contains((lesson.TeacherId, day, p))) continue;

                // قواعد المستخدم الصلبة: حجب أوقات أو منع بعد فترة
                if (IsBlockedByHardRule(rules, lesson, day, p)) continue;

                allowed.Add((day, p));
            }
        }

        return allowed;
    }

    private string ExplainUnplaceable(Timetable timetable, ModelData data, Lesson lesson, string? stage)
    {
        var stageLabel = stage ?? "غير محددة";
        var cfg = FindStageConfig(timetable, stage);

        if (data.Unavailable.Any(k => k.teacherId == lesson.TeacherId))
        {
            var days = ActiveDays(timetable, stage);
            var generic = days.Sum(d => CountGenericPeriods(timetable, d));
            var blocked = data.Unavailable.Count(k => k.teacherId == lesson.TeacherId);

            if (generic > 0 && blocked >= generic)
                return $"المعلم {lesson.Teacher?.FullName ?? "غير معروف"} مُعلن غير متاح في كل الفترات المتاحة للمرحلة {stageLabel}";
        }

        if (cfg is { WorkDays.Count: > 0 })
            return $"أيام العمل المحددة للمرحلة {stageLabel} ({cfg.WorkDays.Count} يوم) لا تسمح بأي حصة";

        if (timetable.PeriodTimes.Count > 0)
            return $"لا توجد توقيتات فترات معرّفة للمرحلة {stageLabel} في أيام العمل";

        return $"لا توجد فترة متاحة للمرحلة {stageLabel}";
    }

    /// <summary>
    /// فحص الجدوى قبل الحل: هل تتجاوز حصص كل فصل/معلم عدد الفترات المتاحة فعلياً؟
    /// يعطي رسالة دقيقة بدل انتظار المحلّل حتى يثبت تعذّر الحل.
    /// </summary>
    private static List<string> DiagnoseCapacity(Timetable timetable, ModelData data)
    {
        var issues = new List<string>();
        var unplaceableIds = data.Unplaceable.Select(l => l.Id).ToHashSet();

        foreach (var group in timetable.Lessons.GroupBy(l => l.ClassId))
        {
            var lessons = group.ToList();
            if (lessons.Any(l => unplaceableIds.Contains(l.Id))) continue;

            var stage = timetable.GetStageForClass(group.Key);
            var days = ActiveDays(timetable, stage);
            var cfg = FindStageConfig(timetable, stage);
            var capacity = days.Sum(d =>
            {
                var allowed = CountAllowedPeriods(timetable, stage, d);
                if (allowed <= 0) return 0;
                var dayCap = cfg?.GetLessonsPerDay((int)d) ?? allowed;
                return Math.Min(allowed, dayCap);
            });

            if (capacity > 0 && lessons.Count > capacity)
            {
                var name = lessons[0].Class?.FullName ?? group.Key.Value.ToString()[..8];
                issues.Add($"الفصل {name}: {lessons.Count} حصة أسبوعياً > {capacity} فترة متاحة ({days.Count} يوم).");
            }
        }

        foreach (var group in timetable.Lessons.GroupBy(l => l.TeacherId))
        {
            var lessons = group.ToList();
            if (lessons.Any(l => unplaceableIds.Contains(l.Id))) continue;

            var slots = new HashSet<(int Day, int Period)>();
            foreach (var stage in lessons.Select(l => timetable.GetStageForClass(l.ClassId)).Distinct())
            {
                foreach (var day in ActiveDays(timetable, stage))
                {
                    for (var p = 1; p <= MaxPeriod; p++)
                        if (IsSlotAllowed(timetable, stage, day, p))
                            slots.Add(((int)day, p));
                }
            }

            if (slots.Count > 0 && lessons.Count > slots.Count)
            {
                var name = lessons[0].Teacher?.FullName ?? group.Key.Value.ToString()[..8];
                issues.Add($"المعلم {name}: {lessons.Count} حصة أسبوعياً > {slots.Count} فترة متاحة.");
            }
        }

        return issues;
    }

    // --------------------------------------------------------------------
    // القيود الصلبة
    // --------------------------------------------------------------------

    /// <summary>هل يمنع موضعاً (يوم/فترة) قاعدةُ مستخدم صلبة (حجب أو منع بعد فترة)؟</summary>
    private static bool IsBlockedByHardRule(RuleProgram rules, Lesson lesson, DomainDayOfWeek day, int period)
    {
        foreach (var block in rules.SlotBlocks)
        {
            if (!block.IsHard) continue;
            if (RuleCompiler.Blocks(block, lesson, day, period)) return true;
        }

        foreach (var limit in rules.NoAfterPeriods)
        {
            if (!limit.IsHard) continue;
            if (period > limit.AfterPeriod && RuleCompiler.Applies(limit, lesson)) return true;
        }

        return false;
    }

    private static void AddHardConstraints(CpModel model, ModelData data, Timetable timetable, RuleProgram rules)
    {
        // كل حصة تُجدول مرة واحدة بالضبط
        foreach (var lessonVars in data.LessonVars.Values)
        {
            if (lessonVars.Count == 0) continue;
            model.Add(LinearExpr.Sum(lessonVars.Select(x => x.v)) == 1);
        }

        // الحد الأقصى للحصص اليومية لكل فصل حسب إعداد مرحلته
        foreach (var classEntry in data.LessonsByClass)
        {
            var stage = timetable.GetStageForClass(classEntry.Key);
            var cfg = FindStageConfig(timetable, stage);
            if (cfg is null) continue;

            foreach (var day in ActiveDays(timetable, stage))
            {
                if (!data.ClassDayVars.TryGetValue((classEntry.Key, day), out var dayVars) ||
                    dayVars.Count == 0)
                    continue;

                // عدد الحصص المخصص لهذا اليوم إن وُجد، وإلا الحد العام للمرحلة
                var perDay = cfg.GetLessonsPerDay((int)day);
                if (perDay <= 0) continue;

                model.Add(LinearExpr.Sum(dayVars.Select(x => x.v)) <= perDay);
            }
        }

        if (data.TeacherSlotTimes.Count > 0)
        {
            // تضارب المعلم بتداخل الزمن الفعلي (يعالج المعلم المشترك بين متوسط وثانوي بتوقيتين مختلفين)
            AddTeacherTimeConstraints(model, data);
        }
        else
        {
            // بلا توقيتات: المقارنة برقم الفترة
            foreach (var teacherDay in data.TeacherDayVars)
            {
                foreach (var slot in teacherDay.Value.GroupBy(x => x.period))
                {
                    var vars = slot.Select(x => x.v).ToList();
                    if (vars.Count > 1)
                        model.Add(LinearExpr.Sum(vars) <= 1);
                }
            }
        }

        // لا يُجدَّل فصلان في نفس الفترة: حصص الفصل الواحد لا تتداخل أبدًا
        foreach (var classEntry in data.LessonsByClass)
        {
            if (classEntry.Value.Count <= 1) continue;

            foreach (var day in Enum.GetValues<DomainDayOfWeek>())
            {
                for (var p = 1; p <= MaxPeriod; p++)
                {
                    var vars = new List<IntVar>(classEntry.Value.Count);

                    foreach (var lesson in classEntry.Value)
                    {
                        if (data.Vars.TryGetValue((lesson.Id, day, p), out var v))
                            vars.Add(v);
                    }

                    if (vars.Count > 1)
                        model.Add(LinearExpr.Sum(vars) <= 1);
                }
            }
        }

        // قواعد المستخدم الصلبة: حد أقصى لحصص مادة في اليوم
        foreach (var rule in rules.MaxPerDays)
        {
            if (!rule.IsHard || rule.Scope != RuleScope.Subject) continue;

            foreach (var entry in data.LessonsByClassSubject)
            {
                if ((Guid)entry.Key.subjectId != rule.TargetId) continue;

                foreach (var day in Enum.GetValues<DomainDayOfWeek>())
                {
                    var vars = new List<IntVar>();
                    foreach (var lesson in entry.Value)
                    {
                        if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;
                        vars.AddRange(slots.Where(s => s.day == day).Select(s => s.v));
                    }

                    if (vars.Count > rule.MaxPerDay)
                        model.Add(LinearExpr.Sum(vars) <= rule.MaxPerDay);
                }
            }
        }

        // لا تضارب غرفة في نفس الفترة
        foreach (var roomEntry in data.LessonsByRoom)
        {
            if (roomEntry.Value.Count <= 1) continue;

            foreach (var day in Enum.GetValues<DomainDayOfWeek>())
            {
                var vars = new List<IntVar>();

                foreach (var lesson in roomEntry.Value)
                {
                    if (!data.LessonVars.TryGetValue(lesson.Id, out var lessonVars)) continue;

                    foreach (var (d, _, v) in lessonVars)
                    {
                        if (d == day) vars.Add(v);
                    }
                }

                if (vars.Count > 1)
                    model.Add(LinearExpr.Sum(vars) <= 1);
            }
        }

        // امتدادات قواعد المستخدم: حدود متتالية/يومية/فجوات
        AddUserExtensionHardConstraints(model, data, rules);
    }

    /// <summary>حصص نطاق (معلم/فصل/مادة) من بيانات النموذج.</summary>
    private static IEnumerable<Lesson> LessonsForScope(ModelData data, RuleScope scope, Guid targetId)
        => scope switch
        {
            RuleScope.Teacher => data.LessonsByTeacher.TryGetValue(targetId, out var t) ? t : Enumerable.Empty<Lesson>(),
            RuleScope.Class => data.LessonsByClass.TryGetValue(targetId, out var c) ? c : Enumerable.Empty<Lesson>(),
            RuleScope.Subject => data.LessonsByClassSubject.Where(e => (Guid)e.Key.subjectId == targetId).SelectMany(e => e.Value),
            _ => Enumerable.Empty<Lesson>(),
        };

    /// <summary>متغيرات الحصص مجمّعة حسب رقم الفترة في يوم معيّن.</summary>
    private static Dictionary<int, List<IntVar>> VarsByPeriodForDay(
        ModelData data, IEnumerable<Lesson> lessons, DomainDayOfWeek day)
    {
        var result = new Dictionary<int, List<IntVar>>();

        foreach (var lesson in lessons)
        {
            if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

            foreach (var (d, p, v) in slots)
            {
                if (d != day) continue;
                if (!result.TryGetValue(p, out var list))
                {
                    list = new List<IntVar>();
                    result[p] = list;
                }
                list.Add(v);
            }
        }

        return result;
    }

    /// <summary>
    /// قواعد المستخدم الصلبة الإضافية:
    /// MaxConsecutive (حد التتابع)، DailyLoad (حد أدنى/أقصى يومي)، MaxGapsPerDay (حد الفجوات اليومية).
    /// </summary>
    private static void AddUserExtensionHardConstraints(CpModel model, ModelData data, RuleProgram rules)
    {
        var days = Enum.GetValues<DomainDayOfWeek>();

        foreach (var rule in rules.MaxConsecutives)
        {
            if (!rule.IsHard) continue;

            foreach (var day in days)
            {
                var byPeriod = VarsByPeriodForDay(data, LessonsForScope(data, rule.Scope, rule.TargetId), day);
                var periods = byPeriod.Keys.OrderBy(x => x).ToList();
                var size = rule.Max + 1;

                for (var i = 0; i + size <= periods.Count; i++)
                {
                    var window = periods.Skip(i).Take(size).ToList();
                    if (window[^1] - window[0] != size - 1) continue;

                    var vars = window.SelectMany(p => byPeriod[p]).ToList();
                    if (vars.Count > rule.Max)
                        model.Add(LinearExpr.Sum(vars) <= rule.Max);
                }
            }
        }

        foreach (var rule in rules.DailyLoads)
        {
            if (!rule.IsHard) continue;

            foreach (var day in days)
            {
                var byPeriod = VarsByPeriodForDay(data, LessonsForScope(data, rule.Scope, rule.TargetId), day);
                var vars = byPeriod.Values.SelectMany(x => x).ToList();
                if (vars.Count == 0) continue;

                if (rule.Max is int mx && vars.Count > mx)
                    model.Add(LinearExpr.Sum(vars) <= mx);

                if (rule.Min is int mn && mn > 0 && mn <= vars.Count)
                    model.Add(LinearExpr.Sum(vars) >= mn);
            }
        }

        foreach (var rule in rules.MaxGapsPerDays)
        {
            if (!rule.IsHard) continue;

            foreach (var day in days)
            {
                var byPeriod = VarsByPeriodForDay(data, LessonsForScope(data, rule.Scope, rule.TargetId), day);
                var periods = byPeriod.Keys.OrderBy(x => x).ToList();
                if (periods.Count < 3) continue;

                var busy = periods.ToDictionary(p => p, p => LinearExpr.Sum(byPeriod[p]));

                for (var a = 0; a < periods.Count; a++)
                {
                    for (var b = a + 1; b < periods.Count; b++)
                    {
                        var middle = new List<LinearExpr>();
                        for (var p = periods[a] + 1; p < periods[b]; p++)
                            if (busy.TryGetValue(p, out var e)) middle.Add(e);

                        var k = middle.Count - rule.Max;
                        if (k <= 0) continue;

                        // Σ middle - k*busy_a - k*busy_b >= -k
                        var expr = LinearExpr.Sum(middle) - busy[periods[a]] * k - busy[periods[b]] * k;
                        model.Add(expr >= -k);
                    }
                }
            }
        }

        AddUserDayScopeHardConstraints(model, data, rules);
    }

    /// <summary>وحدات النطاق: للفصل/المعلم وحدة واحدة، ولمادة وحدة لكل فصل على حدة.</summary>
    private static IEnumerable<List<Lesson>> ScopeUnits(ModelData data, RuleScope scope, Guid targetId)
    {
        if (scope == RuleScope.Subject)
            return data.LessonsByClassSubject
                .Where(e => (Guid)e.Key.subjectId == targetId)
                .Select(e => e.Value);

        var lessons = LessonsForScope(data, scope, targetId).ToList();
        return lessons.Count == 0 ? Enumerable.Empty<List<Lesson>>() : new[] { lessons };
    }

    /// <summary>متغيرات مادة داخل فصل مجمّعة حسب الفترة في يوم معيّن.</summary>
    private static Dictionary<int, List<IntVar>> ClassSubjectVarsByPeriod(
        ModelData data, Guid classId, Guid subjectId, DomainDayOfWeek day)
    {
        var result = new Dictionary<int, List<IntVar>>();
        if (!data.LessonsByClassSubject.TryGetValue((classId, subjectId), out var lessons))
            return result;

        foreach (var lesson in lessons)
        {
            if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

            foreach (var (d, p, v) in slots)
            {
                if (d != day) continue;
                if (!result.TryGetValue(p, out var list))
                {
                    list = new List<IntVar>();
                    result[p] = list;
                }
                list.Add(v);
            }
        }

        return result;
    }

    /// <summary>متغير بولياني يساوي 1 إذا كان مجموع المتغيرات ≥ 1.</summary>
    private static IntVar NewSumBool(CpModel model, string name, IReadOnlyList<IntVar> vars)
    {
        var b = model.NewBoolVar(name);
        if (vars.Count == 0)
        {
            model.Add(b == 0);
            return b;
        }

        var sum = LinearExpr.Sum(vars);
        model.Add(sum >= 1).OnlyEnforceIf(b);
        model.Add(sum == 0).OnlyEnforceIf(b.Not());
        return b;
    }

    /// <summary>
    /// القواعد الصلبة الإضافية المعتمدة على الأيام: أقصى أيام/أسبوع، امتداد اليوم، بدايات مبكرة،
    /// زوج المواد، مرتين يومياً، كتلة بلا فجوات، وأدنى أيام بين التكرارات.
    /// </summary>
    private static void AddUserDayScopeHardConstraints(CpModel model, ModelData data, RuleProgram rules)
    {
        var days = Enum.GetValues<DomainDayOfWeek>();

        foreach (var rule in rules.MaxDaysPerWeeks)
        {
            if (!rule.IsHard) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var active = new List<IntVar>();
                foreach (var day in days)
                {
                    var vars = VarsByPeriodForDay(data, unit, day).Values.SelectMany(x => x).ToList();
                    active.Add(NewSumBool(model, $"umdw_{rule.TargetId:N}_{(int)day}", vars));
                }

                if (active.Count > rule.Max)
                    model.Add(LinearExpr.Sum(active) <= rule.Max);
            }
        }

        foreach (var rule in rules.MaxSpanPerDays)
        {
            if (!rule.IsHard) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                foreach (var day in days)
                {
                    var byPeriod = VarsByPeriodForDay(data, unit, day);
                    var busy = new Dictionary<int, IntVar>();
                    foreach (var kv in byPeriod)
                        busy[kv.Key] = NewSumBool(model, $"umsp_{rule.TargetId:N}_{(int)day}_{kv.Key}", kv.Value);

                    var periods = busy.Keys.OrderBy(x => x).ToList();
                    for (var i = 0; i < periods.Count; i++)
                        for (var j = i + 1; j < periods.Count; j++)
                            if (periods[j] - periods[i] + 1 > rule.Max)
                                model.Add(busy[periods[i]] + busy[periods[j]] <= 1);
                }
            }
        }

        foreach (var rule in rules.BeginsEarlys)
        {
            if (!rule.IsHard) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var earlyBools = new List<LinearExpr>();
                foreach (var day in days)
                {
                    var byPeriod = VarsByPeriodForDay(data, unit, day);
                    if (byPeriod.Count == 0) continue;

                    var dayVars = byPeriod.Values.SelectMany(x => x).ToList();
                    var dayActive = NewSumBool(model, $"ube_a_{rule.TargetId:N}_{(int)day}", dayVars);

                    IntVar? firstHour = byPeriod.TryGetValue(1, out var p1v)
                        ? NewSumBool(model, $"ube_p1_{rule.TargetId:N}_{(int)day}", p1v)
                        : null;

                    var early = model.NewBoolVar($"ube_e_{rule.TargetId:N}_{(int)day}");
                    model.Add(early <= dayActive);
                    if (firstHour is not null)
                    {
                        model.Add(early + firstHour <= 1);
                        model.Add(early >= dayActive - firstHour);
                    }
                    else
                    {
                        model.Add(early == dayActive);
                    }

                    earlyBools.Add(early);
                }

                if (earlyBools.Count > rule.Max)
                    model.Add(LinearExpr.Sum(earlyBools) <= rule.Max);
            }
        }

        foreach (var rule in rules.SubjectPairs)
        {
            if (!rule.IsHard) continue;

            var classes = data.LessonsByClassSubject
                .Where(e => (Guid)e.Key.subjectId == rule.SubjectA)
                .Select(e => e.Key.classId)
                .Distinct();

            foreach (var classId in classes)
            {
                foreach (var day in days)
                {
                    var a = ClassSubjectVarsByPeriod(data, classId, rule.SubjectA, day);
                    var b = ClassSubjectVarsByPeriod(data, classId, rule.SubjectB, day);
                    var aVars = a.Values.SelectMany(x => x).ToList();
                    var bVars = b.Values.SelectMany(x => x).ToList();
                    if (aVars.Count == 0 || bVars.Count == 0) continue;

                    if (rule.NoSameDay)
                    {
                        model.Add(LinearExpr.Sum(aVars) + LinearExpr.Sum(bVars) <= 1);
                    }
                    else if (rule.NoConsecutive)
                    {
                        for (var p = 1; p < MaxPeriod; p++)
                        {
                            if (a.TryGetValue(p, out var ap) && b.TryGetValue(p + 1, out var bn))
                                model.Add(LinearExpr.Sum(ap) + LinearExpr.Sum(bn) <= 1);
                            if (b.TryGetValue(p, out var bp) && a.TryGetValue(p + 1, out var an))
                                model.Add(LinearExpr.Sum(bp) + LinearExpr.Sum(an) <= 1);
                        }
                    }
                }
            }
        }

        foreach (var rule in rules.SubjectTwicePerDays)
        {
            if (!rule.IsHard) continue;

            var classes = data.LessonsByClassSubject
                .Where(e => (Guid)e.Key.subjectId == rule.TargetId)
                .Select(e => e.Key.classId)
                .Distinct();

            foreach (var classId in classes)
            {
                foreach (var day in days)
                {
                    var byPeriod = ClassSubjectVarsByPeriod(data, classId, rule.TargetId, day);
                    var vars = byPeriod.Values.SelectMany(x => x).ToList();
                    if (vars.Count <= 2) continue;

                    model.Add(LinearExpr.Sum(vars) <= 2);

                    if (rule.MustBeConsecutive)
                    {
                        var periods = byPeriod.Keys.OrderBy(x => x).ToList();
                        for (var i = 0; i < periods.Count; i++)
                            for (var j = i + 1; j < periods.Count; j++)
                                if (periods[j] - periods[i] > 1)
                                    model.Add(LinearExpr.Sum(byPeriod[periods[i]]) + LinearExpr.Sum(byPeriod[periods[j]]) <= 1);
                    }
                }
            }
        }

        foreach (var rule in rules.BlockLessons)
        {
            if (!rule.IsHard) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                foreach (var day in days)
                {
                    var byPeriod = VarsByPeriodForDay(data, unit, day);
                    var busy = new Dictionary<int, IntVar>();
                    foreach (var kv in byPeriod)
                        busy[kv.Key] = NewSumBool(model, $"ubl_{rule.TargetId:N}_{(int)day}_{kv.Key}", kv.Value);

                    var periods = busy.Keys.OrderBy(x => x).ToList();
                    for (var i = 0; i < periods.Count; i++)
                        for (var j = i + 1; j < periods.Count; j++)
                        {
                            if (periods[j] - periods[i] <= 1) continue;

                            var middle = new List<LinearExpr>();
                            for (var p = periods[i] + 1; p < periods[j]; p++)
                                if (byPeriod.TryGetValue(p, out var mv)) middle.Add(LinearExpr.Sum(mv));
                            if (middle.Count == 0) continue;

                            model.Add(busy[periods[i]] + busy[periods[j]] - LinearExpr.Sum(middle) <= 1);
                        }
                }
            }
        }

        foreach (var rule in rules.MinDaysBetweens)
        {
            if (!rule.IsHard) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                var active = new Dictionary<int, IntVar>();
                foreach (var day in days)
                {
                    var vars = VarsByPeriodForDay(data, unit, day).Values.SelectMany(x => x).ToList();
                    active[(int)day] = NewSumBool(model, $"umb_{rule.TargetId:N}_{(int)day}", vars);
                }

                var list = active.Keys.OrderBy(x => x).ToList();
                for (var i = 0; i < list.Count; i++)
                    for (var j = i + 1; j < list.Count; j++)
                        if (list[j] - list[i] < rule.MinDays)
                            model.Add(active[list[i]] + active[list[j]] <= 1);
            }
        }

        foreach (var rule in rules.MinDaysPerWeeks)
        {
            if (!rule.IsHard) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var active = new List<IntVar>();
                foreach (var day in days)
                {
                    var vars = VarsByPeriodForDay(data, unit, day).Values.SelectMany(x => x).ToList();
                    active.Add(NewSumBool(model, $"umdw2_{rule.TargetId:N}_{(int)day}", vars));
                }

                if (active.Count >= rule.Min)
                    model.Add(LinearExpr.Sum(active) >= rule.Min);
            }
        }
    }

    /// <summary>
    /// يمنع أن يدرّس المعلم حصتين متداخلتين زمنياً في اليوم نفسه.
    /// نجزّئ الخط الزمني لليوم إلى مقاطع بحدود كل الفترات، ونشترط ألا يغطي أكثر من متغير المقطع الواحد.
    /// </summary>
    private static void AddTeacherTimeConstraints(CpModel model, ModelData data)
    {
        foreach (var entry in data.TeacherSlotTimes)
        {
            foreach (var dayGroup in entry.Value.GroupBy(t => t.day))
            {
                var slots = dayGroup.ToList();
                if (slots.Count <= 1) continue;

                var bounds = slots
                    .SelectMany(s => new[] { s.start, s.end })
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                for (var i = 0; i + 1 < bounds.Count; i++)
                {
                    var segStart = bounds[i];
                    var segEnd = bounds[i + 1];
                    if (segEnd <= segStart) continue;

                    var covering = slots
                        .Where(s => s.start < segEnd && segStart < s.end)
                        .Select(s => s.v)
                        .ToList();

                    if (covering.Count > 1)
                        model.Add(LinearExpr.Sum(covering) <= 1);
                }
            }
        }
    }

    // --------------------------------------------------------------------
    // القيود اللينة (دالة الهدف)
    // --------------------------------------------------------------------

    private static LinearExpr? BuildObjective(CpModel model, ModelData data, Timetable timetable, RuleProgram rules)
    {
        var terms = new List<LinearExpr>();

        foreach (var classEntry in data.LessonsByClass)
        {
            var classId = classEntry.Key;
            var lessons = classEntry.Value;
            var stage = timetable.GetStageForClass(classId);
            var days = ActiveDays(timetable, stage);
            if (days.Count == 0) continue;

            var target = (int)Math.Ceiling(lessons.Count / (double)days.Count);

            foreach (var day in days)
            {
                if (!data.ClassDayVars.TryGetValue((classId, day), out var dayVars) || dayVars.Count == 0)
                    continue;

                var sum = LinearExpr.Sum(dayVars.Select(x => x.v));

                var excess = model.NewIntVar(0, lessons.Count, $"ex_{classId:N}_{(int)day}");
                model.Add(excess >= sum - target);
                terms.Add(excess * 100L);

                terms.AddRange(ConsecutiveWindowTerms(model, dayVars, $"cx_{classId:N}_{(int)day}", 200L));
            }

            var activeVars = new List<IntVar>();
            foreach (var day in days)
            {
                var a = model.NewBoolVar($"ad_{classId:N}_{(int)day}");

                if (data.ClassDayVars.TryGetValue((classId, day), out var dv) && dv.Count > 0)
                {
                    var daySum = LinearExpr.Sum(dv.Select(x => x.v));
                    model.Add(daySum >= 1).OnlyEnforceIf(a);
                    model.Add(daySum == 0).OnlyEnforceIf(a.Not());
                }
                else
                {
                    model.Add(a == 0);
                }

                activeVars.Add(a);
            }

            var deficit = model.NewIntVar(0, Math.Max(days.Count, DefaultMinDaysPerWeek), $"md_{classId:N}");
            model.Add(deficit + LinearExpr.Sum(activeVars) >= DefaultMinDaysPerWeek);
            terms.Add(deficit * 150L);
        }

        // توزيع المادة داخل الأسبوع: أي يوم يتجاوز ⌈عدد حصص المادة في الفصل / الأيام⟩ يُعاقَب
        foreach (var entry in data.LessonsByClassSubject)
        {
            var classId = entry.Key.classId;
            var subjectId = entry.Key.subjectId;
            var subjectDays = ActiveDays(timetable, timetable.GetStageForClass(classId));
            if (subjectDays.Count == 0) continue;

            var subjectTarget = (int)Math.Ceiling(entry.Value.Count / (double)subjectDays.Count);

            foreach (var day in subjectDays)
            {
                var dayVars = new List<IntVar>(entry.Value.Count);

                foreach (var lesson in entry.Value)
                {
                    if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

                    foreach (var (d, _, v) in slots)
                    {
                        if (d == day) dayVars.Add(v);
                    }
                }

                if (dayVars.Count == 0) continue;

                var subjectSum = LinearExpr.Sum(dayVars);
                var subjectExcess = model.NewIntVar(0, entry.Value.Count, $"sx_{classId:N}_{subjectId:N}_{(int)day}");
                model.Add(subjectExcess >= subjectSum - subjectTarget);
                terms.Add(subjectExcess * 80L);
            }
        }

        // إغلاق منخفض الأولوية: جدولة المعلم في هذه الأوقات مسموحة لكن مكلفة (يُتجاوز عند الحاجة فقط)
        if (data.SoftBlocked.Count > 0)
        {
            foreach (var lesson in timetable.Lessons)
            {
                if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

                foreach (var (day, period, v) in slots)
                {
                    if (data.SoftBlocked.Contains((lesson.TeacherId, day, period)))
                        terms.Add(v * 60L);
                }
            }
        }

        // تفضيل المعلم: حصة خارج الفترات التي أعلن المعلم توفره فيها تكلّف حلًا
        if (data.DeclaredTeachers.Count > 0)
        {
            foreach (var teacherEntry in data.LessonsByTeacher)
            {
                if (!data.DeclaredTeachers.Contains((TeacherId)teacherEntry.Key)) continue;

                foreach (var lesson in teacherEntry.Value)
                {
                    if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

                    foreach (var (day, period, v) in slots)
                    {
                        if (!data.Declared.Contains(((TeacherId)teacherEntry.Key, day, period)))
                            terms.Add(v * 10L);
                    }
                }
            }
        }

        foreach (var teacherDay in data.TeacherDayVars)
        {
            terms.AddRange(ConsecutiveWindowTerms(
                model,
                teacherDay.Value,
                $"tw_{teacherDay.Key.Item1.Value:N}_{(int)teacherDay.Key.Item2}",
                150L));
        }

        // قواعد المستخدم اللينة: حجب أوقات، منع بعد فترة، وحد يومي — تُعاقَب دون منع
        foreach (var lesson in timetable.Lessons)
        {
            if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

            foreach (var (day, period, v) in slots)
            {
                foreach (var block in rules.SlotBlocks)
                {
                    if (block.IsHard) continue;
                    if (RuleCompiler.Blocks(block, lesson, day, period)) terms.Add(v * 120L);
                }

                foreach (var limit in rules.NoAfterPeriods)
                {
                    if (limit.IsHard) continue;
                    if (period > limit.AfterPeriod && RuleCompiler.Applies(limit, lesson)) terms.Add(v * 60L);
                }
            }
        }

        // حد يومي لين لمادة: تجاوز الحد يُعاقَب
        foreach (var rule in rules.MaxPerDays)
        {
            if (rule.IsHard || rule.Scope != RuleScope.Subject) continue;

            foreach (var entry in data.LessonsByClassSubject)
            {
                if ((Guid)entry.Key.subjectId != rule.TargetId) continue;

                foreach (var day in Enum.GetValues<DomainDayOfWeek>())
                {
                    var vars = new List<IntVar>();
                    foreach (var lesson in entry.Value)
                    {
                        if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;
                        vars.AddRange(slots.Where(s => s.day == day).Select(s => s.v));
                    }

                    if (vars.Count == 0) continue;

                    var over = model.NewIntVar(0, vars.Count, $"rd_{rule.TargetId:N}_{entry.Key.classId:N}_{(int)day}");
                    model.Add(over >= LinearExpr.Sum(vars) - rule.MaxPerDay);
                    terms.Add(over * 100L);
                }
            }
        }

        terms.AddRange(BuildUserExtensionObjectiveTerms(model, data, rules));

        return terms.Count == 0 ? null : LinearExpr.Sum(terms);
    }

    /// <summary>عقوبات لينة لقواعد المستخدم الإضافية (تتابع/عبء يومي).</summary>
    private static IEnumerable<LinearExpr> BuildUserExtensionObjectiveTerms(
        CpModel model, ModelData data, RuleProgram rules)
    {
        var result = new List<LinearExpr>();
        var days = Enum.GetValues<DomainDayOfWeek>();

        foreach (var rule in rules.MaxConsecutives)
        {
            if (rule.IsHard) continue;

            foreach (var day in days)
            {
                var byPeriod = VarsByPeriodForDay(data, LessonsForScope(data, rule.Scope, rule.TargetId), day);
                var periods = byPeriod.Keys.OrderBy(x => x).ToList();
                var size = rule.Max + 1;

                for (var i = 0; i + size <= periods.Count; i++)
                {
                    var window = periods.Skip(i).Take(size).ToList();
                    if (window[^1] - window[0] != size - 1) continue;

                    var vars = window.SelectMany(p => byPeriod[p]).ToList();
                    if (vars.Count <= rule.Max) continue;

                    var over = model.NewIntVar(0, vars.Count, $"mce_{rule.TargetId:N}_{(int)day}_{window[0]}");
                    model.Add(over >= LinearExpr.Sum(vars) - rule.Max);
                    result.Add(over * 150L);
                }
            }
        }

        foreach (var rule in rules.DailyLoads)
        {
            if (rule.IsHard) continue;

            foreach (var day in days)
            {
                var byPeriod = VarsByPeriodForDay(data, LessonsForScope(data, rule.Scope, rule.TargetId), day);
                var vars = byPeriod.Values.SelectMany(x => x).ToList();
                if (vars.Count == 0) continue;

                if (rule.Max is int mx && vars.Count > mx)
                {
                    var over = model.NewIntVar(0, vars.Count, $"dle_{rule.TargetId:N}_{(int)day}");
                    model.Add(over >= LinearExpr.Sum(vars) - mx);
                    result.Add(over * 100L);
                }

                if (rule.Min is int mn && mn > 0 && vars.Count >= mn)
                {
                    var under = model.NewIntVar(0, mn, $"dlu_{rule.TargetId:N}_{(int)day}");
                    model.Add(under + LinearExpr.Sum(vars) >= mn);
                    result.Add(under * 80L);
                }
            }
        }

        result.AddRange(AddUserDayScopeSoftTerms(model, data, rules));

        return result;
    }

    /// <summary>عقوبات لينة لقواعد الأيام/المواد/القاعات الجديدة (تُطبَّق عند اختيار الأولوية اللينة).</summary>
    private static IEnumerable<LinearExpr> AddUserDayScopeSoftTerms(CpModel model, ModelData data, RuleProgram rules)
    {
        var result = new List<LinearExpr>();
        var days = Enum.GetValues<DomainDayOfWeek>();

        // أقصى أيام أسبوعياً
        foreach (var rule in rules.MaxDaysPerWeeks)
        {
            if (rule.IsHard) continue;
            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var active = new List<IntVar>();
                foreach (var day in days)
                {
                    var vars = VarsByPeriodForDay(data, unit, day).Values.SelectMany(x => x).ToList();
                    active.Add(NewSumBool(model, $"smdw_{rule.TargetId:N}_{(int)day}", vars));
                }

                if (active.Count <= rule.Max) continue;
                var over = model.NewIntVar(0, active.Count, $"smdwo_{rule.TargetId:N}");
                model.Add(over >= LinearExpr.Sum(active) - rule.Max);
                result.Add(over * 120L);
            }
        }

        // أقصى فجوات أسبوعياً (تقريب: مجموع الفجوات اليومية)
        foreach (var rule in rules.MaxGapsPerWeeks)
        {
            var weeklyAllowance = rule.IsHard ? rule.Max : Math.Max(1, rule.Max);

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var weeklyGaps = model.NewIntVar(0, MaxPeriod * 7, $"mgw_{rule.TargetId:N}");
                var dayTerms = new List<LinearExpr>();

                foreach (var day in days)
                {
                    var byPeriod = VarsByPeriodForDay(data, unit, day);
                    if (byPeriod.Count < 2) continue;

                    var busy = new Dictionary<int, IntVar>();
                    foreach (var kv in byPeriod)
                        busy[kv.Key] = NewSumBool(model, $"mgwp_{rule.TargetId:N}_{(int)day}_{kv.Key}", kv.Value);

                    var busyList = busy.Values.ToList();
                    var span = model.NewIntVar(0, MaxPeriod, $"mgws_{rule.TargetId:N}_{(int)day}");
                    foreach (var kv in busy)
                        model.Add(span >= kv.Key * kv.Value);
                    model.Add(span >= LinearExpr.Sum(busyList));

                    dayTerms.Add(span - LinearExpr.Sum(busyList));
                }

                if (dayTerms.Count == 0) continue;
                model.Add(weeklyGaps == LinearExpr.Sum(dayTerms));

                var overW = model.NewIntVar(0, MaxPeriod * 7, $"mgwo_{rule.TargetId:N}");
                model.Add(overW >= weeklyGaps - weeklyAllowance);
                result.Add(overW * 70L);
            }
        }

        // أقصى امتداد لليوم
        foreach (var rule in rules.MaxSpanPerDays)
        {
            if (rule.IsHard) continue;
            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                foreach (var day in days)
                {
                    var byPeriod = VarsByPeriodForDay(data, unit, day);
                    if (byPeriod.Count < 2) continue;

                    var busy = new Dictionary<int, IntVar>();
                    foreach (var kv in byPeriod)
                        busy[kv.Key] = NewSumBool(model, $"smsp_{rule.TargetId:N}_{(int)day}_{kv.Key}", kv.Value);

                    var periods = busy.Keys.OrderBy(x => x).ToList();
                    for (var i = 0; i < periods.Count; i++)
                        for (var j = i + 1; j < periods.Count; j++)
                            if (periods[j] - periods[i] + 1 > rule.Max)
                                result.Add((busy[periods[i]] + busy[periods[j]]) * 90L);
                }
            }
        }

        // بدايات مبكرة
        foreach (var rule in rules.BeginsEarlys)
        {
            if (rule.IsHard) continue;
            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var earlyBools = new List<LinearExpr>();
                foreach (var day in days)
                {
                    var byPeriod = VarsByPeriodForDay(data, unit, day);
                    if (byPeriod.Count == 0) continue;

                    var dayActive = NewSumBool(model, $"sbe_a_{rule.TargetId:N}_{(int)day}",
                        byPeriod.Values.SelectMany(x => x).ToList());

                    var early = model.NewBoolVar($"sbe_e_{rule.TargetId:N}_{(int)day}");
                    if (byPeriod.TryGetValue(1, out var p1v))
                    {
                        var first = NewSumBool(model, $"sbe_p1_{rule.TargetId:N}_{(int)day}", p1v);
                        model.Add(early <= dayActive);
                        model.Add(early + first <= 1);
                        model.Add(early >= dayActive - first);
                    }
                    else
                    {
                        model.Add(early == dayActive);
                    }
                    earlyBools.Add(early);
                }

                if (earlyBools.Count <= rule.Max) continue;
                var over = model.NewIntVar(0, earlyBools.Count, $"sbeo_{rule.TargetId:N}");
                model.Add(over >= LinearExpr.Sum(earlyBools) - rule.Max);
                result.Add(over * 90L);
            }
        }

        // زوج المواد
        foreach (var rule in rules.SubjectPairs)
        {
            if (rule.IsHard) continue;

            var classes = data.LessonsByClassSubject
                .Where(e => (Guid)e.Key.subjectId == rule.SubjectA)
                .Select(e => e.Key.classId)
                .Distinct();

            foreach (var classId in classes)
            {
                foreach (var day in days)
                {
                    var a = ClassSubjectVarsByPeriod(data, classId, rule.SubjectA, day);
                    var b = ClassSubjectVarsByPeriod(data, classId, rule.SubjectB, day);
                    var aVars = a.Values.SelectMany(x => x).ToList();
                    var bVars = b.Values.SelectMany(x => x).ToList();
                    if (aVars.Count == 0 || bVars.Count == 0) continue;

                    if (rule.NoSameDay)
                    {
                        var aPres = NewSumBool(model, $"spa_{classId:N}_{(int)day}", aVars);
                        var bPres = NewSumBool(model, $"spb_{classId:N}_{(int)day}", bVars);
                        var over = model.NewIntVar(0, 1, $"spo_{classId:N}_{(int)day}");
                        model.Add(over >= aPres + bPres - 1);
                        result.Add(over * 200L);
                    }
                    else if (rule.NoConsecutive)
                    {
                        for (var p = 1; p < MaxPeriod; p++)
                        {
                            if (a.TryGetValue(p, out var ap) && b.TryGetValue(p + 1, out var bn))
                                result.Add((LinearExpr.Sum(ap) + LinearExpr.Sum(bn)) * 60L);
                            if (b.TryGetValue(p, out var bp) && a.TryGetValue(p + 1, out var an))
                                result.Add((LinearExpr.Sum(bp) + LinearExpr.Sum(an)) * 60L);
                        }
                    }
                }
            }
        }

        // مادة مرتين يومياً
        foreach (var rule in rules.SubjectTwicePerDays)
        {
            if (rule.IsHard) continue;

            var classes = data.LessonsByClassSubject
                .Where(e => (Guid)e.Key.subjectId == rule.TargetId)
                .Select(e => e.Key.classId)
                .Distinct();

            foreach (var classId in classes)
            {
                foreach (var day in days)
                {
                    var byPeriod = ClassSubjectVarsByPeriod(data, classId, rule.TargetId, day);
                    var vars = byPeriod.Values.SelectMany(x => x).ToList();
                    if (vars.Count == 0) continue;

                    var over = model.NewIntVar(0, vars.Count, $"stpo_{classId:N}_{(int)day}");
                    model.Add(over >= LinearExpr.Sum(vars) - 2);
                    result.Add(over * 90L);
                }
            }
        }

        // أدنى أيام بين التكرارات
        foreach (var rule in rules.MinDaysBetweens)
        {
            if (rule.IsHard) continue;
            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                var active = new Dictionary<int, IntVar>();
                foreach (var day in days)
                {
                    var vars = VarsByPeriodForDay(data, unit, day).Values.SelectMany(x => x).ToList();
                    active[(int)day] = NewSumBool(model, $"smb_{rule.TargetId:N}_{(int)day}", vars);
                }

                var list = active.Keys.OrderBy(x => x).ToList();
                for (var i = 0; i < list.Count; i++)
                    for (var j = i + 1; j < list.Count; j++)
                        if (list[j] - list[i] < rule.MinDays)
                            result.Add((active[list[i]] + active[list[j]]) * 80L);
            }
        }

        result.AddRange(BuildRoomChangeObjectiveTerms(model, data, rules));

        // أدنى أيام عمل أسبوعياً (لين)
        foreach (var rule in rules.MinDaysPerWeeks)
        {
            if (rule.IsHard) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var active = new List<IntVar>();
                foreach (var day in days)
                {
                    var vars = VarsByPeriodForDay(data, unit, day).Values.SelectMany(x => x).ToList();
                    active.Add(NewSumBool(model, $"smdw2_{rule.TargetId:N}_{(int)day}", vars));
                }

                if (active.Count == 0 || active.Count >= rule.Min) continue;

                var under = model.NewIntVar(0, rule.Min, $"smdw2u_{rule.TargetId:N}");
                model.Add(under + LinearExpr.Sum(active) >= rule.Min);
                result.Add(under * 100L);
            }
        }

        return result;
    }

    /// <summary>
    /// حدّ تغيّر القاعة (RoomChangeLimit): يبني عدد تغيّرات القاعة بين الحصص المتتالية في اليوم،
    /// فيفرضه صلباً عند الطلب ويُعاقب تجاوزه ليناً. القاعة ثابتة لكل حصة، فيُقاس التغيّر بين الفترات.
    /// </summary>
    private static IEnumerable<LinearExpr> BuildRoomChangeObjectiveTerms(
        CpModel model, ModelData data, RuleProgram rules)
    {
        var result = new List<LinearExpr>();
        var dayValues = Enum.GetValues<DomainDayOfWeek>();

        foreach (var rule in rules.RoomChangeLimits)
        {
            if (rule.PerDay is null && rule.PerWeek is null) continue;

            foreach (var unit in ScopeUnits(data, rule.Scope, rule.TargetId))
            {
                if (unit.Count == 0) continue;

                var weekTotals = new List<IntVar>();

                foreach (var day in dayValues)
                {
                    var slot = new Dictionary<(int period, Guid room), List<IntVar>>();
                    foreach (var lesson in unit)
                    {
                        if (lesson.Day != day) continue;
                        if (lesson.RoomId is null || lesson.RoomId == RoomId.Empty) continue;
                        if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

                        var room = (Guid)lesson.RoomId.Value;
                        foreach (var (d, p, v) in slots)
                        {
                            if (d != day) continue;
                            var key = (p, room);
                            if (!slot.TryGetValue(key, out var list))
                            {
                                list = new List<IntVar>();
                                slot[key] = list;
                            }
                            list.Add(v);
                        }
                    }

                    if (slot.Count == 0) continue;

                    var rooms = slot.Keys.Select(k => k.room).Distinct().ToList();
                    var periods = slot.Keys.Select(k => k.period).Distinct().OrderBy(x => x).ToList();
                    var busyCache = new Dictionary<(int period, Guid room), IntVar>();

                    IntVar Busy(int period, Guid room)
                    {
                        if (busyCache.TryGetValue((period, room), out var cached)) return cached;
                        slot.TryGetValue((period, room), out var vlist);
                        var b = NewSumBool(model, $"rc_{room:N}_{period}", vlist ?? new List<IntVar>());
                        busyCache[(period, room)] = b;
                        return b;
                    }

                    var changes = new List<LinearExpr>();

                    foreach (var p in periods.Where(p => periods.Contains(p + 1)))
                    {
                        var busyP = NewSumBool(model, $"rcbp_{p}", rooms.Select(r => Busy(p, r)).ToList());
                        var busyP1 = NewSumBool(model, $"rcbp1_{p}", rooms.Select(r => Busy(p + 1, r)).ToList());

                        var both = model.NewBoolVar($"rcboth_{p}");
                        model.Add(both <= busyP);
                        model.Add(both <= busyP1);
                        model.Add(both >= busyP + busyP1 - 1);

                        var same = new List<IntVar>();
                        foreach (var r in rooms)
                        {
                            var bp = Busy(p, r);
                            var bp1 = Busy(p + 1, r);
                            var sr = model.NewBoolVar($"rcsame_{r:N}_{p}");
                            model.Add(sr <= bp);
                            model.Add(sr <= bp1);
                            model.Add(sr >= bp + bp1 - 1);
                            same.Add(sr);
                        }

                        changes.Add(both - LinearExpr.Sum(same));
                    }

                    if (changes.Count == 0) continue;

                    var dayTotal = model.NewIntVar(0, MaxPeriod, $"rcd_{rule.TargetId:N}_{(int)day}");
                    model.Add(dayTotal == LinearExpr.Sum(changes));
                    weekTotals.Add(dayTotal);

                    if (rule.PerDay is int pd)
                    {
                        if (rule.IsHard)
                        {
                            model.Add(dayTotal <= pd);
                        }
                        else
                        {
                            var over = model.NewIntVar(0, MaxPeriod, $"rcod_{rule.TargetId:N}_{(int)day}");
                            model.Add(over >= dayTotal - pd);
                            result.Add(over * 70L);
                        }
                    }
                }

                if (rule.PerWeek is int pw && weekTotals.Count > 0)
                {
                    var weekTotal = model.NewIntVar(0, MaxPeriod * 7, $"rcw_{rule.TargetId:N}");
                    model.Add(weekTotal == LinearExpr.Sum(weekTotals));

                    if (rule.IsHard)
                    {
                        model.Add(weekTotal <= pw);
                    }
                    else
                    {
                        var over = model.NewIntVar(0, MaxPeriod * 7, $"rcow_{rule.TargetId:N}");
                        model.Add(over >= weekTotal - pw);
                        result.Add(over * 40L);
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<LinearExpr> ConsecutiveWindowTerms(
        CpModel model,
        IReadOnlyList<(int period, IntVar v)> slots,
        string prefix,
        long weight)
    {
        var byPeriod = slots
            .GroupBy(s => s.period)
            .ToDictionary(g => g.Key, g => g.First().v);

        var result = new List<LinearExpr>();

        for (var p = 1; p + ConsecutiveWindowSize - 1 <= MaxPeriod; p++)
        {
            var window = new List<IntVar>(ConsecutiveWindowSize);
            var complete = true;

            for (var k = 0; k < ConsecutiveWindowSize; k++)
            {
                if (!byPeriod.TryGetValue(p + k, out var v))
                {
                    complete = false;
                    break;
                }
                window.Add(v);
            }

            if (!complete) continue;

            var hit = model.NewBoolVar($"{prefix}_{p}");
            var sum = LinearExpr.Sum(window);

            model.Add(sum == ConsecutiveWindowSize).OnlyEnforceIf(hit);
            model.Add(sum <= ConsecutiveWindowSize - 1).OnlyEnforceIf(hit.Not());

            result.Add(hit * weight);
        }

        return result;
    }

    // --------------------------------------------------------------------
    // بحث أفضل-من-ن: نجرّب عدة بذور ونختار الأفضل بمقياس المدقق الفعلي
    // --------------------------------------------------------------------

    private sealed record AttemptOutcome(CpSolverStatus Status, ValidationResult? Validation, int Solutions, int Attempts);

    /// <summary>
    /// يشغّل عدة محاولات على نفس النموذج ببذور مختلفة، يحتفظ بالأفضل حسب درجة المدقق.
    /// النتيجة لا تكون أسوأ من محاولة واحدة لأنه يبدأ بها دائماً.
    /// </summary>
    private async Task<AttemptOutcome?> SolveBestByScoreAsync(
        CpModel model,
        ModelData data,
        Timetable timetable,
        GenerationOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        var totalSeconds = Math.Max(0.1, options.EffectiveTimeLimit.TotalSeconds);

        // عدد المحاولات: عدد المتغيّرات المُختار من الواجهة إن > 1، وإلا استدلال بالمهلة
        var attempts = options.VariantCount > 1
            ? options.EffectiveVariantCount
            : totalSeconds >= 12 ? 4
            : totalSeconds >= 6 ? 3
            : totalSeconds >= 4 ? 2
            : 1;

        if (options.DraftMode)
            attempts = 1;

        ValidationResult? best = null;
        var totalSolutions = 0;
        var lastStatus = CpSolverStatus.Unknown;

        // توزيع المهلة ديناميكياً: المحاولة الأولى تأخذ نصيباً وافراً (لضمان إيجاد حل)،
        // والمحاولة التالية تأخذ كل المتبقي إن لم يوجد حل بعد (بدل تقطيع المهلة تقسيماً متساوياً).
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = totalSeconds - watch.Elapsed.TotalSeconds;
            if (remaining <= 1.0) break;

            var slice = i == 0
                ? (attempts > 1 ? Math.Min(remaining, totalSeconds * 0.6) : remaining)
                : (best is null ? remaining : remaining / (attempts - i));

            var seed = options.RandomSeed + i * 7919;
            var outcome = await SolveAsync(model, TimeSpan.FromSeconds(slice), seed, options.UseParallel, cancellationToken, progress);

            if (outcome.Cancelled)
                return null;

            lastStatus = outcome.Status;
            if (outcome.Status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
                continue;

            // لقطة الحالة الحالية (= أفضل حل حتى الآن) لاستعادتها إن جاءت المحاولة أسوأ
            var before = timetable.Lessons
                .Select(l => (lesson: l, day: l.Day, period: l.Period))
                .ToList();

            ApplySolution(outcome.Solver!, data, timetable);
            var validation = _validator.Validate(TimetableContextFactory.Build(timetable));
            totalSolutions += outcome.Solutions;

            if (best is null || IsBetter(validation, best))
            {
                best = validation;
            }
            else
            {
                foreach (var item in before)
                    item.lesson.Reschedule(item.day, item.period);
            }
        }

        if (best is null)
            return new AttemptOutcome(lastStatus, null, totalSolutions, attempts);

        return new AttemptOutcome(lastStatus, best, totalSolutions, attempts);
    }

    private static bool IsBetter(ValidationResult candidate, ValidationResult current)
    {
        if (candidate.QualityScore.Value > current.QualityScore.Value) return true;
        if (candidate.QualityScore.Value < current.QualityScore.Value) return false;
        return candidate.ConflictsCount < current.ConflictsCount;
    }

    // --------------------------------------------------------------------
    // التشغيل والإلغاء والتقدّم
    // --------------------------------------------------------------------

    private async Task<SolveOutcome> SolveAsync(
        CpModel model,
        TimeSpan timeLimit,
        int randomSeed,
        bool useParallel,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var solver = new CpSolver();
        var seconds = Math.Max(0.1, timeLimit.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        var workers = useParallel ? Math.Max(1, Environment.ProcessorCount) : 1;

        solver.StringParameters =
            $"max_time_in_seconds:{seconds} " +
            $"num_search_workers:{workers} " +
            $"random_seed:{randomSeed} " +
            "log_to_stdout:false";

        var stopwatch = Stopwatch.StartNew();
        var callback = new SolveProgressCallback(progress, stopwatch);

        var solveTask = Task.Run(() => solver.Solve(model, callback), CancellationToken.None);

        var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                callback.StopSearch();
            }
            catch
            {
                // لا ندع فشل إشارة الإيقاف يمنع الإلغاء
            }

            cancelTcs.TrySetResult(true);
        });

        using var timer = new Timer(_ =>
        {
            try
            {
                progress?.Report(
                    $"جارٍ البحث عن أفضل جدول… {stopwatch.Elapsed.TotalSeconds:F0} ث، {callback.Solutions} حل حتى الآن");
            }
            catch
            {
                // تقارير التقدّم لا يجب أن تُفشل الحل
            }
        }, null, 1000, 1000);

        var completed = await Task.WhenAny(solveTask, cancelTcs.Task).ConfigureAwait(false);

        if (completed == cancelTcs.Task)
        {
            // لا نلمس المحرك أثناء عمله؛ يُنهيه StopSearch أو ينتهي وحده عند انتهاء المهلة
            _ = solveTask.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return new SolveOutcome(CpSolverStatus.Unknown, null, callback.Solutions, cancelled: true);
        }

        var status = await solveTask.ConfigureAwait(false);
        return new SolveOutcome(status, solver, callback.Solutions, cancelled: false);
    }

    private sealed class SolveProgressCallback : CpSolverSolutionCallback
    {
        private readonly IProgress<string>? _progress;
        private readonly Stopwatch _stopwatch;
        private int _solutions;

        public SolveProgressCallback(IProgress<string>? progress, Stopwatch stopwatch)
        {
            _progress = progress;
            _stopwatch = stopwatch;
        }

        public int Solutions => Volatile.Read(ref _solutions);

        public override void OnSolutionCallback()
        {
            Interlocked.Increment(ref _solutions);

            try
            {
                _progress?.Report(
                    $"تم العثور على حل بدرجة {ObjectiveValue:F0} في {_stopwatch.Elapsed.TotalSeconds:F1} ث");
            }
            catch
            {
                // تقارير التقدّم لا يجب أن تُفشل الحل
            }
        }
    }

    private sealed class SolveOutcome
    {
        public SolveOutcome(CpSolverStatus status, CpSolver? solver, int solutions, bool cancelled)
        {
            Status = status;
            Solver = solver;
            Solutions = solutions;
            Cancelled = cancelled;
        }

        public CpSolverStatus Status { get; }
        public CpSolver? Solver { get; }
        public int Solutions { get; }
        public bool Cancelled { get; }
    }

    // --------------------------------------------------------------------
    // تطبيق الحل والتشخيص
    // --------------------------------------------------------------------

    /// <summary>تلميح الجدول الحالي لـ CP-SAT: يبدأ الحل من التوزيع القائم بدل الحفّ صفرًا</summary>
    private static void HintCurrentAssignment(CpModel model, ModelData data, Timetable timetable)
    {
        foreach (var lesson in timetable.Lessons)
        {
            if (!data.LessonVars.TryGetValue(lesson.Id, out var slots)) continue;

            var current = slots.FirstOrDefault(s => s.day == lesson.Day && s.period == lesson.Period.Value);
            if (current.v is null) continue;

            foreach (var (_, _, v) in slots)
                model.AddHint(v, ReferenceEquals(v, current.v) ? 1 : 0);
        }
    }

    private static void ApplySolution(CpSolver solver, ModelData data, Timetable timetable)
    {
        foreach (var lesson in timetable.Lessons)
        {
            if (!data.LessonVars.TryGetValue(lesson.Id, out var lessonVars))
                continue;

            foreach (var (day, period, v) in lessonVars)
            {
                if (solver.Value(v) == 1)
                {
                    lesson.Reschedule(day, PeriodNumber.Create((byte)period));
                    break;
                }
            }
        }
    }

    private static IReadOnlyList<string> FindFixedLessonConflicts(Timetable timetable)
    {
        var messages = new List<string>();

        foreach (var group in timetable.Lessons
                     .Where(l => l.IsFixed)
                     .GroupBy(l => (l.TeacherId, l.Day, l.Period)))
        {
            if (group.Count() > 1)
            {
                messages.Add(
                    $"المعلم {group.First().Teacher?.FullName ?? "غير معروف"} لديه {group.Count()} حصص ثابتة في " +
                    $"{group.Key.Day} الفترة {group.Key.Period.Value}");
            }
        }

        foreach (var group in timetable.Lessons
                     .Where(l => l.IsFixed && l.RoomId is not null && l.RoomId != RoomId.Empty)
                     .GroupBy(l => (roomId: l.RoomId!, l.Day, l.Period)))
        {
            if (group.Count() > 1)
            {
                messages.Add(
                    $"الغرفة {group.First().Room?.Name ?? "غير معروفة"} تحتوي {group.Count()} حصص ثابتة في " +
                    $"{group.Key.Day} الفترة {group.Key.Period.Value}");
            }
        }

        return messages;
    }

    private static List<string> CollectWarnings(Timetable timetable)
    {
        var warnings = new List<string>();

        var roomsById = timetable.Lessons
            .Where(l => l.Room is not null && l.RoomId is not null)
            .GroupBy(l => l.RoomId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Room!);

        foreach (var lesson in timetable.Lessons)
        {
            if (lesson.RoomId is null || lesson.RoomId == RoomId.Empty) continue;
            if (!roomsById.TryGetValue(lesson.RoomId.Value, out var room)) continue;
            if (lesson.Class is null) continue;

            if (lesson.Class.StudentCount > room.Capacity)
            {
                warnings.Add(
                    $"حصة {lesson.GetDisplayName()}: عدد الطلاب ({lesson.Class.StudentCount}) " +
                    $"يتجاوز سعة الغرفة ({room.Name}: {room.Capacity})");
            }
        }

        return warnings;
    }

    private static string BuildUnplaceableMessage(ModelData data)
    {
        var lines = new List<string>
        {
            $"{data.Unplaceable.Count} حصة لا تملك أي فترة متاحة:"
        };

        foreach (var lesson in data.Unplaceable.Take(15))
            lines.Add($" • {lesson.GetDisplayName()}: {data.UnplaceableReasons[lesson.Id]}");

        if (data.Unplaceable.Count > 15)
            lines.Add($" … و{data.Unplaceable.Count - 15} حصة أخرى");

        return string.Join("\n", lines);
    }

    private string BuildFailureMessage(
        Timetable timetable, ModelData data, CpSolverStatus status, IReadOnlyList<string> warnings)
    {
        var lines = new List<string>();

        if (data.Unplaceable.Count > 0)
        {
            lines.AddRange(data.Unplaceable
                .Take(10)
                .Select(l => $" • {l.GetDisplayName()}: {data.UnplaceableReasons[l.Id]}"));
        }

        foreach (var classEntry in data.LessonsByClass)
        {
            var lessons = classEntry.Value;
            var stage = timetable.GetStageForClass(classEntry.Key);
            var days = ActiveDays(timetable, stage);
            var cfg = FindStageConfig(timetable, stage);

            var capacity = 0;
            foreach (var day in days)
            {
                var allowed = CountAllowedPeriods(timetable, stage, day);
                var cap = cfg?.GetLessonsPerDay((int)day) ?? 0;
                capacity += cap > 0 ? Math.Min(allowed, cap) : allowed;
            }

            if (lessons.Count > capacity)
            {
                var name = lessons.FirstOrDefault()?.Class?.FullName ?? classEntry.Key.ToString();
                lines.Add($" • الفصل \"{name}\" يطلب {lessons.Count} حصة بينما الخانات المتاحة {capacity} فقط.");
            }
        }

        foreach (var teacherEntry in data.LessonsByTeacher)
        {
            var lessons = teacherEntry.Value;
            var capacity = 0;

            foreach (var day in ActiveDays(timetable, null))
            {
                var allowed = CountGenericPeriods(timetable, day);
                allowed -= data.Unavailable.Count(k => k.teacherId == teacherEntry.Key && k.day == day);
                capacity += Math.Max(0, allowed);
            }

            if (lessons.Count > capacity)
            {
                var name = lessons.FirstOrDefault()?.Teacher?.FullName ?? teacherEntry.Key.ToString();
                lines.Add($" • المعلم {name} يُدرّس {lessons.Count} حصة بينما الخانات المتاحة {capacity} فقط.");
            }
        }

        if (warnings.Count > 0)
            lines.AddRange(warnings.Take(5).Select(w => $" • {w}"));

        if (lines.Count == 0)
            lines.Add($"تعذر إيجاد حل داخل الوقت المحدد (الحالة: {status}).");

        return "تعذر توليد الجدول:\n" + string.Join("\n", lines);
    }

    // --------------------------------------------------------------------
    // نموذج مساعد
    // --------------------------------------------------------------------

    private sealed class ModelData
    {
        public CpModel Model { get; } = new();

        public Dictionary<(Guid, DomainDayOfWeek, int), IntVar> Vars { get; } = new();

        public Dictionary<Guid, List<(DomainDayOfWeek day, int period, IntVar v)>> LessonVars { get; } = new();

        public Dictionary<Guid, List<Lesson>> LessonsByClass { get; } = new();

        public Dictionary<Guid, List<Lesson>> LessonsByTeacher { get; } = new();

        public Dictionary<(Guid classId, Guid subjectId), List<Lesson>> LessonsByClassSubject { get; } = new();

        public Dictionary<(Guid, DomainDayOfWeek), List<(int period, IntVar v)>> ClassDayVars { get; } = new();

        public Dictionary<(TeacherId, DomainDayOfWeek), List<(int period, IntVar v)>> TeacherDayVars { get; } = new();

        /// <summary>فترات المعلم بأزمنتها الفعلية لحساب التضارب بتداخل الزمن (المعلم المشترك بين المراحل)</summary>
        public Dictionary<Guid, List<(DomainDayOfWeek day, int start, int end, IntVar v)>> TeacherSlotTimes { get; } = new();

        public Dictionary<RoomId, List<Lesson>> LessonsByRoom { get; } = new();

        public HashSet<(TeacherId teacherId, DomainDayOfWeek day, int period)> Unavailable { get; } = new();

        /// <summary>الفترات التي أعلنت فيها معلمين صراحةً عن توفرها (تفضيل لين)</summary>
        public HashSet<(TeacherId teacherId, DomainDayOfWeek day, int period)> Declared { get; } = new();

        /// <summary>أوقات أغلقها المعلم بأولوية منخفضة: مسموحة لكن يُعاقَب الجدولة فيها</summary>
        public HashSet<(TeacherId teacherId, DomainDayOfWeek day, int period)> SoftBlocked { get; } = new();

        /// <summary>المعلمون الذين لديهم إعلان توفر واحد على الأقل</summary>
        public HashSet<TeacherId> DeclaredTeachers { get; } = new();

        public List<Lesson> Unplaceable { get; } = new();

        public Dictionary<Guid, string> UnplaceableReasons { get; } = new();
    }
}

internal static class ModelDataExtensions
{
    public static List<TValue> GetOrAdd<TKey, TValue>(
        this Dictionary<TKey, List<TValue>> dictionary, TKey key)
        where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = new List<TValue>();
            dictionary[key] = list;
        }

        return list;
    }
}
