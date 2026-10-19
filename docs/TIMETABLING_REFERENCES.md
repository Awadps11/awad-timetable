# مرجع التوليد والقيود والطباعة — FET و aSc / EduPage

مرجع دائم (living document) يجمع كل ما جُمع عن محرّكي/أنظمة الجدولة المرجعية (FET و aSc/EduPage)
بصيغة قابلة للتنفيذ، مع **خريطة حالة** لكل ميزة في تطبيقنا `AwadTimetable`.

> قواعد الاستخدام:
> - كل قيد/ميزة هنا لها سطر في «خريطة القيود الموحّدة» بحالته: `منفّذ` / `جزئي` / `غير منفّذ`.
> - عند تنفيذ ميزة، حدّث حالتها واسم ملف الهدف (`file:line`).
> - أسماء القيود التقنية بالإنجليزية كما في الأنظمة الأصلية، والوصف بالعربية.

---

## 0. فهرس سريع
1. FET — نظرة عامة
2. FET — قيود الزمن (كاملة + المعاملات)
3. FET — قيود المكان (كاملة + المعاملات)
4. FET — خوارزمية التوليد (Recursive Swapping)
5. FET — إعدادات التوليد والتشغيل
6. FET — الاستيراد/التصدير والإخراج والطباعة
7. aSc / EduPage — نظرة عامة وأصناف القيود
8. aSc / EduPage — التوليد والإرخاء والقفل والتحسين
9. خريطة القيود الموحّدة + حالة التنفيذ لدينا
10. خريطة الطريق (الأولويات)
11. المصادر

---

## 1. FET — نظرة عامة
- FET = **Free Timetabling Software**، رخصة AGPLv3، مبني على Qt، يعمل على Windows/Linux/macOS.
- الإصدار المرجعي وقت الجمع: **FET-7.12.0**.
- مُترجم بالعربية ضمن عشرات اللغات.
- بنية طلاب مرنة: **years → groups → subgroups** (السنوات/المجموعات قابلة للتداخل، والمجموعات الفرعية غير متداخلة). يمكن تعريف «طالب فرد» كمجموعة مستقلة.
- حدود المحرك: أيام ≤ 1000، حصص/يوم ≤ 1440، فصول студентов ≤ 30000، أنشطة ≤ 500000، قاعات/مبانٍ ≤ 30000، عدد غير محدود فعلياً للمعلمين/المواد/الوسوم/القيود.
- النشاط (activity) له: مجموعة طلاب، مادة، معلّم(ون) متعددون، وسوم (activity tags)، مدة كلية، تقسيم (split)، min days، وزن، متتالية.
- **كل قيد له وزن من 0.0% إلى 100.0%**، وبعض القيود «إلزامية» مسموح لها بـ 100% فقط.

## 2. FET — قيود الزمن

المجموعة الأساسية الإلزامية (100% فقط):
- **Break**: يوم/فترات غير متاحة لجميع المعلمين والطلاب.
- **Basic compulsory time constraints**: لا يمكن لمعلّم/طالب أن يكون في نشاطين بنفس الوقت (تولّد آلياً).

### 2.1 للمعلّمين (Teacher) — غالباً صورتان: معلّم واحد / كل المعلّمين
| القيد | المعاملات | المعنى |
|---|---|---|
| Not available | معلّم + أيام/فترات | فترات لا يعمل فيها (100% فقط) |
| Max days per week | معلّم + عدد الأيام | أقصى أيام عمل أسبوعياً |
| Min days per week | معلّم + عدد الأيام | أدنى أيام عمل (لا يحسب الأيام الفارغة) |
| Max gaps per day | معلّم + عدد الفجوات | أقصى فجوات يومياً |
| Max gaps per week | معلّم + عدد الفجوات | أقصى فجوات أسبوعياً |
| Max hours daily | معلّم + عدد | أقصى حصص يومياً |
| Min hours daily | معلّم + عدد | أدنى حصص يومياً |
| Max hours continuously | معلّم + عدد | أقصى حصص متتالية |
| Max hours daily with activity tag | معلّم + وسم + عدد | أقصى حصص يومياً لوسم معيّن |
| Max hours continuously with tag | معلّم + وسم + عدد | أقصى حصص متتالية لوسم |
| Min hours daily with activity tag | معلّم + وسم + عدد | أدنى حصص يومياً لوسم |
| Working in an hourly interval, max days per week | معلّم + فاصل (أيام×فترات) + أقصى أيام | كبح العمل في نافذة زمنية |
| Min gaps between an ordered pair of activity tags | وسم(1) ثم وسم(2) + أقل عدد فجوات | ترتيب/تباعد وسمين |
| Min resting hours | معلّم + عدد | أقل ساعات راحة (للورديات) |

### 2.2 للطلاب (Student sets)
نفس عائلة المعلّمين تقريباً: Not available، Max/Min days per week، Max gaps per day/week،
Max/Min hours daily، Max hours continuously، Max span per day، Max/Min hours daily with tag،
Max hours continuously with tag، Min gaps between ordered tag pair، Working in hourly interval max days/week،
Min resting hours، بالإضافة إلى:
- **Max span per day**: أقصى امتداد لليوم الدراسي (من أول حصة لآخرها).
- **Begins early (max beginnings at second hour)**: أقصى عدد مرات بدء اليوم من الحصة الثانية.

### 2.3 للأنشطة / الأنشطة الفرعية (Activities / Subactivities)
| القيد | المعاملات | المعنى |
|---|---|---|
| Single preferred starting time | نشاط + يوم/حصة | وقت بدء مفضّل واحد |
| Set of preferred starting times | نشاط/مجموعة/أنشطة فرعية + قائمة أوقات | مجموعة أوقات مفضّلة |
| Set of preferred time slots | نشاط/مجموعة + أيام×فترات | شرائح مفضّلة |
| Min days between a set of activities | مجموعة + min days + علامة متتالية | يُضاف تلقائياً للأنشطة المقسمة (افتراضي 95%) |
| Max days between a set of activities | مجموعة + max days | أقصى أيام بين المجموعة |
| Activity(ies) ends students day | نشاط/مجموعة | يُلزم النشاط بأن يكون آخر يوم الطلاب |
| Same starting time (day+hour) | مجموعة أنشطة | نفس البداية (يوم+حصة) |
| Same starting day (any hours) | مجموعة | نفس اليوم فقط |
| Same starting hour (any days) | مجموعة | نفس الحصة فقط |
| Not overlapping | مجموعة أنشطة/وسوم | لا تتداخل |
| Two activities are consecutive | نشاطان | متتاليان بنفس اليوم ومتجاورة ومُرتّبان |
| Two/Three activities are grouped | 2 أو 3 أنشطة | نفس اليوم، متجاورة، الترتيب حر |
| Two activities are ordered | نشاطان | الترتيب فقط |
| Min gaps (hours) between a set of activities | مجموعة + عدد | أقل فجوات بين مجموعة |
| Occupies max time slots from selection | مجموعة + (أيام×فترات) + أقصى | كبح مرن على مجموعة خانات |
| Occupies min time slots from selection | مجموعة + اختيار + أدنى | عكس السابق |
| Max simultaneous activities from a set in selected slots | مجموعة + اختيار + أقصى تزامن | حدّ تزامن مرن |

