import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin, switchMap, timer } from 'rxjs';
import { RecommendationListItem } from '../../core/models/analysis.models';
import { CollectorCoverage } from '../../core/models/collector.models';
import { StatisticsStatus } from '../../core/models/statistics.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-statistics',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">İstatistik Analizi</h1>
        <p class="page-subtitle">Optimizer statistics verisi freshness, sampling, NORECOMPUTE, persisted sample ve redundant statistics kurallarıyla analiz edilir.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> UPDATE / DROP STATISTICS otomatik çalıştırılmaz</div>
    </div>

    @if (coverageWarnings().length) {
      <div class="coverage-warning">
        <mat-icon>database</mat-icon>
        <div>
          <strong>Statistics analizi tüm veritabanlarını kapsamıyor</strong>
          <p>Monitoring hesabının bazı veritabanlarında metadata/DMV yetkisi yok. Bu DB'lerin statistics kayıtları sonuçlarda eksik olabilir.</p>
          @for (row of coverageWarnings(); track row.serverProfileId) {
            <details><summary>{{ row.serverName }} · ayrıntıyı göster</summary><pre>{{ row.warningMessage }}</pre></details>
          }
        </div>
      </div>
    }

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else {
      <div class="summary">
        <mat-card><span>Yüksek / orta risk</span><strong>{{ riskCount() }}</strong><small>statistics motoru</small></mat-card>
        <mat-card><span>Öneri</span><strong>{{ statisticsRecommendations().length }}</strong><small>DBA aksiyon adayı</small></mat-card>
        <mat-card><span>NORECOMPUTE</span><strong>{{ noRecomputeCount() }}</strong><small>auto-update engelli</small></mat-card>
        <mat-card><span>Düşük sample</span><strong>{{ samplingReviewCount() }}</strong><small>büyük tablo incelemesi</small></mat-card>
        <mat-card><span>Filtered statistics</span><strong>{{ filteredCount() }}</strong><small>dar veri alt kümesi</small></mat-card>
      </div>

      <section class="advice-section">
        <div class="section-title">
          <div><h2>Statistics Aksiyon ve Önerileri</h2><p>STATS-001…005 bulguları recommendation motoruyla eşleştirilir. Scriptler yalnız tanı/inceleme içindir; Advisor üretimde otomatik DDL çalıştırmaz.</p></div>
          <span>{{ statisticsRecommendations().length }} öneri</span>
        </div>

        @if (!statisticsRecommendations().length) {
          <mat-card class="empty compact"><mat-icon>task_alt</mat-icon><div><h3>Aktif statistics önerisi yok</h3><p>Mevcut snapshot üzerinde STATS kuralları aksiyon gerektiren bir bulgu üretmemiş.</p></div></mat-card>
        } @else {
          <div class="advice-list">
            @for (rec of statisticsRecommendations(); track rec.id) {
              <mat-card class="advice-card" [class.high]="rec.severity >= 3">
                <div class="advice-head">
                  <div>
                    <div class="scope"><mat-icon>dns</mat-icon>{{ rec.serverName }}</div>
                    <h3>{{ rec.title }}</h3>
                    <div class="badges"><span class="rule">{{ rec.ruleId }}</span><span>{{ rec.databaseName || 'Sunucu' }}</span><span>Güven {{ rec.confidenceScore | number:'1.0-0' }}%</span><span>Risk {{ rec.riskLevel }}</span><span>Fayda {{ rec.expectedBenefit }}</span></div>
                  </div>
                  <div class="priority">{{ rec.priorityScore | number:'1.0-0' }}</div>
                </div>
                <div class="finding"><strong>Analiz</strong><p>{{ rec.whatWasFound }}</p><small>{{ rec.whyItMatters }}</small></div>
                <div class="action"><strong><mat-icon>tips_and_updates</mat-icon>Önerilen DBA aksiyonu</strong><p>{{ rec.recommendedAction }}</p></div>
                @if (rec.scriptText) {
                  <details><summary>Salt-okunur doğrulama SQL'ini göster</summary><pre>{{ rec.scriptText }}</pre></details>
                }
              </mat-card>
            }
          </div>
        }
      </section>

      <section>
        <div class="section-title">
          <div><h2>Statistics Snapshot Detayı</h2><p>Her statistics nesnesinin güncellik, değişim ve sample durumu ayrı yorumlanır.</p></div>
          <span>{{ items().length }} kayıt</span>
        </div>

        @if (!items().length) {
          <mat-card class="empty panel"><mat-icon>analytics</mat-icon><h3>Henüz statistics telemetrisi yok</h3><p>Statistics Advisor ilk başarılı örnekten sonra kayıtları gösterecek.</p></mat-card>
        } @else {
          <div class="grid">
            @for (item of items(); track item.id) {
              <mat-card class="card" [class.no-recompute]="item.noRecompute">
                <div class="head">
                  <div>
                    <div class="scope"><mat-icon>dns</mat-icon>{{ item.serverName }}</div>
                    <strong>{{ item.databaseName }} › {{ item.tableName }}</strong>
                    <span>{{ item.statisticsName }}</span>
                    <div class="badges">
                      <span class="type">{{ item.statisticsType }}</span>
                      @if (item.noRecompute) { <span class="danger"><mat-icon>pause_circle</mat-icon>NORECOMPUTE</span> }
                      @if (item.hasFilter) { <span class="filter"><mat-icon>filter_alt</mat-icon>FILTERED</span> }
                      @if (isLowSample(item)) { <span class="sample"><mat-icon>science</mat-icon>DÜŞÜK SAMPLE</span> }
                    </div>
                  </div>
                  <div class="level" [class.high]="item.diagnosticLevel === 'High'" [class.medium]="item.diagnosticLevel === 'Medium'">{{ item.modificationPercent | number:'1.0-1' }}%</div>
                </div>

                <div class="diagnostic" [class.high]="item.diagnosticLevel === 'High'">
                  <div class="diagnostic-title"><mat-icon>psychology</mat-icon>{{ item.diagnosticHeadline }}</div>
                  <p>{{ item.diagnosticSummary }}</p>
                  <div class="inspect"><mat-icon>checklist</mat-icon>{{ item.suggestedInspection }}</div>
                </div>

                <div class="metrics">
                  <div><span>Satır</span><strong>{{ item.rows | number }}</strong></div>
                  <div><span>Değişen</span><strong>{{ item.modificationCounter | number }}</strong></div>
                  <div><span>Değişim</span><strong>{{ item.modificationPercent | number:'1.0-1' }}%</strong></div>
                  <div><span>Sample</span><strong>{{ item.samplePercent != null ? ((item.samplePercent | number:'1.0-2') + '%') : '—' }}</strong></div>
                  <div><span>Örneklenen</span><strong>{{ item.rowsSampled | number }}</strong></div>
                </div>

                <div class="dates">
                  <span>Son güncelleme <strong>{{ item.lastUpdated ? (item.lastUpdated | date:'dd.MM.yyyy HH:mm') : 'Bilinmiyor' }}</strong></span>
                  <span>Son ölçüm <strong>{{ item.capturedAt | date:'dd.MM.yyyy HH:mm' }}</strong></span>
                </div>

                @if (item.filterDefinition) {
                  <details><summary>Statistics filtresini göster</summary><code>{{ item.filterDefinition }}</code></details>
                }
              </mat-card>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:999px;padding:7px 10px;color:#64748b;font-size:.7rem;background:#fff}.readonly mat-icon{font-size:16px;width:16px;height:16px}.coverage-warning{display:flex;gap:11px;margin:-5px 0 18px;padding:13px 15px;border:1px solid #f2d5a2;background:#fff8e8;border-radius:12px;color:#744b00}.coverage-warning>mat-icon{margin-top:1px}.coverage-warning strong{font-size:.8rem}.coverage-warning p{margin:4px 0 7px;font-size:.7rem;color:#81632a}.coverage-warning details{margin-top:5px}.coverage-warning summary{cursor:pointer;font-size:.68rem;font-weight:700}.coverage-warning pre{white-space:pre-wrap;margin:6px 0 0;padding:8px 9px;background:#fff;border:1px solid #f1dfbd;border-radius:7px;color:#6a542d;font-size:.65rem}.loading{height:240px;display:grid;place-items:center}.summary{display:grid;grid-template-columns:repeat(5,1fr);gap:12px;margin-bottom:22px}.summary mat-card{padding:14px 16px;border:1px solid #e2e7ef;box-shadow:none}.summary span{display:block;color:#778297;font-size:.68rem}.summary strong{display:block;margin-top:5px;font-size:1.13rem}.summary small{display:block;margin-top:3px;color:#9aa4b4;font-size:.6rem}section{margin-top:26px}.section-title{display:flex;justify-content:space-between;align-items:flex-end;margin-bottom:10px}.section-title h2{margin:0;font-size:1rem}.section-title p{margin:4px 0 0;color:#7c899b;font-size:.68rem;max-width:950px}.section-title>span{font-size:.65rem;color:#78869a;background:#edf1f6;border-radius:999px;padding:5px 8px}.empty{min-height:180px;display:grid;place-items:center;text-align:center;padding:28px}.empty.compact{min-height:0;display:flex;justify-content:flex-start;text-align:left;gap:10px}.empty mat-icon{color:#64748b}.empty h3,.empty p{margin:0}.empty p{margin-top:3px;color:#7b8798;font-size:.7rem}.advice-list{display:grid;gap:12px}.advice-card{padding:16px;border:1px solid #dfe5ef;border-left:4px solid #d4932d;border-radius:14px;box-shadow:0 6px 22px rgba(15,23,42,.04)}.advice-card.high{border-left-color:#c94848}.advice-head{display:flex;justify-content:space-between;gap:12px}.advice-head h3{margin:5px 0 7px;font-size:.86rem}.priority{height:max-content;min-width:44px;text-align:center;padding:7px;background:#f1f4f8;border-radius:9px;font-weight:800}.finding,.action{margin-top:10px;padding:10px 11px;border-radius:9px}.finding{background:#f7f9fc}.action{background:#eff8f3;border:1px solid #d7ebdf}.finding strong,.action strong{font-size:.68rem}.finding p,.action p{margin:5px 0 0;color:#526176;font-size:.72rem;line-height:1.45}.finding small{display:block;margin-top:4px;color:#748196;font-size:.64rem}.action strong{display:flex;align-items:center;gap:5px;color:#1e6f4e}.action mat-icon{font-size:16px;width:16px;height:16px}.scope{display:flex;align-items:center;gap:5px;color:#8290a3;font-size:.64rem}.scope mat-icon{font-size:14px;width:14px;height:14px}.badges{display:flex;gap:6px;flex-wrap:wrap;margin-top:8px}.badges span{display:flex;align-items:center;gap:3px;border-radius:999px;padding:4px 7px;font-size:.58rem;font-weight:750;background:#edf1f7;color:#536279}.badges .rule{background:#e9efff;color:#3157a4}.badges mat-icon{font-size:13px;width:13px;height:13px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(430px,1fr));gap:14px}.card{padding:17px;border:1px solid #e1e7f0;border-radius:15px;box-shadow:0 7px 24px rgba(15,23,42,.045)}.card.no-recompute{border-color:#f0c8c8}.head{display:flex;justify-content:space-between;gap:14px}.head strong,.head>div>span{display:block}.head strong{margin-top:4px;font-size:.82rem}.head>div>span{font-size:.71rem;color:#748196;margin-top:3px}.badges .type{background:#edf1f7;color:#536279}.badges .danger{background:#ffe8e8;color:#a32b2b}.badges .filter{background:#e9efff;color:#3157a4}.badges .sample{background:#fff3df;color:#986000}.level{background:#edf2f7;color:#536174;border-radius:9px;height:max-content;padding:7px 9px;font-weight:780}.level.medium{background:#fff5dd;color:#986700}.level.high{background:#ffeded;color:#aa3030}.diagnostic{margin-top:12px;padding:11px 12px;border:1px solid #dfe6f7;background:#f7f9ff;border-radius:10px}.diagnostic.high{background:#fff9f1;border-color:#f0dfca}.diagnostic-title{display:flex;align-items:center;gap:5px;color:#344d77;font-size:.7rem;font-weight:800}.diagnostic-title mat-icon{font-size:17px;width:17px;height:17px}.diagnostic p{margin:6px 0;color:#526177;font-size:.73rem;line-height:1.45}.inspect{display:flex;align-items:flex-start;gap:5px;color:#35506e;font-size:.68rem;font-weight:650}.inspect mat-icon{font-size:15px;width:15px;height:15px;color:#21805a}.metrics{display:grid;grid-template-columns:repeat(5,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5;margin:13px 0}.metrics div{text-align:center;padding:9px 5px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.61rem}.metrics strong{font-size:.77rem}.dates{display:flex;justify-content:space-between;gap:10px;font-size:.65rem;color:#8490a2}.dates span,.dates strong{display:block}.dates strong{color:#4e5c70;margin-top:2px;font-size:.68rem}details{margin-top:11px;font-size:.68rem;color:#59687d}summary{cursor:pointer;font-weight:700}details code,details pre{display:block;margin-top:7px;padding:9px;background:#f5f7fa;border-radius:7px;white-space:pre-wrap;word-break:break-word;font-size:.65rem}@media(max-width:1100px){.summary{grid-template-columns:repeat(3,1fr)}}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.grid{grid-template-columns:1fr}.heading,.dates{display:block}.metrics{grid-template-columns:repeat(3,1fr)}}@media(max-width:600px){.summary{grid-template-columns:1fr}.advice-head{display:block}}
  `]
})
export class StatisticsComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly items = signal<StatisticsStatus[]>([]);
  readonly recommendations = signal<RecommendationListItem[]>([]);
  readonly coverage = signal<CollectorCoverage[]>([]);
  readonly loading = signal(true);
  readonly coverageWarnings = () => this.coverage().filter(x => x.status === 'Warning' || x.status === 'Failed');
  readonly statisticsRecommendations = () => this.recommendations()
    .filter(x => x.category === 'Statistics')
    .sort((a, b) => b.priorityScore - a.priorityScore)
    .slice(0, 30);

  ngOnInit(): void {
    timer(0, 600_000).pipe(
      switchMap(() => forkJoin({
        items: this.api.getStatisticsStatus(150),
        recommendations: this.api.getRecommendations('All'),
        coverage: this.api.getCollectorCoverage('StatisticsAdvisor')
      })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.items.set(result.items);
        this.recommendations.set(result.recommendations);
        this.coverage.set(result.coverage);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  riskCount(): number { return this.items().filter(x => x.diagnosticLevel === 'High' || x.diagnosticLevel === 'Medium').length; }
  noRecomputeCount(): number { return this.items().filter(x => x.noRecompute).length; }
  filteredCount(): number { return this.items().filter(x => x.hasFilter).length; }
  samplingReviewCount(): number { return this.items().filter(x => this.isLowSample(x) && x.modificationPercent >= 5).length; }
  isLowSample(item: StatisticsStatus): boolean { return item.rows >= 1_000_000 && item.samplePercent != null && item.samplePercent < 10; }
}
