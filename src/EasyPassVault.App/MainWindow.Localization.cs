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

    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker.SelectedIndex < 0) return;
        language = LanguagePicker.SelectedIndex;
        ((ItemTitleConverter)Resources["ItemTitleConverter"]).Translate = T;
        for (int i = 0; i < Labels.Length; i++)
            Resources[$"Ui.{i}"] = Labels[i][language].Replace("\\n", "\n", StringComparison.Ordinal);

        UpdateVaultHeader();
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
Закрыть файл хранилища|Close vault file|关闭密码库文件|वॉल्ट फ़ाइल बंद करें|Cerrar archivo de bóveda|إغلاق ملف الخزنة|Fermer le fichier du coffre|ভল্ট ফাইল বন্ধ করুন|Fechar arquivo do cofre|Tutup berkas brankas
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
НАЗВАНИЕ|TITLE|标题|शीर्षक|TÍTULO|العنوان|TITRE|শিরোনাম|TÍTULO|JUDUL
ПАПКА|FOLDER|文件夹|फ़ोल्डर|CARPETA|المجلد|DOSSIER|ফোল্ডার|PASTA|FOLDER
ЛОГИН|USERNAME|用户名|उपयोगकर्ता नाम|USUARIO|اسم المستخدم|IDENTIFIANT|ব্যবহারকারীর নাম|USUÁRIO|NAMA PENGGUNA
ПАРОЛЬ|PASSWORD|密码|पासवर्ड|CONTRASEÑA|كلمة المرور|MOT DE PASSE|পাসওয়ার্ড|SENHA|KATA SANDI
Показать|Show|显示|दिखाएँ|Mostrar|إظهار|Afficher|দেখান|Mostrar|Tampilkan
Скрыть|Hide|隐藏|छिपाएँ|Ocultar|إخفاء|Masquer|লুকান|Ocultar|Sembunyikan
САЙТ|WEBSITE|网站|वेबसाइट|SITIO WEB|الموقع|SITE WEB|ওয়েবসাইট|SITE|SITUS WEB
ТЕКСТ|TEXT|文本|पाठ|TEXTO|النص|TEXTE|লেখা|TEXTO|TEKS
Удалить элемент|Delete item|删除项目|आइटम हटाएँ|Eliminar elemento|حذف العنصر|Supprimer l’élément|আইটেম মুছুন|Excluir item|Hapus item
Последнее сохранение: {0}|Last saved: {0}|上次保存：{0}|अंतिम सहेजना: {0}|Último guardado: {0}|آخر حفظ: {0}|Dernier enregistrement : {0}|শেষ সংরক্ষণ: {0}|Último salvamento: {0}|Terakhir disimpan: {0}
Дата сохранения неизвестна|Save date unknown|保存日期未知|सहेजने की तारीख अज्ञात है|Fecha de guardado desconocida|تاريخ الحفظ غير معروف|Date d’enregistrement inconnue|সংরক্ষণের তারিখ অজানা|Data de salvamento desconhecida|Tanggal penyimpanan tidak diketahui
Версия сохранена|Version saved|版本已保存|संस्करण सहेजा गया|Versión guardada|تم حفظ النسخة|Version enregistrée|সংস্করণ সংরক্ষিত|Versão salva|Versi tersimpan
Новая запись|New entry|新条目|नई प्रविष्टि|Nueva entrada|سجل جديد|Nouvelle entrée|নতুন এন্ট্রি|Nova entrada|Entri baru
Новый файл|New file|新文件|नई फ़ाइल|Nuevo archivo|ملف جديد|Nouveau fichier|নতুন ফাইল|Novo arquivo|Berkas baru
Корень хранилища|Vault root|密码库根目录|वॉल्ट का मूल फ़ोल्डर|Raíz de la bóveda|جذر الخزنة|Racine du coffre|ভল্টের মূল ফোল্ডার|Raiz do cofre|Folder akar brankas
ЗАМЕТКА|NOTE|备注|नोट|NOTA|ملاحظة|NOTE|নোট|NOTA|CATATAN
СОДЕРЖИМОЕ ФАЙЛА|FILE CONTENTS|文件内容|फ़ाइल की सामग्री|CONTENIDO DEL ARCHIVO|محتوى الملف|CONTENU DU FICHIER|ফাইলের বিষয়বস্তু|CONTEÚDO DO ARQUIVO|ISI BERKAS
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
1. Откройте или создайте хранилище.\n2. Добавьте записи или файлы.\n3. Нажмите «Сохранить и зашифровать» или Ctrl+S, чтобы сохранить все изменения.|1. Open or create a vault.\n2. Add entries or files.\n3. Use Save and encrypt or Ctrl+S to save all changes.|1. 打开或创建密码库。\n2. 添加条目或文件。\n3. 点击“保存并加密”或按 Ctrl+S 保存所有更改。|1. वॉल्ट खोलें या बनाएँ।\n2. प्रविष्टियाँ या फ़ाइलें जोड़ें।\n3. सभी बदलाव सहेजने के लिए सहेजें और एन्क्रिप्ट करें या Ctrl+S दबाएँ।|1. Abre o crea una bóveda.\n2. Añade entradas o archivos.\n3. Pulsa Guardar y cifrar o Ctrl+S para guardar todos los cambios.|١. افتح خزنة أو أنشئها.\n٢. أضف سجلات أو ملفات.\n٣. اختر حفظ وتشفير أو اضغط Ctrl+S لحفظ كل التغييرات.|1. Ouvrez ou créez un coffre.\n2. Ajoutez des entrées ou fichiers.\n3. Utilisez Enregistrer et chiffrer ou Ctrl+S pour tout enregistrer.|১. ভল্ট খুলুন বা তৈরি করুন।\n২. এন্ট্রি বা ফাইল যোগ করুন।\n৩. সব পরিবর্তন সংরক্ষণ করতে সংরক্ষণ ও এনক্রিপ্ট করুন বা Ctrl+S চাপুন।|1. Abra ou crie um cofre.\n2. Adicione entradas ou arquivos.\n3. Use Salvar e criptografar ou Ctrl+S para salvar tudo.|1. Buka atau buat brankas.\n2. Tambahkan entri atau berkas.\n3. Pilih Simpan dan enkripsi atau tekan Ctrl+S untuk menyimpan semua perubahan.
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
""";
}

public sealed class ItemTitleConverter : IValueConverter
{
    internal Func<string, string> Translate { get; set; } = static text => text;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is VaultItem item
            ? string.IsNullOrWhiteSpace(item.Title) ? Translate(item.DisplayTitle) : item.Title
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