> أفكار عملية: قيود «أوقات مفضّلة» تُفرض بوزن جزئي فتُحسّن الجودة بدل الاستحالة؛ استخدم
> min-days 95% → 99.75% → 100% تدريجياً؛ احذف min-days الزائدة بعد فرض same-starting.

## 3. FET — قيود المكان

- **Basic compulsory space constraints**: القاعة تستضيف نشاطاً واحداً بنفس الوقت (100%).
- **A room is not available**: قاعة + أيام/فترات (100%).

### 3.1 للمعلّمين
- **Home room(s)** / **set of home rooms**، **Max room changes per day/week**، **Min gaps between room changes**،
  **Max building changes per day/week**، **Min gaps between building changes**.

### 3.2 للطلاب (Student sets)
نفس عائلة المعلّمين: Home room(s)، Max room changes/day/week، Min gaps between room changes،
Max building changes/day/week، Min gaps between building changes.

### 3.3 قاعات مفضّلة (Preferred rooms)
- لمادة، و/أو وسم (subject، subject+tag)، أو لنشاط/نشاط فرعي منفرد.
- القاعة المفضّلة تتجاوز القاعة الأم (home room).

### 3.4 لمجموعة أنشطة
- **Have the same room if they are consecutive**، **Occupy a maximum number of different rooms**.
- **Room capacity**: للقاعة سعة، وللنشاط عدد طلاب (تلقائي إذا −1).

## 4. FET — خوارزمية التوليد (Recursive Swapping)
- خوارزمية heuristic تُحاكي مجدولاً بشرياً (استبدلت الخوارزمية الوراثية في v5.0.0, 2007).
- الحالة مخزّنة كمصفوفتين `times[]` + `rooms[]`، تبدأ غير موزّعة (unallocated).

الخطوات:
1. **ترتيب الأنشطة بالأصعب أولاً** (most-difficult-first) → تسريع نحو 10×.
2. ضع النشاط في أول شريحة صالحة؛ عند وجود عدة خيارات اختر **عشوائياً**.
3. إن لم يوجد مكان صالح: لكل شريحة احسب الأنشطة المتعارضة، اختر **أكثر الشروع خلوّاً (emptiest slot)**،
   ثم **أخرج (eject)** المتعارضات و**استدعِ recursively** لإعادة توزيعها.
   - حدّ عمق التتبع: **≤ 14**.
   - حدّ عدد الاستدعاءات التتبعية: **≤ 2 × عدد الأنشطة الداخلية**.
   - من المستوى **≥ 5** يُجرّب «أفضل شريحة» فقط ثم يتراجع (unwind).
4. إن فشل المستوى 0، ضع النشاط عنوةً وأفرغ المتعارضات.
- **Tabu list** بحجم (عدد الأنشطة × حصص/أسبوع) لمنع الدخول في حلقة.
- فجوات وبدايات الطلاب: مجموع الامتداد ≤ مجموع الحصص الأسبوعية للمجموعة الفرعية.

## 5. FET — إعدادات التوليد والتشغيل
- أوضاع: **Generate new** (يستبدل النتائج)، **Generate multiple variants**، توزيع نصف آلي/يدوي، **قفل الأنشطة (locking)**.
- خيارات: حدّ زمني لكل تشغيل (مثلاً ≤ 20 دقيقة للحالات الصعبة)، عدد المتغيّرات، **بذرة عشوائية (رقمان صحيحان)**
  تُحفظ مع النتائج لإعادة الإنتاج، معايير الإيقاف (انتهاء الوقت/توريع الكل)، عدّاد الأنشطة الموضوعة، عرض ترتيب الوضع الأولي.
- سجلات: `conflicts.txt`، قائمة المخالفات اللينة (soft-conflicts)، ملف الفترات الحرة للمعلمين، إحصائيات.
- لا يوجد خيار تعدّد أنوية موثّق في المصادر.

## 6. FET — الاستيراد/التصدير والإخراج والطباعة
- **الإدخال**: ملف XML معياري (.fet) قابل للتحرير يدوياً/بمحرر XML.
- **CSV import** (UTF-8، فاصل `,`، اقتباس `"`، والقوائم المتعددة تُدمج بـ `+`):
  Students (year/group/subgroup + أعداد)، Teachers، Subjects، Activity Tags، Rooms+Buildings
  (قاعة، سعة، مبنى)، Activities (طلاب، مادة، معلمون، وسوم، مدة كلية، تقسيم `1+1+1`، min days، وزن، متتالية).
  الاستيراد ينشئ تلقائياً المواد/المعلمين/الوسوم غير المعروفة (لكن لا الطلاب).
- **CSV export**: الأنشطة؛ إحصائيات الأنشطة؛ **Timetable CSV** (Activity Id، اليوم، الحصة، الطلاب، المادة، المعلمون، الوسوم، القاعة، تعليقات؛ المدة > 1 → سطر لكل حصة).
- **الإخراج**: HTML (xhtml قابلة للتنسيق بـ CSS)، XML، CSV، مجلد لكل تشغيل مع `index`.
- **HTML level 1–7**: 2 = جداول صغيرة (افتراضي)، 5 = تمييز عند المرور على الأسماء المتطابقة بـ JS، 7 = ملوّن.
  خيارات: طباعة تفصيلية، طباعة الوسوم، تمييز غير المتاح/الفترات الفاصلة، تقسيم الجداول بمحور زمني يومي،
  تكرار الأسماء يميناً، طباعة الأنشطة المتزامنة في خانة واحدة.
- ملفات: جداول للطلاب/المعلمين/القاعات + إحصائيات + مخالفات لينة + conflicts + الفترات الحرة + اسم المؤسسة.
- **طباعة**: `page-break-before:always` على الجداول؛ `table.modulo2` لجدولين/صفحة؛ أو عبر تعريف الطابعة 2/4/8 لكل ورقة.

## 7. aSc / EduPage — نظرة عامة وأصناف القيود
من كتالوج الجمع السابق (استيراد/تصدير XML عبر `asc-full-2024.xml` كعيّنة):

