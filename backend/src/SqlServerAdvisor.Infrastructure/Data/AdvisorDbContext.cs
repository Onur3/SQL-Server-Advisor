using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Infrastructure.Data;

public sealed class AdvisorDbContext(DbContextOptions<AdvisorDbContext> options) : DbContext(options)
{
    public DbSet<ServerProfile> Servers => Set<ServerProfile>();
    public DbSet<CollectorSetting> CollectorSettings => Set<CollectorSetting>();
    public DbSet<ServerCapability> ServerCapabilities => Set<ServerCapability>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();
    public DbSet<CollectorRun> CollectorRuns => Set<CollectorRun>();
    public DbSet<ServerSnapshot> ServerSnapshots => Set<ServerSnapshot>();
    public DbSet<DatabaseSnapshot> DatabaseSnapshots => Set<DatabaseSnapshot>();
    public DbSet<DatabaseFileSnapshot> DatabaseFileSnapshots => Set<DatabaseFileSnapshot>();
    public DbSet<ConfigurationSnapshot> ConfigurationSnapshots => Set<ConfigurationSnapshot>();
    public DbSet<WaitSnapshot> WaitSnapshots => Set<WaitSnapshot>();
    public DbSet<FileIoSnapshot> FileIoSnapshots => Set<FileIoSnapshot>();
    public DbSet<TempDbSnapshot> TempDbSnapshots => Set<TempDbSnapshot>();
    public DbSet<BlockingEvent> BlockingEvents => Set<BlockingEvent>();
    public DbSet<DeadlockEvent> DeadlockEvents => Set<DeadlockEvent>();
    public DbSet<QueryDefinition> Queries => Set<QueryDefinition>();
    public DbSet<QueryPlan> QueryPlans => Set<QueryPlan>();
    public DbSet<QueryRuntimeSnapshot> QueryRuntimeSnapshots => Set<QueryRuntimeSnapshot>();
    public DbSet<IndexSnapshot> IndexSnapshots => Set<IndexSnapshot>();
    public DbSet<MissingIndexSnapshot> MissingIndexSnapshots => Set<MissingIndexSnapshot>();
    public DbSet<StatisticsSnapshot> StatisticsSnapshots => Set<StatisticsSnapshot>();
    public DbSet<CodeObjectSnapshot> CodeObjects => Set<CodeObjectSnapshot>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<FindingEvidence> FindingEvidence => Set<FindingEvidence>();
    public DbSet<FindingRelation> FindingRelations => Set<FindingRelation>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RecommendationValidation> RecommendationValidations => Set<RecommendationValidation>();
    public DbSet<AlertEvent> Alerts => Set<AlertEvent>();
    public DbSet<UserAccess> UserAccess => Set<UserAccess>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ServerHourlyAggregate> ServerHourlyAggregates => Set<ServerHourlyAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAdmin(modelBuilder);
        ConfigureCollector(modelBuilder);
        ConfigureSnapshots(modelBuilder);
        ConfigureQueries(modelBuilder);
        ConfigureAnalysis(modelBuilder);
    }

    private static void ConfigureAdmin(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerProfile>(b =>
        {
            b.ToTable("Server", "ADM");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Host).HasMaxLength(255).IsRequired();
            b.Property(x => x.DefaultDatabase).HasMaxLength(128).IsRequired();
            b.Property(x => x.Username).HasMaxLength(256);
            b.Property(x => x.ProtectedPassword).HasColumnType("nvarchar(max)");
            b.Property(x => x.LastError).HasMaxLength(2000);
            b.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<CollectorSetting>(b =>
        {
            b.ToTable("CollectorSetting", "ADM");
            b.HasKey(x => x.Id);
            b.Property(x => x.CollectorType).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.ServerProfileId, x.CollectorType }).IsUnique();
        });

        modelBuilder.Entity<ServerCapability>(b =>
        {
            b.ToTable("ServerCapability", "ADM");
            b.HasKey(x => x.Id);
            b.Property(x => x.CapabilityKey).HasMaxLength(100).IsRequired();
            b.Property(x => x.Detail).HasMaxLength(2000);
            b.HasIndex(x => new { x.ServerProfileId, x.CapabilityKey }).IsUnique();
        });

        modelBuilder.Entity<UserAccess>(b =>
        {
            b.ToTable("UserAccess", "ADM");
            b.HasKey(x => x.Id);
            b.Property(x => x.WindowsIdentity).HasMaxLength(300).IsRequired();
            b.Property(x => x.Role).HasMaxLength(30).IsRequired();
            b.HasIndex(x => x.WindowsIdentity).IsUnique();
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("AuditEvent", "ADM");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserIdentity).HasMaxLength(300).IsRequired();
            b.Property(x => x.Action).HasMaxLength(100).IsRequired();
            b.Property(x => x.TargetType).HasMaxLength(100);
            b.Property(x => x.TargetId).HasMaxLength(200);
            b.Property(x => x.Detail).HasMaxLength(4000);
            b.HasIndex(x => x.CreatedAt);
        });
    }

    private static void ConfigureCollector(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkerHeartbeat>(b =>
        {
            b.ToTable("WorkerHeartbeat", "COL");
            b.HasKey(x => x.Id);
            b.Property(x => x.MachineName).HasMaxLength(255);
            b.Property(x => x.Version).HasMaxLength(50);
            b.Property(x => x.Status).HasMaxLength(30);
            b.HasIndex(x => x.LastHeartbeatAt);
        });

        modelBuilder.Entity<CollectorRun>(b =>
        {
            b.ToTable("CollectorRun", "COL");
            b.HasKey(x => x.Id);
            b.Property(x => x.CollectorType).HasMaxLength(100).IsRequired();
            b.Property(x => x.Status).HasMaxLength(30).IsRequired();
            b.Property(x => x.ErrorMessage).HasMaxLength(4000);
            b.HasIndex(x => new { x.ServerProfileId, x.CollectorType, x.StartedAt });
        });
    }

    private static void ConfigureSnapshots(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerSnapshot>(b =>
        {
            b.ToTable("Server", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.ServerName).HasMaxLength(255);
            b.Property(x => x.ProductVersion).HasMaxLength(100);
            b.Property(x => x.Edition).HasMaxLength(255);
            b.HasIndex(x => new { x.ServerProfileId, x.CapturedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<DatabaseSnapshot>(b =>
        {
            b.ToTable("Database", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            b.Property(x => x.StateDesc).HasMaxLength(60);
            b.Property(x => x.RecoveryModelDesc).HasMaxLength(60);
            b.Property(x => x.PageVerifyOptionDesc).HasMaxLength(60);
            b.Property(x => x.QueryStoreStateDesc).HasMaxLength(60);
            b.Property(x => x.SizeMb).HasPrecision(19, 2);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseId, x.CapturedAt });
        });

        modelBuilder.Entity<DatabaseFileSnapshot>(b =>
        {
            b.ToTable("DatabaseFile", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            b.Property(x => x.LogicalName).HasMaxLength(128).IsRequired();
            b.Property(x => x.TypeDesc).HasMaxLength(30);
            b.Property(x => x.PhysicalName).HasMaxLength(1000);
            b.Property(x => x.SizeMb).HasPrecision(19, 2);
            b.Property(x => x.MaxSizeMb).HasPrecision(19, 2);
            b.Property(x => x.GrowthValue).HasPrecision(19, 2);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseName, x.CapturedAt });
        });

        modelBuilder.Entity<ConfigurationSnapshot>(b =>
        {
            b.ToTable("Configuration", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.ServerProfileId, x.Name, x.CapturedAt });
        });

        modelBuilder.Entity<WaitSnapshot>(b =>
        {
            b.ToTable("Wait", "SNP");
            b.Property(x => x.SignalWaitTimeMs).HasColumnName("SignalWaitMs");
            b.Property(x => x.DeltaSignalWaitTimeMs).HasColumnName("DeltaSignalWaitMs");
            b.HasKey(x => x.Id);
            b.Property(x => x.WaitType).HasMaxLength(120).IsRequired();
            b.HasIndex(x => new { x.ServerProfileId, x.WaitType, x.CapturedAt });
        });

        modelBuilder.Entity<FileIoSnapshot>(b =>
        {
            b.ToTable("FileIO", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128);
            b.Property(x => x.LogicalName).HasMaxLength(128);
            b.Property(x => x.TypeDesc).HasMaxLength(30);
            b.Property(x => x.ReadLatencyMs).HasPrecision(19, 3);
            b.Property(x => x.WriteLatencyMs).HasPrecision(19, 3);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseId, x.FileId, x.CapturedAt });
        });

        modelBuilder.Entity<TempDbSnapshot>(b =>
        {
            b.ToTable("TempDb", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserObjectMb).HasPrecision(19, 2);
            b.Property(x => x.InternalObjectMb).HasPrecision(19, 2);
            b.Property(x => x.VersionStoreMb).HasPrecision(19, 2);
            b.Property(x => x.FreeSpaceMb).HasPrecision(19, 2);
            b.Property(x => x.TotalMb).HasPrecision(19, 2);
            b.HasIndex(x => new { x.ServerProfileId, x.CapturedAt });
        });

        modelBuilder.Entity<BlockingEvent>(b =>
        {
            b.ToTable("Blocking", "EVT");
            b.Property(x => x.BlockerStatus).HasMaxLength(30);
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128);
            b.Property(x => x.WaitType).HasMaxLength(120);
            b.Property(x => x.WaitResource).HasMaxLength(1000);
            b.Property(x => x.HostName).HasMaxLength(255);
            b.Property(x => x.ProgramName).HasMaxLength(255);
            b.Property(x => x.LoginName).HasMaxLength(255);
            b.HasIndex(x => new { x.ServerProfileId, x.CapturedAt });
        });

        modelBuilder.Entity<DeadlockEvent>(b =>
        {
            b.ToTable("Deadlock", "EVT");
            b.HasKey(x => x.Id);
            b.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
            b.Property(x => x.VictimProcessId).HasMaxLength(128);
            b.Property(x => x.DatabaseNames).HasMaxLength(1000);
            b.Property(x => x.ObjectNames).HasMaxLength(2000);
            b.HasIndex(x => new { x.ServerProfileId, x.EventTime });
            b.HasIndex(x => new { x.ServerProfileId, x.Fingerprint });
        });
    }

    private static void ConfigureQueries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QueryDefinition>(b =>
        {
            b.ToTable("Query", "QRY");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            b.Property(x => x.QueryHash).HasMaxLength(128).IsRequired();
            b.Property(x => x.NormalizedHash).HasMaxLength(128).IsRequired();
            b.Property(x => x.ObjectName).HasMaxLength(512);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseName, x.QueryHash }).IsUnique();
        });

        modelBuilder.Entity<QueryPlan>(b =>
        {
            b.ToTable("Plan", "QRY");
            b.HasKey(x => x.Id);
            b.Property(x => x.PlanHash).HasMaxLength(128).IsRequired();
            b.Property(x => x.Source).HasMaxLength(30).IsRequired();
            b.HasIndex(x => new { x.QueryId, x.PlanHash, x.Source }).IsUnique();
        });

        modelBuilder.Entity<QueryRuntimeSnapshot>(b =>
        {
            b.ToTable("Runtime", "QRY");
            b.HasKey(x => x.Id);
            b.Property(x => x.Source).HasMaxLength(30).IsRequired();
            b.Property(x => x.TotalCpuMs).HasPrecision(19, 3);
            b.Property(x => x.AverageCpuMs).HasPrecision(19, 3);
            b.Property(x => x.TotalDurationMs).HasPrecision(19, 3);
            b.Property(x => x.AverageDurationMs).HasPrecision(19, 3);
            b.Property(x => x.AverageLogicalReads).HasPrecision(19, 3);
            b.Property(x => x.ImpactScore).HasPrecision(6, 2);
            b.HasIndex(x => new { x.ServerProfileId, x.QueryId, x.CapturedAt });
        });

        modelBuilder.Entity<IndexSnapshot>(b =>
        {
            b.ToTable("Index", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            b.Property(x => x.TableName).HasMaxLength(512).IsRequired();
            b.Property(x => x.IndexName).HasMaxLength(512).IsRequired();
            b.Property(x => x.TypeDesc).HasMaxLength(60);
            b.Property(x => x.SizeMb).HasPrecision(19, 2);
            b.Property(x => x.AvgFragmentationPercent).HasPrecision(9, 3);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseName, x.ObjectId, x.IndexId, x.CapturedAt });
        });

        modelBuilder.Entity<MissingIndexSnapshot>(b =>
        {
            b.ToTable("MissingIndex", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            b.Property(x => x.TableName).HasMaxLength(512).IsRequired();
            b.Property(x => x.AvgTotalUserCost).HasPrecision(19, 3);
            b.Property(x => x.AvgUserImpact).HasPrecision(9, 3);
            b.Property(x => x.ImprovementMeasure).HasPrecision(19, 3);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseName, x.CapturedAt });
        });

        modelBuilder.Entity<StatisticsSnapshot>(b =>
        {
            b.ToTable("Statistics", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            b.Property(x => x.TableName).HasMaxLength(512).IsRequired();
            b.Property(x => x.StatisticsName).HasMaxLength(512).IsRequired();
            b.Property(x => x.SamplePercent).HasPrecision(9, 3);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseName, x.ObjectId, x.StatisticsId, x.CapturedAt });
        });

        modelBuilder.Entity<CodeObjectSnapshot>(b =>
        {
            b.ToTable("CodeObject", "SNP");
            b.HasKey(x => x.Id);
            b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            b.Property(x => x.SchemaName).HasMaxLength(128).IsRequired();
            b.Property(x => x.ObjectName).HasMaxLength(512).IsRequired();
            b.Property(x => x.ObjectType).HasMaxLength(60).IsRequired();
            b.Property(x => x.DefinitionHash).HasMaxLength(128).IsRequired();
            b.Property(x => x.ParseMessage).HasMaxLength(2000);
            b.HasIndex(x => new { x.ServerProfileId, x.DatabaseName, x.ObjectId, x.DefinitionHash }).IsUnique();
        });
    }

    private static void ConfigureAnalysis(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Finding>(b =>
        {
            b.ToTable("Finding", "ANL");
            b.HasKey(x => x.Id);
            b.Property(x => x.RuleId).HasMaxLength(50).IsRequired();
            b.Property(x => x.Category).HasMaxLength(50).IsRequired();
            b.Property(x => x.DatabaseName).HasMaxLength(128);
            b.Property(x => x.ObjectName).HasMaxLength(512);
            b.Property(x => x.Title).HasMaxLength(500).IsRequired();
            b.Property(x => x.TechnicalDescription).HasMaxLength(4000);
            b.Property(x => x.Fingerprint).HasMaxLength(300).IsRequired();
            b.Property(x => x.Status).HasMaxLength(30);
            b.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
            b.Property(x => x.ImpactScore).HasPrecision(5, 2);
            b.Property(x => x.FindingScore).HasPrecision(7, 2);
            b.HasIndex(x => new { x.ServerProfileId, x.Status, x.LastDetectedAt });
            b.HasIndex(x => new { x.ServerProfileId, x.Fingerprint, x.Status });
        });

        modelBuilder.Entity<FindingEvidence>(b =>
        {
            b.ToTable("FindingEvidence", "ANL");
            b.HasKey(x => x.Id);
            b.Property(x => x.Metric).HasMaxLength(100).IsRequired();
            b.Property(x => x.ObservedValue).HasMaxLength(1000).IsRequired();
            b.Property(x => x.ExpectedValue).HasMaxLength(1000);
            b.Property(x => x.Unit).HasMaxLength(50);
            b.Property(x => x.Source).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.HasIndex(x => x.FindingId);
        });

        modelBuilder.Entity<FindingRelation>(b =>
        {
            b.ToTable("FindingRelation", "ANL");
            b.HasKey(x => x.Id);
            b.Property(x => x.RelationType).HasMaxLength(60).IsRequired();
            b.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
            b.HasIndex(x => new { x.ParentFindingId, x.ChildFindingId, x.RelationType }).IsUnique();
        });

        modelBuilder.Entity<Recommendation>(b =>
        {
            b.ToTable("Recommendation", "REC", tb => tb.HasCheckConstraint("CK_REC_Recommendation_CanExecute", "[CanExecute] = 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.PriorityScore).HasPrecision(5, 2);
            b.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
            b.Property(x => x.Title).HasMaxLength(500).IsRequired();
            b.Property(x => x.ExpectedBenefit).HasMaxLength(30);
            b.Property(x => x.RiskLevel).HasMaxLength(30);
            b.Property(x => x.Status).HasMaxLength(30);
            b.Property(x => x.CanExecute).HasDefaultValue(false);
            b.Property(x => x.WorkflowNote).HasMaxLength(4000);
            b.HasIndex(x => new { x.Status, x.PriorityScore });
            b.HasIndex(x => x.FindingId).IsUnique();
        });

        modelBuilder.Entity<RecommendationValidation>(b =>
        {
            b.ToTable("RecommendationValidation", "REC");
            b.HasKey(x => x.Id);
            b.Property(x => x.BeforeDurationMs).HasPrecision(19, 3);
            b.Property(x => x.AfterDurationMs).HasPrecision(19, 3);
            b.Property(x => x.BeforeCpuMs).HasPrecision(19, 3);
            b.Property(x => x.AfterCpuMs).HasPrecision(19, 3);
            b.Property(x => x.BeforeLogicalReads).HasPrecision(19, 3);
            b.Property(x => x.AfterLogicalReads).HasPrecision(19, 3);
            b.Property(x => x.Status).HasMaxLength(30);
            b.Property(x => x.Result).HasMaxLength(30);
            b.Property(x => x.Detail).HasMaxLength(4000);
            b.HasIndex(x => x.RecommendationId);
        });

        modelBuilder.Entity<AlertEvent>(b =>
        {
            b.ToTable("AlertEvent", "ALR");
            b.HasKey(x => x.Id);
            b.Property(x => x.Severity).HasMaxLength(30).IsRequired();
            b.Property(x => x.Title).HasMaxLength(500).IsRequired();
            b.Property(x => x.Message).HasMaxLength(4000);
            b.Property(x => x.AcknowledgedBy).HasMaxLength(300);
            b.HasIndex(x => new { x.ServerProfileId, x.CreatedAt });
        });

        modelBuilder.Entity<ServerHourlyAggregate>(b =>
        {
            b.ToTable("ServerHourly", "AGG");
            b.HasKey(x => x.Id);
            b.Property(x => x.AvgSqlCpuPercent).HasPrecision(9, 3);
            b.Property(x => x.AvgAvailableMemoryMb).HasPrecision(19, 3);
            b.Property(x => x.AvgActiveSessions).HasPrecision(19, 3);
            b.Property(x => x.AvgBlockedRequests).HasPrecision(19, 3);
            b.HasIndex(x => new { x.ServerProfileId, x.HourStart }).IsUnique();
        });
    }
}
