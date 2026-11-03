using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HWWidget;

/// <summary>
/// Lightweight UI catalog. Italian remains the source; long descriptions fall back to
/// English, while the main labels/menus are translated for the requested locales.
/// </summary>
static class Loc
{
    public sealed record Language(string Code, string Name);

    public static readonly Language[] Languages =
    {
        new("it-IT", "Italiano"),
        new("en-GB", "English"),
        new("en-US", "English (US)"),
        new("ru-RU", "Русский"),
        new("zh-CN", "中文（简体）"),
        new("ko-KR", "한국어"),
        new("ja-JP", "日本語"),
        new("hi-IN", "हिन्दी"),
        new("de-DE", "Deutsch"),
        new("es-ES", "Español"),
        new("fr-FR", "Français"),
        new("el-GR", "Ελληνικά"),
        new("pt-PT", "Português"),
        new("ar-SA", "العربية"),
    };

    static readonly Dictionary<string, Dictionary<string, string>> Catalog = new();
    static readonly Dictionary<string, string> Reverse = new(StringComparer.Ordinal);
    static readonly string FilePath = Path.Combine(WidgetConfig.Dir, "language.txt");

    public static string Current { get; private set; } = Load();

    public static bool IsRtl => Current is "ar-SA";

    static Loc()
    {
        foreach (var line in Core.Trim().Split('\n'))
        {
            var c = line.Trim().Split('|');
            if (c.Length != 14) continue;
            Add(c[1], "en-GB"); Add(c[2], "it-IT"); Add(c[3], "ru-RU");
            Add(c[4], "zh-CN"); Add(c[5], "ko-KR"); Add(c[6], "ja-JP");
            Add(c[7], "hi-IN"); Add(c[8], "de-DE"); Add(c[9], "es-ES");
            Add(c[10], "fr-FR"); Add(c[11], "el-GR"); Add(c[12], "pt-PT");
            Add(c[13], "ar-SA");

            void Add(string value, string lang)
            {
                if (!Catalog.TryGetValue(c[0], out var map))
                    Catalog[c[0]] = map = new Dictionary<string, string>();
                map[lang] = value;
                Reverse.TryAdd(value, c[0]);
            }
        }
        foreach (var line in EnglishOnly.Trim().Split('\n'))
        {
            var i = line.IndexOf('|');
            if (i <= 0) continue;
            string key = line[..i].Trim(), value = line[(i + 1)..].Trim();
            Catalog[key] = new Dictionary<string, string> { ["en-GB"] = value };
            Reverse.TryAdd(key, key);
            Reverse.TryAdd(value, key);
        }
    }

    static string Load()
    {
        try
        {
            string code = File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : "";
            return Languages.Any(l => l.Code == code) ? code : "it-IT";
        }
        catch { return "it-IT"; }
    }

    public static void Set(string code)
    {
        if (!Languages.Any(l => l.Code == code)) code = "it-IT";
        Current = code;
        try
        {
            Directory.CreateDirectory(WidgetConfig.Dir);
            File.WriteAllText(FilePath, code);
        }
        catch { }
    }

    public static string T(string source) => For(Current, source);

    internal static string For(string language, string source)
    {
        if (string.IsNullOrEmpty(source) || !Reverse.TryGetValue(source, out var key)) return source;
        if (language == "en-US") language = "en-GB";
        if (Catalog[key].TryGetValue(language, out var value) && value.Length > 0) return value;
        if (language != "it-IT" && Catalog[key].TryGetValue("en-GB", out var en) && en.Length > 0) return en;
        return source;
    }

    /// <summary>Translates already-built WPF content, including menus and popup contents.</summary>
    public static void Apply(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        Walk(root, seen, 0);
    }

