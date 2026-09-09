namespace SqlServerAdvisor.Application.Presentation;

public sealed record PerformanceInterpretation(
    string Level,
    string Headline,
    string Summary,
    string SuggestedInspection);

public static class QueryInterpretation
{
    public static PerformanceInterpretation Build(
        long executionCount,
        decimal averageCpuMs,
        decimal averageDurationMs,
        decimal averageLogicalReads,
        decimal impactScore)
    {
        var cpuWeight = Math.Min(35m, averageCpuMs / 500m * 35m);
        var durationWeight = Math.Min(30m, averageDurationMs / 2000m * 30m);
        var readWeight = Math.Min(25m, averageLogicalReads / 100000m * 25m);

        var dominant = "karma maliyet";
        var inspection = "Execution plan üzerinde scan/seek, cardinality tahmini, lookup ve join stratejilerini birlikte inceleyin.";

        if (cpuWeight >= durationWeight && cpuWeight >= readWeight)
        {
            dominant = "CPU maliyeti";
            inspection = "Execution plan'da pahalı operatorleri, scalar işlemleri, paralellik davranışını ve cardinality tahminlerini inceleyin.";
        }
        else if (readWeight >= cpuWeight && readWeight >= durationWeight)
        {
            dominant = "logical read maliyeti";
            inspection = "Scan, key lookup ve geniş veri erişim yollarını; mevcut indeks key/include kapsamını kontrol edin.";
        }
        else if (durationWeight >= cpuWeight && durationWeight >= readWeight)
        {
            dominant = "çalışma süresi";
            inspection = "Wait/blocking korelasyonunu, memory grant'i, I/O gecikmesini ve execution plan operator sürelerini inceleyin.";
        }

        var level = impactScore >= 85m ? "Critical" : impactScore >= 70m ? "High" : impactScore >= 50m ? "Medium" : "Low";
        var frequencyText = executionCount >= 1000 ? "çok sık çalışıyor" : executionCount >= 100 ? "sık çalışıyor" : executionCount >= 10 ? "tekrarlı çalışıyor" : "düşük sıklıkta çalışıyor";

        return new PerformanceInterpretation(
            level,
            $"Baskın maliyet: {dominant}",
            $"Bu sorgu {frequencyText}. Ortalama CPU {averageCpuMs:N0} ms, süre {averageDurationMs:N0} ms ve logical read {averageLogicalReads:N0}. Impact skoru {impactScore:N0}/100.",
            inspection);
    }
}

public static class IndexInterpretation
{
    public static PerformanceInterpretation BuildFragmentation(
        decimal fragmentationPercent,
        long pageCount,
        decimal sizeMb,
        long userSeeks,
        long userScans,
        long userLookups,
        long userUpdates,
        int usageSinceDays,
        bool hasFilter)
    {
        var reads = userSeeks + userScans + userLookups;
        var matureWindow = usageSinceDays >= 7;
        var lowValuePattern = matureWindow && reads <= Math.Max(10L, userUpdates / 100L) && userUpdates >= 5000;

        if (lowValuePattern)
        {
            return new PerformanceInterpretation(
                "Medium",
                "Önce indeks değerini sorgulayın",
                $"İndeks %{fragmentationPercent:N1} fragmented ancak yaklaşık {usageSinceDays} günlük kullanım penceresinde {reads:N0} read / {userUpdates:N0} update gösteriyor. Boyut ~{sizeMb:N1} MB.",
                "Bu indeks için bakım yapmadan önce gerçekten gerekli olup olmadığını, dönemsel workload'u ve execution plan kullanımını doğrulayın.");
        }

        var level = fragmentationPercent >= 50m && pageCount >= 10000 ? "High" : fragmentationPercent >= 30m ? "Medium" : "Low";
        var activity = reads == 0 ? "okuma aktivitesi görünmüyor" : reads >= userUpdates ? "okuma ağırlıklı kullanılıyor" : "write ağırlıklı kullanılıyor";
        var filterText = hasFilter ? " Filtreli indekstir." : string.Empty;
        var summary = $"İndeks %{fragmentationPercent:N1} fragmented, {pageCount:N0} page (~{sizeMb:N1} MB), {activity}. Kullanım gözlemi ~{usageSinceDays} gün.{filterText}";
        var inspection = reads == 0
            ? "Kullanım penceresi yeterliyse düşük değerli indeks ihtimalini doğrulayın; sırf fragmentation için bakım yapmayın."
            : "Storage latency ve workload ile birlikte REORGANIZE/REBUILD ihtiyacını bakım penceresinde değerlendirin.";
        return new PerformanceInterpretation(level, "Fiziksel indeks sağlığı", summary, inspection);
    }

