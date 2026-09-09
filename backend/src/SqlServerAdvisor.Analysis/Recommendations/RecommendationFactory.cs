using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Analysis.Recommendations;

public sealed class RecommendationFactory : IRecommendationFactory
{
    public Recommendation? Create(Finding finding)
    {
        var recommendation = finding.RuleId switch
        {
            "BLK-001" => CreateBlockingRecommendation(finding),
            "BLK-002" => CreateBlockingRecommendation(finding),
            "CPU-001" => CreateCpuRecommendation(finding),
            "MEM-001" => CreateMemoryRecommendation(finding),
            "WAIT-001" => CreateWaitRecommendation(finding),
            "QRY-001" => CreateQueryRecommendation(finding),
            "IDX-001" => CreateFragmentationRecommendation(finding),
            "IDX-002" => CreateMissingIndexRecommendation(finding),
            "IDX-003" => CreateLowValueIndexRecommendation(finding),
            "IDX-004" => CreateOverlapIndexRecommendation(finding),
            "STATS-001" => CreateStatisticsRecommendation(finding),
            "STATS-002" => CreateNoRecomputeStatisticsRecommendation(finding),
            "STATS-003" => CreateSamplingStatisticsRecommendation(finding),
            "DLK-001" => CreateDeadlockRecommendation(finding),
            _ => null
        };

        if (recommendation is not null)
        {
            recommendation.FindingId = finding.Id;
            recommendation.CanExecute = false;
        }

        return recommendation;
    }