أصناف القيود الأساسية في aSc (Timetable → Constraints):
- **قيود المعلّمين**: عدم توفّر، أقصى/أدنى ساعات يومية/أسبوعية، فجوات، أيام عمل، تتابع.
- **قيود الفصول/المجموعات**: عدم توفّر، أقصى ساعات يومية، فجوات، بداية/نهاية اليوم، فترات مزدوجة.
- **قيود المواد**: الأوقات المفضّلة/غير المسموحة، أقصى حصص يومية، **علاقات زوج المواد**
  (لا نفس اليوم / لا تتابع)، مادة مرتين يومياً (لا متتاليتين / متتاليتين)، مادة مرة واحدة يومياً، **block lessons**.
- **قيود القاعات**: سعة، قاعة مفضّلة/إلزامية، قاعة للفصل، تعارضات القاعات.
- **قيود المعلمين–المواد/الفصول**: ربط العلاقات، ألّا يكون معلّم معيّن لمادة معيّنة.

## 8. aSc / EduPage — التوليد والإرخاء والقفل والتحسين
- **تعقيد التوليد**: أوضاع **Normal / Large / Huge** تؤثر في عمق البحث وميزانيته.
- **إرخاء القيود (Constraint relaxation)** عند تعذّر الحل + **تقرير القيود المخروقة** (أي قيد وكم خرقه).
- **وضع المسودة (Draft mode)**: توليد سريع تقريبي قبل الطلب النهائي.
- **قفل/تثبيت الحصص (lock/pin lessons)**: تثبيت حصص مجمّدة لا تُحرّك أثناء إعادة التوليد.
- **Improve / تحسين موضعي**: تحسين حل قائم دون إعادة التوليد الكامل.
- **الأنماط/القالب**: تعقيد التوليد + عدّاد المحاولات + الوقت.

## 9. مطابقة المشروع للمرجع — حالة التنفيذ الفعلية

> تم التحقق من الشيفرة الفعلية. الحالات: `✅ منفّذ`، `🟡 جزئي`، `⬜ غير منفّذ`.
> «مدمج» = قيد داخل `Core\Constraints\Constraints.cs` يُشغّله المدقق (وبعضه داخل هدف CP-SAT)،
> «قاعدة مستخدم» = عبر `RuleKind` في `RuleCompiler.cs`.

### 9.1 قواعد المستخدم المنفّذة (RuleKind)
| القيد | FET | aSc | الحالة | الملف |
|---|---|---|---|---|
| EndTime (منع بعد فترة) | ends students day | نهاية يوم | ✅ | `RuleKind.EndTime` · `RuleCompiler.cs` |
| TeacherLink / Availability (حجب أوقات) | Not available | عدم توفّر | ✅ | `RuleCompiler.SlotBlockRule` |
| SubjectDistribution (حد يومي للمادة) | Max hours daily | أقصى حصص يومية | ✅ | `RuleKind.SubjectDistribution` |
| MaxConsecutive | max hours continuously | تتابع | ✅ | `RuleKind.MaxConsecutive=4` |
| MaxGapsPerDay | max gaps per day | فجوات يومية | ✅ | `RuleKind.MaxGapsPerDay=5` |
| DailyLoad (أدنى/أقصى يومي) | min/max hours daily | أقصى ساعات يومية | ✅ | `RuleKind.DailyLoad=6` |
| MaxDaysPerWeek | max days per week | أيام العمل | ✅ | `RuleKind.MaxDaysPerWeek=7` |
| MaxGapsPerWeek | max gaps per week | فجوات أسبوعية | 🟡 لين فقط | `RuleKind.MaxGapsPerWeek=8` |
| MaxSpanPerDay | max span per day | — | ✅ | `RuleKind.MaxSpanPerDay=9` |
| BeginsEarly | begins early (2nd hour) | — | ✅ | `RuleKind.BeginsEarly=10` |
| SubjectPair (لا نفس اليوم/لا تتابع) | — | علاقة مواد | ✅ | `RuleKind.SubjectPair=11` |
| SubjectTwicePerDay (مع التتابع) | — | — | ✅ | `RuleKind.SubjectTwicePerDay=12` |
| BlockLessons (كتلة بلا فجوات) | grouped / block lessons | block lessons | 🟡 | `RuleKind.BlockLessons=13` |
| SameStartingTime | same starting time | — | 🟡 لين تقريبي | `RuleKind.SameStartingTime=14` |
| NotOverlapping | not overlapping | — | ✅ (مدقق) | `RuleKind.NotOverlapping=15` |
| MinDaysBetweenActivities | min days between | min days | ✅ | `RuleKind.MinDaysBetweenActivities=16` |
| PreferredRoom | home rooms | قاعة مفضّلة | 🟡 (تخزين + مطابقة) | `RuleKind.PreferredRoom=17` |
| RoomChangeLimit | max room changes/day/week | — | ✅ (صلب/لين، مُرمَّز في المحرك) | `RuleKind.RoomChangeLimit=18` |
| MinDaysPerWeek | min days per week | أيام العمل | ✅ | `RuleKind.MinDaysPerWeek=19` |

النطاق (معلم/فصل/مادة/قاعة) والأولوية (صلب/عالية/متوسطة/منخفضة/تجاهل) مدعومة، والمعاملات JSON
(`days,periods,afterPeriod,maxPerDay,maxConsecutive,maxGapsPerDay,minPerDay,`
`maxDaysPerWeek,maxGapsPerWeek,maxSpanPerDay,maxBeginningsSecondHour,`
`secondTargetId,noSameDay,noConsecutive,mustBeConsecutive,minDaysBetween,`
`maxRoomChangesPerDay,maxRoomChangesPerWeek`) تُصرَّف في `RuleCompiler.Compile` وتُطبَّق صلبةً في
`CpSatTimetableSolver.AddUserExtensionHardConstraints` + `AddUserDayScopeHardConstraints`
ولينةً في `BuildUserExtensionObjectiveTerms` + `AddUserDayScopeSoftTerms`، ويُتحقق منها في `UserRulesConstraint`.
ملاحظة: `MaxGapsPerWeek`/`SameStartingTime` لينة فقط، و`PreferredRoom`/`RoomChangeLimit` لم تُرمَّز في المحرك بعد.
ملف الاختبارات: `AwadTimetable.Tests\RuleCompilerTests.cs`.

