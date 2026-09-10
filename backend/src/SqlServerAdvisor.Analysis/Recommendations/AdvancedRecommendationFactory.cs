using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Analysis.Recommendations;

public sealed class AdvancedRecommendationFactory : IRecommendationFactory
{
    private readonly RecommendationFactory _baseFactory = new();

    public Recommendation? Create(Finding finding)
    {
        var recommendation = finding.RuleId switch
        {
            "IDX-005" => CreatePrefixConsolidationRecommendation(finding),
            "IDX-006" => CreateIndexModificationRecommendation(finding),
            "IDX-007" => CreateWriteHotspotRecommendation(finding),
            "STATS-004" => CreatePersistedSamplingRecommendation(finding),
            "STATS-005" => CreateRedundantStatisticsRecommendation(finding),
            _ => _baseFactory.Create(finding)
        };

        if (recommendation is not null)
        {
            recommendation.FindingId = finding.Id;
            recommendation.CanExecute = false;
        }

        return recommendation;
    }

    private static Recommendation CreatePrefixConsolidationRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Prefix indeksleri tek tasarım altında konsolide etmeyi değerlendirin",
        Explanation = "Daha dar bir indeks, daha geniş bir indeksin sol key prefix'i ve kolon kapsamı içinde kalıyor. Yeterli kullanım gözlemi ve düşük relatif read değeri varsa ayrı indeksin write, log, disk ve buffer-pool maliyeti gereksiz olabilir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Medium",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "İki indeksin execution plan kullanımını ve dönemsel workload bağımlılığını karşılaştırın. Dar indeksin bağımsız faydası doğrulanamıyorsa test ortamında kaldırma/konsolidasyon senaryosunu ölçün. Üretimde DROP INDEX yalnız DBA onayıyla uygulanmalıdır.",
        ScriptText = "SELECT i.object_id, i.index_id, i.name, i.is_unique, i.is_primary_key, i.has_filter, i.filter_definition, ISNULL(us.user_seeks,0) AS user_seeks, ISNULL(us.user_scans,0) AS user_scans, ISNULL(us.user_lookups,0) AS user_lookups, ISNULL(us.user_updates,0) AS user_updates FROM sys.indexes i LEFT JOIN sys.dm_db_index_usage_stats us ON us.database_id=DB_ID() AND us.object_id=i.object_id AND us.index_id=i.index_id WHERE i.object_id=OBJECT_ID(@TableName) ORDER BY i.index_id;",
        Status = "New"
    };

    private static Recommendation CreateIndexModificationRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Yeni indeks yerine mevcut indeksi kontrollü yeniden tasarlayın",
        Explanation = "Missing-index talebiyle aynı key dizisine sahip mevcut bir indeks bulundu ancak INCLUDE kapsamı eksik. İkinci aynı-key indeks oluşturmak yerine mevcut indeksin genişletilmesi toplam write ve storage maliyetini düşürebilir.",
        ExpectedBenefit = "High",
        RiskLevel = "Medium",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Mevcut indeksin kullandığı planları, write yükünü ve ihtiyaç duyulan ek INCLUDE kolonlarını doğrulayın. Test ortamında mevcut indeksin yeniden oluşturulmuş tasarımını karşılaştırın; CREATE ... WITH (DROP_EXISTING = ON) veya başka DDL yalnız DBA tarafından onay sonrası uygulanmalıdır.",
        ScriptText = "SELECT i.index_id, i.name, i.fill_factor, i.has_filter, i.filter_definition, ic.key_ordinal, ic.is_descending_key, ic.is_included_column, c.name AS column_name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(@TableName) ORDER BY i.index_id, ic.key_ordinal, ic.index_column_id;",
        Status = "New"
    };

    private static Recommendation CreateWriteHotspotRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Write-hot indeksin page allocation ve latch davranışını optimize edin",
        Explanation = "Leaf insert ile birlikte yüksek page allocation veya page-latch beklemesi gözlendi. Bu durum random insert, key dağılımı, page split, hot-page veya uygun olmayan fill-factor davranışına işaret edebilir; tek başına fill factor değiştirme gerekçesi değildir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Medium",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Key dağılımını, insert desenini, fill factor değerini, page-latch beklemelerini ve indeks boyutunu birlikte inceleyin. Fill-factor veya key tasarımı değişikliğini önce temsilî write workload ile test edin.",
        ScriptText = "SELECT i.name, i.fill_factor, ios.leaf_insert_count, ios.leaf_delete_count, ios.leaf_update_count, ios.leaf_allocation_count, ios.page_latch_wait_count, ios.page_latch_wait_in_ms FROM sys.indexes i CROSS APPLY sys.dm_db_index_operational_stats(DB_ID(), i.object_id, i.index_id, NULL) ios WHERE i.object_id=OBJECT_ID(@TableName) ORDER BY ios.page_latch_wait_in_ms DESC;",
        Status = "New"
    };

    private static Recommendation CreatePersistedSamplingRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Persisted statistics sample stratejisini yeniden değerlendirin",
        Explanation = "Düşük bir persisted sample oranı sonraki statistics güncellemelerinde tekrar kullanılabilir. Büyük veya değişken veri dağılımında bu seçim histogram kalitesini ve cardinality tahminlerini olumsuz etkileyebilir.",
        ExpectedBenefit = "Medium",
        RiskLevel = "Low",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "İlgili sorguların estimated/actual row farkını ve histogramı inceleyin. Daha uygun SAMPLE/FULLSCAN oranını test ortamında ölçün; Advisor otomatik UPDATE STATISTICS çalıştırmaz.",
        ScriptText = "SELECT s.name, p.last_updated, p.rows, p.rows_sampled, p.steps, p.unfiltered_rows, p.modification_counter, p.persisted_sample_percent FROM sys.stats s OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) p WHERE s.object_id=OBJECT_ID(@TableName) ORDER BY s.stats_id;",
        Status = "New"
    };

    private static Recommendation CreateRedundantStatisticsRecommendation(Finding finding) => new()
    {
        PriorityScore = finding.FindingScore,
        Title = "Aynı kolon kapsamındaki statistics nesnelerini konsolide etmeyi değerlendirin",
        Explanation = "Aynı kolon dizisi ve filtre kapsamına sahip birden fazla statistics nesnesi bulundu. Özellikle ayrı user/auto statistics bir index statistics tarafından aynı kapsamda karşılanıyorsa bakım maliyeti gereksiz olabilir.",
        ExpectedBenefit = "Low",
        RiskLevel = "Medium",
        ConfidenceScore = finding.ConfidenceScore,
        RecommendedAction = "Statistics nesnelerinin index bağı, oluşturulma amacı, güncellenme zamanı, sample oranı ve sorgu planı kullanımını doğrulayın. Redundant olduğu kanıtlanan standalone statistics için DROP STATISTICS yalnız DBA onayıyla değerlendirilmelidir.",
        ScriptText = "SELECT s.stats_id, s.name, s.auto_created, s.user_created, s.no_recompute, s.has_filter, s.filter_definition, p.last_updated, p.rows, p.rows_sampled, p.modification_counter FROM sys.stats s OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) p WHERE s.object_id=OBJECT_ID(@TableName) ORDER BY s.stats_id;",
        Status = "New"
    };
}