    static void Walk(DependencyObject d, HashSet<DependencyObject> seen, int depth)
    {
        if (depth > 128) return;
        if (!seen.Add(d)) return;
        if (d is Window w)
        {
            string title = T(w.Title);
            if (title != w.Title) w.Title = title;
        }
        if (d is TextBlock tb)
        {
            string text = T(tb.Text);
            if (text != tb.Text) tb.Text = text;
        }
        if (d is ContentControl cc && cc.Content is string s)
        {
            string text = T(s);
            if (text != s) cc.Content = text;
        }
        if (d is HeaderedContentControl hc && hc.Header is string h)
        {
            string text = T(h);
            if (text != h) hc.Header = text;
        }
        if (d is FrameworkElement fe && fe.ToolTip is string tip)
        {
            string text = T(tip);
            if (text != tip) fe.ToolTip = text;
        }
        if (d is FrameworkElement rtl)
        {
            var direction = IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            if (rtl.FlowDirection != direction) rtl.FlowDirection = direction;
        }
        // ComboBox/ItemControl templates are deep and can materialize recursively; their
        // item labels are already localized when the combo is built.
        if (d is ComboBox or Popup) return;

        foreach (var child in LogicalTreeHelper.GetChildren(d))
            if (child is DependencyObject o) Walk(o, seen, depth + 1);
        if (d is Visual)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                Walk(VisualTreeHelper.GetChild(d, i), seen, depth + 1);
    }