### 9.2 القيود المدمجة المنفّذة (وكان المستند السابق يعدّها غير منفّذة)
| القيد | النوع | FET | aSc | الحالة | الملف:سطر |
|---|---|---|---|---|---|
| تعارض المعلم | صلب | basic compulsory | تعارض معلم | ✅ | `Constraints.cs:206` |
| تعارض القاعة | صلب | basic space | تعارض قاعة | ✅ | `Constraints.cs:298` |
| عدم توفّر المعلم (حجب صلب) | صلب | Not available | عدم توفّر | ✅ | `Constraints.cs:350` |
| حجب منخفض الأولوية (لين) | لين | — | — | ✅ | `Constraints.cs:398` |
| سعة القاعة | صلب | room capacity | سعة القاعة | ✅ | `Constraints.cs:436` |
| أقصى حصص يومية (حسب المرحلة) | صلب | max hours daily | أقصى ساعات يومية | ✅ | `Constraints.cs:481` |
| لا حصص في الاستراحة | صلب | Break | فترات استراحة | ✅ | `Constraints.cs:530` |
| أدنى حصص يومية (حسب المرحلة) | لين | min hours daily | أدنى ساعات يومية | ✅ | `Constraints.cs:564` |
| حصة مزدوجة عبر الاستراحة | لين | — | فترة مزدوجة | ✅ | `Constraints.cs:609` |
| الانتقال بين المباني | لين | building changes | — | 🟡 يكتشف تتابع مبنيين فقط (لا حدّ يومي/أسبوعي) | `Constraints.cs:669` |
| تفضيل المعلم (الفترات المعلنة) | لين | preferred times | تفضيل | ✅ | `Constraints.cs:940` |
| الفترات المتتالية | لين | max hours continuously | تتابع | ✅ (`RuleKind.MaxConsecutive` لكل نطاق) + قيد مدمج افتراضي 2 | `Constraints.cs` · `RuleCompiler.cs` |
| توزيع الحصص على الأيام | لين | — | توزيع | ✅ | `Constraints.cs:1051` |
| أدنى أيام أسبوعية | لين | min days per week | أيام العمل | ✅ (`RuleKind.MinDaysPerWeek` لكل نطاق) + قيد مدمج لكل فصل | `Constraints.cs` · `RuleCompiler.cs` |
| تفضيل/مطابقة القاعة | لين | preferred rooms | قاعة مفضّلة | 🟡 يعتمد على أعلام المادة/القاعة، لا قوائم مفضّلة | `Constraints.cs:1130` |
| توزيع المواد على الأسبوع | لين | — | توزيع المواد | ✅ | `Constraints.cs:1182` |
| توازن عبء المعلم (أقصى يومي/أسبوعي) | لين | max hours daily/week | أقصى ساعات | ✅ | `Constraints.cs:1218` |
| مادة مرة واحدة يومياً | لين | — | مادة مرة واحدة يومياً | ✅ | `Constraints.cs:1271` |

ملاحظة مهمة: هدف CP-SAT (`BuildObjective`) يشمل: تجاوز الهدف اليومي للفصل، نافذة تتابع بثلاث حصص،
عجز الأيام الدنيا، توزيع المادة، الحجب المنخفض، تفضيل المعلم، وقواعد المستخدم اللينة.
أما `RoomPreference` و`BuildingChange` و`MinPeriodsPerDay` و`Distribution` فهي **مدقق فقط** ولا تدخل هدف CP-SAT.

### 9.3 قيود المرجع المتبقية (فجوات حقيقية)
| القيد | FET | aSc | الحالة |
|---|---|---|---|
| Max days per week (معلم/طالب) | ✅ | أيام العمل | ✅ `RuleKind.MaxDaysPerWeek` — صلب في المحرك لكل نطاق |
| Max gaps per week | ✅ | فجوات أسبوعية | 🟡 `RuleKind.MaxGapsPerWeek` — لين فقط (تقريب: مجموع الفجوات اليومية) |
| Max span per day | ✅ | — | ✅ `RuleKind.MaxSpanPerDay` — صلب في المحرك |
| Begins early (2nd hour) | ✅ | — | ✅ `RuleKind.BeginsEarly` — صلب في المحرك |
| Min/Max hours daily with activity tag | ✅ | — | ⬜ (لا مفهوم «وسم النشاط» بعد) |
| Min gaps between ordered tag pair | ✅ | علاقات الوسوم | ⬜ |
| Working in hourly interval max days/week | ✅ | — | ⬜ |
| Min resting hours | ✅ | ورديات | ⬜ |
| Min days between activities (auto 95%) | ✅ | min days | ✅ `RuleKind.MinDaysBetweenActivities` (صلب/لين) |
| Max days between activities | ✅ | — | ⬜ |
| Same starting time / day / hour | ✅ | — | 🟡 `RuleKind.SameStartingTime` — لين تقريبي فقط |
| Not overlapping (مجموعة أنشطة) | ✅ | — | ✅ `RuleKind.NotOverlapping` — مدقق صلب |
| Two/three grouped / ordered / consecutive | ✅ | block lessons | 🟡 `RuleKind.BlockLessons` يغطي المجموعة المتصلة بلا فجوات |
| Min gaps between a set of activities | ✅ | — | ⬜ |
| Occupies max/min time slots from selection | ✅ | — | ⬜ |
| Max simultaneous in selected slots | ✅ | — | ⬜ |
| علاقة زوج المواد (لا نفس اليوم/لا تتابع) | — | ✅ | ✅ `RuleKind.SubjectPair` (noSameDay/noConsecutive) |
| مادة مرتين يومياً لا متتاليتين/متتاليتين | — | ✅ | ✅ `RuleKind.SubjectTwicePerDay` (mustBeConsecutive) |
| Home room(s) / قوائم قاعات مفضّلة | ✅ | قاعة مفضّلة/إلزامية | 🟡 `RuleKind.PreferredRoom` — يخزّن القاعة المفضّلة، فحص مطابقة فقط (القاعات ثابتة لكل حصة) |
| Max room/building changes/day/week | ✅ | — | ✅ لتغيّر القاعة (`RuleKind.RoomChangeLimit` مع ترميز محرك صلب/لين)، 🟡 للمبنى |
| Min gaps between room/building changes | ✅ | — | 🟡 (جزئي) |
| Same room if consecutive | ✅ | — | ⬜ |
| Occupy max different rooms | ✅ | — | ⬜ |