    private static Recommendation CreateBlockingRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Blocking zincirini ve head blocker oturumunu inceleyin",
        Explanation = "Aktif blocking uygulama gecikmesine ve transaction zincirlerinin büyümesine neden olabilir. Önce head blocker, açık transaction yaşı ve çalışan SQL belirlenmelidir.",
        ExpectedBenefit = "High",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Head blocker oturumunu, transaction süresini ve ilgili sorgu planını inceleyin. Uygulama transaction kapsamını ve indeks erişim yollarını doğrulayın. Oturum sonlandırma gibi üretim değişikliklerini SQL Server Advisor otomatik uygulamaz.",
        ScriptText = "SELECT r.session_id, r.blocking_session_id, r.status, r.wait_type, r.wait_time, r.wait_resource, DB_NAME(r.database_id) AS database_name, s.login_name, s.host_name, s.program_name, t.text AS sql_text FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t WHERE r.blocking_session_id > 0 ORDER BY r.wait_time DESC;",
        Status = "New"
    };

    private static Recommendation CreateCpuRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "En yüksek CPU tüketen sorguları inceleyin",
        Explanation = "Yüksek SQL CPU tek başına bir sorgunun hatalı olduğunu kanıtlamaz. Toplam ve ortalama CPU tüketimi yüksek sorgular belirlenip execution plan, logical read ve çalışma sıklığı ile birlikte değerlendirilmelidir.",
        ExpectedBenefit = "High",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Önce en yüksek CPU tüketen sorguları salt-okunur DMV sorgusuyla belirleyin. Ardından execution plan, indeks erişimleri, cardinality tahminleri ve paralellik davranışını inceleyin. Önerilen değişiklikleri DBA onayı olmadan uygulamayın.",
        ScriptText = "SELECT TOP (25) qs.execution_count, qs.total_worker_time / 1000.0 AS total_cpu_ms, (qs.total_worker_time / NULLIF(qs.execution_count,0)) / 1000.0 AS avg_cpu_ms, qs.total_logical_reads, DB_NAME(COALESCE(st.dbid, qp.dbid)) AS database_name, SUBSTRING(st.text, (qs.statement_start_offset/2)+1, ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset)/2)+1) AS statement_text FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st OUTER APPLY sys.dm_exec_query_plan(qs.plan_handle) qp ORDER BY qs.total_worker_time DESC;",
        Status = "New"
    };

    private static Recommendation CreateMemoryRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "SQL Server bellek sınırlarını ve OS rezervini doğrulayın",
        Explanation = "Düşük kullanılabilir fiziksel bellek, işletim sistemi veya SQL Server üzerinde bellek baskısına işaret edebilir. max server memory değeri fiziksel RAM, diğer servisler ve işletim sistemi rezervi dikkate alınarak değerlendirilmelidir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Mevcut max server memory ayarını, işletim sistemi kullanılabilir belleğini ve SQL Server process memory durumunu inceleyin. Ayar değişikliği gerekiyorsa kapasite değerlendirmesi ve DBA onayı sonrasında uygulayın.",
        ScriptText = "SELECT name, value_in_use FROM sys.configurations WHERE name IN ('max server memory (MB)','min server memory (MB)'); SELECT total_physical_memory_kb/1024 AS total_physical_memory_mb, available_physical_memory_kb/1024 AS available_physical_memory_mb, system_memory_state_desc FROM sys.dm_os_sys_memory; SELECT physical_memory_in_use_kb/1024 AS sql_physical_memory_mb, process_physical_memory_low, process_virtual_memory_low FROM sys.dm_os_process_memory;",
        Status = "New"
    };

    private static Recommendation CreateWaitRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Baskın wait türünün kaynağını korelasyonla inceleyin",
        Explanation = "Wait stats bir semptomdur; doğrudan ayar değişikliği gerekçesi değildir. Wait türü, aynı zaman aralığındaki sorgu yükü, I/O, blocking, memory grant ve CPU verileriyle birlikte değerlendirilmelidir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Önce son örnekleme aralığındaki wait artışını doğrulayın. Ardından ilgili kaynak grubuna göre sorguları, dosya I/O gecikmesini, blocking zincirlerini veya memory grant durumunu inceleyin. SQL Server Advisor üretim ayarı veya indeks değişikliğini otomatik uygulamaz.",
        ScriptText = "SELECT TOP (30) wait_type, waiting_tasks_count, wait_time_ms, signal_wait_time_ms, wait_time_ms - signal_wait_time_ms AS resource_wait_ms FROM sys.dm_os_wait_stats WHERE wait_time_ms > 0 ORDER BY wait_time_ms DESC;",
        Status = "New"
    };

    private static Recommendation CreateQueryRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Sorgunun execution plan ve veri erişim maliyetini inceleyin",
        Explanation = "Sorgu plan cache ölçümlerinde yüksek ortalama CPU, süre veya logical read tüketimi gösteriyor. Bu bir tuning adayıdır; doğrudan indeks oluşturma veya query hint uygulama gerekçesi değildir.",
        ExpectedBenefit = "High",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Execution plan üzerinde scan/seek tercihlerini, cardinality tahminlerini, key lookup maliyetini, sort/hash spill işaretlerini ve parameter sensitivity davranışını inceleyin. İndeks veya sorgu değişikliğini test ortamında doğrulayıp DBA onayıyla uygulayın.",
        ScriptText = "SELECT TOP (50) CONVERT(varchar(130), qs.query_hash, 1) AS query_hash, qs.execution_count, qs.total_worker_time/1000.0 AS total_cpu_ms, (qs.total_worker_time/NULLIF(qs.execution_count,0))/1000.0 AS avg_cpu_ms, (qs.total_elapsed_time/NULLIF(qs.execution_count,0))/1000.0 AS avg_duration_ms, qs.total_logical_reads*1.0/NULLIF(qs.execution_count,0) AS avg_logical_reads, DB_NAME(COALESCE(st.dbid, qp.dbid)) AS database_name, st.text, qp.query_plan FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st OUTER APPLY sys.dm_exec_query_plan(qs.plan_handle) qp ORDER BY qs.total_worker_time DESC;",
        Status = "New"
    };

    private static Recommendation CreateFragmentationRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "İndeks bakım ihtiyacını kullanım ve I/O ile doğrulayın",
        Explanation = "Advisor yüksek fragmentation yanında indeksin kullanımını ve SQL Server restart'tan beri gözlenen kullanım süresini de hesaba kattı. Yine de fragmentation tek başına rebuild/reorganize kararı değildir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "İlgili indeksin page count, read/write kullanımı ve storage latency değerlerini doğrulayın. Bakım gerekiyorsa REORGANIZE/REBUILD seçimini ve bakım penceresini DBA olarak planlayın.",
        ScriptText = "SELECT DB_NAME(database_id) AS database_name, object_id, index_id, avg_fragmentation_in_percent, page_count FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') WHERE index_id > 0 AND page_count >= 1000 ORDER BY page_count DESC;",
        Status = "New"
    };

    private static Recommendation CreateMissingIndexRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Eksik indeks adayını execution plan ile doğrulayın",
        Explanation = "Advisor missing-index DMV adayını mevcut indeks key/include kapsamına karşı konservatif olarak kontrol etti. Yine de DMV önerisi tek başına indeks tasarımı değildir.",
        ExpectedBenefit = "High",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "İlgili sorgu planlarında scan/lookup maliyetini doğrulayın; mevcut indeksler, key sırası, include kolonları ve write yükünü birlikte değerlendirerek konsolide bir tasarım oluşturun. Advisor otomatik CREATE INDEX çalıştırmaz.",
        ScriptText = "SELECT DB_NAME(mid.database_id) AS database_name, OBJECT_SCHEMA_NAME(mid.object_id, mid.database_id) AS schema_name, OBJECT_NAME(mid.object_id, mid.database_id) AS table_name, mid.equality_columns, mid.inequality_columns, mid.included_columns, migs.user_seeks, migs.user_scans, migs.avg_total_user_cost, migs.avg_user_impact FROM sys.dm_db_missing_index_details mid JOIN sys.dm_db_missing_index_groups mig ON mig.index_handle = mid.index_handle JOIN sys.dm_db_missing_index_group_stats migs ON migs.group_handle = mig.index_group_handle ORDER BY (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) DESC;",
        Status = "New"
    };

    private static Recommendation CreateLowValueIndexRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "İndeksin gerçek kullanım değerini doğrulayın",
        Explanation = "İndeks yeterli gözlem süresinde çok az okunurken yoğun write bakımı oluşturuyor. Bu, kaldırma veya konsolidasyon için inceleme adayıdır; doğrudan DROP INDEX kararı değildir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Medium",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "SQL Server restart zamanını, dönemsel raporları ve execution plan bağımlılıklarını kontrol edin. İndeks gerçekten gereksizse test ortamında kaldırma etkisini ölçün ve yalnız DBA onayıyla değişiklik yapın.",
        ScriptText = "SELECT i.object_id, i.index_id, i.name, i.is_unique, i.is_primary_key, ISNULL(us.user_seeks,0) AS user_seeks, ISNULL(us.user_scans,0) AS user_scans, ISNULL(us.user_lookups,0) AS user_lookups, ISNULL(us.user_updates,0) AS user_updates FROM sys.indexes i LEFT JOIN sys.dm_db_index_usage_stats us ON us.database_id=DB_ID() AND us.object_id=i.object_id AND us.index_id=i.index_id WHERE i.index_id>0 ORDER BY ISNULL(us.user_updates,0) DESC; SELECT sqlserver_start_time FROM sys.dm_os_sys_info;",
        Status = "New"
    };

    private static Recommendation CreateOverlapIndexRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Örtüşen indeksleri konsolidasyon açısından karşılaştırın",
        Explanation = "Aynı key yapısını paylaşan indeksler benzer erişim yolları sağlarken write, log, disk ve buffer pool maliyetini çoğaltabilir. INCLUDE veya filtre farkları nedeniyle her zaman redundant değildir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Medium",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "İki indeksin key/include/filter tanımlarını, boyutlarını ve query plan kullanımını karşılaştırın. Konsolidasyon mümkünse test ortamında workload etkisini doğrulayın; Advisor otomatik DROP/ALTER INDEX çalıştırmaz.",
        ScriptText = "SELECT i.object_id, i.index_id, i.name, i.has_filter, i.filter_definition, ic.key_ordinal, ic.is_included_column, c.name AS column_name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.index_id>0 ORDER BY i.object_id, i.index_id, ic.key_ordinal, ic.index_column_id;",
        Status = "New"
    };

    private static Recommendation CreateStatisticsRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Statistics güncelliğini ve sorgu planı etkisini doğrulayın",
        Explanation = "Yüksek modification oranı ve statistics yaşı optimizer cardinality tahminlerini etkileyebilir. Gerçek plan etkisi doğrulanmadan UPDATE STATISTICS çalıştırılmamalıdır.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "last_updated, rows, rows_sampled, modification_counter ve ilgili execution plan estimated/actual row farklarını birlikte inceleyin. Güncelleme gerekiyorsa uygun sample ve bakım penceresiyle DBA kontrollü uygulayın.",
        ScriptText = "SELECT OBJECT_SCHEMA_NAME(s.object_id) AS schema_name, OBJECT_NAME(s.object_id) AS table_name, s.name AS statistics_name, s.auto_created, s.user_created, s.no_recompute, s.has_filter, p.last_updated, p.rows, p.rows_sampled, p.modification_counter FROM sys.stats s OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) p ORDER BY p.modification_counter DESC;",
        Status = "New"
    };

    private static Recommendation CreateNoRecomputeStatisticsRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "NORECOMPUTE ayarının bilinçli olup olmadığını doğrulayın",
        Explanation = "Statistics NORECOMPUTE durumunda anlamlı değişiklik biriktirmiş. Bu ayar bilinçli değilse optimizer uzun süre eski histogram kullanabilir.",
        ExpectedBenefit = "High",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "İlgili statistics'in neden NORECOMPUTE olduğunu, uygulama/bakım politikalarını ve estimated/actual row farklarını inceleyin. Ayar veya statistics güncellemesini yalnız DBA kararıyla değiştirin.",
        ScriptText = "SELECT OBJECT_SCHEMA_NAME(s.object_id) AS schema_name, OBJECT_NAME(s.object_id) AS table_name, s.name, s.no_recompute, p.last_updated, p.rows, p.rows_sampled, p.modification_counter FROM sys.stats s OUTER APPLY sys.dm_db_stats_properties(s.object_id,s.stats_id) p WHERE s.no_recompute=1 ORDER BY p.modification_counter DESC;",
        Status = "New"
    };

    private static Recommendation CreateSamplingStatisticsRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Statistics sampling kalitesini execution plan ile doğrulayın",
        Explanation = "Büyük tablo statistics'inde düşük sample ile anlamlı değişim birlikte görülüyor. Düşük sample SQL Server'ın normal davranışı olabilir; doğrudan FULLSCAN gerekçesi değildir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Histogram dağılımını ve estimated/actual row sapmasını kontrol edin. Sorun doğrulanırsa test ortamında uygun SAMPLE oranını karşılaştırın; büyük tablolarda FULLSCAN maliyetini hesaba katın.",
        ScriptText = "SELECT OBJECT_SCHEMA_NAME(s.object_id) AS schema_name, OBJECT_NAME(s.object_id) AS table_name, s.name, p.last_updated, p.rows, p.rows_sampled, CAST(p.rows_sampled*100.0/NULLIF(p.rows,0) AS decimal(9,2)) AS sample_percent, p.modification_counter FROM sys.stats s OUTER APPLY sys.dm_db_stats_properties(s.object_id,s.stats_id) p WHERE p.rows>=1000000 ORDER BY sample_percent, p.modification_counter DESC;",
        Status = "New"
    };

    private static Recommendation CreateDeadlockRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Deadlock graph üzerindeki erişim sırası ve indeks yollarını inceleyin",
        Explanation = "Deadlock, eşzamanlı transaction'ların birbirinin kilitlerini döngüsel olarak beklemesi sonucu SQL Server'ın bir işlemi victim seçerek sonlandırmasıdır. Çözüm graph içindeki kaynaklar, transaction sırası ve sorgu planları üzerinden yapılmalıdır.",
        ExpectedBenefit = "High",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Deadlock XML içindeki victim, process-list ve resource-list düğümlerini inceleyin. Transaction'ların nesnelere aynı sırayla erişmesini, transaction kapsamının kısa tutulmasını ve uygun indeks erişim yollarını değerlendirin. Session kill veya otomatik production değişikliği uygulanmaz.",
        ScriptText = "SELECT s.name AS session_name, t.target_name, CAST(t.target_data AS xml) AS target_data FROM sys.dm_xe_sessions s JOIN sys.dm_xe_session_targets t ON t.event_session_address = s.address WHERE s.name = N'system_health' AND t.target_name = N'ring_buffer';",
        Status = "New"
    };
}
