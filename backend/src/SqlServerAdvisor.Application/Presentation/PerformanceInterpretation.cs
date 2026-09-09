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
        long userUpdates)
    {
        var reads = userSeeks + userScans;
        var level = fragmentationPercent >= 50m && pageCount >= 10000 ? "High" : fragmentationPercent >= 30m ? "Medium" : "Low";
        var activity = reads == 0 ? "okuma aktivitesi görünmüyor" : reads >= userUpdates ? "okuma ağırlıklı kullanılıyor" : "write ağırlıklı kullanılıyor";
        var summary = $"İndeks %{fragmentationPercent:N1} fragmented, {pageCount:N0} page (~{sizeMb:N1} MB) ve {activity}.";
        var inspection = reads == 0
            ? "Kullanılmayan/az kullanılan indeks ihtimalini doğrulayın; sırf fragmentation için bakım yapmayın."
            : "Storage latency ve page density ile birlikte REORGANIZE/REBUILD ihtiyacını bakım penceresinde değerlendirin.";
        return new PerformanceInterpretation(level, "Fiziksel indeks sağlığı", summary, inspection);
    }

    public static PerformanceInterpretation BuildMissing(
        long userSeeks,
        long userScans,
        decimal avgUserImpact,
        decimal improvementMeasure)
    {
        var reads = userSeeks + userScans;
        var level = improvementMeasure >= 5_000_000m && avgUserImpact >= 80m ? "High" : improvementMeasure >= 500_000m ? "Medium" : "Low";
        var summary = $"SQL Server bu kolon kombinasyonu için {reads:N0} seek/scan ve %{avgUserImpact:N1} tahmini kullanıcı etkisi bildirdi. Improvement measure {improvementMeasure:N0}.";
        var inspection = "Aynı tablo üzerindeki mevcut indeksleri, key sırasını, include örtüşmesini ve write maliyetini karşılaştırmadan CREATE INDEX çalıştırmayın.";
        return new PerformanceInterpretation(level, "İndeks tuning adayı", summary, inspection);
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
        DateTimeOffset now)
    {
        var ageDays = lastUpdated.HasValue ? Math.Max(0, (now.UtcDateTime - lastUpdated.Value).TotalDays) : (double?)null;
        var staleByChange = modificationPercent >= 20m;
        var staleByAge = ageDays is >= 30;
        var lowSample = samplePercent.HasValue && samplePercent.Value < 10m && rows >= 1_000_000;

        var level = (staleByChange && staleByAge) || modificationPercent >= 40m ? "High"
            : staleByChange || staleByAge || lowSample ? "Medium"
            : "Low";

        var ageText = ageDays.HasValue ? $"{ageDays.Value:N0} gün önce" : "güncelleme zamanı bilinmiyor";
        var sampleText = samplePercent.HasValue ? $"%{samplePercent.Value:N1} sample" : $"{rowsSampled:N0}/{rows:N0} satır sample";
        var summary = $"Statistics {ageText} güncellenmiş; %{modificationPercent:N1} satır değişmiş ve {sampleText} kullanılmış.";
        var inspection = level == "Low"
            ? "Şu an belirgin bir freshness riski yok; ilgili query planlarda estimated/actual row farkı oluşursa yeniden değerlendirin."
            : "İlgili query planlarda estimated/actual row farkını doğrulayın; gerekiyorsa tablo büyüklüğüne uygun sample ile DBA kontrollü statistics güncellemesi planlayın.";

        return new PerformanceInterpretation(level, "Optimizer statistics durumu", summary, inspection);
    }
}