### 9.4 ميزات التوليد/التشغيل/الطباعة (حالة فعلية)
| الميزة | FET | aSc | الحالة | الملف |
|---|---|---|---|---|
| محرك CP-SAT | — | — | ✅ | `CpSatTimetableSolver.cs` |
| محرك FET (خارجي fet-cl) | ✅ | — | ✅ | `FetEngine.cs` |
| محرك aSc (تصدير/استيراد) | — | ✅ | ✅ | `AscEngine.cs` |
| إرخاء القيود + تقرير المخالفات | soft-weights + conflicts.txt | relaxation + report | ✅ (خيار من الإعدادات + تقرير مخالفات مجمّع في نافذة التوليد) | `SettingsKeys.RelaxConstraints` · `MainWindowViewModel.cs` |
| تعقيد التوليد (Normal/Large/Huge) | وقت/بذرة/عمق | ✅ | ✅ | `GenerationOptions.Complexity` |
| وضع المسودة (Draft) | — | ✅ | ✅ (خيار `DraftMode` في الإعدادات) | `GenerationOptions.DraftMode` |
| قفل/تثبيت الحصص | Locking | ✅ | ✅ | `Lesson.IsFixed` · `Fix()` · `RespectFixedLessons` · `MainWindowViewModel.cs:3315` |
| Improve (تحسين موضعي) | semi-automatic | ✅ | ✅ | `OptimizeAsync` · `OptimizeTimetableAsync` · `optimize_timetable` |
| توليد متغيّرات متعددة | ✅ | ✅ | ✅ (خيار عدد المتغيّرات من الإعدادات) | `GenerationOptions.VariantCount` · `SolveBestByScoreAsync` |
| بذرة عشوائية وإعادة الإنتاج | ✅ | ✅ | ✅ (بذرة محفوظة في الإعدادات + وضع عشوائي عند 0) | `GenerationOptions.RandomSeed` |
| تقسيم مدة الحصة (split) | ✅ | ✅ | ✅ (قائمة الجدول (Split) + أداة `split_lesson`) | `LessonSplitter.cs` · `SplitLessonDialog.xaml` |
| CSV/XLSX استيراد وتصدير | ✅ | ✅ | ✅ | `ComponentTransfer` · `LessonCsvImporter` · `SubjectListImporter` |
| استيراد aSc XML | — | ✅ | ✅ | `AscXmlReader` · `AscXmlImportService` |
| استيراد XML لأنظمة أخرى (Untis/WinLD/Vulcan/SchILD) | — | — | ✅ | `VendorXmlImporter` |
| HTML + JS hover + levels | ✅ | ✅ | 🟡 (HTML عبر WebView2 + دمج + شعار، بلا مستويات/JS hover) | `WebReportService.cs` |
| قوالب طباعة (8 جاهزة + مخصصة + شعار + دمج) | ✅ | ✅ | ✅ | `PrintTemplate.cs` · `PrintTemplatesDialog` |
| مستشار الجدول (Advisor) | — | ✅ | ✅ | `ScheduleAdvisor.cs` |
| بدائل (3 فئات) | — | ✅ | ✅ | `SubstitutionGenerator.cs` |
| إشراف/فسح (Dozor) | — | ✅ | ✅ | `SupervisionGenerator.cs` |
| خطة انتظار (1/2/3) | — | ✅ | ✅ | `WaitingPlan.cs` · `WaitingScheduleGenerator.cs` |
| إزاحة الأيام | — | ✅ | ✅ | `DayShifter.cs` |
| مشاركة عبر QR/HTTP محلي | — | ✅ | ✅ | `ShareServerService.cs` |
| المساعد الذكي (أدوات CRUD + توليد + طباعة) | — | — | ✅ | `AssistantTools.cs` · `CrudAiTools.cs` |

### 9.5 تصحيحات للمستند السابق
كان المستند السابق يصنّف كثيراً من الميزات المنفّذة فعلاً كـ «غير منفّذ». صُحِّح التالي (كان ⬜ → صار ✅):
سعة القاعة، الاستراحة، أدنى/أقصى حصص يومية، توزيع المواد، توازن عبء المعلم، مادة مرة واحدة يومياً
(`SubjectOncePerDayConstraint`)، أدنى أيام أسبوعية (جزئي)، وقفل/تثبيت الحصص و Improve (كانا ⬜ وصارا ✅).

## 10. خريطة الطريق (محدَّثة بحسب الفجوات الحقيقية)
1. **إرخاء القيود + تقرير القيود المخروقة** — ✅ أُنجز (تبويب «التوليد» في الإعدادات + تقرير المخالفات).
2. **تعقيد التوليد (Normal/Large/Huge)** + **بذرة قابلة للضبط/الحفظ** لإعادة الإنتاج — ✅ أُنجز.
3. **وضع المسودة** (توليد سريع) — ✅ أُنجز (`DraftMode`).
4. **توليد متغيّرات متعددة** من الواجهة — ✅ أُنجز (`VariantCount`).
5. **قيود القاعات**: 🟡 `PreferredRoom` (تخزين + مطابقة) و`RoomChangeLimit` (مُرمَّز صلباً/ليناً في المحرك). يبقى: حدّ تغيّر المبنى، «نفس القاعة للمتتالية»، «أقصى عدد قاعات مختلفة».
6. **قيود المواد**: ✅ أُنجز — `SubjectPair` / `SubjectTwicePerDay` / `BlockLessons` / `MinDaysBetweenActivities` / `SameStartingTime` / `NotOverlapping`.
7. **عائلة الأيام/الفجوات**: ✅ أُنجز — `MaxDaysPerWeek` / `MaxGapsPerWeek` (لين) / `MaxSpanPerDay` / `BeginsEarly`. يبقى: min/max hours with activity tag، min resting hours، working in hourly interval.
8. **جعل `ConsecutivePeriods` و`MinDaysPerWeek` قابلين للضبط** لكل نطاق — ✅ أُنجز (`RuleKind.MaxConsecutive` + `RuleKind.MinDaysPerWeek`)، مع بقاء القيود المدمجة كقيم افتراضية.
9. **تقسيم مدة الحصة (split)** + min days التلقائي — ✅ أُنجز (`LessonSplitter` + `SplitLessonDialog` + `RuleKind.MinDaysBetweenActivities` التلقائي + أداة `split_lesson`).

## 11. المصادر
FET:
- https://lalescu.ro/liviu/fet/
- https://lalescu.ro/liviu/fet/features.html
- https://lalescu.ro/liviu/fet/doc/en/instructions.html
- https://lalescu.ro/liviu/fet/doc/en/generation-algorithm-details.html
- https://www.timetabling.de/manual/FET-manual.en.html (الدليل الكامل)

aSc / EduPage:
- كتالوج الجمع السابق من وثائق aSc/EduPage (استيراد/تصدير، قيود، توليد، طباعة).
- عيّنة aSc الحقيقية: `AwadTimetable.Tests\Fixtures\asc-full-2024.xml`.

---

_آخر تحديث: 2026-10-17 — أُضيفت أنواع قواعد جديدة (RuleKind 7..18) لعائلة الأيام/المواد/القاعات
مع ترميزها في المحرك CP-SAT والمدقق والواجهة واختباراتها؛ وأُضيفت خيارات التوليد المتقدمة
(تعقيد Normal/Large/Huge، بذرة محفوظة، عدد متغيّرات، وضع مسودة، إرخاء القيود + تقرير المخالفات)
في `GenerationOptions` وتبويب «التوليد» بالإعدادات، وترميز `RoomChangeLimit` في المحرك، وإضافة
`RuleKind.MinDaysPerWeek`، وتقسيم الحصة (Split). الحالة: **247 اختباراً ناجحاً**. (الإصدار 2.1.0)._

