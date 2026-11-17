using System.Globalization;

namespace EasyPassVault.Droid;

/// <summary>
/// Interface texts. The key is the English text; a row of the catalog holds it in every language, in the
/// order of <see cref="Languages"/>. English is the default.
/// </summary>
internal static class L
{
    public static readonly (string Name, string Culture)[] Languages =
    [
        ("English", "en"), ("Русский", "ru"), ("简体中文", "zh-CN"), ("हिन्दी", "hi"),
        ("Español", "es"), ("العربية", "ar"), ("Français", "fr")
    ];
    private const int Russian = 1;

    private static readonly string[][] Rows = Catalog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(row => row.Split('|')).ToArray();
    private static readonly Dictionary<string, string[]> ByEnglish = Rows.ToDictionary(row => row[0]);
    // The core reports errors in Russian: this finds the row of such a message.
    private static readonly Dictionary<string, string[]> ByRussian = Rows.GroupBy(row => row[Russian]).ToDictionary(group => group.Key, group => group.First());

    public static int Language { get; private set; }
    public static string Code => Languages[Language].Culture;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Code);
    public static bool RightToLeft => Code == "ar";

    public static void Select(string? code)
    {
        int index = Array.FindIndex(Languages, language => language.Culture == code);
        Language = index < 0 ? 0 : index;
    }

    public static string T(string english) => ByEnglish.TryGetValue(english, out var row) ? row[Language] : english;

    public static string T(string english, params object[] values) => string.Format(Culture, T(english), values);

    /// <summary>A message of the core in the chosen language; an unknown one becomes a general message.</summary>
    public static string Error(string coreMessage) =>
        Language == Russian ? coreMessage
        : ByRussian.TryGetValue(coreMessage, out var row) ? row[Language]
        : T("The vault file is damaged or not supported.");

    /// <summary>A text the core returns in Russian for display (placeholder titles, the root folder).</summary>
    public static string FromCore(string russian) => ByRussian.TryGetValue(russian, out var row) ? row[Language] : russian;

    private const string Catalog = """
Vault viewer|Просмотр хранилища|密码库查看器|वॉल्ट व्यूअर|Visor de bóveda|عارض الخزنة|Visionneuse de coffre
Vault file|Файл хранилища|密码库文件|वॉल्ट फ़ाइल|Archivo de bóveda|ملف الخزنة|Fichier du coffre
No file chosen|Файл не выбран|未选择文件|कोई फ़ाइल नहीं चुनी गई|Ningún archivo elegido|لم يتم اختيار ملف|Aucun fichier choisi
Choose file|Выбрать файл|选择文件|फ़ाइल चुनें|Elegir archivo|اختيار ملف|Choisir un fichier
Master password|Главный пароль|主密码|मास्टर पासवर्ड|Contraseña maestra|كلمة المرور الرئيسية|Mot de passe principal
Open|Открыть|打开|खोलें|Abrir|فتح|Ouvrir
Version {0} · view only · offline|Версия {0} · только просмотр · без сети|版本 {0} · 仅查看 · 离线|संस्करण {0} · केवल देखने के लिए · ऑफ़लाइन|Versión {0} · solo lectura · sin conexión|الإصدار {0} · للعرض فقط · بلا اتصال|Version {0} · lecture seule · hors ligne
There is no app to choose files on this phone.|На телефоне нет приложения для выбора файлов.|此手机上没有可用于选择文件的应用。|इस फ़ोन में फ़ाइल चुनने के लिए कोई ऐप नहीं है।|No hay ninguna aplicación para elegir archivos en este teléfono.|لا يوجد تطبيق لاختيار الملفات على هذا الهاتف.|Aucune application pour choisir des fichiers sur ce téléphone.
Choose the vault file first.|Сначала выберите файл хранилища.|请先选择密码库文件。|पहले वॉल्ट फ़ाइल चुनें।|Primero elija el archivo de bóveda.|اختر ملف الخزنة أولاً.|Choisissez d’abord le fichier du coffre.
Enter the master password.|Введите главный пароль.|请输入主密码。|मास्टर पासवर्ड दर्ज करें।|Introduzca la contraseña maestra.|أدخل كلمة المرور الرئيسية.|Saisissez le mot de passe principal.
Could not read the file. Choose it again.|Не удалось прочитать файл. Выберите его заново.|无法读取文件。请重新选择。|फ़ाइल पढ़ी नहीं जा सकी। इसे फिर से चुनें।|No se pudo leer el archivo. Elíjalo de nuevo.|تعذّرت قراءة الملف. اختره من جديد.|Impossible de lire le fichier. Choisissez-le à nouveau.
Not enough memory to open the vault.|Не хватило памяти, чтобы открыть хранилище.|内存不足，无法打开密码库。|वॉल्ट खोलने के लिए पर्याप्त मेमोरी नहीं है।|No hay memoria suficiente para abrir la bóveda.|الذاكرة غير كافية لفتح الخزنة.|Mémoire insuffisante pour ouvrir le coffre.
Lock|Заблокировать|锁定|लॉक करें|Bloquear|قفل|Verrouiller
Search all folders|Поиск по всем папкам|搜索所有文件夹|सभी फ़ोल्डरों में खोजें|Buscar en todas las carpetas|البحث في كل المجلدات|Rechercher dans tous les dossiers
All folders|Все папки|所有文件夹|सभी फ़ोल्डर|Todas las carpetas|كل المجلدات|Tous les dossiers
Entries|Записи|条目|प्रविष्टियाँ|Entradas|الإدخالات|Entrées
Files|Файлы|文件|फ़ाइलें|Archivos|الملفات|Fichiers
Nothing found|Ничего не найдено|未找到任何内容|कुछ नहीं मिला|No se encontró nada|لم يُعثر على شيء|Aucun résultat
No entries here|Здесь нет записей|这里没有条目|यहाँ कोई प्रविष्टि नहीं है|Aquí no hay entradas|لا توجد إدخالات هنا|Aucune entrée ici
No files here|Здесь нет файлов|这里没有文件|यहाँ कोई फ़ाइल नहीं है|Aquí no hay archivos|لا توجد ملفات هنا|Aucun fichier ici
File|Файл|文件|फ़ाइल|Archivo|ملف|Fichier
Entry|Запись|条目|प्रविष्टि|Entrada|إدخال|Entrée
attachments: {0}|вложений: {0}|附件：{0}|अटैचमेंट: {0}|adjuntos: {0}|المرفقات: {0}|pièces jointes : {0}
Back|Назад|返回|वापस|Atrás|رجوع|Retour
modified {0}|изменено {0}|修改于 {0}|बदला गया {0}|modificado {0}|عُدّل {0}|modifié {0}
Username|Логин|用户名|उपयोगकर्ता नाम|Usuario|اسم المستخدم|Identifiant
Password|Пароль|密码|पासवर्ड|Contraseña|كلمة المرور|Mot de passe
Website|Сайт|网站|वेबसाइट|Sitio web|الموقع|Site web
Note|Заметка|备注|नोट|Nota|ملاحظة|Note
Text|Текст|文本|पाठ|Texto|النص|Texte
Copy|Копировать|复制|कॉपी करें|Copiar|نسخ|Copier
Show|Показать|显示|दिखाएँ|Mostrar|إظهار|Afficher
Hide|Скрыть|隐藏|छिपाएँ|Ocultar|إخفاء|Masquer
Attachments|Вложения|附件|अटैचमेंट|Adjuntos|المرفقات|Pièces jointes
Attachments can be opened and saved in the Windows version.|Открыть и сохранить вложения можно в версии для Windows.|附件可在 Windows 版中打开和保存。|अटैचमेंट Windows संस्करण में खोले और सहेजे जा सकते हैं।|Los adjuntos se pueden abrir y guardar en la versión para Windows.|يمكن فتح المرفقات وحفظها في إصدار Windows.|Les pièces jointes s’ouvrent et s’enregistrent dans la version Windows.
This item has no data.|В этом элементе нет данных.|此项没有数据。|इस आइटम में कोई डेटा नहीं है।|Este elemento no tiene datos.|لا توجد بيانات في هذا العنصر.|Cet élément ne contient aucune donnée.
{0}: copied|{0}: скопировано|{0}：已复制|{0}: कॉपी किया गया|{0}: copiado|{0}: تم النسخ|{0} : copié
There is no app to open the website.|Нет приложения, чтобы открыть сайт.|没有可打开网站的应用。|वेबसाइट खोलने के लिए कोई ऐप नहीं है।|No hay ninguna aplicación para abrir el sitio web.|لا يوجد تطبيق لفتح الموقع.|Aucune application pour ouvrir le site web.
Settings|Настройки|设置|सेटिंग्स|Ajustes|الإعدادات|Paramètres
Language|Язык|语言|भाषा|Idioma|اللغة|Langue
Auto-lock|Автоблокировка|自动锁定|ऑटो-लॉक|Bloqueo automático|القفل التلقائي|Verrouillage automatique
Off|Выключена|关闭|बंद|Desactivado|متوقف|Désactivé
After 1 minute in the background|Через 1 минуту после сворачивания|在后台 1 分钟后|बैकग्राउंड में 1 मिनट के बाद|Tras 1 minuto en segundo plano|بعد دقيقة واحدة في الخلفية|Après 1 minute en arrière-plan
After 5 minutes in the background|Через 5 минут после сворачивания|在后台 5 分钟后|बैकग्राउंड में 5 मिनट के बाद|Tras 5 minutos en segundo plano|بعد 5 دقائق في الخلفية|Après 5 minutes en arrière-plan
While it is off, the vault stays open until you press Lock or close the app.|Пока выключена, хранилище остаётся открытым, пока вы не нажмёте «Заблокировать» или не закроете приложение.|关闭时，密码库将保持打开，直到您点按“锁定”或关闭应用。|बंद रहने पर वॉल्ट तब तक खुला रहता है जब तक आप «लॉक करें» नहीं दबाते या ऐप बंद नहीं करते।|Si está desactivado, la bóveda sigue abierta hasta que pulse «Bloquear» o cierre la aplicación.|عند إيقافه تبقى الخزنة مفتوحة حتى تضغط «قفل» أو تغلق التطبيق.|Lorsqu’il est désactivé, le coffre reste ouvert jusqu’à ce que vous appuyiez sur « Verrouiller » ou fermiez l’application.
The vault was locked: the app stayed in the background too long.|Хранилище заблокировано: приложение слишком долго было свёрнуто.|密码库已锁定：应用在后台停留过久。|वॉल्ट लॉक हो गया: ऐप बहुत देर तक बैकग्राउंड में रहा।|La bóveda se bloqueó: la aplicación estuvo demasiado tiempo en segundo plano.|تم قفل الخزنة: بقي التطبيق في الخلفية مدة طويلة.|Le coffre a été verrouillé : l’application est restée trop longtemps en arrière-plan.
Vault root|Корень хранилища|密码库根目录|वॉल्ट रूट|Raíz de la bóveda|جذر الخزنة|Racine du coffre
New entry|Новая запись|新条目|नई प्रविष्टि|Nueva entrada|إدخال جديد|Nouvelle entrée
New file|Новый файл|新文件|नई फ़ाइल|Nuevo archivo|ملف جديد|Nouveau fichier
B|Б|B|B|B|B|o
KB|КБ|KB|KB|KB|KB|Ko
MB|МБ|MB|MB|MB|MB|Mo
Could not open the vault: wrong master password, or the file is damaged or was changed.|Не удалось открыть хранилище: неверный главный пароль либо файл повреждён или изменён.|无法打开密码库：主密码错误，或文件已损坏或被更改。|वॉल्ट नहीं खुल सका: मास्टर पासवर्ड गलत है या फ़ाइल क्षतिग्रस्त है या बदली गई है।|No se pudo abrir la bóveda: la contraseña maestra es incorrecta o el archivo está dañado o fue modificado.|تعذّر فتح الخزنة: كلمة المرور الرئيسية خاطئة أو الملف تالف أو تم تغييره.|Impossible d’ouvrir le coffre : mot de passe principal incorrect, ou fichier endommagé ou modifié.
Invalid vault file size: it is empty or larger than 512 MB. The file was not decrypted or changed.|Недопустимый размер файла хранилища: он пустой или больше 512 МБ. Файл не расшифровывался и не изменён.|密码库文件大小无效：文件为空或超过 512 MB。文件未被解密或更改。|वॉल्ट फ़ाइल का आकार अमान्य है: यह खाली है या 512 MB से बड़ी है। फ़ाइल न डिक्रिप्ट की गई, न बदली गई।|Tamaño de archivo no válido: está vacío o supera los 512 MB. El archivo no se descifró ni se modificó.|حجم ملف الخزنة غير صالح: الملف فارغ أو أكبر من 512 ميغابايت. لم يُفك تشفير الملف ولم يُغيَّر.|Taille du fichier invalide : il est vide ou dépasse 512 Mo. Le fichier n’a été ni déchiffré ni modifié.
This is not a KDBX vault file.|Это не файл хранилища KDBX.|这不是 KDBX 密码库文件。|यह KDBX वॉल्ट फ़ाइल नहीं है।|Este no es un archivo de bóveda KDBX.|هذا ليس ملف خزنة KDBX.|Ce n’est pas un fichier de coffre KDBX.
Only KDBX 4.0 and 4.1 are supported.|Поддерживаются только KDBX 4.0 и 4.1.|仅支持 KDBX 4.0 和 4.1。|केवल KDBX 4.0 और 4.1 समर्थित हैं।|Solo se admiten KDBX 4.0 y 4.1.|يُدعم KDBX 4.0 و4.1 فقط.|Seuls KDBX 4.0 et 4.1 sont pris en charge.
The vault file is truncated or damaged.|Файл хранилища обрезан или повреждён.|密码库文件被截断或已损坏。|वॉल्ट फ़ाइल अधूरी या क्षतिग्रस्त है।|El archivo de bóveda está truncado o dañado.|ملف الخزنة مبتور أو تالف.|Le fichier du coffre est tronqué ou endommagé.
The vault file is damaged or not supported.|Файл хранилища повреждён или не поддерживается.|密码库文件已损坏或不受支持。|वॉल्ट फ़ाइल क्षतिग्रस्त है या समर्थित नहीं है।|El archivo de bóveda está dañado o no es compatible.|ملف الخزنة تالف أو غير مدعوم.|Le fichier du coffre est endommagé ou non pris en charge.
""";
}
