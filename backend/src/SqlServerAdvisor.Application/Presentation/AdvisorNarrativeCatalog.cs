namespace SqlServerAdvisor.Application.Presentation;

public sealed record RuleNarrative(
    string RuleName,
    string CategoryLabel,
    string WhatWasFound,
    string WhyItMatters,
    string NextCheck,
    string Icon);

public static class AdvisorNarrativeCatalog
{
    public static RuleNarrative For(string ruleId) => ruleId switch
    {
        "CPU-001" => new(
            "Yüksek SQL CPU",
            "Sunucu Sağlığı",
            "SQL Server işlemci kullanımı belirlenen güvenli aralığın üzerine çıktı.",
            "Sürekli yüksek CPU sorgu gecikmesini artırır, concurrency kapasitesini düşürür ve diğer darboğazları büyütebilir.",
            "Aynı zaman aralığındaki en pahalı sorguları CPU, logical read ve execution plan ile birlikte inceleyin.",
            "memory"),

        "MEM-001" => new(
            "Düşük Kullanılabilir Bellek",
            "Sunucu Sağlığı",
            "İşletim sisteminde SQL Server ve diğer servisler için kalan fiziksel bellek düşük seviyeye indi.",
            "Bellek baskısı plan cache churn, paging, memory grant beklemeleri ve genel sorgu yavaşlamasına dönüşebilir.",
            "max server memory, OS rezervi, process memory ve RESOURCE_SEMAPHORE wait'lerini birlikte kontrol edin.",
            "memory"),

        "BLK-001" => new(
            "Blocking Baskısı",
            "Eşzamanlılık",
            "Sunucuda birden fazla isteğin başka oturumlar tarafından bekletildiği tespit edildi.",
            "Blocking uzadıkça uygulama yanıt süreleri büyür ve transaction zinciri daha fazla oturumu etkileyebilir.",
            "Head blocker oturumunu, açık transaction süresini, wait resource'u ve ilgili sorgu planını inceleyin.",
            "lock_clock"),

        "BLK-002" => new(
            "Head Blocker",
            "Eşzamanlılık",
            "Belirli bir blocker session aktif istekleri bekletiyor.",
            "Tek bir uzun transaction veya verimsiz erişim yolu çok sayıda isteği seri şekilde durdurabilir.",
            "Blocker session'ın SQL metnini, transaction yaşını, eriştiği nesneleri ve indeks yolunu doğrulayın.",
            "lock"),

        "WAIT-001" => new(
            "Kaynak Wait Baskısı",
            "Wait Analizi",
            "Son örnekleme aralığında normal arka plan wait'lerinden farklı, anlamlı bir kaynak beklemesi yükseldi.",
            "Wait türü doğrudan kök neden değildir ancak CPU, disk, lock veya memory darboğazının güçlü bir semptomudur.",
            "Wait türünü aynı zaman aralığındaki sorgu, blocking, I/O ve bellek verileriyle korele edin.",
            "hourglass_top"),

        "QRY-001" => new(
            "Pahalı Sorgu",
            "Sorgu Performansı",
            "Plan cache ölçümlerinde yüksek CPU, süre veya logical read tüketen bir sorgu tespit edildi.",
            "Tek bir pahalı sorgu yüksek sıklıkta çalışıyorsa toplam sunucu yükünün önemli bölümünü oluşturabilir.",
            "Execution plan'da scan/seek, cardinality, key lookup, sort/hash ve parameter sensitivity noktalarını inceleyin.",
            "query_stats"),

        "IDX-001" => new(
            "İndeks Bakım Adayı",
            "İndeks Sağlığı",
            "Anlamlı büyüklükte ve kullanılan bir indekste yüksek fiziksel fragmentation tespit edildi.",
            "Büyük ve yoğun okunan indekslerde fragmentation daha fazla sayfa okuması ve I/O maliyeti oluşturabilir; ancak tek başına bakım kararı değildir.",
            "Page count, kullanım sıklığı ve storage latency ile birlikte REORGANIZE/REBUILD ihtiyacını doğrulayın.",
            "account_tree"),

        "IDX-002" => new(
            "Eksik İndeks Adayı",
            "İndeks Sağlığı",
            "SQL Server missing-index DMV yüksek potansiyel fayda gösteren ve mevcut indekslerce doğrudan kapsanmayan bir aday bildirdi.",
            "Doğru tasarlanmış bir indeks pahalı scan ve lookup maliyetini azaltabilir; yanlış indeks ise write ve bakım maliyetini artırır.",
            "İlgili execution plan'ı, key/include sırasını ve write yoğunluğunu doğrulayın; doğrudan CREATE INDEX çalıştırmayın.",
            "playlist_add_check"),

        "IDX-003" => new(
            "Düşük Değer / Yüksek Bakım Maliyeti",
            "İndeks Sağlığı",
            "Yeterli kullanım gözlem süresinde çok az okunan fakat yoğun şekilde güncellenen bir indeks tespit edildi.",
            "Her ek indeks INSERT/UPDATE/DELETE sırasında bakım, log ve depolama maliyeti oluşturur. Kullanılmayan indeksler gereksiz write baskısı yaratabilir.",
            "SQL restart zamanını, raporlama/ay sonu gibi seyrek workload'ları ve query plan bağımlılıklarını doğrulamadan DROP INDEX uygulamayın.",
            "delete_sweep"),

        "IDX-004" => new(
            "Örtüşen İndeks Adayı",
            "İndeks Sağlığı",
            "Aynı tabloda aynı key yapısını paylaşan ve başka bir indeks tarafından kapsanabilecek bir indeks tespit edildi.",
            "Örtüşen indeksler benzer okuma yollarını sağlarken write, log, buffer pool ve bakım maliyetini çoğaltabilir.",
            "İki indeksin query plan kullanımını, INCLUDE farklarını, filtrelerini ve boyutlarını karşılaştırıp konsolidasyon ihtiyacını değerlendirin.",
            "difference"),

        "STATS-001" => new(
            "Statistics Freshness Riski",
            "Optimizer",
            "Statistics üzerinde yüksek değişiklik oranı ve/veya uzun güncelleme yaşı tespit edildi.",
            "Eski statistics cardinality tahminlerini bozarak yanlış join, scan veya memory grant kararlarına yol açabilir.",
            "İlgili sorgu planındaki estimated/actual row farklarını ve statistics sample bilgisini doğrulayın.",
            "analytics"),

        "STATS-002" => new(
            "NORECOMPUTE Statistics Riski",
            "Optimizer",
            "NORECOMPUTE durumundaki bir statistics üzerinde anlamlı veri değişimi birikmiş.",
            "Auto-update engellendiğinde distribution değişse bile optimizer eski histogramı kullanmaya devam edebilir.",
            "Bu ayarın bilinçli olup olmadığını, estimated/actual row farklarını ve ilgili workload'u doğrulayın.",
            "pause_circle"),

        "STATS-003" => new(
            "Statistics Sample İnceleme Adayı",
            "Optimizer",
            "Büyük bir tabloda düşük sample oranı ile birlikte anlamlı veri değişimi tespit edildi.",
            "Düşük sample tek başına hata değildir ancak skewed dağılımlarda histogram kalitesini ve cardinality tahminlerini etkileyebilir.",
            "Histogram/plan tahminlerini doğrulayın; gerekli görülürse bakım penceresinde uygun SAMPLE oranını test edin, körlemesine FULLSCAN uygulamayın.",
            "science"),

        "DLK-001" => new(
            "Deadlock Deseni",
            "Eşzamanlılık",
            "SQL Server system_health verisinde bir deadlock olayı/deseni tespit edildi.",
            "Deadlock bir transaction'ın SQL Server tarafından kurban seçilerek geri alınması anlamına gelir ve uygulamada hata/yeniden deneme oluşturur.",
            "Deadlock graph üzerinde process sırasını, kaynakları, erişim sırasını ve indeks yollarını karşılaştırın.",
            "sync_problem"),

        _ => new(
            ruleId,
            "Analiz",
            "Advisor kuralı mevcut telemetride incelenmesi gereken bir durum tespit etti.",
            "Bu sinyal performans veya kararlılık üzerinde etkili olabilir ve diğer metriklerle birlikte değerlendirilmelidir.",
            "Teknik kanıtı ve aynı zaman aralığındaki ilgili telemetry verilerini inceleyin.",
            "rule")
    };

    public static string BuildScope(string serverName, string? databaseName, string? objectName)
    {
        var parts = new List<string> { serverName };
        if (!string.IsNullOrWhiteSpace(databaseName)) parts.Add(databaseName);
        if (!string.IsNullOrWhiteSpace(objectName)) parts.Add(objectName);
        return string.Join(" › ", parts);
    }
}
