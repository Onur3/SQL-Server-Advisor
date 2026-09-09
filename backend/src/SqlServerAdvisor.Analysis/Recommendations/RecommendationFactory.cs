using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Analysis.Recommendations;

public sealed class RecommendationFactory : IRecommendationFactory
{
    public Recommendation? Create(Finding finding)
    {
        var recommendation = finding.RuleId switch
        {
            "WAIT-001" => new Recommendation
            {
                Title = "Inspect interval waits and correlate with workload",
                Explanation = finding.TechnicalDescription,
                RecommendedAction = "Compare repeated intervals with request, storage and CPU metrics. Wait totals accumulate across tasks and are not wall-clock utilization. Review with your DBA.",
                ScriptText = "SELECT wait_type, waiting_tasks_count, wait_time_ms, signal_wait_time_ms FROM sys.dm_os_wait_stats ORDER BY wait_time_ms DESC;",
                PriorityScore = finding.FindingScore, ConfidenceScore = finding.ConfidenceScore,
                ExpectedBenefit = "Medium", RiskLevel = "Low", Status = "New", CanExecute = false
            },
            "BLK-002" => CreateBlockingRecommendation(finding),
            "BLK-001" => CreateBlockingRecommendation(finding),
            "CPU-001" => CreateCpuRecommendation(finding),
            "MEM-001" => CreateMemoryRecommendation(finding),
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
        ScriptText = "SELECT TOP (25) qs.execution_count, qs.total_worker_time / 1000.0 AS total_cpu_ms, (qs.total_worker_time / NULLIF(qs.execution_count,0)) / 1000.0 AS avg_cpu_ms, qs.total_logical_reads, DB_NAME(st.dbid) AS database_name, SUBSTRING(st.text, (qs.statement_start_offset/2)+1, ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset)/2)+1) AS statement_text FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st ORDER BY qs.total_worker_time DESC;",
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
}