_تحديث لاحق (2026-10-17): إصلاح عطل «تعذر التوليد — 6 حصة لا تملك أي فترة متاحة» في المدارس
المشتركة: صار تحديد مرحلة الفصل يعتمد على **اسم الفصل** («أول ثانوي 1») لا على رقم الصف وحده
(أرقام الصفوف 1-3 تتكرر بين المراحل)، ورُبطت `ClassNames` في `Timetable`، وأُضيف احتياط في المحرك
(BSS: عند غياب توقيت لمرحلة الفصل يُجرَّب توقيت المراحل المُعرَّفة بدل الفشل الكامل)، مع تسجيل
أسباب الفشل في السجل. اختبارات `StageDetectionTests`._

_تحديث لاحق (2026-10-17): إصلاح نقل الحصة إلى فصل غير مُسند: صار `LessonPlacementEvaluator`
يمنع نقل حصة (معلم+مادة) إلى فصل ليس ضمن إسناداتها في `Timetable.Assignments` (طبقة الإسناد
المركزية، مع تجاهل الإسنادات المعطَّلة `IsEnabled=false`)، فلا يظهر الموضع المقبول بالأخضر ولا
يُسمح بالإفلات مطلقًا. إن لم تُستخدم طبقة الإسناد لهذا الزوج تُترك الحركة اليدوية حرّة. اختبارات
ثلاثة في `LessonPlacementEvaluatorTests`. الحالة: **250 اختباراً ناجحاً**._

## 12. تحديث إصلاحات تدفق «جدول جديد ← توليد» (2026-10-18)

المشكلة الفعلية لم تكن في محرك CP-SAT (اختبار واقعي لـ 12 فصلاً × 30 حصة = 360 حصة نجح)،
بل في التدفق والبيانات المحفوظة:

| الإصلاح | الملف | الأثر |
|---|---|---|
| **مساحة عمل مستقلة لكل جدول جديد**: يُنشأ سجل `School` خاص فارغ تماماً (لا معلمين/مواد/فصول/قاعات) | `CurrentSchoolService.CreateEmptyWorkspaceAsync` · `NewTimetableDialog` | جدول جديد = لا بيانات من جداول أو مدارس أخرى |
| خيار **«استخدام بيانات مدرسة محفوظة سابقاً بنفس الاسم»** (افتراضياً غير محدد) | `CurrentSchoolService.GetOrCreateWorkspaceByNameAsync` | إعادة استخدام بيانات مدرسة عند الحاجة فقط |
| **تبديل سياق مساحة العمل** عند إنشاء/فتح جدول (قوائم الإدارة تُحمَّل لمساحة الجدول فقط) | `MainWindowViewModel` · `CurrentSchoolService.SetCurrent` | لا تظهر معلمون/مواد مدرسة أخرى |
| قائمة الجداول المحفوظة تعرض **كل مساحات العمل** مع اسم المدرسة | `TimetableStorageService.ListAsync` · `DbTimetablesDialog` | فتح أي جدول من أي مدرسة |
| **الاستيراد يبدأ مساحة عمل فارغة** باسم الملف (إلا إذا كان هناك جدول مفتوح) | `MainWindowViewModel.ImportAscAsync` | الاستيراد هو المصدر الوحيد لبيانات جديدة |
| حذف الجدول ينظّف مساحة عمله (المعلمون/المواد/الفصول/القاعات/المدرسة) إن لم تعد مستخدمة | `TimetableStorageService.DeleteAsync` · `DeleteWorkspaceData` | لا تراكم بيانات مدارس محذوفة |
| **توليد تلقائي للحصص من الإسنادات** عند الضغط على «توليد» إذا كان الجدول فارغاً | `MainWindowViewModel.SyncLessonsFromAssignments` · `TeachingAssignmentLessonSync` | زر التوليد يعمل مباشرة بعد الإسناد |
| مزامنة الإسنادات **قابلة للتكرار** (إنشاء الناقص وحذف الزائد، مع عدم المساس بالحصص المثبّتة) | `Core/Services/TeachingAssignmentLessonSync.cs` | لا تكرار حصص ولا فقدان حصص مثبّتة |
| إصلاح الفهرس الفريد للجداول ليشمل الاسم + إلغاء تفرد اسم المدرسة | `TimetableStorageService.EnsureDatabaseSchema` · `TimetableDbContext` | أكثر من جدول/مساحة عمل بنفس السنة والفصل |
| إلغاء تفرد (الصف، الشعبة) للفصول | `TimetableStorageService.EnsureDatabaseSchema` · `TimetableDbContext` | عدم فشل إنشاء شعب الانتظار/الأقسام المتكررة |
| حفظ **درجة الجودة** مع الجدول | `TimetableSettings.QualityScore` | لا تعود 0% بعد إعادة الفتح |
| إعادة محاولة فتح آخر جدول محفوظ عند تغيّر المخطط + تجربة الجداول التالية | `TimetableStorageService.LoadAsync` · `LastTimetableAutoLoadAsync` | لا يظهر «تعذر فتح آخر جدول» صامتًا |
| تقليل ضجيج السجل (استعلامات EF/HTTP) | `App.ConfigureLogging` | سجلات قابلة للقراءة بدل 17MB/يوم |
| منع اختيار محرك FET بلا مسار `fet-cl` | `NewTimetableDialog` · `GenerateTimetableAsync` | لا وهم «محرك متاح» ثم فشل |

### 12.1 المرحلة الثانية (2026-10-18)

