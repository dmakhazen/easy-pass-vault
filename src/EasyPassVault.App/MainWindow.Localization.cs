using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using EasyPassVault.Core;

namespace EasyPassVault.App;

public partial class MainWindow
{
    private sealed record UiLanguage(string Name, string Culture);
    private static readonly UiLanguage[] Languages =
    [
        new("Русский", "ru"), new("English", "en"), new("简体中文", "zh-CN"),
        new("हिन्दी", "hi"), new("Español", "es"), new("العربية", "ar"),
        new("Français", "fr"), new("বাংলা", "bn"), new("Português", "pt-BR"),
        new("Bahasa Indonesia", "id")
    ];

    private static readonly string[][] Labels = Catalog.Split('\n')
        .Select(row => row.TrimEnd('\r').Split('|')).ToArray();
    private static readonly Dictionary<string, string[]> LabelsBySource =
        Labels.ToDictionary(row => row[0].Replace("\\n", "\n", StringComparison.Ordinal));
    private int language;
    private CultureInfo UiCulture => CultureInfo.GetCultureInfo(Languages[language].Culture);

    private string T(string source) => LabelsBySource.TryGetValue(source, out var translations)
        ? translations[language]
        : source;

    private string TranslateError(string message) => language == 0 || LabelsBySource.ContainsKey(message)
        ? T(message)
        : T("Операция не выполнена.");

    private void SetLanguage(int index)
    {
        if (index < 0 || index >= Languages.Length) return;
        language = index;
        ((ItemTitleConverter)Resources["ItemTitleConverter"]).Translate = T;
        for (int i = 0; i < Labels.Length; i++)
            Resources[$"Ui.{i}"] = Labels[i][language].Replace("\\n", "\n", StringComparison.Ordinal);

        for (int i = 0; i < LanguageMenu.Items.Count; i++)
            ((MenuItem)LanguageMenu.Items[i]!).IsChecked = i == language;
        LanguageMenu.Header = T("Язык интерфейса") + ": " + Languages[language].Name;
        // Menus are outside the visual tree, so their texts are set here instead of DynamicResource.
        FolderMenuAdd.Header = T("Новая вложенная папка");
        FolderMenuRename.Header = T("Переименовать");
        FolderMenuDelete.Header = T("Удалить папку");
        VersionsMenuItem.Header = T("Версии этого хранилища…");
        if (session is null) UpdateWelcome();
        ApplySortTexts();
        UpdateVaultHeader();
        UpdateSaveState();
        if (session is not null)
        {
            RefreshLists();
            ShowDraft();
        }
        var version = typeof(MainWindow).Assembly.GetName().Version!;
        Status.Text = session is null
            ? string.Format(UiCulture, T("Версия {0}"), FormattableString.Invariant($"{version.Major}.{version.Minor:D2}"))
            : T("Хранилище открыто");
    }