    // key|en|it|ru|zh|ko|ja|hi|de|es|fr|el|pt|ar
    const string Core = """
    HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget|HW Widget
    Hub di controllo|Control Hub|Hub di controllo|Центр управления|控制中心|제어 센터|コントロール ハブ|नियंत्रण केंद्र|Steuerungs-Hub|Centro de control|Centre de contrôle|Κέντρο ελέγχου|Centro de controlo|مركز التحكم
    Aggiungi widget|Add widget|Aggiungi widget|Добавить виджет|添加小部件|위젯 추가|ウィジェットを追加|विजेट जोड़ें|Widget hinzufügen|Añadir widget|Ajouter un widget|Προσθήκη widget|Adicionar widget|إضافة أداة
    Impostazioni app|App settings|Impostazioni app|Настройки приложения|应用设置|앱 설정|アプリ設定|ऐप सेटिंग्स|App-Einstellungen|Configuración de la aplicación|Paramètres de l'application|Ρυθμίσεις εφαρμογής|Definições da aplicação|إعدادات التطبيق
    Nuovi pannelli|New panels|Nuovi pannelli|Новые панели|新建面板|새 패널|新しいパネル|नए पैनल|Neue Panels|Nuevos paneles|Nouveaux panneaux|Νέα πάνελ|Novos painéis|لوحات جديدة
    WIDGET|WIDGET|WIDGET|ВИДЖЕТЫ|小组件|위젯|ウィジェット|विजेट|WIDGETS|WIDGETS|WIDGETS|WIDGET|WIDGETS|الأدوات
    APPLICAZIONE|APPLICATION|APPLICAZIONE|ПРИЛОЖЕНИЕ|应用程序|앱|アプリ|ऐप|ANWENDUNG|APLICACIÓN|APPLICATION|ΕΦΑΡΜΟΓΗ|APLICAÇÃO|التطبيق
    TUTTO|ALL|TUTTO|ВСЁ|全部|전체|すべて|सभी|ALLES|TODO|TOUT|ΟΛΑ|TUDO|الكل
    RETE|NETWORK|RETE|СЕТЬ|网络|네트워크|ネットワーク|नेटवर्क|NETZWERK|RED|RÉSEAU|ΔΙΚΤΥΟ|REDE|الشبكة
    NET|NETWORK|NET|СЕТЬ|网络|네트워크|ネットワーク|नेटवर्क|NETZWERK|RED|RÉSEAU|ΔΙΚΤΥΟ|REDE|الشبكة
    DISCO|DISK|DISCO|ДИСК|磁盘|디스크|ディスク|डिस्क|DATENTRÄGER|DISCO|DISQUE|ΔΙΣΚΟΣ|DISCO|القرص
    Saldo|Balance|Saldo|Баланс|余额|잔액|残高|शेष|Guthaben|Saldo|Solde|Υπόλοιπο|Saldo|الرصيد
    disponibile|available|disponibile|доступно|可用|사용 가능|利用可能|उपलब्ध|verfügbar|disponible|disponible|διαθέσιμο|disponível|متاح
    non disponibile|unavailable|non disponibile|недоступно|不可用|사용 불가|利用不可|अनुपलब्ध|nicht verfügbar|no disponible|indisponible|μη διαθέσιμο|indisponível|غير متاح
    Oggi|Today|Oggi|Сегодня|今天|오늘|今日|आज|Heute|Hoy|Aujourd'hui|Σήμερα|Hoje|اليوم
    Mese|Month|Mese|Месяц|本月|이번 달|今月|महीना|Monat|Mes|Mois|Μήνας|Mês|الشهر
    Consumo|Usage|Consumo|Расход|用量|사용량|使用量|उपयोग|Nutzung|Consumo|Utilisation|Χρήση|Utilização|الاستخدام
    Uso|Usage|Uso|Использование|使用率|사용률|使用率|उपयोग|Nutzung|Uso|Utilisation|Χρήση|Utilização|الاستخدام
    Temp|Temp|Temp|Темп.|温度|온도|温度|तापमान|Temp.|Temp.|Temp.|Θερμ.|Temp.|الحرارة
    VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM|VRAM
    Lettura|Read|Lettura|Чтение|读取|읽기|読み取り|पढ़ना|Lesen|Lectura|Lecture|Ανάγνωση|Leitura|قراءة
    Scrittura|Write|Scrittura|Запись|写入|쓰기|書き込み|लिखना|Schreiben|Escritura|Écriture|Εγγραφή|Escrita|كتابة
    Upload|Upload|Upload|Загрузка|上传|업로드|アップロード|अपलोड|Upload|Subida|Envoi|Μεταφόρτωση|Upload|رفع
    Download|Download|Download|Скачивание|下载|다운로드|ダウンロード|डाउनलोड|Download|Descarga|Téléchargement|Λήψη|Download|تنزيل
    Lingua|Language|Lingua|Язык|语言|언어|言語|भाषा|Sprache|Idioma|Langue|Γλώσσα|Idioma|اللغة
    Dimensioni|Size|Dimensioni|Размер|尺寸|크기|サイズ|आकार|Größe|Tamaño|Taille|Μέγεθος|Tamanho|الحجم
    Grafici|Graphs|Grafici|Графики|图表|그래프|グラフ|ग्राफ़|Diagramme|Gráficos|Graphiques|Γραφήματα|Gráficos|الرسوم البيانية
    Comportamento|Behavior|Comportamento|Поведение|行为|동작|動作|व्यवहार|Verhalten|Comportamiento|Comportement|Συμπεριφορά|Comportamento|السلوك
    Posizione|Position|Posizione|Позиция|位置|위치|位置|स्थिति|Position|Posición|Position|Θέση|Posição|الموضع
    Sistema|System|Sistema|Система|系统|시스템|システム|सिस्टम|System|Sistema|Système|Σύστημα|Sistema|النظام
    Aggiornamento|Update|Aggiornamento|Обновление|更新|업데이트|更新|अपडेट|Update|Actualización|Mise à jour|Ενημέρωση|Atualização|التحديث
    Tema|Theme|Tema|Тема|主题|테마|テーマ|थीम|Design|Tema|Thème|Θέμα|Tema|المظهر
    Layout|Layout|Layout|Макет|布局|레이아웃|レイアウト|लेआउट|Layout|Diseño|Disposition|Διάταξη|Esquema|التخطيط
    Materiale|Material|Materiale|Материал|材质|소재|素材|सामग्री|Material|Material|Matériau|Υλικό|Material|المادة
    Chiudi|Close|Chiudi|Закрыть|关闭|닫기|閉じる|बंद करें|Schließen|Cerrar|Fermer|Κλείσιμο|Fechar|إغلاق
    Salva|Save|Salva|Сохранить|保存|저장|保存|सहेजें|Speichern|Guardar|Enregistrer|Αποθήκευση|Guardar|حفظ
    Annulla|Cancel|Annulla|Отмена|取消|취소|キャンセル|रद्द करें|Abbrechen|Cancelar|Annuler|Άκυρο|Cancelar|إلغاء
    Crea|Create|Crea|Создать|创建|만들기|作成|बनाएं|Erstellen|Crear|Créer|Δημιουργία|Criar|إنشاء
    Sempre in primo piano|Always on top|Sempre in primo piano|Поверх всех окон|总在最前|항상 위|常に最前面|हमेशा शीर्ष पर|Immer im Vordergrund|Siempre visible|Toujours au premier plan|Πάντα στην κορυφή|Sempre no topo|دائمًا في المقدمة
    Tieni sul desktop|Keep on desktop|Tieni sul desktop|Оставить на рабочем столе|保持在桌面|바탕 화면에 유지|デスクトップに保持|डेस्कटॉप पर रखें|Auf dem Desktop halten|Mantener en el escritorio|Garder sur le bureau|Διατήρηση στην επιφάνεια εργασίας|Manter no ambiente de trabalho|الإبقاء على سطح المكتب
    Blocca posizione|Lock position|Blocca posizione|Заблокировать позицию|锁定位置|위치 잠금|位置を固定|स्थिति लॉक करें|Position sperren|Bloquear posición|Verrouiller la position|Κλείδωμα θέσης|Bloquear posição|قفل الموضع
    Clic attraverso|Click-through|Clic attraverso|Пропускать клики|点击穿透|클릭 통과|クリックを透過|क्लिक-थ्रू|Klicks durchlassen|Clic transparente|Clic à travers|Διέλευση κλικ|Clique através|تمرير النقرات
    Nuovo widget|New widget|Nuovo widget|Новый виджет|新建小部件|새 위젯|新しいウィジェット|नया विजेट|Neues Widget|Nuevo widget|Nouveau widget|Νέο widget|Novo widget|أداة جديدة
    Esci|Exit|Esci|Выход|退出|종료|終了|बाहर निकलें|Beenden|Salir|Quitter|Έξοδος|Sair|خروج
    Riavvia|Restart|Riavvia|Перезапустить|重启|다시 시작|再起動|पुनः आरंभ करें|Neu starten|Reiniciar|Redémarrer|Επανεκκίνηση|Reiniciar|إعادة التشغيل
    Dettagli|Details|Dettagli|Детали|详情|세부 정보|詳細|विवरण|Details|Detalles|Détails|Λεπτομέρειες|Detalhes|التفاصيل
    Core|Core|Core|Ядро|核心|코어|コア|कोर|Kern|Núcleo|Cœur|Πυρήνας|Núcleo|نواة
    Frequenza|Frequency|Frequenza|Частота|频率|주파수|周波数|आवृत्ति|Frequenz|Frecuencia|Fréquence|Συχνότητα|Frequência|التردد
    Memoria|Memory|Memoria|Память|内存|메모리|メモリ|मेमोरी|Speicher|Memoria|Mémoire|Μνήμη|Memória|الذاكرة
    Dischi|Disks|Dischi|Диски|磁盘|디스크|ディスク|डिस्क|Datenträger|Discos|Disques|Δίσκοι|Discos|الأقراص
    Adattatore|Adapter|Adattatore|Адаптер|适配器|어댑터|アダプター|एडाप्टर|Adapter|Adaptador|Adaptateur|Προσαρμογέας|Adaptador|المحول
    Link|Link|Link|Канал|链路|링크|リンク|लिंक|Link|Enlace|Liaison|Ζεύξη|Ligação|الرابط
    Contenuto|Content|Contenuto|Содержимое|内容|콘텐츠|内容|सामग्री|Inhalt|Contenido|Contenu|Περιεχόμενο|Conteúdo|المحتوى
    Aspetto|Appearance|Aspetto|Внешний вид|外观|모양|外観|रूप|Darstellung|Apariencia|Apparence|Εμφάνιση|Aparência|المظهر
    Colori dei misuratori|Meter colors|Colori dei misuratori|Цвета индикаторов|仪表颜色|측정기 색상|メーターの色|मीटर रंग|Messfarbe|Colores de medidores|Couleurs des jauges|Χρώματα μετρητών|Cores dos medidores|ألوان المؤشرات
    Totale|Total|Totale|Всего|总计|합계|合計|कुल|Gesamt|Total|Total|Σύνολο|Total|الإجمالي
    Engine|Engine|Engine|Движок|引擎|엔진|エンジン|इंजन|Engine|Motor|Moteur|Μηχανή|Motor|المحرك
    """;

