# BLCA Admin Manger 1.1.4

تطوير BLCA Development Team.

يحتوي مجلد `Source` على المصدر القابل للتعديل للبرنامج، والمكتبة الأساسية، والاختبارات، وأمثلة الإعدادات. الملف `BLCA Admin Manger.exe` الموجود بجانب مجلد `Source` هو إصدار Windows x64 المنشور ويضم بيئة التشغيل. لتشغيل تعديلات المصدر، ابنِه ثم شغّل الملف الناتج في مجلد مخرجاته.

## البناء من المصدر

استخدم Windows مع حزمة .NET 8 SDK. يحدد ملف `global.json` الإصدار 8.0.423 أو إصدارًا أحدث مثبتًا من سلسلة .NET 8. يحتاج تطبيق Windows Forms أيضًا إلى حزم استهداف Windows Desktop.

نفّذ أوامر PowerShell التالية من هذا المجلد:

```powershell
dotnet restore .\Manager\App\BLCA.Admin.Manager.csproj --configfile .\NuGet.Config
dotnet build .\Manager\App\BLCA.Admin.Manager.csproj -c Release --no-restore
dotnet restore .\Tests\BLCA.Admin.Tests.csproj --configfile .\NuGet.Config
dotnet run --project .\Tests\BLCA.Admin.Tests.csproj -c Release --no-restore
```

ينتج الملف `Manager\App\bin\Release\net8.0-windows\BLCA Admin Manger.exe`. شغّله مع بقية الملفات الموجودة في مجلد المخرجات. يحتاج هذا البناء إلى .NET 8 Desktop Runtime على الجهاز الذي يشغّله.

```powershell
& '.\Manager\App\bin\Release\net8.0-windows\BLCA Admin Manger.exe'
```

يمسح ملف `NuGet.Config` مصادر الحزم البعيدة. تستخدم المشاريع مراجع محلية ولا تحتوي على مراجع لحزم خارجية. يتطلب البناء دون اتصال تثبيت SDK وحزم الاستهداف مسبقًا. أما النشر الذي يضم بيئة التشغيل فيحتاج أيضًا إلى حزم بيئة التشغيل محليًا أو مصدر حزم مضبوط.

لإنشاء حزمة Windows x64 تضم بيئة التشغيل، عند توفر حزمها المطلوبة:

```powershell
dotnet publish .\Manager\App\BLCA.Admin.Manager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --configfile .\NuGet.Config -o .\publish
```

يُنشئ هذا الأمر `Source\publish\BLCA Admin Manger.exe`، وتضم نتيجة النشر بيئة التشغيل المطلوبة. شغّل الناتج من `publish` بعد البناء أو انسخ مخرجات النشر التي تريد توزيعها إلى مجلد الإصدار. ملف التشغيل الجاهز بجانب `Source` يُحدّث عند نسخ نتيجة النشر إليه.

```powershell
& '.\publish\BLCA Admin Manger.exe'
```

يضمّن مشروع التطبيق الشعار `Manager\App\Assets\BLCA_Logo.png` ويستخدم `Manager\App\Assets\BLCA.ico` أيقونةً لملف Windows التنفيذي. احتفظ بالملفين عند نسخ المصدر.

## نقل بيانات إصدار سابق

عند ترقية خادم قائم، أوقف الخادم وبرنامج الإدارة وأي وكيل خلفي أولًا. احتفظ بنسخة من مجلد بيانات الإدارة الحالي داخل ملف التعريف الفعلي، ثم أعد تسمية هذا المجلد إلى `BLCAAdminSuite` في الموضع نفسه، مع الاحتفاظ بكامل محتواه، بما فيه `config.json` و`config.rev` و`audit` و`manager-data` والباندات وبقية الملفات. شغّل المود المحدّث وبرنامج BLCA Admin Manger 1.1.4 معًا، ثم اختر ملف التعريف أو مجلد `BLCAAdminSuite` من واجهة البرنامج. بهذه الخطوات تستمر قراءة الصلاحيات والباندات والسجل الموجود بعد نقل المجلد يدويًا.

للاحتفاظ باختيارات المجلدات المحلية، يمكن أثناء إغلاق البرنامج إعادة تسمية مجلد تفضيلاته الحالي ضمن `%LOCALAPPDATA%` إلى `BLCAAdminManager` مع الاحتفاظ بملف `settings.json`. ويمكن إعادة اختيار ملف التعريف ومجلد السجلات من الواجهة بعد التحديث.

## الاتصال بملف تعريف الخادم

شغّل `BLCA Admin Manger.exe`، واختر مجلد ملف التعريف الفعلي لخادم Arma Reforger، ثم اتصل. يمكنك أيضًا اختيار مجلد `BLCAAdminSuite` الموجود داخله. يقرأ البرنامج الملفات التي يستخدمها مود الخادم المتوافق ويكتب إليها في ذلك المجلد. إذا كان الخادم على جهاز آخر، فيجب أن يتيح المسار المحدد الوصول إلى ملفات الخادم الفعلية بالصلاحيات اللازمة.

ينشئ مود الخادم بياناته داخل ملف التعريف أثناء تشغيل الخادم. أضف الإداريين واضبط صلاحياتهم في البرنامج، ثم استخدم لوحة الإدارة داخل اللعبة لتسجيل الدخول وتفعيل الجلسة المخصصة. يوضح `Examples\config.json` و`Examples\trusted-player-metadata.csv` صيغ البيانات المدعومة. راجع قيم الأمثلة قبل استخدامها مع ملف تعريف فعلي.

لمتابعة معالجة سجلات التدقيق والطلبات أثناء إغلاق النافذة الرئيسية، شغّل الوكيل الخلفي مع مسار ملف التعريف الفعلي. أضف مجلد سجلات المحرك عند الحاجة إلى استيراد سجلات المحرر:

```powershell
& '.\BLCA Admin Manger.exe' --agent --profile 'C:\ReforgerServer\profile' --logs 'C:\ReforgerServer\profile\logs'
```

نفّذ الأمر من مجلد مخرجات البناء أو النشر. يظهر الوكيل في منطقة إشعارات Windows، ويمكن إيقافه من قائمته.

تُحفظ تفضيلات البرنامج في `%LOCALAPPDATA%\BLCAAdminManager\settings.json`، وبيانات الخادم في مجلد `BLCAAdminSuite` داخل ملف التعريف. تستخدم المشاريع ومساحات الأسماء الداخلية اسم `BLCA.Admin`.

دليل الاستخدام الكامل موجود في `..\README_AR.md`، والدليل الإنجليزي في `..\README_EN.md`. يحتفظ الشعار المضمّن بحروف BLC الواردة في الصورة الأصلية، واسم المنتج هو BLCA Admin Manger.