| الإصلاح | الملف | الأثر |
|---|---|---|
| **حفظ/تحميل تفريغ وندب المعلمين** (`TeacherAvailabilities`) | `TimetableStorageService.SaveAvailabilitiesAsync` · `LoadAsync` | لم يعد التوفر يُفقد عند إعادة فتح الجدول |
| **عزل أدوات المساعد الذكي** عن باقي مساحات العمل | `SchoolQueryTool` · `CrudAiTools` (`CrudSupport.GetSchoolIdAsync`) | لا يقرأ/يعدّل بيانات مدارس أخرى |
| **التراجع/الإعادة** يبدّلان مساحة العمل ويعيدان تحميل القوائم، وتُفرَّغ عند فتح/إنشاء جدول | `MainWindowViewModel.UndoAsync/RedoAsync` · `ResetHistory` · `SwitchWorkspaceAsync` | لا تراجع إلى جدول مدرسة أخرى |
| **حذف الجدول المفتوح** يفرّغ الواجهة ومساحة العمل | `OpenFromDatabaseAsync` · `CurrentSchoolService.ClearCurrent` | لا واجهة معلّقة بعد الحذف |
| **تنظيف بقايا الإعدادات** لجداول محذوفة (مقارنة غير حسّاسة لحالة الأحرف) | `EnsureDatabaseSchema` | إزالة 30 صفاً يتيماً وأمثالها |
| **حفظ مستقر**: الجدول يُحدَّث بمعرّفه نفسه بدل حذف/إنشاء في كل حفظ | `TimetableStorageService.SaveAsync` · `Timetable(Guid, …)` | ثبات معرّف الجدول |
| **عرض مساحة العمل في العنوان وشريط الحالة** | `MainWindowViewModel.WindowTitle/CurrentSchoolName` · `MainWindow.xaml` | منع الخلط بين المدارس |
| **استنتاج الصف والشعبة من اسم الفصل** عند الاستيراد | `StageHelper.ParseGrade/ParseSection` · استيراد المكوّنات وXML | صفوف صحيحة (7..12) بدل 1 دائماً |
| **فحص الجدوى قبل الحل** (تجاوز سعة الفصل/المعلم) | `CpSatTimetableSolver.DiagnoseCapacity` | رسالة دقيقة بدل «تعذر التوليد» |
| **استيراد الإسنادات** من CSV/Excel (معلم، مادة، فصل، حصص) | `Core/Services/TeachingAssignmentImporter.cs` · `ImportAssignmentsAsync` | بناء المنهج دفعة واحدة بدل الإدخال اليدوي |

اختبارات جديدة: `TeachingAssignmentSyncTests` (منها السعة والجدوى)، `TeachingAssignmentImporterTests`، `StageHelperTests`.
الحالة: **274 اختباراً ناجحاً، 0 تحذيرات، 0 أخطاء**.

### 12.2 المرحلة الثالثة (2026-10-18)

| الإصلاح | الملف | الأثر |
|---|---|---|
| **خيارات القاعات**: قاعة مخصّصة لمعلم (`Room.AssignedTeacherId`) وقاعة مشتركة (`Room.IsShared`) عند الإضافة/التطبيق على المحدد | `CoreEntities.Room` · `RoomsView.xaml` · `RoomManagementViewModel` · `EnsureDatabaseSchema` | قاعة المعلم تُسند لحصصه، والمشتركة تُستخدم للشعب بلا قاعة مخصّصة عند كفايتها |
| **ربط الحصة بقاعة** يدوياً | موجود في `LessonDialog` (RoomComboBox → `AssignRoom`) | القاعة اليدوية تتقدّم على الإسناد التلقائي |
| **أولوية إسناد القاعة**: يدوي > قاعة المعلم > قاعة الفصل المخصّصة > قاعة مشتركة (احتياط) | `Core/Services/ClassRoomAssigner.cs` | منطق واضح ومُختبَر |
| **إسناد قاعة لكل شعبة** عند وجود قاعات مُعرَّفة (وإلا تبقى الحصص بلا قاعة كما هو مقبول) | `Core/Services/ClassRoomAssigner.cs` · `PrepareForScheduling` | كل شعبة لها قاعتها؛ تعارض القاعات يحلّه المحرك |
| **مدير المدارس/مساحات العمل**: عرض بالأعداد، إعادة تسمية، نسخة، حذف، وفتح أحدث جدول | `WorkspaceService.cs` · `Views/WorkspacesDialog.xaml` · `ManageWorkspacesAsync` | إدارة كل المدارس من مكان واحد |
| **معاينة الاستيراد** قبل الكتابة: aSc (عدد المواد/المعلمين/الفصول/القاعات/الحصص)، المكوّنات، والإسنادات | `ImportAscAsync` · `ImportComponentAsync` · `ImportAssignmentsAsync` | لا كتابة قبل تأكيد المستخدم |

اختبارات: `ClassRoomAssignerTests` (6) + اختبارات سابقة.
الحالة: **280 اختباراً ناجحاً، 0 تحذيرات، 0 أخطاء**.

## 13. مراجعة متعددة الوكلاء + اختبار مقياسي حقيقي (2026-10-18)

اختبار توليدي حقيقي: مدرسة **40 معلماً، 12 فصلاً، 40 مادة، 5 أيام × 7 حصص**.
طلاب: `ScaleGenerationTests` — **420 حصة (12×35) جُدولت بنجاح في ~11 ثانية بلا تضاربات**،
والمهلة المخصصة 120 ثانية. هذا يثبت أن المحرك يعمل على مقياس مدرسة كاملة.

### 13.1 إصلاحات المحرك (بعد المراجعة)
| الإصلاح | الملف | الأثر |
|---|---|---|
| تفعيل قيود **MinDaysBetweenActivities / MinDaysPerWeek** الصلبة (كان فحص `l.Day` يُعطّلها فيصدر نجاح مع مخالفات) | `CpSatTimetableSolver.cs` (AddUserDayScope*) | القيود تُفرض فعلاً |
| **لا نجاح مع مخالفات صلبة**: تُرجع `Failure` مع قائمة المخالفات (إلا في وضع الإرخاء) | `CpSatTimetableSolver.GenerateAsync` | لا «نجاح وهمي» |
| **توزيع المهلة ديناميكياً** (المحاولة الأولى نصيب وافر، والتالية كل المتبقي إن لم يوجد حل) | `SolveBestByScoreAsync` | لا يُهدر الوقت عند تقطيع المحاولات |
| **بوابة تشغيل** (`SemaphoreSlim`) تمنع توليدين/تحسينين متزامنين (الواجهة + المساعد) | `CpSatTimetableSolver` | لا سباق على الجدول المشترك |
| **فحص حصة مثبتة بلا فترة** + **فحص السعة بحدود الحصص اليومية** | `GenerateAsync` · `DiagnoseCapacity` | رسائل دقيقة بدل استنفاد المهلة |

### 13.2 إصلاحات التخزين والمدارس
| الإصلاح | الملف | الأثر |
|---|---|---|
| **تفريغ/ندب المعلمين**: ضبط عمود `TeacherId1` + تسجيل الأخطاء + نطاق لكل جدول (`TimetableId`) | `TimetableStorageService.SaveAvailabilitiesAsync` · `LoadAsync` | يُحفظ فعلاً ولا يمسّ جداول أخرى |
| **حفظ الحصص ذرّي** (منع غياب الحصص عند فشل الحفظ) وحفظ **معرّف الحصة** | `TimetableStorageService.SaveAsync` · `Lesson(Guid, …)` | لا فقدان لحصص ولا كسر لربط البدائل |
| **نسخ مساحة العمل** يعيد ربط قاعة المعلم/قاعة الفصل/المدرس المشرف ويحفظ الرمز | `WorkspaceService.DuplicateAsync` | لا مراجع معلّقة |
| **مسح كاش المساحة** عند حذف المساحة الحالية | `WorkspaceService.DeleteAsync` · `CurrentSchoolService.ClearCurrent/Current` | لا كتابة تحت مدرسة محذوفة |
| **عزل أدوات المساعد** لقراءة/كتابة المعلمين والمواد والفصول والقاعات على مستوى المساحة | `AssistantTools.AddLessonAiTool` · `CrudAiTools` · `SchoolQueryTool` | لا تسرّب بين المدارس |