    public static PerformanceInterpretation BuildMissing(
        long userSeeks,
        long userScans,
        decimal avgUserImpact,
        decimal improvementMeasure,
        bool coveredByExistingIndex)
    {
        var reads = userSeeks + userScans;
        if (coveredByExistingIndex)
        {
            return new PerformanceInterpretation(
                "Low",
                "Mevcut indeks muhtemelen kapsıyor",
                $"DMV {reads:N0} seek/scan ve %{avgUserImpact:N1} tahmini etki bildiriyor; ancak mevcut bir indeks gerekli key/include kolonlarını konservatif kapsama kontrolünde karşılıyor.",
                "Yeni indeks oluşturmadan önce mevcut indeksin ilgili execution plan'da neden seçilmediğini araştırın; statistics/cardinality veya key sırası etkili olabilir.");
        }

        var level = improvementMeasure >= 5_000_000m && avgUserImpact >= 80m ? "High" : improvementMeasure >= 500_000m ? "Medium" : "Low";
        var summary = $"SQL Server bu kolon kombinasyonu için {reads:N0} seek/scan ve %{avgUserImpact:N1} tahmini kullanıcı etkisi bildirdi. Improvement measure {improvementMeasure:N0}.";
        var inspection = "Aynı tablo üzerindeki mevcut indeksleri, key sırasını, include örtüşmesini ve write maliyetini karşılaştırmadan CREATE INDEX çalıştırmayın.";
        return new PerformanceInterpretation(level, "Yeni indeks tuning adayı", summary, inspection);
    }
}

public static class StatisticsInterpretation
{
    public static PerformanceInterpretation Build(
        long rows,
        long rowsSampled,
        decimal modificationPercent,
        DateTime? lastUpdated,
        decimal? samplePercent,
        bool autoCreated,
        bool userCreated,
        bool noRecompute,
        bool hasFilter,
        DateTimeOffset now)
    {
        var ageDays = lastUpdated.HasValue ? Math.Max(0, (now.UtcDateTime - DateTime.SpecifyKind(lastUpdated.Value, DateTimeKind.Utc)).TotalDays) : (double?)null;
        var staleByChange = modificationPercent >= 20m;
        var staleByAge = ageDays is >= 30;
        var lowSample = samplePercent.HasValue && samplePercent.Value < 10m && rows >= 1_000_000;
        var noRecomputeRisk = noRecompute && modificationPercent >= 5m;

        var level = noRecomputeRisk && modificationPercent >= 10m ? "High"
            : (staleByChange && staleByAge) || modificationPercent >= 40m ? "High"
            : staleByChange || staleByAge || lowSample || noRecomputeRisk ? "Medium"
            : "Low";

        var ageText = ageDays.HasValue ? $"{ageDays.Value:N0} gün önce" : "güncelleme zamanı bilinmiyor";
        var sampleText = samplePercent.HasValue ? $"%{samplePercent.Value:N1} sample" : $"{rowsSampled:N0}/{rows:N0} satır sample";
        var typeText = autoCreated ? "AUTO" : userCreated ? "USER" : "INDEX";
        var flags = new List<string>();
        if (noRecompute) flags.Add("NORECOMPUTE");
        if (hasFilter) flags.Add("FILTERED");
        var flagText = flags.Count > 0 ? $" Bayraklar: {string.Join(", ", flags)}." : string.Empty;
        var summary = $"{typeText} statistics {ageText} güncellenmiş; %{modificationPercent:N1} satır değişmiş ve {sampleText} kullanılmış.{flagText}";

        var inspection = noRecomputeRisk
            ? "NORECOMPUTE ayarının bilinçli olup olmadığını ve ilgili planlarda estimated/actual row sapmasını öncelikli kontrol edin."
            : lowSample && modificationPercent >= 5m
                ? "Büyük tabloda düşük sample + değişim birlikte görülüyor; histogram ve estimated/actual row farkını doğrulayıp uygun SAMPLE oranını test edin."
                : level == "Low"
                    ? "Şu an belirgin bir freshness riski yok; ilgili query planlarda estimated/actual row farkı oluşursa yeniden değerlendirin."
                    : "İlgili query planlarda estimated/actual row farkını doğrulayın; gerekiyorsa tablo büyüklüğüne uygun sample ile DBA kontrollü statistics güncellemesi planlayın.";

        return new PerformanceInterpretation(level, "Optimizer statistics durumu", summary, inspection);
    }
}