    // English fallback for longer help text and labels outside the core widget UI.
    const string EnglishOnly = """
    Crea un nuovo pannello: puoi averne quanti vuoi, ognuno con posizione, dimensioni e contenuto propri.|Create a new panel: add as many as you want, each with its own position, size and content.
    Scegli il widget da creare|Choose the widget to create
    CPU, GPU, RAM, disco e rete in un unico pannello.|CPU, GPU, RAM, disk and network in one panel.
    Utilizzo e frequenza della CPU.|CPU usage and frequency.
    Utilizzo, temperatura e VRAM della scheda video.|GPU usage, temperature and VRAM.
    Memoria utilizzata e frequenza.|Used memory and frequency.
    Upload e download dell'adattatore attivo.|Upload and download for the active adapter.
    Due schede con i dati principali.|Two cards with the main metrics.
    Budget rimasto, budget consumato e token totali di DeepSeek, ChatGPT e Claude (solo numeri).|Remaining budget, spent budget and total tokens for DeepSeek, ChatGPT and Claude (numbers only).
    Saldo, spesa di oggi e del mese, token e cache hit per modello, con grafico giornaliero.|Balance, today/month spend, tokens and cache hits per model, with a daily chart.
    Comportamento generale di HW Widget su questo PC.|General HW Widget behavior on this PC.
    Resta sopra le altre finestre.|Stays above other windows.
    Resta visibile anche quando premi Win+D.|Stays visible when you press Win+D.
    Impedisce spostamento e ridimensionamento.|Prevents moving and resizing.
    Il widget non riceve più i clic (Ctrl+Alt+H per uscire).|The widget no longer receives clicks (Ctrl+Alt+H to exit).
    Soluzione se compare l'overlay FPS di NVIDIA.|Use this if the NVIDIA FPS overlay appears.
    Su quale schermo posizionarlo.|Which screen to place it on.
    Lingua dell'interfaccia.|Interface language.
    Token di utilizzo|Usage token
    Pannello + grafici|Panel + graphs
    AI e chiavi API|AI and API keys
    Questa sezione|This section
    Chiavi|Keys
    Aggiornamenti|Updates
    Avvio e accesso|Startup and access
    Manutenzione|Maintenance
    Informazioni|Information
    Controlla aggiornamenti|Check for updates
    Controlla all'avvio|Check at startup
    Salva chiavi|Save keys
    Rileggi ora|Refresh now
    Verifica token DeepSeek|Verify DeepSeek token
    Attivo|Active
    Chiudi tutti|Close all
    Cartella impostazioni|Settings folder
    Azioni|Actions
    Composizione|Composition
    Porta in primo piano|Bring to front
    Adatta l'altezza al contenuto|Fit height to content
    Nome del widget|Widget name
    Scala generale|Global scale
    Dimensione testo|Text size
    Altezza elementi|Row height
    Larghezza finestra|Window width
    Altezza finestra|Window height
    Opacità pannello|Panel opacity
    Stile|Style
    Durata|Duration
    Larghezza grafici|Graph width
    Altezza grafici|Graph height
    Elementi visibili|Visible elements
    Sezione AI|AI section
    Ordine degli elementi|Element order
    Righe compatte|Compact rows
    Card per elemento|Card per element
    Tessere|Tiles
    Pannello (barre)|Panel (bars)
    Area|Area
    Linea|Line
    Barre|Bars
    Scalini|Steps
    Come Windows|Follow Windows
    Scuro|Dark
    Chiaro|Light
    Acrylic sfocato|Blurred acrylic
    Pannello pieno|Solid panel
    Blu|Blue
    Verde|Green
    Ambra|Amber
    Rosso|Red
    Viola|Purple
    Turchese|Turquoise
    Rosa|Pink
    Grigio|Grey
    Avvio, tray, uscita|Startup, tray, exit
    Mostra / nascondi widget|Show / hide widgets
    Clic attraverso (HUD)|Click-through (HUD)
    HUD: clic attraverso (Ctrl+Alt+H)|HUD: click-through (Ctrl+Alt+H)
    Avvia con Windows|Start with Windows
    Hub di controllo…|Control Hub…
    Chiudi questo widget|Close this widget
    Esci da HW Widget|Exit HW Widget
    Widget chiusi|Closed widgets
    Riapri|Reopen
    Configurazione salvata: si riapre con posizione, dimensioni e layout originali.|Saved configuration: reopens with the original position, size and layout.
    La configurazione resta salvata e si può riaprire da Impostazioni app.|The configuration is kept and can be reopened from App settings.
    """;
}
