import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { timer, switchMap, forkJoin } from 'rxjs';
import { AdvisorApiService } from '../../core/services/advisor-api.service';
import { DashboardServer, WorkerStatus } from '../../core/models/server.models';
import { FindingListItem, RecommendationListItem } from '../../core/models/analysis.models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatProgressSpinnerModule, MatIconModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Genel Bakış</h1>
        <p class="page-subtitle">Canlı SQL sağlığı ile Advisor'ın bulgu ve aksiyon önceliklerini birlikte görün.</p>
      </div>
      @if (worker(); as w) {
        <div class="worker" [class.offline]="!w.online">
          <span class="status-dot" [class.online]="w.online" [class.offline]="!w.online"></span>
          <div><strong>Collector {{ w.online ? 'ONLINE' : 'OFFLINE' }}</strong>@if (w.machineName) { <small>{{ w.machineName }} · {{ w.version }}</small> }</div>
        </div>
      }
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!servers().length) {
      <div class="panel empty">
        <mat-icon>dns</mat-icon><h3>Henüz izlenen SQL Server yok</h3><p>SQL Sunucuları ekranından ilk salt-okunur bağlantıyı ekleyin.</p>
      </div>
    } @else {
      <div class="kpi-grid">
        <div class="kpi panel"><div class="kpi-icon blue"><mat-icon>dns</mat-icon></div><div><span>İzlenen sunucu</span><strong>{{ servers().length }}</strong></div></div>
        <div class="kpi panel"><div class="kpi-icon amber"><mat-icon>problem</mat-icon></div><div><span>Açık bulgu</span><strong>{{ totalOpenFindings() }}</strong></div></div>
        <div class="kpi panel"><div class="kpi-icon red"><mat-icon>priority_high</mat-icon></div><div><span>Yüksek + Kritik</span><strong>{{ totalImportantFindings() }}</strong></div></div>
        <div class="kpi panel"><div class="kpi-icon green"><mat-icon>tips_and_updates</mat-icon></div><div><span>Aktif öneri</span><strong>{{ activeRecommendations().length }}</strong></div></div>
      </div>

      <div class="section-title"><div><h2>Sunucu Sağlığı</h2><p>Health skoru canlı metriklere ek olarak açık Advisor bulgularını da dikkate alır.</p></div></div>
      <div class="server-grid">
        @for (server of servers(); track server.id) {
          <mat-card class="server-card">
            <mat-card-header>
              <div class="server-head">
                <div>
                  <div class="server-name"><span class="status-dot" [class.online]="server.online" [class.offline]="!server.online"></span>{{ server.name }}</div>
                  <div class="host">{{ server.host }}</div>
                </div>
                <div class="health" [class.good]="(server.healthScore ?? 0) >= 80" [class.warn]="(server.healthScore ?? 0) >= 60 && (server.healthScore ?? 0) < 80" [class.bad]="(server.healthScore ?? 0) < 60">
                  <strong>{{ server.healthScore ?? '—' }}</strong><span>ADVISOR HEALTH</span>
                </div>
              </div>
            </mat-card-header>
            <mat-card-content>
              <div class="metrics">
                <div><span>SQL CPU</span><strong>{{ server.sqlCpuPercent ?? '—' }}{{ server.sqlCpuPercent != null ? '%' : '' }}</strong></div>
                <div><span>Boş RAM</span><strong>{{ formatMemory(server.availableMemoryMb) }}</strong></div>
                <div><span>Aktif Session</span><strong>{{ server.activeSessions }}</strong></div>
                <div><span>Blocking</span><strong [class.danger-text]="server.blockedRequests > 0">{{ server.blockedRequests }}</strong></div>
              </div>
              <div class="advisor-row">
                <div class="finding-count"><mat-icon>problem</mat-icon><span><b>{{ server.openFindingCount }}</b> açık bulgu</span></div>
                @if (server.criticalFindingCount > 0) { <span class="mini danger">{{ server.criticalFindingCount }} kritik</span> }
                @if (server.highFindingCount > 0) { <span class="mini warn">{{ server.highFindingCount }} yüksek</span> }
                <span class="coverage">Veri kapsamı {{ server.dataCoveragePercent }}%</span>
              </div>
              <div class="last">Son snapshot: {{ server.lastSnapshotAt ? (server.lastSnapshotAt | date:'dd.MM.yyyy HH:mm:ss') : 'Henüz yok' }}</div>
            </mat-card-content>
          </mat-card>
        }
      </div>

      <div class="section-title action-heading"><div><h2>Önce Bunlara Bak</h2><p>Motorun açık bulgular ve öneriler arasından önceliklendirdiği ilk aksiyonlar.</p></div><a routerLink="/findings">Tüm bulgular <mat-icon>arrow_forward</mat-icon></a></div>
      <div class="action-grid">
        <section class="panel action-panel">
          <div class="panel-head"><div><mat-icon>troubleshoot</mat-icon><strong>En Önemli Bulgular</strong></div><span>{{ topFindings().length }} kayıt</span></div>
          @if (!topFindings().length) {
            <div class="clean"><mat-icon>task_alt</mat-icon><span>Açık kritik/yüksek bulgu yok.</span></div>
          } @else {
            <div class="action-list">
              @for (finding of topFindings(); track finding.id) {
                <a routerLink="/findings" class="action-item">
                  <div class="action-icon s{{ finding.severity }}"><mat-icon>{{ finding.icon }}</mat-icon></div>
                  <div class="action-copy"><div class="action-meta"><span>{{ finding.ruleName }}</span><span>{{ finding.scopeText }}</span></div><strong>{{ finding.title }}</strong><p>{{ finding.whatWasFound }}</p></div>
                  <div class="action-score">{{ finding.findingScore | number:'1.0-0' }}</div>
                </a>
              }
            </div>
          }
        </section>

        <section class="panel action-panel">
          <div class="panel-head"><div><mat-icon>tips_and_updates</mat-icon><strong>Önerilen DBA Aksiyonları</strong></div><a routerLink="/recommendations">Tümünü aç</a></div>
          @if (!topRecommendations().length) {
            <div class="clean"><mat-icon>done_all</mat-icon><span>Aktif öneri yok.</span></div>
          } @else {
            <div class="action-list">
              @for (item of topRecommendations(); track item.id) {
                <a routerLink="/recommendations" class="action-item">
                  <div class="action-icon recommendation-icon"><mat-icon>checklist</mat-icon></div>
                  <div class="action-copy"><div class="action-meta"><span>{{ item.ruleName }}</span><span>{{ item.scopeText }}</span></div><strong>{{ item.title }}</strong><p>{{ item.recommendedAction }}</p></div>
                  <div class="action-score">{{ item.priorityScore | number:'1.0-0' }}</div>
                </a>
              }
            </div>
          }
        </section>
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start}.worker{display:flex;align-items:flex-start;gap:7px;background:#eaf7f0;color:#137348;border:1px solid #c6ead7;border-radius:11px;padding:9px 12px}.worker.offline{background:#fff0f0;color:#a52a2a;border-color:#f1caca}.worker strong,.worker small{display:block}.worker strong{font-size:.74rem}.worker small{margin-top:3px;font-size:.63rem;font-weight:500;opacity:.75}.loading{height:240px;display:grid;place-items:center}.empty{min-height:260px;display:grid;place-items:center;text-align:center;padding:45px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#64748b}.empty h3,.empty p{margin:0}.empty p{color:#718096}
    .kpi-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:4px 0 24px}.kpi{display:flex;align-items:center;gap:11px;padding:13px 14px}.kpi-icon{width:39px;height:39px;border-radius:10px;display:grid;place-items:center}.kpi-icon mat-icon{font-size:20px;width:20px;height:20px}.kpi-icon.blue{background:#eaf0ff;color:#3456c5}.kpi-icon.amber{background:#fff3dd;color:#ad6813}.kpi-icon.red{background:#ffe9e6;color:#b5322c}.kpi-icon.green{background:#e8f7ef;color:#17764d}.kpi span,.kpi strong{display:block}.kpi span{font-size:.65rem;color:#7c899c}.kpi strong{font-size:1.18rem;margin-top:2px}
    .section-title{display:flex;justify-content:space-between;align-items:flex-end;margin:0 0 12px}.section-title h2{font-size:1rem;margin:0}.section-title p{margin:4px 0 0;font-size:.7rem;color:#7a8799}.section-title>a{display:flex;align-items:center;gap:4px;color:#3157d5;font-size:.72rem;font-weight:700}.section-title>a mat-icon{font-size:16px;width:16px;height:16px}
    .server-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(400px,1fr));gap:16px;margin-bottom:28px}.server-card{border-radius:15px;border:1px solid #e1e7f0;box-shadow:0 8px 26px rgba(15,23,42,.045)}.server-card mat-card-header{display:block;padding:17px 18px 10px}.server-head{display:flex;justify-content:space-between;align-items:center}.server-name{font-weight:760;font-size:1.03rem}.host{color:#758196;font-size:.76rem;margin:4px 0 0 16px}.health{text-align:center;border-radius:10px;min-width:74px;padding:7px 9px;background:#eef2f7}.health strong{font-size:1.2rem;display:block}.health span{font-size:.53rem;letter-spacing:.07em}.health.good{background:#e9f8ef;color:#137348}.health.warn{background:#fff6de;color:#a16c00}.health.bad{background:#ffeded;color:#aa3030}
    .metrics{display:grid;grid-template-columns:repeat(4,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5}.metrics div{padding:12px 7px;text-align:center;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.62rem;margin-bottom:4px}.metrics strong{font-size:.92rem}.danger-text{color:#c33333}.advisor-row{display:flex;align-items:center;gap:7px;padding-top:11px}.finding-count{display:flex;align-items:center;gap:5px;color:#536174;font-size:.68rem}.finding-count mat-icon{font-size:16px;width:16px;height:16px}.mini{border-radius:999px;padding:4px 7px;font-size:.61rem;font-weight:750}.mini.danger{background:#fee7e5;color:#aa302b}.mini.warn{background:#fff0dc;color:#a65d10}.coverage{margin-left:auto;color:#8390a2;font-size:.63rem}.last{font-size:.65rem;color:#8993a4;padding-top:8px;text-align:right}
    .action-heading{margin-top:2px}.action-grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}.action-panel{overflow:hidden}.panel-head{display:flex;justify-content:space-between;align-items:center;padding:13px 15px;border-bottom:1px solid #e8edf4}.panel-head>div{display:flex;align-items:center;gap:7px}.panel-head mat-icon{font-size:19px;width:19px;height:19px;color:#53657d}.panel-head strong{font-size:.78rem}.panel-head span,.panel-head a{font-size:.65rem;color:#7d899c}.panel-head a{color:#3157d5;font-weight:700}.action-list{display:grid}.action-item{display:grid;grid-template-columns:36px 1fr 42px;gap:10px;padding:12px 14px;border-bottom:1px solid #edf0f5;color:inherit;transition:.15s}.action-item:last-child{border-bottom:0}.action-item:hover{background:#f8faff}.action-icon{width:34px;height:34px;border-radius:9px;display:grid;place-items:center;background:#eef2f7;color:#536174}.action-icon.s3{background:#fff0df;color:#b45e12}.action-icon.s4{background:#ffe8e5;color:#b52e2a}.action-icon.recommendation-icon{background:#e9f7ef;color:#19764e}.action-icon mat-icon{font-size:18px;width:18px;height:18px}.action-copy{min-width:0}.action-meta{display:flex;gap:6px;white-space:nowrap;overflow:hidden}.action-meta span{font-size:.59rem;color:#8793a5;overflow:hidden;text-overflow:ellipsis}.action-copy>strong{display:block;margin:3px 0;font-size:.75rem;color:#2e3a4e;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.action-copy p{margin:0;font-size:.66rem;color:#6f7b8e;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.action-score{align-self:center;text-align:center;border-radius:8px;background:#f1f4f8;padding:6px 4px;font-size:.73rem;font-weight:800;color:#425066}.clean{min-height:120px;display:flex;flex-direction:column;gap:7px;align-items:center;justify-content:center;color:#738196;font-size:.7rem}.clean mat-icon{color:#269263}
    @media(max-width:1100px){.kpi-grid{grid-template-columns:1fr 1fr}.action-grid{grid-template-columns:1fr}}
    @media(max-width:700px){.heading{display:block}.worker{margin-bottom:18px;width:max-content}.kpi-grid{grid-template-columns:1fr 1fr}.server-grid{grid-template-columns:1fr}.metrics{grid-template-columns:1fr 1fr}.action-meta span:last-child{display:none}}
  `]
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly servers = signal<DashboardServer[]>([]);
  readonly worker = signal<WorkerStatus | null>(null);
  readonly findings = signal<FindingListItem[]>([]);
  readonly recommendations = signal<RecommendationListItem[]>([]);
  readonly loading = signal(true);

  readonly activeFindings = computed(() => this.findings().filter(x => x.status === 'Open'));
  readonly activeRecommendations = computed(() => this.recommendations().filter(x => x.status === 'New'));
  readonly totalOpenFindings = computed(() => this.activeFindings().length);
  readonly totalImportantFindings = computed(() => this.activeFindings().filter(x => x.severity >= 3).length);
  readonly topFindings = computed(() => [...this.activeFindings()].sort((a, b) => b.findingScore - a.findingScore).slice(0, 4));
  readonly topRecommendations = computed(() => [...this.activeRecommendations()].sort((a, b) => b.priorityScore - a.priorityScore).slice(0, 4));

  ngOnInit(): void {
    timer(0, 15_000).pipe(
      switchMap(() => forkJoin({
        servers: this.api.getDashboardServers(),
        worker: this.api.getWorkerStatus(),
        findings: this.api.getFindings('Open'),
        recommendations: this.api.getRecommendations('New')
      })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.servers.set(result.servers);
        this.worker.set(result.worker);
        this.findings.set(result.findings);
        this.recommendations.set(result.recommendations);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  formatMemory(value?: number | null): string {
    if (value == null) return '—';
    return value >= 1024 ? `${(value / 1024).toFixed(1)} GB` : `${value} MB`;
  }
}