    private const string Catalog = """
easy-pass-vault: документ с замком|easy-pass-vault: locked document|easy-pass-vault：加锁的文档|easy-pass-vault: बंद दस्तावेज़|easy-pass-vault: documento bloqueado|easy-pass-vault: مستند مقفل|easy-pass-vault : document verrouillé|easy-pass-vault: লক করা নথি|easy-pass-vault: documento bloqueado|easy-pass-vault: dokumen terkunci
Локальное хранилище|Local vault|本地密码库|स्थानीय वॉल्ट|Bóveda local|خزنة محلية|Coffre local|স্থানীয় ভল্ট|Cofre local|Brankas lokal
В папке есть более новая версия этого хранилища. Изменения не записаны. Откройте последнюю версию.|A newer version of this vault is in the folder. Changes were not saved. Open the latest version.|文件夹中有此密码库的更新版本。更改未保存。请打开最新版本。|फ़ोल्डर में इस वॉल्ट का नया संस्करण है। बदलाव सहेजे नहीं गए। नवीनतम संस्करण खोलें।|La carpeta contiene una versión más reciente de esta bóveda. Los cambios no se guardaron. Abre la versión más reciente.|يوجد في المجلد إصدار أحدث من هذه الخزنة. لم تُحفظ التغييرات. افتح أحدث إصدار.|Le dossier contient une version plus récente de ce coffre. Les modifications n’ont pas été enregistrées. Ouvrez la dernière version.|ফোল্ডারে এই ভল্টের নতুন সংস্করণ আছে। পরিবর্তন সংরক্ষিত হয়নি। সর্বশেষ সংস্করণ খুলুন।|A pasta contém uma versão mais recente deste cofre. As alterações não foram salvas. Abra a versão mais recente.|Folder berisi versi brankas yang lebih baru. Perubahan tidak disimpan. Buka versi terbaru.
«Сохранить и зашифровать» (Ctrl+S) записывает изменения в открытый файл. Вторая кнопка создаёт рядом новый файл название_дата_vНомер со сквозным номером: прежний файл не меняется, а новый становится открытым.|Save and encrypt (Ctrl+S) writes changes to the open file. The second button creates a new file name_date_vNumber next to it with a continuous number: the previous file stays unchanged and the new one becomes open.|“保存并加密”（Ctrl+S）将更改写入当前打开的文件。第二个按钮会在旁边创建新文件 名称_日期_v编号，编号连续递增：原文件保持不变，新文件成为当前文件。|सहेजें और एन्क्रिप्ट करें (Ctrl+S) बदलाव खुली फ़ाइल में लिखता है। दूसरा बटन पास में नई फ़ाइल नाम_तारीख_vक्रमांक बनाता है, क्रमांक लगातार बढ़ता है: पिछली फ़ाइल नहीं बदलती और नई फ़ाइल खुल जाती है।|Guardar y cifrar (Ctrl+S) escribe los cambios en el archivo abierto. El segundo botón crea al lado un archivo nuevo nombre_fecha_vNúmero con numeración continua: el archivo anterior no cambia y el nuevo queda abierto.|حفظ وتشفير (Ctrl+S) يكتب التغييرات في الملف المفتوح. الزر الثاني ينشئ بجانبه ملفًا جديدًا الاسم_التاريخ_vالرقم بترقيم متواصل: يبقى الملف السابق دون تغيير ويصبح الجديد هو المفتوح.|Enregistrer et chiffrer (Ctrl+S) écrit les modifications dans le fichier ouvert. Le second bouton crée à côté un nouveau fichier nom_date_vNuméro à numérotation continue : l’ancien fichier reste inchangé et le nouveau devient actif.|সংরক্ষণ ও এনক্রিপ্ট (Ctrl+S) পরিবর্তন খোলা ফাইলে লেখে। দ্বিতীয় বোতাম পাশে নতুন ফাইল নাম_তারিখ_vনম্বর তৈরি করে, নম্বর ক্রমাগত বাড়ে: আগের ফাইল বদলায় না, নতুনটি খোলা হয়।|Salvar e criptografar (Ctrl+S) grava as alterações no arquivo aberto. O segundo botão cria ao lado um novo arquivo nome_data_vNúmero com numeração contínua: o arquivo anterior não muda e o novo fica aberto.|Simpan dan enkripsi (Ctrl+S) menulis perubahan ke berkas yang terbuka. Tombol kedua membuat berkas baru nama_tanggal_vNomor di sebelahnya dengan nomor berurutan: berkas sebelumnya tidak berubah dan berkas baru menjadi terbuka.
Закрыть хранилище|Close vault|关闭密码库|वॉल्ट बंद करें|Cerrar bóveda|إغلاق الخزنة|Fermer le coffre|ভল্ট বন্ধ করুন|Fechar cofre|Tutup brankas
Язык интерфейса|Interface language|界面语言|इंटरफ़ेस की भाषा|Idioma de la interfaz|لغة الواجهة|Langue de l’interface|ইন্টারফেসের ভাষা|Idioma da interface|Bahasa antarmuka
Версия {0}|Version {0}|版本 {0}|संस्करण {0}|Versión {0}|الإصدار {0}|Version {0}|সংস্করণ {0}|Versão {0}|Versi {0}
Ваши пароли. Ваш файл.|Your passwords. Your file.|你的密码，你的文件。|आपके पासवर्ड। आपकी फ़ाइल।|Tus contraseñas. Tu archivo.|كلمات مرورك. ملفك.|Vos mots de passe. Votre fichier.|আপনার পাসওয়ার্ড। আপনার ফাইল।|Suas senhas. Seu arquivo.|Kata sandi Anda. Berkas Anda.
Создайте зашифрованное хранилище или откройте существующий файл KDBX. Главный пароль требуется при каждом открытии.|Create an encrypted vault or open a KDBX file. Enter the master password each time you open it.|创建加密密码库或打开 KDBX 文件。每次打开都需要主密码。|एन्क्रिप्ट किया गया वॉल्ट बनाएँ या KDBX फ़ाइल खोलें। हर बार मास्टर पासवर्ड चाहिए।|Crea una bóveda cifrada o abre un archivo KDBX. Introduce la contraseña maestra cada vez.|أنشئ خزنة مشفرة أو افتح ملف KDBX. أدخل كلمة المرور الرئيسية عند كل فتح.|Créez un coffre chiffré ou ouvrez un fichier KDBX. Le mot de passe principal est requis à chaque ouverture.|এনক্রিপ্ট করা ভল্ট তৈরি করুন বা KDBX ফাইল খুলুন। প্রতিবার মাস্টার পাসওয়ার্ড লাগবে।|Crie um cofre criptografado ou abra um arquivo KDBX. Digite a senha mestra a cada abertura.|Buat brankas terenkripsi atau buka berkas KDBX. Masukkan kata sandi utama setiap kali membuka.
Открыть файл хранилища|Open vault file|打开密码库文件|वॉल्ट फ़ाइल खोलें|Abrir archivo de bóveda|فتح ملف الخزنة|Ouvrir un fichier de coffre|ভল্ট ফাইল খুলুন|Abrir arquivo do cofre|Buka berkas brankas
Создать новое…|Create new…|新建…|नया बनाएँ…|Crear nueva…|إنشاء خزنة…|Créer un coffre…|নতুন তৈরি করুন…|Criar novo…|Buat baru…
Восстановления забытого главного пароля нет. Храните отдельную копию зашифрованного файла для защиты от потери носителя.|A forgotten master password cannot be recovered. Keep a separate copy of the encrypted file.|忘记主密码后无法找回。请单独备份加密文件。|भूला हुआ मास्टर पासवर्ड वापस नहीं मिल सकता। एन्क्रिप्ट की गई फ़ाइल की अलग प्रति रखें।|No se puede recuperar una contraseña maestra olvidada. Guarda una copia separada del archivo cifrado.|لا يمكن استعادة كلمة المرور الرئيسية إذا نسيتها. احتفظ بنسخة منفصلة من الملف المشفر.|Un mot de passe principal oublié est irrécupérable. Gardez une copie séparée du fichier chiffré.|ভুলে যাওয়া মাস্টার পাসওয়ার্ড পুনরুদ্ধার করা যায় না। এনক্রিপ্ট করা ফাইলের আলাদা কপি রাখুন।|Uma senha mestra esquecida não pode ser recuperada. Guarde uma cópia separada do arquivo criptografado.|Kata sandi utama yang terlupa tidak dapat dipulihkan. Simpan salinan terpisah dari berkas terenkripsi.
ПАПКИ|FOLDERS|文件夹|फ़ोल्डर|CARPETAS|المجلدات|DOSSIERS|ফোল্ডার|PASTAS|FOLDER
+ Новая папка|+ New folder|+ 新建文件夹|+ नया फ़ोल्डर|+ Nueva carpeta|+ مجلد جديد|+ Nouveau dossier|+ নতুন ফোল্ডার|+ Nova pasta|+ Folder baru
Переименовать|Rename|重命名|नाम बदलें|Renombrar|إعادة تسمية|Renommer|নাম বদলান|Renomear|Ubah nama
Удалить пустую папку|Delete empty folder|删除空文件夹|खाली फ़ोल्डर हटाएँ|Eliminar carpeta vacía|حذف مجلد فارغ|Supprimer le dossier vide|খালি ফোল্ডার মুছুন|Excluir pasta vazia|Hapus folder kosong
Все папки|All folders|所有文件夹|सभी फ़ोल्डर|Todas las carpetas|كل المجلدات|Tous les dossiers|সব ফোল্ডার|Todas as pastas|Semua folder
Записи|Entries|条目|प्रविष्टियाँ|Entradas|السجلات|Entrées|এন্ট্রি|Entradas|Entri
Файлы|Files|文件|फ़ाइलें|Archivos|الملفات|Fichiers|ফাইল|Arquivos|Berkas
Поиск по названию, логину, сайту и тексту|Search title, username, site and text|按标题、用户名、网站和文本搜索|शीर्षक, उपयोगकर्ता नाम, साइट और पाठ खोजें|Buscar por título, usuario, sitio y texto|البحث بالعنوان واسم المستخدم والموقع والنص|Rechercher par titre, identifiant, site et texte|শিরোনাম, ব্যবহারকারী, সাইট ও লেখা খুঁজুন|Buscar por título, usuário, site e texto|Cari judul, nama pengguna, situs, dan teks
Поиск|Search|搜索|खोजें|Buscar|بحث|Rechercher|অনুসন্ধান|Buscar|Cari
+ Запись|+ Entry|+ 条目|+ प्रविष्टि|+ Entrada|+ سجل|+ Entrée|+ এন্ট্রি|+ Entrada|+ Entri
+ Файл|+ File|+ 文件|+ फ़ाइल|+ Archivo|+ ملف|+ Fichier|+ ফাইল|+ Arquivo|+ Berkas
Выберите элемент слева\nили создайте новый.|Select an item on the left or create one.|请在左侧选择或新建项目。|बाईं ओर कोई आइटम चुनें या नया बनाएँ।|Selecciona un elemento a la izquierda o crea uno.|اختر عنصرًا من اليسار أو أنشئ عنصرًا جديدًا.|Sélectionnez un élément à gauche ou créez-en un.|বাঁ দিক থেকে একটি আইটেম বেছে নিন বা নতুন তৈরি করুন।|Selecione um item à esquerda ou crie um.|Pilih item di sebelah kiri atau buat yang baru.
Отменить изменения|Discard changes|放弃更改|बदलाव वापस लें|Descartar cambios|تجاهل التغييرات|Annuler les modifications|পরিবর্তন বাতিল করুন|Descartar alterações|Buang perubahan
Сохранить и зашифровать|Save and encrypt|保存并加密|सहेजें और एन्क्रिप्ट करें|Guardar y cifrar|حفظ وتشفير|Enregistrer et chiffrer|সংরক্ষণ ও এনক্রিপ্ট করুন|Salvar e criptografar|Simpan dan enkripsi
Название|Title|标题|शीर्षक|Título|العنوان|Titre|শিরোনাম|Título|Judul
Папка|Folder|文件夹|फ़ोल्डर|Carpeta|المجلد|Dossier|ফোল্ডার|Pasta|Folder
Логин|Username|用户名|उपयोगकर्ता नाम|Usuario|اسم المستخدم|Identifiant|ব্যবহারকারীর নাম|Usuário|Nama pengguna
Пароль|Password|密码|पासवर्ड|Contraseña|كلمة المرور|Mot de passe|পাসওয়ার্ড|Senha|Kata sandi
Показать|Show|显示|दिखाएँ|Mostrar|إظهار|Afficher|দেখান|Mostrar|Tampilkan
Скрыть|Hide|隐藏|छिपाएँ|Ocultar|إخفاء|Masquer|লুকান|Ocultar|Sembunyikan
Сайт|Website|网站|वेबसाइट|Sitio web|الموقع|Site web|ওয়েবসাইট|Site|Situs web
Текст|Text|文本|पाठ|Texto|النص|Texte|লেখা|Texto|Teks
Удалить элемент|Delete item|删除项目|आइटम हटाएँ|Eliminar elemento|حذف العنصر|Supprimer l’élément|আইটেম মুছুন|Excluir item|Hapus item
Последнее сохранение: {0}|Last saved: {0}|上次保存：{0}|अंतिम सहेजना: {0}|Último guardado: {0}|آخر حفظ: {0}|Dernier enregistrement : {0}|শেষ সংরক্ষণ: {0}|Último salvamento: {0}|Terakhir disimpan: {0}
Дата сохранения неизвестна|Save date unknown|保存日期未知|सहेजने की तारीख अज्ञात है|Fecha de guardado desconocida|تاريخ الحفظ غير معروف|Date d’enregistrement inconnue|সংরক্ষণের তারিখ অজানা|Data de salvamento desconhecida|Tanggal penyimpanan tidak diketahui
Версия сохранена|Version saved|版本已保存|संस्करण सहेजा गया|Versión guardada|تم حفظ النسخة|Version enregistrée|সংস্করণ সংরক্ষিত|Versão salva|Versi tersimpan
Новая запись|New entry|新条目|नई प्रविष्टि|Nueva entrada|سجل جديد|Nouvelle entrée|নতুন এন্ট্রি|Nova entrada|Entri baru
Новый файл|New file|新文件|नई फ़ाइल|Nuevo archivo|ملف جديد|Nouveau fichier|নতুন ফাইল|Novo arquivo|Berkas baru
Корень хранилища|Vault root|密码库根目录|वॉल्ट का मूल फ़ोल्डर|Raíz de la bóveda|جذر الخزنة|Racine du coffre|ভল্টের মূল ফোল্ডার|Raiz do cofre|Folder akar brankas
Заметка|Note|备注|नोट|Nota|ملاحظة|Note|নোট|Nota|Catatan
Содержимое файла|File contents|文件内容|फ़ाइल की सामग्री|Contenido del archivo|محتوى الملف|Contenu du fichier|ফাইলের বিষয়বস্তু|Conteúdo do arquivo|Isi berkas
Есть несохранённые изменения|Unsaved changes|有未保存的更改|बदलाव सहेजे नहीं गए|Cambios sin guardar|تغييرات غير محفوظة|Modifications non enregistrées|পরিবর্তন সংরক্ষিত হয়নি|Alterações não salvas|Perubahan belum disimpan
Сохранено в зашифрованный файл|Saved to the encrypted file|已保存到加密文件|एन्क्रिप्ट की गई फ़ाइल में सहेजा गया|Guardado en el archivo cifrado|تم الحفظ في الملف المشفر|Enregistré dans le fichier chiffré|এনক্রিপ্ট করা ফাইলে সংরক্ষিত|Salvo no arquivo criptografado|Disimpan ke berkas terenkripsi
Новый черновик · заполните любое поле|New draft · fill in any field|新草稿 · 请填写任意字段|नया मसौदा · कोई फ़ील्ड भरें|Nuevo borrador · completa algún campo|مسودة جديدة · املأ أي حقل|Nouveau brouillon · remplissez un champ|নতুন খসড়া · যেকোনো ঘর পূরণ করুন|Novo rascunho · preencha algum campo|Draf baru · isi salah satu bidang
Открытие хранилища|Open vault|打开密码库|वॉल्ट खोलें|Abrir bóveda|فتح الخزنة|Ouvrir le coffre|ভল্ট খুলুন|Abrir cofre|Buka brankas
Введите главный пароль|Enter the master password|输入主密码|मास्टर पासवर्ड डालें|Introduce la contraseña maestra|أدخل كلمة المرور الرئيسية|Saisissez le mot de passe principal|মাস্টার পাসওয়ার্ড দিন|Digite a senha mestra|Masukkan kata sandi utama
Новое хранилище|New vault|新建密码库|नया वॉल्ट|Nueva bóveda|خزنة جديدة|Nouveau coffre|নতুন ভল্ট|Novo cofre|Brankas baru
Придумайте главный пароль от 12 символов. Восстановить забытый пароль нельзя.|Choose a master password of at least 12 characters. It cannot be recovered if forgotten.|请设置至少 12 个字符的主密码。忘记后无法找回。|कम से कम 12 वर्णों का मास्टर पासवर्ड चुनें। भूलने पर इसे वापस नहीं पाया जा सकता।|Elige una contraseña maestra de al menos 12 caracteres. Si la olvidas, no se puede recuperar.|اختر كلمة مرور رئيسية من 12 حرفًا على الأقل. لا يمكن استعادتها إذا نسيتها.|Choisissez un mot de passe principal d’au moins 12 caractères. Il est irrécupérable en cas d’oubli.|অন্তত ১২ অক্ষরের মাস্টার পাসওয়ার্ড বেছে নিন। ভুলে গেলে এটি পুনরুদ্ধার করা যাবে না।|Escolha uma senha mestra com pelo menos 12 caracteres. Se esquecê-la, não poderá recuperá-la.|Pilih kata sandi utama minimal 12 karakter. Kata sandi yang terlupa tidak dapat dipulihkan.
Повторите главный пароль|Repeat the master password|再次输入主密码|मास्टर पासवर्ड फिर डालें|Repite la contraseña maestra|أعد إدخال كلمة المرور الرئيسية|Répétez le mot de passe principal|মাস্টার পাসওয়ার্ড আবার দিন|Repita a senha mestra|Ulangi kata sandi utama
Отмена|Cancel|取消|रद्द करें|Cancelar|إلغاء|Annuler|বাতিল|Cancelar|Batal
Создать|Create|创建|बनाएँ|Crear|إنشاء|Créer|তৈরি করুন|Criar|Buat
Продолжить|Continue|继续|जारी रखें|Continuar|متابعة|Continuer|চালিয়ে যান|Continuar|Lanjutkan
Поле не может быть пустым.|This field is required.|此字段为必填项。|यह फ़ील्ड ज़रूरी है।|Este campo es obligatorio.|هذا الحقل مطلوب.|Ce champ est obligatoire.|এই ঘরটি পূরণ করতে হবে।|Este campo é obrigatório.|Bidang ini wajib diisi.
Пароли не совпадают.|Passwords do not match.|两次输入的密码不一致。|पासवर्ड मेल नहीं खाते।|Las contraseñas no coinciden.|كلمتا المرور غير متطابقتين.|Les mots de passe ne correspondent pas.|পাসওয়ার্ড দুটি মেলেনি।|As senhas não coincidem.|Kata sandi tidak cocok.
Введите не менее 12 символов.|Enter at least 12 characters.|请输入至少 12 个字符。|कम से कम 12 वर्ण डालें।|Introduce al menos 12 caracteres.|أدخل 12 حرفًا على الأقل.|Saisissez au moins 12 caractères.|অন্তত ১২ অক্ষর দিন।|Digite pelo menos 12 caracteres.|Masukkan minimal 12 karakter.
Ошибка · изменения не сохранены|Error · changes not saved|错误 · 更改未保存|त्रुटि · बदलाव सहेजे नहीं गए|Error · cambios sin guardar|خطأ · لم تحفظ التغييرات|Erreur · modifications non enregistrées|ত্রুটি · পরিবর্তন সংরক্ষিত হয়নি|Erro · alterações não salvas|Galat · perubahan belum disimpan
Хранилище открыто|Vault open|密码库已打开|वॉल्ट खुला है|Bóveda abierta|الخزنة مفتوحة|Coffre ouvert|ভল্ট খোলা|Cofre aberto|Brankas terbuka
Хранилище закрыто|Vault closed|密码库已关闭|वॉल्ट बंद है|Bóveda cerrada|الخزنة مغلقة|Coffre fermé|ভল্ট বন্ধ|Cofre fechado|Brankas ditutup
Открытие и проверка целостности…|Opening and checking integrity…|正在打开并检查完整性…|खोला जा रहा है और अखंडता जाँची जा रही है…|Abriendo y verificando la integridad…|جارٍ الفتح والتحقق من السلامة…|Ouverture et vérification de l’intégrité…|খোলা হচ্ছে ও অখণ্ডতা যাচাই চলছে…|Abrindo e verificando a integridade…|Membuka dan memeriksa integritas…
Создание зашифрованного хранилища…|Creating encrypted vault…|正在创建加密密码库…|एन्क्रिप्ट किया गया वॉल्ट बनाया जा रहा है…|Creando bóveda cifrada…|جارٍ إنشاء خزنة مشفرة…|Création du coffre chiffré…|এনক্রিপ্ট করা ভল্ট তৈরি হচ্ছে…|Criando cofre criptografado…|Membuat brankas terenkripsi…
Шифрование и безопасное сохранение…|Encrypting and saving…|正在加密并保存…|एन्क्रिप्ट और सहेजा जा रहा है…|Cifrando y guardando…|جارٍ التشفير والحفظ…|Chiffrement et enregistrement…|এনক্রিপ্ট ও সংরক্ষণ হচ্ছে…|Criptografando e salvando…|Mengenkripsi dan menyimpan…
Заполните хотя бы одно поле.|Fill in at least one field.|请至少填写一个字段。|कम से कम एक फ़ील्ड भरें।|Completa al menos un campo.|املأ حقلًا واحدًا على الأقل.|Remplissez au moins un champ.|অন্তত একটি ঘর পূরণ করুন।|Preencha pelo menos um campo.|Isi minimal satu bidang.
Сохранить изменения?|Save changes?|保存更改？|बदलाव सहेजें?|¿Guardar cambios?|حفظ التغييرات؟|Enregistrer les modifications ?|পরিবর্তন সংরক্ষণ করবেন?|Salvar alterações?|Simpan perubahan?
Да — сохранить. Нет — отбросить. Отмена — остаться.|Yes saves. No discards. Cancel stays here.|是：保存。否：放弃。取消：留在此处。|हाँ: सहेजें। नहीं: छोड़ें। रद्द करें: यहीं रहें।|Sí guarda. No descarta. Cancelar te mantiene aquí.|نعم للحفظ. لا للتجاهل. إلغاء للبقاء هنا.|Oui enregistre. Non ignore. Annuler reste ici.|হ্যাঁ: সংরক্ষণ। না: বাতিল। বাতিল: এখানেই থাকুন।|Sim salva. Não descarta. Cancelar mantém esta tela.|Ya menyimpan. Tidak membuang. Batal tetap di sini.
Удалить выбранный элемент?|Delete selected item?|删除所选项目？|चुना गया आइटम हटाएँ?|¿Eliminar el elemento seleccionado?|حذف العنصر المحدد؟|Supprimer l’élément sélectionné ?|নির্বাচিত আইটেম মুছবেন?|Excluir item selecionado?|Hapus item terpilih?
Удалить пустую папку?|Delete the empty folder?|删除空文件夹？|खाली फ़ोल्डर हटाएँ?|¿Eliminar la carpeta vacía?|حذف المجلد الفارغ؟|Supprimer le dossier vide ?|খালি ফোল্ডার মুছবেন?|Excluir pasta vazia?|Hapus folder kosong?
Новая папка|New folder|新建文件夹|नया फ़ोल्डर|Nueva carpeta|مجلد جديد|Nouveau dossier|নতুন ফোল্ডার|Nova pasta|Folder baru
Название папки|Folder name|文件夹名称|फ़ोल्डर का नाम|Nombre de carpeta|اسم المجلد|Nom du dossier|ফোল্ডারের নাম|Nome da pasta|Nama folder
Новое название|New name|新名称|नया नाम|Nuevo nombre|الاسم الجديد|Nouveau nom|নতুন নাম|Novo nome|Nama baru
Операция не выполнена.|Operation failed.|操作失败。|कार्रवाई विफल रही।|La operación falló.|تعذرت العملية.|L’opération a échoué.|কাজটি সম্পন্ন হয়নি।|A operação falhou.|Operasi gagal.
Не удалось открыть хранилище: неверный главный пароль либо файл повреждён или изменён.|Could not open the vault. The master password is wrong or the file is damaged or modified.|无法打开密码库。主密码错误，或文件已损坏或更改。|वॉल्ट नहीं खुला। मास्टर पासवर्ड गलत है या फ़ाइल क्षतिग्रस्त या बदली हुई है।|No se pudo abrir la bóveda. La contraseña maestra es incorrecta o el archivo está dañado o modificado.|تعذر فتح الخزنة. كلمة المرور الرئيسية خاطئة أو الملف تالف أو معدّل.|Impossible d’ouvrir le coffre. Le mot de passe principal est incorrect ou le fichier est endommagé ou modifié.|ভল্ট খোলা যায়নি। মাস্টার পাসওয়ার্ড ভুল অথবা ফাইল ক্ষতিগ্রস্ত বা পরিবর্তিত।|Não foi possível abrir o cofre. A senha mestra está incorreta ou o arquivo foi danificado ou alterado.|Brankas tidak dapat dibuka. Kata sandi utama salah atau berkas rusak atau telah diubah.
Файл уже существует. Выберите другое имя; существующее хранилище не перезаписано.|A file with that name already exists.|同名文件已存在。|इस नाम की फ़ाइल पहले से मौजूद है।|Ya existe un archivo con ese nombre.|يوجد ملف بهذا الاسم بالفعل.|Un fichier porte déjà ce nom.|এই নামে একটি ফাইল আগে থেকেই আছে।|Já existe um arquivo com esse nome.|Berkas dengan nama itu sudah ada.
Экспорт в текст…|Export text…|导出文本…|टेक्स्ट निर्यात करें…|Exportar texto…|تصدير نص…|Exporter en texte…|লেখা রপ্তানি…|Exportar texto…|Ekspor teks…
Экспорт включает все сохранённые записи и пароли в открытом виде. Продолжить?|Export includes all saved entries and passwords in plain text. Continue?|导出内容包含所有已保存的条目及明文密码。继续吗？|निर्यात में सभी सहेजी गई प्रविष्टियाँ और पासवर्ड खुले पाठ में होंगे। जारी रखें?|La exportación incluye todas las entradas y contraseñas guardadas en texto sin cifrar. ¿Continuar?|يشمل التصدير جميع السجلات وكلمات المرور المحفوظة كنص عادي. هل تريد المتابعة؟|L’export inclut toutes les entrées et tous les mots de passe enregistrés en clair. Continuer ?|রপ্তানিতে সব সংরক্ষিত এন্ট্রি ও পাসওয়ার্ড খোলা লেখায় থাকবে। চালিয়ে যাবেন?|A exportação inclui todas as entradas e senhas salvas em texto simples. Continuar?|Ekspor memuat semua entri dan kata sandi tersimpan dalam teks biasa. Lanjutkan?
Экспорт завершён|Export complete|导出完成|निर्यात पूरा हुआ|Exportación completada|اكتمل التصدير|Export terminé|রপ্তানি সম্পন্ন|Exportação concluída|Ekspor selesai
Сохранение…|Saving…|正在保存…|सहेजा जा रहा है…|Guardando…|جارٍ الحفظ…|Enregistrement…|সংরক্ষণ হচ্ছে…|Salvando…|Menyimpan…
Изменения отменены|Changes reverted|更改已撤销|बदलाव वापस लिए गए|Cambios revertidos|تم التراجع عن التغييرات|Modifications annulées|পরিবর্তন বাতিল হয়েছে|Alterações revertidas|Perubahan diurungkan
Элемент удалён|Item deleted|项目已删除|आइटम हटाया गया|Elemento eliminado|تم حذف العنصر|Élément supprimé|আইটেম মুছে ফেলা হয়েছে|Item excluído|Item dihapus
Папка сохранена|Folder saved|文件夹已保存|फ़ोल्डर सहेजा गया|Carpeta guardada|تم حفظ المجلد|Dossier enregistré|ফোল্ডার সংরক্ষিত|Pasta salva|Folder disimpan
Папка удалена|Folder deleted|文件夹已删除|फ़ोल्डर हटाया गया|Carpeta eliminada|تم حذف المجلد|Dossier supprimé|ফোল্ডার মুছে ফেলা হয়েছে|Pasta excluída|Folder dihapus
Дождитесь завершения операции.|Wait for the operation to finish.|请等待操作完成。|कार्रवाई पूरी होने तक प्रतीक्षा करें।|Espera a que termine la operación.|انتظر حتى تكتمل العملية.|Attendez la fin de l’opération.|কাজ শেষ হওয়া পর্যন্ত অপেক্ষা করুন।|Aguarde a conclusão da operação.|Tunggu hingga operasi selesai.
Закрыть черновик|Close draft|关闭草稿|मसौदा बंद करें|Cerrar borrador|إغلاق المسودة|Fermer le brouillon|খসড়া বন্ধ করুন|Fechar rascunho|Tutup draf
Все|All|全部|सभी|Todo|الكل|Tout|সব|Tudo|Semua
Выберите файл с расширением .kdbx.|Choose a file with the .kdbx extension.|请选择扩展名为 .kdbx 的文件。|.kdbx एक्सटेंशन वाली फ़ाइल चुनें।|Elige un archivo con extensión .kdbx.|اختر ملفًا بامتداد .kdbx.|Choisissez un fichier avec l’extension .kdbx.|.kdbx এক্সটেনশনের ফাইল বেছে নিন।|Escolha um arquivo com a extensão .kdbx.|Pilih berkas dengan ekstensi .kdbx.
Папка перемещена|Folder moved|文件夹已移动|फ़ोल्डर स्थानांतरित हुआ|Carpeta movida|تم نقل المجلد|Dossier déplacé|ফোল্ডার সরানো হয়েছে|Pasta movida|Folder dipindahkan
1. Откройте или создайте хранилище.\n2. Добавьте записи или файлы.\n3. Нажмите «Сохранить» или Ctrl+S, чтобы сохранить все изменения.|1. Open or create a vault.\n2. Add entries or files.\n3. Use Save or Ctrl+S to save all changes.|1. 打开或创建密码库。\n2. 添加条目或文件。\n3. 点击“保存”或按 Ctrl+S 保存所有更改。|1. वॉल्ट खोलें या बनाएँ।\n2. प्रविष्टियाँ या फ़ाइलें जोड़ें।\n3. सभी बदलाव सहेजने के लिए सहेजें या Ctrl+S दबाएँ।|1. Abre o crea una bóveda.\n2. Añade entradas o archivos.\n3. Pulsa Guardar o Ctrl+S para guardar todos los cambios.|١. افتح خزنة أو أنشئها.\n٢. أضف سجلات أو ملفات.\n٣. اختر حفظ أو اضغط Ctrl+S لحفظ كل التغييرات.|1. Ouvrez ou créez un coffre.\n2. Ajoutez des entrées ou fichiers.\n3. Utilisez Enregistrer ou Ctrl+S pour tout enregistrer.|১. ভল্ট খুলুন বা তৈরি করুন।\n২. এন্ট্রি বা ফাইল যোগ করুন।\n৩. সব পরিবর্তন সংরক্ষণ করতে সংরক্ষণ করুন বা Ctrl+S চাপুন।|1. Abra ou crie um cofre.\n2. Adicione entradas ou arquivos.\n3. Use Salvar ou Ctrl+S para salvar tudo.|1. Buka atau buat brankas.\n2. Tambahkan entri atau berkas.\n3. Pilih Simpan atau tekan Ctrl+S untuk menyimpan semua perubahan.
Сохранено|Saved|已保存|सहेजा गया|Guardado|تم الحفظ|Enregistré|সংরক্ষিত|Salvo|Tersimpan
Сначала сохраните или отмените черновики в этой папке.|Save or discard the drafts in this folder first.|请先保存或放弃此文件夹中的草稿。|पहले इस फ़ोल्डर के मसौदे सहेजें या छोड़ें।|Primero guarda o descarta los borradores de esta carpeta.|احفظ مسودات هذا المجلد أو تجاهلها أولًا.|Enregistrez ou abandonnez d’abord les brouillons de ce dossier.|আগে এই ফোল্ডারের খসড়াগুলো সংরক্ষণ বা বাতিল করুন।|Primeiro salve ou descarte os rascunhos desta pasta.|Simpan atau buang draf di folder ini terlebih dahulu.
Выберите папку в дереве.|Select a folder in the tree.|请在目录树中选择一个文件夹。|ट्री में कोई फ़ोल्डर चुनें।|Selecciona una carpeta en el árbol.|اختر مجلدًا من الشجرة.|Sélectionnez un dossier dans l’arborescence.|ট্রি থেকে একটি ফোল্ডার বেছে নিন।|Selecione uma pasta na árvore.|Pilih folder di pohon.
Корневую папку удалить нельзя.|The root folder cannot be deleted.|不能删除根文件夹。|मूल फ़ोल्डर हटाया नहीं जा सकता।|No se puede eliminar la carpeta raíz.|لا يمكن حذف المجلد الجذر.|Impossible de supprimer le dossier racine.|মূল ফোল্ডার মুছে ফেলা যায় না।|A pasta raiz não pode ser excluída.|Folder akar tidak dapat dihapus.
В папке есть элементы или подпапки. Поиск и фильтры могут скрывать элементы.|This folder contains items or subfolders. Search and filters may hide items.|此文件夹包含项目或子文件夹。搜索和筛选可能会隐藏项目。|इस फ़ोल्डर में आइटम या उपफ़ोल्डर हैं। खोज और फ़िल्टर कुछ आइटम छिपा सकते हैं।|La carpeta contiene elementos o subcarpetas. La búsqueda y los filtros pueden ocultar elementos.|يحتوي المجلد على عناصر أو مجلدات فرعية. قد يخفي البحث والتصفية بعض العناصر.|Ce dossier contient des éléments ou des sous-dossiers. La recherche et les filtres peuvent masquer des éléments.|ফোল্ডারে আইটেম বা সাবফোল্ডার আছে। অনুসন্ধান ও ফিল্টার আইটেম লুকিয়ে রাখতে পারে।|A pasta contém itens ou subpastas. A busca e os filtros podem ocultar itens.|Folder ini berisi item atau subfolder. Pencarian dan filter dapat menyembunyikan item.
Очистить поиск|Clear search|清除搜索|खोज साफ़ करें|Borrar búsqueda|مسح البحث|Effacer la recherche|অনুসন্ধান মুছুন|Limpar busca|Hapus pencarian
Поиск ничего не нашёл в этом разделе.|No search results in this view.|当前视图没有搜索结果。|इस दृश्य में कोई खोज परिणाम नहीं मिला।|No hay resultados en esta vista.|لا توجد نتائج بحث في هذا العرض.|Aucun résultat dans cette vue.|এই ভিউতে কোনো অনুসন্ধান ফল নেই।|Nenhum resultado nesta visualização.|Tidak ada hasil pencarian di tampilan ini.
В папке уже есть хранилище с таким названием. Выберите другое название.|This folder already has a vault with this name. Choose another name.|此文件夹中已有同名密码库。请选择其他名称。|इस फ़ोल्डर में इसी नाम का वॉल्ट पहले से है। कोई दूसरा नाम चुनें।|Esta carpeta ya tiene una bóveda con este nombre. Elige otro nombre.|يوجد في هذا المجلد خزنة بهذا الاسم. اختر اسمًا آخر.|Ce dossier contient déjà un coffre portant ce nom. Choisissez un autre nom.|এই ফোল্ডারে এই নামের ভল্ট আগে থেকেই আছে। অন্য নাম বেছে নিন।|Esta pasta já tem um cofre com este nome. Escolha outro nome.|Folder ini sudah berisi brankas dengan nama ini. Pilih nama lain.
Это не последняя версия хранилища. В папке есть более новая: {0}.|This is not the latest version of the vault. A newer one is in the folder: {0}.|这不是密码库的最新版本。文件夹中有更新的版本：{0}。|यह वॉल्ट का नवीनतम संस्करण नहीं है। फ़ोल्डर में नया संस्करण है: {0}।|Esta no es la versión más reciente de la bóveda. Hay una más nueva en la carpeta: {0}.|هذا ليس أحدث إصدار من الخزنة. يوجد إصدار أحدث في المجلد: {0}.|Ce n’est pas la dernière version du coffre. Une version plus récente se trouve dans le dossier : {0}.|এটি ভল্টের সর্বশেষ সংস্করণ নয়। ফোল্ডারে নতুন সংস্করণ আছে: {0}।|Esta não é a versão mais recente do cofre. Há uma mais nova na pasta: {0}.|Ini bukan versi brankas terbaru. Ada versi yang lebih baru di folder: {0}.
Создать новый файл и зашифровать в {0}|Create new file and encrypt to {0}|新建文件并加密到 {0}|नई फ़ाइल बनाएँ और {0} में एन्क्रिप्ट करें|Crear archivo nuevo y cifrar en {0}|إنشاء ملف جديد وتشفيره في {0}|Créer un nouveau fichier et chiffrer dans {0}|নতুন ফাইল তৈরি করে {0}-এ এনক্রিপ্ট করুন|Criar novo arquivo e criptografar em {0}|Buat berkas baru dan enkripsi ke {0}
Сохранить|Save|保存|सहेजें|Guardar|حفظ|Enregistrer|সংরক্ষণ করুন|Salvar|Simpan
Новый файл v{0}|New file v{0}|新文件 v{0}|नई फ़ाइल v{0}|Archivo nuevo v{0}|ملف جديد v{0}|Nouveau fichier v{0}|নতুন ফাইল v{0}|Novo arquivo v{0}|Berkas baru v{0}
Не сохранено правок: {0}|Unsaved changes: {0}|未保存的更改：{0}|बिना सहेजे बदलाव: {0}|Cambios sin guardar: {0}|تغييرات غير محفوظة: {0}|Modifications non enregistrées : {0}|অসংরক্ষিত পরিবর্তন: {0}|Alterações não salvas: {0}|Perubahan belum disimpan: {0}
Компактно|Compact|紧凑|संक्षिप्त|Compacto|مضغوط|Compact|সংক্ষিপ্ত|Compacto|Ringkas
Широко|Wide|宽屏|चौड़ा|Amplio|واسع|Large|প্রশস্ত|Amplo|Lebar
Назад к списку|Back to list|返回列表|सूची पर वापस|Volver a la lista|العودة إلى القائمة|Retour à la liste|তালিকায় ফিরুন|Voltar à lista|Kembali ke daftar
Меню|Menu|菜单|मेनू|Menú|القائمة|Menu|মেনু|Menu|Menu
Текстовый файл|Text file|文本文件|टेक्स्ट फ़ाइल|Archivo de texto|ملف نصي|Fichier texte|টেক্সট ফাইল|Arquivo de texto|Berkas teks
Вид окна|Window view|窗口视图|विंडो दृश्य|Vista de la ventana|عرض النافذة|Affichage de la fenêtre|উইন্ডোর ভিউ|Visualização da janela|Tampilan jendela
Копировать|Copy|复制|कॉपी करें|Copiar|نسخ|Copier|কপি করুন|Copiar|Salin
Открыть|Open|打开|खोलें|Abrir|فتح|Ouvrir|খুলুন|Abrir|Buka
Скопировать логин|Copy username|复制用户名|उपयोगकर्ता नाम कॉपी करें|Copiar usuario|نسخ اسم المستخدم|Copier l’identifiant|ব্যবহারকারীর নাম কপি করুন|Copiar usuário|Salin nama pengguna
Скопировать пароль|Copy password|复制密码|पासवर्ड कॉपी करें|Copiar contraseña|نسخ كلمة المرور|Copier le mot de passe|পাসওয়ার্ড কপি করুন|Copiar senha|Salin kata sandi
Логин скопирован|Username copied|用户名已复制|उपयोगकर्ता नाम कॉपी हुआ|Usuario copiado|تم نسخ اسم المستخدم|Identifiant copié|ব্যবহারকারীর নাম কপি হয়েছে|Usuário copiado|Nama pengguna disalin
Пароль скопирован|Password copied|密码已复制|पासवर्ड कॉपी हुआ|Contraseña copiada|تم نسخ كلمة المرور|Mot de passe copié|পাসওয়ার্ড কপি হয়েছে|Senha copiada|Kata sandi disalin
Адрес скопирован|Address copied|地址已复制|पता कॉपी हुआ|Dirección copiada|تم نسخ العنوان|Adresse copiée|ঠিকানা কপি হয়েছে|Endereço copiado|Alamat disalin
Буфер обмена занят другой программой. Повторите копирование.|The clipboard is in use by another program. Try copying again.|剪贴板正被其他程序占用。请重新复制。|क्लिपबोर्ड का उपयोग कोई दूसरा प्रोग्राम कर रहा है। फिर से कॉपी करें।|Otro programa está usando el portapapeles. Vuelve a copiar.|الحافظة مستخدمة من برنامج آخر. أعد النسخ.|Le presse-papiers est utilisé par un autre programme. Recommencez la copie.|ক্লিপবোর্ড অন্য প্রোগ্রাম ব্যবহার করছে। আবার কপি করুন।|A área de transferência está em uso por outro programa. Copie novamente.|Papan klip sedang dipakai program lain. Salin lagi.
Не удалось открыть адрес в браузере.|Could not open the address in the browser.|无法在浏览器中打开该地址。|ब्राउज़र में पता नहीं खुल सका।|No se pudo abrir la dirección en el navegador.|تعذر فتح العنوان في المتصفح.|Impossible d’ouvrir l’adresse dans le navigateur.|ব্রাউজারে ঠিকানা খোলা যায়নি।|Não foi possível abrir o endereço no navegador.|Alamat tidak dapat dibuka di peramban.
Новая вложенная папка|New subfolder|新建子文件夹|नया उपफ़ोल्डर|Nueva subcarpeta|مجلد فرعي جديد|Nouveau sous-dossier|নতুন সাবফোল্ডার|Nova subpasta|Subfolder baru
Удалить папку|Delete folder|删除文件夹|फ़ोल्डर हटाएँ|Eliminar carpeta|حذف المجلد|Supprimer le dossier|ফোল্ডার মুছুন|Excluir pasta|Hapus folder
Действия с папкой|Folder actions|文件夹操作|फ़ोल्डर की कार्रवाइयाँ|Acciones de carpeta|إجراءات المجلد|Actions du dossier|ফোল্ডারের কাজ|Ações da pasta|Tindakan folder
Сортировка|Sort|排序|क्रम|Orden|الترتيب|Tri|সাজানো|Ordem|Urutan
По названию А→Я|By title A→Z|按标题 A→Z|शीर्षक से अ→ह|Por título A→Z|حسب العنوان أ→ي|Par titre A→Z|শিরোনাম অনুযায়ী অ→হ|Por título A→Z|Menurut judul A→Z
По названию Я→А|By title Z→A|按标题 Z→A|शीर्षक से ह→अ|Por título Z→A|حسب العنوان ي→أ|Par titre Z→A|শিরোনাম অনুযায়ী হ→অ|Por título Z→A|Menurut judul Z→A
По дате: сначала новые|By date: newest first|按日期：最新在前|तारीख से: नए पहले|Por fecha: recientes primero|حسب التاريخ: الأحدث أولًا|Par date : récents d’abord|তারিখ অনুযায়ী: নতুন আগে|Por data: recentes primeiro|Menurut tanggal: terbaru dulu
По дате: сначала старые|By date: oldest first|按日期：最早在前|तारीख से: पुराने पहले|Por fecha: antiguos primero|حسب التاريخ: الأقدم أولًا|Par date : anciens d’abord|তারিখ অনুযায়ী: পুরোনো আগে|Por data: antigos primeiro|Menurut tanggal: terlama dulu
А→Я|A→Z|A→Z|अ→ह|A→Z|أ→ي|A→Z|অ→হ|A→Z|A→Z
Я→А|Z→A|Z→A|ह→अ|Z→A|ي→أ|Z→A|হ→অ|Z→A|Z→A
Сортировка папок|Folder order|文件夹排序|फ़ोल्डरों का क्रम|Orden de carpetas|ترتيب المجلدات|Tri des dossiers|ফোল্ডার সাজানো|Ordem das pastas|Urutan folder
Адрес должен начинаться с http:// или https://|The address must start with http:// or https://|地址必须以 http:// 或 https:// 开头|पता http:// या https:// से शुरू होना चाहिए|La dirección debe empezar por http:// o https://|يجب أن يبدأ العنوان بـ http:// أو https://|L’adresse doit commencer par http:// ou https://|ঠিকানা http:// বা https:// দিয়ে শুরু হতে হবে|O endereço deve começar com http:// ou https://|Alamat harus diawali http:// atau https://
Скопировать адрес|Copy address|复制地址|पता कॉपी करें|Copiar dirección|نسخ العنوان|Copier l’adresse|ঠিকানা কপি করুন|Copiar endereço|Salin alamat
Быстрый поиск (Ctrl+K)|Quick search (Ctrl+K)|快速搜索 (Ctrl+K)|त्वरित खोज (Ctrl+K)|Búsqueda rápida (Ctrl+K)|بحث سريع (Ctrl+K)|Recherche rapide (Ctrl+K)|দ্রুত অনুসন্ধান (Ctrl+K)|Busca rápida (Ctrl+K)|Pencarian cepat (Ctrl+K)
Быстрый поиск|Quick search|快速搜索|त्वरित खोज|Búsqueda rápida|بحث سريع|Recherche rapide|দ্রুত অনুসন্ধান|Busca rápida|Pencarian cepat
НАЙДЕНО {0}|FOUND {0}|找到 {0} 项|{0} मिले|ENCONTRADOS {0}|تم العثور على {0}|TROUVÉS {0}|{0}টি পাওয়া গেছে|ENCONTRADOS {0}|DITEMUKAN {0}
Ничего не найдено|Nothing found|未找到任何内容|कुछ नहीं मिला|No se encontró nada|لم يتم العثور على شيء|Aucun résultat|কিছু পাওয়া যায়নি|Nada encontrado|Tidak ada yang ditemukan
пароль в буфер|copy password|复制密码|पासवर्ड कॉपी|copiar contraseña|نسخ كلمة المرور|copier le mot de passe|পাসওয়ার্ড কপি|copiar senha|salin kata sandi
логин в буфер|copy username|复制用户名|उपयोगकर्ता नाम कॉपी|copiar usuario|نسخ اسم المستخدم|copier l’identifiant|ব্যবহারকারীর নাম কপি|copiar usuário|salin nama pengguna
открыть запись|open item|打开项目|आइटम खोलें|abrir elemento|فتح العنصر|ouvrir l’élément|আইটেম খুলুন|abrir item|buka item
выбор|select|选择|चुनें|seleccionar|اختيار|sélection|নির্বাচন|selecionar|pilih
скопировать пароль|copy password|复制密码|पासवर्ड कॉपी करें|copiar contraseña|نسخ كلمة المرور|copier le mot de passe|পাসওয়ার্ড কপি করুন|copiar senha|salin kata sandi
открыть файл|open file|打开文件|फ़ाइल खोलें|abrir archivo|فتح الملف|ouvrir le fichier|ফাইল খুলুন|abrir arquivo|buka berkas
У записи нет пароля|This entry has no password|此条目没有密码|इस प्रविष्टि में पासवर्ड नहीं है|Esta entrada no tiene contraseña|لا توجد كلمة مرور لهذا الإدخال|Cette entrée n’a pas de mot de passe|এই এন্ট্রিতে পাসওয়ার্ড নেই|Esta entrada não tem senha|Entri ini tidak memiliki kata sandi
У записи нет логина|This entry has no username|此条目没有用户名|इस प्रविष्टि में उपयोगकर्ता नाम नहीं है|Esta entrada no tiene usuario|لا يوجد اسم مستخدم لهذا الإدخال|Cette entrée n’a pas d’identifiant|এই এন্ট্রিতে ব্যবহারকারীর নাম নেই|Esta entrada não tem usuário|Entri ini tidak memiliki nama pengguna
С возвращением|Welcome back|欢迎回来|वापसी पर स्वागत है|Bienvenido de nuevo|مرحبًا بعودتك|Bon retour|আবার স্বাগতম|Bem-vindo de volta|Selamat datang kembali
Главный пароль|Master password|主密码|मास्टर पासवर्ड|Contraseña maestra|كلمة المرور الرئيسية|Mot de passe principal|মাস্টার পাসওয়ার্ড|Senha mestra|Kata sandi utama
последний|last opened|上次打开|पिछली बार खुला|último|آخر ما فُتح|dernier ouvert|সর্বশেষ খোলা|último aberto|terakhir dibuka
ДРУГИЕ НЕДАВНИЕ|OTHER RECENT FILES|其他最近文件|अन्य हाल की फ़ाइलें|OTROS RECIENTES|ملفات حديثة أخرى|AUTRES FICHIERS RÉCENTS|অন্যান্য সাম্প্রতিক ফাইল|OUTROS RECENTES|BERKAS TERBARU LAINNYA
есть версия новее|a newer version exists|有更新的版本|नया संस्करण मौजूद है|hay una versión más nueva|يوجد إصدار أحدث|une version plus récente existe|নতুন সংস্করণ আছে|há uma versão mais nova|ada versi lebih baru
файл не найден|file not found|找不到文件|फ़ाइल नहीं मिली|archivo no encontrado|الملف غير موجود|fichier introuvable|ফাইল পাওয়া যায়নি|arquivo não encontrado|berkas tidak ditemukan
Убрать из списка|Remove from list|从列表中移除|सूची से हटाएँ|Quitar de la lista|إزالة من القائمة|Retirer de la liste|তালিকা থেকে সরান|Remover da lista|Hapus dari daftar
Открыть другой файл…|Open another file…|打开其他文件…|दूसरी फ़ाइल खोलें…|Abrir otro archivo…|فتح ملف آخر…|Ouvrir un autre fichier…|অন্য ফাইল খুলুন…|Abrir outro arquivo…|Buka berkas lain…
Создать новое хранилище|Create new vault|新建密码库|नया वॉल्ट बनाएँ|Crear bóveda nueva|إنشاء خزنة جديدة|Créer un nouveau coffre|নতুন ভল্ট তৈরি করুন|Criar novo cofre|Buat brankas baru
открыт {0}|opened {0}|打开于 {0}|{0} को खोला गया|abierto {0}|فُتح {0}|ouvert le {0}|খোলা হয়েছে {0}|aberto em {0}|dibuka {0}
самая новая|newest|最新|सबसे नया|la más reciente|الأحدث|la plus récente|সর্বশেষ|a mais recente|terbaru
Версии этого хранилища…|Versions of this vault…|此密码库的版本…|इस वॉल्ट के संस्करण…|Versiones de esta bóveda…|إصدارات هذه الخزنة…|Versions de ce coffre…|এই ভল্টের সংস্করণ…|Versões deste cofre…|Versi brankas ini…
Версии хранилища «{0}»|Versions of vault “{0}”|密码库“{0}”的版本|वॉल्ट “{0}” के संस्करण|Versiones de la bóveda «{0}»|إصدارات الخزنة «{0}»|Versions du coffre « {0} »|ভল্ট “{0}”-এর সংস্করণ|Versões do cofre “{0}”|Versi brankas “{0}”
папка {0}|folder {0}|文件夹 {0}|फ़ोल्डर {0}|carpeta {0}|المجلد {0}|dossier {0}|ফোল্ডার {0}|pasta {0}|folder {0}
открыта|open now|已打开|खुला है|abierta|مفتوحة|ouverte|খোলা আছে|aberta|sedang dibuka
изменён {0}|modified {0}|修改于 {0}|{0} को बदला गया|modificado {0}|عُدّل {0}|modifié le {0}|পরিবর্তিত {0}|modificado em {0}|diubah {0}
Следующий файл: {0}|Next file: {0}|下一个文件：{0}|अगली फ़ाइल: {0}|Siguiente archivo: {0}|الملف التالي: {0}|Fichier suivant : {0}|পরবর্তী ফাইল: {0}|Próximo arquivo: {0}|Berkas berikutnya: {0}
Показать в проводнике|Show in Explorer|在资源管理器中显示|एक्सप्लोरर में दिखाएँ|Mostrar en el Explorador|إظهار في المستكشف|Afficher dans l’Explorateur|এক্সপ্লোরারে দেখান|Mostrar no Explorador|Tampilkan di Explorer
{0} КБ|{0} KB|{0} KB|{0} KB|{0} KB|{0} ك.ب|{0} Ko|{0} KB|{0} KB|{0} KB
{0} МБ|{0} MB|{0} MB|{0} MB|{0} MB|{0} م.ب|{0} Mo|{0} MB|{0} MB|{0} MB
Не удалось открыть проводник.|Could not open Explorer.|无法打开资源管理器。|एक्सप्लोरर नहीं खुल सका।|No se pudo abrir el Explorador.|تعذر فتح المستكشف.|Impossible d’ouvrir l’Explorateur.|এক্সপ্লোরার খোলা যায়নি।|Não foi possível abrir o Explorador.|Tidak dapat membuka Explorer.
Сгенерировать пароль|Generate password|生成密码|पासवर्ड बनाएँ|Generar contraseña|إنشاء كلمة مرور|Générer un mot de passe|পাসওয়ার্ড তৈরি করুন|Gerar senha|Buat kata sandi
Генератор пароля|Password generator|密码生成器|पासवर्ड जनरेटर|Generador de contraseñas|مولّد كلمات المرور|Générateur de mots de passe|পাসওয়ার্ড জেনারেটর|Gerador de senhas|Pembuat kata sandi
Сгенерировать заново|Generate again|重新生成|फिर से बनाएँ|Generar de nuevo|إنشاء من جديد|Générer à nouveau|আবার তৈরি করুন|Gerar novamente|Buat ulang
Надёжность|Strength|强度|मज़बूती|Seguridad|القوة|Robustesse|শক্তি|Força|Kekuatan
слабая|weak|弱|कमज़ोर|baja|ضعيفة|faible|দুর্বল|fraca|lemah
средняя|medium|中|मध्यम|media|متوسطة|moyenne|মাঝারি|média|sedang
высокая|strong|强|मज़बूत|alta|قوية|élevée|শক্তিশালী|forte|kuat
Длина|Length|长度|लंबाई|Longitud|الطول|Longueur|দৈর্ঘ্য|Comprimento|Panjang
Заглавные A–Z|Uppercase A–Z|大写字母 A–Z|बड़े अक्षर A–Z|Mayúsculas A–Z|أحرف كبيرة A–Z|Majuscules A–Z|বড় হাতের A–Z|Maiúsculas A–Z|Huruf besar A–Z
Строчные a–z|Lowercase a–z|小写字母 a–z|छोटे अक्षर a–z|Minúsculas a–z|أحرف صغيرة a–z|Minuscules a–z|ছোট হাতের a–z|Minúsculas a–z|Huruf kecil a–z
Цифры 0–9|Digits 0–9|数字 0–9|अंक 0–9|Números 0–9|أرقام 0–9|Chiffres 0–9|অঙ্ক 0–9|Números 0–9|Angka 0–9
Символы|Symbols|符号|चिह्न|Símbolos|رموز|Symboles|চিহ্ন|Símbolos|Simbol
Без похожих: O 0 l 1 I|No look-alikes: O 0 l 1 I|排除相似字符：O 0 l 1 I|मिलते-जुलते नहीं: O 0 l 1 I|Sin parecidos: O 0 l 1 I|بدون المتشابهة: O 0 l 1 I|Sans caractères ambigus : O 0 l 1 I|একই রকম বাদে: O 0 l 1 I|Sem parecidos: O 0 l 1 I|Tanpa yang mirip: O 0 l 1 I
Подставить в запись|Use in entry|填入条目|प्रविष्टि में डालें|Usar en la entrada|استخدام في الإدخال|Utiliser dans l’entrée|এন্ট্রিতে বসান|Usar na entrada|Pakai di entri
Выберите хотя бы один набор символов.|Choose at least one character set.|请至少选择一种字符集。|कम से कम एक वर्ण समूह चुनें।|Elige al menos un conjunto de caracteres.|اختر مجموعة أحرف واحدة على الأقل.|Choisissez au moins un jeu de caractères.|অন্তত একটি অক্ষরের সেট বেছে নিন।|Escolha pelo menos um conjunto de caracteres.|Pilih setidaknya satu kumpulan karakter.
Пароль подставлен. Сохраните запись.|Password inserted. Save the entry.|已填入密码。请保存条目。|पासवर्ड डाल दिया गया। प्रविष्टि सहेजें।|Contraseña insertada. Guarda la entrada.|تم إدراج كلمة المرور. احفظ الإدخال.|Mot de passe inséré. Enregistrez l’entrée.|পাসওয়ার্ড বসানো হয়েছে। এন্ট্রি সংরক্ষণ করুন।|Senha inserida. Salve a entrada.|Kata sandi dimasukkan. Simpan entri.
Заголовок|Heading|标题|शीर्षक|Encabezado|عنوان|Titre|শিরোনাম|Título|Judul
Разделитель|Divider|分隔线|विभाजक|Separador|فاصل|Séparateur|বিভাজক|Separador|Pemisah
Как оформить текст|How to format text|如何设置文本格式|टेक्स्ट को कैसे सजाएँ|Cómo dar formato al texto|كيفية تنسيق النص|Mise en forme du texte|লেখা কীভাবে সাজাবেন|Como formatar o texto|Cara memformat teks
Строка, которая начинается с «# », показывается как заголовок. Строка «---» показывается как разделитель. В файле хранится обычный текст с этими знаками. Другой разметки нет.|A line that starts with “# ” is shown as a heading. A “---” line is shown as a divider. The file stores plain text with these marks. There is no other markup.|以“# ”开头的行显示为标题。“---”行显示为分隔线。文件中保存的是带有这些符号的纯文本。没有其他标记。|“# ” से शुरू होने वाली पंक्ति शीर्षक के रूप में दिखती है। “---” वाली पंक्ति विभाजक के रूप में दिखती है। फ़ाइल में इन चिह्नों के साथ सादा टेक्स्ट रहता है। कोई अन्य मार्कअप नहीं है।|Una línea que empieza con «# » se muestra como encabezado. Una línea «---» se muestra como separador. El archivo guarda texto sin formato con estos signos. No hay otro marcado.|السطر الذي يبدأ بـ «# » يظهر عنوانًا. السطر «---» يظهر فاصلًا. يحفظ الملف نصًا عاديًا بهذه العلامات. لا يوجد تنسيق آخر.|Une ligne qui commence par « # » s’affiche comme un titre. Une ligne « --- » s’affiche comme un séparateur. Le fichier contient du texte brut avec ces signes. Il n’y a pas d’autre balisage.|“# ” দিয়ে শুরু হওয়া লাইন শিরোনাম হিসেবে দেখায়। “---” লাইন বিভাজক হিসেবে দেখায়। ফাইলে এই চিহ্নসহ সাধারণ লেখা থাকে। অন্য কোনো মার্কআপ নেই।|Uma linha que começa com “# ” aparece como título. Uma linha “---” aparece como separador. O arquivo guarda texto simples com esses sinais. Não há outra marcação.|Baris yang diawali “# ” ditampilkan sebagai judul. Baris “---” ditampilkan sebagai pemisah. Berkas menyimpan teks biasa dengan tanda ini. Tidak ada markup lain.
Вложения · {0}|Attachments · {0}|附件 · {0}|अनुलग्नक · {0}|Adjuntos · {0}|المرفقات · {0}|Pièces jointes · {0}|সংযুক্তি · {0}|Anexos · {0}|Lampiran · {0}
всего {0} · хранятся внутри зашифрованного файла|{0} in total · stored inside the encrypted file|共 {0} · 存储在加密文件内|कुल {0} · एन्क्रिप्टेड फ़ाइल के अंदर रखे जाते हैं|{0} en total · se guardan dentro del archivo cifrado|المجموع {0} · تُحفظ داخل الملف المشفّر|{0} au total · stockées dans le fichier chiffré|মোট {0} · এনক্রিপ্ট করা ফাইলের ভিতরে রাখা হয়|{0} no total · guardados dentro do arquivo criptografado|total {0} · disimpan di dalam berkas terenkripsi
Перетащите сюда картинки, документы или архивы|Drop images, documents or archives here|将图片、文档或压缩包拖到这里|चित्र, दस्तावेज़ या आर्काइव यहाँ खींचकर छोड़ें|Arrastra aquí imágenes, documentos o archivos comprimidos|اسحب الصور أو المستندات أو الأرشيفات إلى هنا|Déposez ici des images, des documents ou des archives|ছবি, নথি বা আর্কাইভ এখানে টেনে আনুন|Arraste para cá imagens, documentos ou arquivos compactados|Seret gambar, dokumen, atau arsip ke sini
или|or|或|या|o|أو|ou|অথবা|ou|atau
Выбрать файлы…|Choose files…|选择文件…|फ़ाइलें चुनें…|Elegir archivos…|اختيار ملفات…|Choisir des fichiers…|ফাইল বেছে নিন…|Escolher arquivos…|Pilih berkas…
Просмотр|View|查看|देखें|Ver|عرض|Afficher|দেখুন|Ver|Lihat
Сохранить как…|Save as…|另存为…|इस रूप में सहेजें…|Guardar como…|حفظ باسم…|Enregistrer sous…|এভাবে সংরক্ষণ করুন…|Salvar como…|Simpan sebagai…
Удалить вложение|Remove attachment|删除附件|अनुलग्नक हटाएँ|Quitar adjunto|إزالة المرفق|Supprimer la pièce jointe|সংযুক্তি সরান|Remover anexo|Hapus lampiran
Картинка|Image|图片|चित्र|Imagen|صورة|Image|ছবি|Imagem|Gambar
Документ|Document|文档|दस्तावेज़|Documento|مستند|Document|নথি|Documento|Dokumen
Архив|Archive|压缩包|आर्काइव|Archivo comprimido|أرشيف|Archive|আর্কাইভ|Arquivo compactado|Arsip
Файл|File|文件|फ़ाइल|Archivo|ملف|Fichier|ফাইল|Arquivo|Berkas
добавлено {0}|added {0}|添加于 {0}|{0} को जोड़ा गया|añadido {0}|أُضيف {0}|ajouté le {0}|যোগ করা হয়েছে {0}|adicionado em {0}|ditambahkan {0}
Место в хранилище|Vault space|密码库空间|वॉल्ट में जगह|Espacio en la bóveda|مساحة الخزنة|Espace du coffre|ভল্টের জায়গা|Espaço no cofre|Ruang brankas
{0} из 512 МБ|{0} of 512 MB|{0} / 512 MB|512 MB में से {0}|{0} de 512 MB|{0} من 512 م.ب|{0} sur 512 Mo|512 MB-এর মধ্যে {0}|{0} de 512 MB|{0} dari 512 MB
Удалить вложение «{0}»?|Remove attachment “{0}”?|删除附件“{0}”？|अनुलग्नक “{0}” हटाएँ?|¿Quitar el adjunto «{0}»?|إزالة المرفق «{0}»؟|Supprimer la pièce jointe « {0} » ?|সংযুক্তি “{0}” সরাবেন?|Remover o anexo “{0}”?|Hapus lampiran “{0}”?
Удалить|Remove|删除|हटाएँ|Quitar|إزالة|Supprimer|সরান|Remover|Hapus
Добавить|Add|添加|जोड़ें|Añadir|إضافة|Ajouter|যোগ করুন|Adicionar|Tambah
Хранилище станет примерно {0} МБ. Открытие и каждое сохранение будут занимать заметное время, а каждая новая версия файла займёт столько же места на диске.|The vault will be about {0} MB. Opening and every save will take noticeable time, and every new version file will take the same space on disk.|密码库将约为 {0} MB。打开和每次保存都会明显变慢，每个新版本文件也会占用同样的磁盘空间。|वॉल्ट लगभग {0} MB का हो जाएगा। खोलने और हर बार सहेजने में ध्यान देने योग्य समय लगेगा, और हर नई संस्करण फ़ाइल डिस्क पर इतनी ही जगह लेगी।|La bóveda ocupará unos {0} MB. Abrirla y cada guardado tardarán un tiempo notable, y cada nuevo archivo de versión ocupará el mismo espacio en disco.|سيصبح حجم الخزنة نحو {0} م.ب. سيستغرق الفتح وكل حفظ وقتًا ملحوظًا، وسيشغل كل ملف إصدار جديد المساحة نفسها على القرص.|Le coffre fera environ {0} Mo. L’ouverture et chaque enregistrement prendront un temps sensible, et chaque nouveau fichier de version occupera autant d’espace disque.|ভল্ট প্রায় {0} MB হবে। খোলা ও প্রতিবার সংরক্ষণে লক্ষণীয় সময় লাগবে, আর প্রতিটি নতুন সংস্করণ ফাইল ডিস্কে একই জায়গা নেবে।|O cofre ficará com cerca de {0} MB. Abrir e cada salvamento levarão um tempo perceptível, e cada novo arquivo de versão ocupará o mesmo espaço em disco.|Brankas akan menjadi sekitar {0} MB. Membuka dan setiap penyimpanan akan memakan waktu yang terasa, dan setiap berkas versi baru akan memakan ruang disk yang sama.
Хранилище станет примерно {0} МБ — больше предела 512 МБ. Файлы не добавлены. Такой файл открывался бы и сохранялся слишком долго и мог бы не поместиться в память. Храните крупные файлы отдельно.|The vault would be about {0} MB, over the 512 MB limit. The files were not added. Such a file would take too long to open and save and might not fit in memory. Keep large files separately.|密码库将约为 {0} MB，超过 512 MB 上限。文件未添加。这样的文件打开和保存都会太慢，且可能无法放入内存。请将大文件单独保存。|वॉल्ट लगभग {0} MB का हो जाता — 512 MB की सीमा से अधिक। फ़ाइलें नहीं जोड़ी गईं। ऐसी फ़ाइल खोलने और सहेजने में बहुत समय लेती और शायद मेमोरी में न समाती। बड़ी फ़ाइलें अलग रखें।|La bóveda ocuparía unos {0} MB, más que el límite de 512 MB. Los archivos no se añadieron. Un archivo así tardaría demasiado en abrirse y guardarse y podría no caber en memoria. Guarda los archivos grandes por separado.|سيصبح حجم الخزنة نحو {0} م.ب، أي أكثر من حد 512 م.ب. لم تُضف الملفات. سيستغرق فتح هذا الملف وحفظه وقتًا طويلًا جدًا وقد لا يتسع في الذاكرة. احفظ الملفات الكبيرة بشكل منفصل.|Le coffre ferait environ {0} Mo, au-delà de la limite de 512 Mo. Les fichiers n’ont pas été ajoutés. Un tel fichier serait trop long à ouvrir et à enregistrer et pourrait ne pas tenir en mémoire. Conservez les gros fichiers à part.|ভল্ট প্রায় {0} MB হত — 512 MB সীমার বেশি। ফাইল যোগ করা হয়নি। এমন ফাইল খুলতে ও সংরক্ষণ করতে খুব বেশি সময় লাগত এবং মেমরিতে নাও ধরতে পারত। বড় ফাইল আলাদা রাখুন।|O cofre ficaria com cerca de {0} MB, acima do limite de 512 MB. Os arquivos não foram adicionados. Um arquivo assim demoraria demais para abrir e salvar e poderia não caber na memória. Guarde arquivos grandes separadamente.|Brankas akan menjadi sekitar {0} MB, melebihi batas 512 MB. Berkas tidak ditambahkan. Berkas seperti itu akan terlalu lama dibuka dan disimpan serta mungkin tidak muat di memori. Simpan berkas besar secara terpisah.
Открываю…|Opening…|正在打开…|खोला जा रहा है…|Abriendo…|جارٍ الفتح…|Ouverture…|খোলা হচ্ছে…|Abrindo…|Membuka…
Сохраняю и шифрую…|Saving and encrypting…|正在保存并加密…|सहेजा और एन्क्रिप्ट किया जा रहा है…|Guardando y cifrando…|جارٍ الحفظ والتشفير…|Enregistrement et chiffrement…|সংরক্ষণ ও এনক্রিপ্ট করা হচ্ছে…|Salvando e criptografando…|Menyimpan dan mengenkripsi…
Читаю файлы…|Reading files…|正在读取文件…|फ़ाइलें पढ़ी जा रही हैं…|Leyendo archivos…|جارٍ قراءة الملفات…|Lecture des fichiers…|ফাইল পড়া হচ্ছে…|Lendo arquivos…|Membaca berkas…
Не удалось показать картинку: формат не поддерживается или файл повреждён.|Could not show the image: the format is not supported or the file is damaged.|无法显示图片：格式不受支持或文件已损坏。|चित्र नहीं दिखाया जा सका: प्रारूप समर्थित नहीं है या फ़ाइल क्षतिग्रस्त है।|No se pudo mostrar la imagen: el formato no es compatible o el archivo está dañado.|تعذر عرض الصورة: التنسيق غير مدعوم أو الملف تالف.|Impossible d’afficher l’image : format non pris en charge ou fichier endommagé.|ছবি দেখানো যায়নি: ফরম্যাট সমর্থিত নয় বা ফাইল ক্ষতিগ্রস্ত।|Não foi possível mostrar a imagem: o formato não é suportado ou o arquivo está danificado.|Tidak dapat menampilkan gambar: format tidak didukung atau berkas rusak.
Вложение сохранено|Attachment saved|附件已保存|अनुलग्नक सहेजा गया|Adjunto guardado|تم حفظ المرفق|Pièce jointe enregistrée|সংযুক্তি সংরক্ষিত|Anexo salvo|Lampiran disimpan
Вложение удалено. Сохраните хранилище.|Attachment removed. Save the vault.|附件已删除。请保存密码库。|अनुलग्नक हटाया गया। वॉल्ट सहेजें।|Adjunto quitado. Guarda la bóveda.|أُزيل المرفق. احفظ الخزنة.|Pièce jointe supprimée. Enregistrez le coffre.|সংযুক্তি সরানো হয়েছে। ভল্ট সংরক্ষণ করুন।|Anexo removido. Salve o cofre.|Lampiran dihapus. Simpan brankas.
Вложений добавлено: {0}. Сохраните хранилище.|Attachments added: {0}. Save the vault.|已添加附件：{0}。请保存密码库。|जोड़े गए अनुलग्नक: {0}। वॉल्ट सहेजें।|Adjuntos añadidos: {0}. Guarda la bóveda.|المرفقات المضافة: {0}. احفظ الخزنة.|Pièces jointes ajoutées : {0}. Enregistrez le coffre.|সংযুক্তি যোগ হয়েছে: {0}। ভল্ট সংরক্ষণ করুন।|Anexos adicionados: {0}. Salve o cofre.|Lampiran ditambahkan: {0}. Simpan brankas.
Недопустимый размер файла хранилища: он пустой или больше 512 МБ. Файл не расшифровывался и не изменён.|Invalid vault file size: it is empty or larger than 512 MB. The file was not decrypted or changed.|密码库文件大小无效：文件为空或超过 512 MB。文件未被解密，也未被修改。|वॉल्ट फ़ाइल का आकार अमान्य है: वह खाली है या 512 MB से बड़ी है। फ़ाइल न डिक्रिप्ट की गई, न बदली गई।|Tamaño de archivo de bóveda no válido: está vacío o supera 512 MB. El archivo no se descifró ni se modificó.|حجم ملف الخزنة غير صالح: فارغ أو أكبر من 512 م.ب. لم يُفك تشفير الملف ولم يُغيَّر.|Taille de fichier de coffre non valide : il est vide ou dépasse 512 Mo. Le fichier n’a été ni déchiffré ni modifié.|ভল্ট ফাইলের আকার অবৈধ: এটি খালি বা 512 MB-এর বেশি। ফাইল ডিক্রিপ্ট বা পরিবর্তন করা হয়নি।|Tamanho de arquivo de cofre inválido: está vazio ou tem mais de 512 MB. O arquivo não foi descriptografado nem alterado.|Ukuran berkas brankas tidak valid: kosong atau lebih dari 512 MB. Berkas tidak didekripsi dan tidak diubah.
Хранилище стало бы больше 512 МБ. Изменения не записаны, файл на диске не изменён. Удалите крупные вложения и сохраните снова.|The vault would be larger than 512 MB. The changes were not written and the file on disk is unchanged. Remove large attachments and save again.|密码库将超过 512 MB。更改未写入，磁盘上的文件未被修改。请删除较大的附件后再保存。|वॉल्ट 512 MB से बड़ा हो जाता। बदलाव नहीं लिखे गए, डिस्क की फ़ाइल नहीं बदली। बड़े अनुलग्नक हटाकर फिर से सहेजें।|La bóveda superaría 512 MB. Los cambios no se escribieron y el archivo en disco no cambió. Quita los adjuntos grandes y guarda de nuevo.|ستصبح الخزنة أكبر من 512 م.ب. لم تُكتب التغييرات ولم يتغير الملف على القرص. أزل المرفقات الكبيرة واحفظ مجددًا.|Le coffre dépasserait 512 Mo. Les modifications n’ont pas été écrites et le fichier sur le disque n’a pas changé. Supprimez les grosses pièces jointes et enregistrez à nouveau.|ভল্ট 512 MB-এর বেশি হয়ে যেত। পরিবর্তন লেখা হয়নি, ডিস্কের ফাইল বদলায়নি। বড় সংযুক্তি সরিয়ে আবার সংরক্ষণ করুন।|O cofre ficaria maior que 512 MB. As alterações não foram gravadas e o arquivo no disco não mudou. Remova anexos grandes e salve novamente.|Brankas akan lebih dari 512 MB. Perubahan tidak ditulis dan berkas di disk tidak berubah. Hapus lampiran besar lalu simpan lagi.
Файл больше 512 МБ и не поместится в хранилище. Вложение не добавлено.|The file is larger than 512 MB and will not fit in the vault. The attachment was not added.|文件超过 512 MB，无法放入密码库。附件未添加。|फ़ाइल 512 MB से बड़ी है और वॉल्ट में नहीं समाएगी। अनुलग्नक नहीं जोड़ा गया।|El archivo supera 512 MB y no cabe en la bóveda. El adjunto no se añadió.|الملف أكبر من 512 م.ب ولن يتسع في الخزنة. لم يُضف المرفق.|Le fichier dépasse 512 Mo et ne tiendra pas dans le coffre. La pièce jointe n’a pas été ajoutée.|ফাইলটি 512 MB-এর বেশি, ভল্টে ধরবে না। সংযুক্তি যোগ করা হয়নি।|O arquivo tem mais de 512 MB e não cabe no cofre. O anexo não foi adicionado.|Berkas lebih dari 512 MB dan tidak muat di brankas. Lampiran tidak ditambahkan.
Добавить вложения|Add attachments|添加附件|अनुलग्नक जोड़ें|Añadir adjuntos|إضافة مرفقات|Ajouter des pièces jointes|সংযুক্তি যোগ করুন|Adicionar anexos|Tambah lampiran
хранятся внутри зашифрованного файла|stored inside the encrypted file|存储在加密文件内|एन्क्रिप्टेड फ़ाइल के अंदर रखे जाते हैं|se guardan dentro del archivo cifrado|تُحفظ داخل الملف المشفّر|stockées dans le fichier chiffré|এনক্রিপ্ট করা ফাইলের ভিতরে রাখা হয়|guardados dentro do arquivo criptografado|disimpan di dalam berkas terenkripsi
Вложения|Attachments|附件|अनुलग्नक|Adjuntos|المرفقات|Pièces jointes|সংযুক্তি|Anexos|Lampiran
У вложений одной записи имена должны быть разными и непустыми.|Attachments of one item must have different, non-empty names.|同一条目的附件名称必须互不相同且不为空。|एक आइटम के अनुलग्नकों के नाम अलग-अलग और खाली नहीं होने चाहिए।|Los adjuntos de un elemento deben tener nombres distintos y no vacíos.|يجب أن تكون أسماء مرفقات العنصر الواحد مختلفة وغير فارغة.|Les pièces jointes d’un élément doivent avoir des noms différents et non vides.|একটি আইটেমের সংযুক্তির নাম আলাদা ও খালি নয় এমন হতে হবে।|Os anexos de um item devem ter nomes diferentes e não vazios.|Lampiran satu item harus memiliki nama berbeda dan tidak kosong.
Найти далее|Find next|查找下一个|अगला खोजें|Buscar siguiente|البحث عن التالي|Suivant|পরেরটি খুঁজুন|Localizar próximo|Cari berikutnya
Копировать всё|Copy all|全部复制|सब कॉपी करें|Copiar todo|نسخ الكل|Tout copier|সব কপি করুন|Copiar tudo|Salin semua
Поиск по тексту (Ctrl+F)|Search the text (Ctrl+F)|在文本中搜索 (Ctrl+F)|टेक्स्ट में खोजें (Ctrl+F)|Buscar en el texto (Ctrl+F)|البحث في النص (Ctrl+F)|Rechercher dans le texte (Ctrl+F)|লেখায় খুঁজুন (Ctrl+F)|Pesquisar no texto (Ctrl+F)|Cari di teks (Ctrl+F)
Не найдено|Not found|未找到|नहीं मिला|No encontrado|غير موجود|Introuvable|পাওয়া যায়নি|Não encontrado|Tidak ditemukan
Файл не является корректным JSON, показан исходный текст (ошибка в строке {0}).|The file is not valid JSON; the original text is shown (error on line {0}).|文件不是有效的 JSON，显示原始文本（第 {0} 行出错）。|फ़ाइल मान्य JSON नहीं है; मूल टेक्स्ट दिखाया गया है (पंक्ति {0} में त्रुटि)।|El archivo no es un JSON válido; se muestra el texto original (error en la línea {0}).|الملف ليس JSON صالحًا؛ يُعرض النص الأصلي (خطأ في السطر {0}).|Le fichier n’est pas un JSON valide ; le texte d’origine est affiché (erreur à la ligne {0}).|ফাইলটি বৈধ JSON নয়; মূল লেখা দেখানো হয়েছে (লাইন {0}-এ ত্রুটি)।|O arquivo não é um JSON válido; o texto original é mostrado (erro na linha {0}).|Berkas bukan JSON yang valid; teks asli ditampilkan (kesalahan di baris {0}).
Файл не является корректным JSON, показан исходный текст.|The file is not valid JSON; the original text is shown.|文件不是有效的 JSON，显示原始文本。|फ़ाइल मान्य JSON नहीं है; मूल टेक्स्ट दिखाया गया है।|El archivo no es un JSON válido; se muestra el texto original.|الملف ليس JSON صالحًا؛ يُعرض النص الأصلي.|Le fichier n’est pas un JSON valide ; le texte d’origine est affiché.|ফাইলটি বৈধ JSON নয়; মূল লেখা দেখানো হয়েছে।|O arquivo não é um JSON válido; o texto original é mostrado.|Berkas bukan JSON yang valid; teks asli ditampilkan.
Файл не в кодировке UTF-8: нечитаемые символы заменены на «�».|The file is not UTF-8: unreadable characters are replaced with “�”.|文件不是 UTF-8 编码：无法读取的字符已替换为“�”。|फ़ाइल UTF-8 में नहीं है: न पढ़े जा सकने वाले वर्ण “�” से बदले गए हैं।|El archivo no está en UTF-8: los caracteres ilegibles se sustituyen por «�».|الملف ليس بترميز UTF-8: استُبدلت الأحرف غير المقروءة بـ «�».|Le fichier n’est pas en UTF-8 : les caractères illisibles sont remplacés par « � ».|ফাইলটি UTF-8 নয়: অপাঠ্য অক্ষর “�” দিয়ে বদলানো হয়েছে।|O arquivo não está em UTF-8: caracteres ilegíveis foram substituídos por “�”.|Berkas bukan UTF-8: karakter yang tak terbaca diganti dengan “�”.
Вложение больше 20 МБ ({0}). Просмотр может открываться медленно. Открыть?|The attachment is larger than 20 MB ({0}). The preview may open slowly. Open it?|附件大于 20 MB（{0}）。预览可能打开较慢。要打开吗？|अनुलग्नक 20 MB से बड़ा है ({0})। पूर्वावलोकन धीरे खुल सकता है। खोलें?|El adjunto supera 20 MB ({0}). La vista previa puede tardar en abrirse. ¿Abrirla?|المرفق أكبر من 20 م.ب ({0}). قد تُفتح المعاينة ببطء. هل تريد فتحها؟|La pièce jointe dépasse 20 Mo ({0}). L’aperçu peut s’ouvrir lentement. L’ouvrir ?|সংযুক্তিটি 20 MB-এর বেশি ({0})। প্রিভিউ ধীরে খুলতে পারে। খুলবেন?|O anexo tem mais de 20 MB ({0}). A visualização pode abrir devagar. Abrir?|Lampiran lebih dari 20 MB ({0}). Pratinjau mungkin terbuka lambat. Buka?
Текст скопирован|Text copied|文本已复制|टेक्स्ट कॉपी हो गया|Texto copiado|تم نسخ النص|Texte copié|লেখা কপি হয়েছে|Texto copiado|Teks disalin
""";
}

public sealed class ItemTitleConverter : IValueConverter
{
    internal Func<string, string> Translate { get; set; } = static text => text;

    /// <summary>Without a parameter: the title. "letter": first letter for the square badge. "sub": second line.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not VaultItem item) return "";
        string title = string.IsNullOrWhiteSpace(item.Title) ? Translate(item.DisplayTitle) : item.Title;
        return (parameter as string) switch
        {
            "letter" => StringInfo.GetNextTextElement(title.TrimStart()).ToUpper(culture),
            "sub" => item.Kind == ItemKind.Text ? Translate("Текстовый файл") : item.Login,
            _ => title
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