### 13.3 إصلاحات الواجهة
| الإصلاح | الملف | الأثر |
|---|---|---|
| `SwitchWorkspaceAsync` يضبط اسم المدرسة وسياق المساعد ويمسح الاختيار/التضاربات | `MainWindowViewModel` | لا خلط بين المدارس |
| **إيقاف قراءة القوائم** إن تغيّرت مساحة العمل أثناء التحميل (حاجز جيل) | `Teacher/Subject/Class/RoomManagementViewModel.LoadAsync` | لا تظهر قوائم مدرسة أخرى |
| **التوليد التلقائي عند الإقلاع** على خيط الواجهة (بدل `Task.Run`) | `MainWindowViewModel` | لا سباق على المجموعات |
| استيراد JSON وفتح الجداول يمرّان عبر تبديل المساحة ويُفرغ العرض عند الحذف/الفراغ | `ImportJson` · `OpenFromDatabaseAsync` · `ManageWorkspacesAsync` · `ClearTimetableDisplay` | لا عرض قديم عالق |
| حراسة الأزرار من النقر المزدوج في نوافذ «جدول جديد» و«المدارس» | `NewTimetableDialog` · `WorkspacesDialog` | لا إنشاء مساحات/نسخ مكررة |
| اختيار قاعة يملأ خياريها الحاليين قبل التطبيق | `RoomManagementViewModel.OnSelectedRoomChanged` | لا مسح خاطئ للخيارات |
| حفظ تلقائي بعد **التحسين** | `OptimizeTimetableAsync` | ثبات النتائج |

### 13.4 متبقٍّ (منخفض الأثر)
- مساعد محادثة لكل مساحة عمل، تذكّر آخر مساحة/جدول، تعديل قاعة حصة موجودة من لوحة الخصائص.
- تحويل مخطط EF إلى Migrations حقيقية بدل الـ ALTER اليدوي.
- قيود صلبة في المحرك غير مُنفَّذة بعد (`RoomCapacity`, `NotOverlapping`, `PreferredRoom`, `SameStartingTime`).

### 13.5 تحقّق من سعات مدارس مختلفة (2026-10-18)
`MultiSchoolScaleTests` — عشرة سيناريوهات، كلها ≥36 معلماً و≥12 فصلاً و≥50 مادة، وبعض المعلمين يدرّسون أكثر من مادة.
**جميعها نجحت** (توليد كامل بلا تضاربات وبلا حصص فاشلة؛ زمن 11–22 ثانية للحالة):

| الإعداد | الحصص | معلمون متعددو المواد | الزمن |
|---|---|---|---|
| 36t/12c/50s/5×7 | 420 | 14 | ~13s |
| 40t/12c/50s/5×7 | 420 | 10 | ~13s |
| 45t/12c/60s/5×7 | 420 | 15 | ~13s |
| 50t/12c/80s/5×7 (12 مادة/فصل) | 420 | 30 | ~13s |
| 36t/12c/80s/5×7 | 420 | 36 | ~14s |
| 60t/16c/70s/5×7 | 560 | 10 | ~22s |
| 36t/12c/50s/6×6 | 432 | 14 | ~12s |
| 48t/12c/100s/5×7 | 420 | 48 | ~13s |
| 36t/12c/55s/5×7 (14 مادة/فصل) | 420 | 19 | ~14s |
| 72t/12c/120s/5×7 | 420 | 48 | ~12s |

الخلاصة: التطبيق يولّد جداول كاملة لهذه السعات، ويوزّع المعلمين متعددي المواد بلا تعارض.

## 14. اختبارات ضغط + بيانات aSc حقيقية + FET/aSc فعلياً (2026-10-18)

### 14.1 ضغط أقسى (`StressGenerationTests`)
| الحالة | الحصص | الزمن |
|---|---|---|
| 20 فصلاً / 700 حصة / 60 معلماً / 80 مادة | 700 | ~49s |
| 20 فصلاً / 700 حصة / 40 معلماً (حِمل ثقيل) | 700 | ~48s |
| 18 فصلاً / 630 حصة / 36 معلماً / 60 مادة | 630 | ~38s |
| 25 فصلاً / 875 حصة / 80 معلماً / 120 مادة | 875 | ~67s |
| 12 فصلاً + حجب فترة كاملة لنصف المعلمين | 420 | ~34s |
| 12 فصلاً + حجب يومين كاملين لنصف المعلمين | 420 | ~53s |
| مراحل مختلطة (6/7/7 حصص يومياً) / 600 حصة | 600 | ~34s |

ملاحظة: حالة «حجب فترتين (1 و7) لنصف المعلمين» كانت **غير قابلة للحل بنيوياً** (12 فصلاً يحتاج 12 معلماً في كل فترة محجوبة ولا يوجد سوى 12 غير محجوبين)، فأُضيف فحص سعة لكل معلم داخل الاختبار لتمييز المستحيل عن عجز المحرك.

### 14.2 بيانات aSc حقيقية (`RealDataEngineTests`)
ملف المستخدم (168KB، مطابق SHA-256 للعينة `asc-full-2024.xml`) — **18 فصلاً، 38 معلماً، 34 مادة، 603 حصة**:
- اشتقاق الإسنادات من الحصص الحقيقية: **223 إسناداً**، ثم توليد من الصفر: **603/603 حصة، 0 تضاربات، ~16s**.
- **محرك aSc**: تصدير الجدول ثم قراءته مجدداً — 18 فصلاً، 38 معلماً، 34 مادة، **603 بطاقة** سليمة.
- **FET الحقيقي**: نُزّلت حزمة FET 7.12.0 الرسمية، استُخرج `fet-cl.exe` وشُغّل فعلياً:
  **603 حصة مجدولة، 0 تضاربات، ~15s**.
- أُضيف تصدير **القاعات** إلى ملف FET (`Rooms_List` + `ConstraintActivityPreferredRoom` لكل حصة لها قاعة) مع اختبار مستقل؛ ملف aSc الحالي لا يحتوي قاعات (0 وسم `classroom`).

الحالة الإجمالية: **305 اختبارات ناجحة، 0 تحذيرات، 0 أخطاء**.



