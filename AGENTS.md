# AGENTS.md — AwadTimetable

تطبيق WPF عربي لبناء الجداول المدرسية (C# / .NET).

## مرجع دائم
قبل العمل على القيود أو التوليد أو الطباعة، راجع:
- `docs/TIMETABLING_REFERENCES.md` — كتالوج FET و aSc/EduPage الكامل + خريطة حالة كل ميزة + خريطة الطريق.

## البناء والاختبار
- Build: `dotnet build AwadTimetable.sln`
- Test: `dotnet test AwadTimetable.Tests\AwadTimetable.Tests.csproj`
- المحرّك: CP-SAT (OR-Tools 9.10) بمعرّفات `(Guid, day, period)`.

## ملاحظات تحرير
- الملفات UTF-8؛ `read`/`edit` يتعاملان مع العربية جيداً، أما bash فقد يُظهرها مشوّهة (استخدم PowerShell + `UTF8Encoding($false)` للتحرير البرمجي).
- لا كتابة على قاعدة بيانات المستخدم بلا نسخة احتياطية.
