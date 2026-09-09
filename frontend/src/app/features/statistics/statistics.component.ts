import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { switchMap, timer } from 'rxjs';
import { StatisticsStatus } from '../../core/models/statistics.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-statistics',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">İstatistikler</h1>
        <p class="page-subtitle">Optimizer statistics verisi freshness, sampling, NORECOMPUTE ve filtre bağlamıyla yorumlanır.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> UPDATE STATISTICS otomatik çalıştırılmaz</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!items().length) {
      <mat-card class="empty panel"><mat-icon>analytics</mat-icon><h3>Henüz statistics telemetrisi yok</h3><p>Statistics Advisor ilk başarılı örnekten sonra kayıtları gösterecek.</p></mat-card>
    } @else {
      <div class="summary">
        <mat-card><span>Yüksek / orta risk</span><strong>{{ riskCount() }}</strong><small>motor yorumu</small></mat-card>
        <mat-card><span>NORECOMPUTE</span><strong>{{ noRecomputeCount() }}</strong><small>auto-update engelli</small></mat-card>
        <mat-card><span>Düşük sample + büyük tablo</span><strong>{{ samplingReviewCount() }}</strong><small>inceleme adayı</small></mat-card>
        <mat-card><span>Filtered statistics</span><strong>{{ filteredCount() }}</strong><small>dar veri alt kümesi</small></mat-card>
      </div>

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
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:999px;padding:7px 10px;color:#64748b;font-size:.7rem;background:#fff}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.empty{min-height:240px;display:grid;place-items:center;text-align:center;padding:40px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#64748b}.empty h3,.empty p{margin:0}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:18px}.summary mat-card{padding:14px 16px;border:1px solid #e2e7ef;box-shadow:none}.summary span{display:block;color:#778297;font-size:.68rem}.summary strong{display:block;margin-top:5px;font-size:1.13rem}.summary small{display:block;margin-top:3px;color:#9aa4b4;font-size:.6rem}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(430px,1fr));gap:14px}.card{padding:17px;border:1px solid #e1e7f0;border-radius:15px;box-shadow:0 7px 24px rgba(15,23,42,.045)}.card.no-recompute{border-color:#f0c8c8}.head{display:flex;justify-content:space-between;gap:14px}.head strong,.head>div>span{display:block}.head strong{margin-top:4px;font-size:.82rem}.head>div>span{font-size:.71rem;color:#748196;margin-top:3px}.scope{display:flex;align-items:center;gap:5px;color:#8290a3;font-size:.64rem}.scope mat-icon{font-size:14px;width:14px;height:14px}.badges{display:flex;gap:6px;flex-wrap:wrap;margin-top:8px}.badges span{display:flex;align-items:center;gap:3px;border-radius:999px;padding:4px 7px;font-size:.58rem;font-weight:750}.badges mat-icon{font-size:13px;width:13px;height:13px}.badges .type{background:#edf1f7;color:#536279}.badges .danger{background:#ffe8e8;color:#a32b2b}.badges .filter{background:#e9efff;color:#3157a4}.badges .sample{background:#fff3df;color:#986000}.level{background:#edf2f7;color:#536174;border-radius:9px;height:max-content;padding:7px 9px;font-weight:780}.level.medium{background:#fff5dd;color:#986700}.level.high{background:#ffeded;color:#aa3030}.diagnostic{margin-top:12px;padding:11px 12px;border:1px solid #dfe6f7;background:#f7f9ff;border-radius:10px}.diagnostic.high{background:#fff9f1;border-color:#f0dfca}.diagnostic-title{display:flex;align-items:center;gap:5px;color:#344d77;font-size:.7rem;font-weight:800}.diagnostic-title mat-icon{font-size:17px;width:17px;height:17px}.diagnostic p{margin:6px 0;color:#526177;font-size:.73rem;line-height:1.45}.inspect{display:flex;align-items:flex-start;gap:5px;color:#35506e;font-size:.68rem;font-weight:650}.inspect mat-icon{font-size:15px;width:15px;height:15px;color:#21805a}.metrics{display:grid;grid-template-columns:repeat(5,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5;margin:13px 0}.metrics div{text-align:center;padding:9px 5px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.61rem}.metrics strong{font-size:.77rem}.dates{display:flex;justify-content:space-between;gap:10px;font-size:.65rem;color:#8490a2}.dates span,.dates strong{display:block}.dates strong{color:#4e5c70;margin-top:2px;font-size:.68rem}details{margin-top:11px;font-size:.68rem;color:#59687d}summary{cursor:pointer;font-weight:700}details code{display:block;margin-top:7px;padding:8px;background:#f5f7fa;border-radius:7px;white-space:normal;word-break:break-word}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.grid{grid-template-columns:1fr}.heading,.dates{display:block}.metrics{grid-template-columns:repeat(3,1fr)}}
  `]
})
export class StatisticsComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly items = signal<StatisticsStatus[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    timer(0, 600_000).pipe(
      switchMap(() => this.api.getStatisticsStatus(100)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: rows => { this.items.set(rows); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  riskCount(): number { return this.items().filter(x => x.diagnosticLevel === 'High' || x.diagnosticLevel === 'Medium').length; }
  noRecomputeCount(): number { return this.items().filter(x => x.noRecompute).length; }
  filteredCount(): number { return this.items().filter(x => x.hasFilter).length; }
  samplingReviewCount(): number { return this.items().filter(x => this.isLowSample(x) && x.modificationPercent >= 5).length; }
  isLowSample(item: StatisticsStatus): boolean { return item.rows >= 1_000_000 && item.samplePercent != null && item.samplePercent < 10; }
}
