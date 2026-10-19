using System;
using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;
using AwadTimetable.Solver;
using Microsoft.Extensions.DependencyInjection;

namespace AwadTimetable.UI.Services;

/// <summary>مفاتيح الإعدادات الخاصة بمحرك FET</summary>
public static class FetSettings
{
    public const string FetClPathKey = "Generator.FetClPath";
}

/// <summary>مفاتيح الإعدادات الخاصة بمحرك aSc</summary>
public static class AscSettings
{
    public const string AscExePathKey = "Generator.AscExePath";
    public const string ExportFolderKey = "Generator.AscExportFolder";
}

/// <summary>يختار مُولِّد الجدول حسب المحرك المحفوظ في الجدول</summary>
public interface IGeneratorResolver
{
    ITimetableGenerator Resolve(GeneratorEngine engine);
}

public sealed class GeneratorResolver : IGeneratorResolver
{
    private readonly CpSatTimetableSolver _cpSat;
    private readonly IServiceProvider _services;

    public GeneratorResolver(CpSatTimetableSolver cpSat, IServiceProvider services)
    {
        _cpSat = cpSat ?? throw new ArgumentNullException(nameof(cpSat));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ITimetableGenerator Resolve(GeneratorEngine engine) => engine switch
    {
        GeneratorEngine.CpSat => _cpSat,
        GeneratorEngine.Fet => _services.GetRequiredService<FetEngine>(),
        GeneratorEngine.Asc => _services.GetRequiredService<AscEngine>(),
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "محرك توليد غير معروف"),
    };
}
