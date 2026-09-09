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
        <p class="page-subtitle">Optimizer statistics verisini güncellik, değişim oranı ve sampling açısından yorumlayın.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> UPDATE STATISTICS otomatik çalıştırılmaz</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!items().length) {
      <mat-card class="empty panel"><mat-icon>analytics</mat-icon><h3>Henüz statistics telemetrisi yok</h3><p>Statistics Advisor ilk başarılı örnekten sonra kayıtları gösterecek.</p></mat-card>
    } @else {
      <div class="summary">
        <mat-card><span>İzlenen statistics</span><strong>{{ items().length }}</strong></mat-card>
        <mat-card><span>En yüksek değişim</span><strong>{{ maxModification() | number:'1.0-1' }}%</strong></mat-card>
        <mat-card><span>≥ %10 değişen</span><strong>{{ changedCount() }}</strong></mat-card>
        <mat-card><span>1M+ satırlı</span><strong>{{ largeCount() }}</strong></mat-card>
      </div>

      <div class="grid">
        @for (item of items(); track item.id) {
          <mat-card class="card">
            <div class="head">
              <div><div class="scope"><mat-icon>dns</mat-icon>{{ item.serverName }}</div><strong>{{ item.databaseName }} › {{ item.tableName }}</strong><span>{{ item.statisticsName }}</span></div>
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
              <div><span>Sample</span><strong>{{ item.samplePercent != null ? ((item.samplePercent | number:'1.0-1') + '%') : '—' }}</strong></div>
              <div><span>Örneklenen satır</span><strong>{{ item.rowsSampled | number }}</strong></div>
            </div>
            <div class="dates"><span>Son güncelleme <strong>{{ item.lastUpdated ? (item.lastUpdated | date:'dd.MM.yyyy HH:mm') : 'Bilinmiyor' }}</strong></span><span>Son ölçüm <strong>{{ item.capturedAt | date:'dd.MM.yyyy HH:mm' }}</strong></span></div>
          </mat-card>
        }
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:999px;padding:7px 10px;color:#64748b;font-size:.7rem;background:#fff}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.empty{min-height:240px;display:grid;place-items:center;text-align:center;padding:40px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#64748b}.empty h3,.empty p{margin:0}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:18px}.summary mat-card{padding:14px 16px;border:1px solid #e2e7ef;box-shadow:none}.summary span{display:block;color:#778297;font-size:.68rem}.summary strong{display:block;margin-top:5px;font-size:1.13rem}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(410px,1fr));gap:14px}.card{padding:17px;border:1px solid #e1e7f0;border-radius:15px;box-shadow:0 7px 24px rgba(15,23,42,.045)}.head{display:flex;justify-content:space-between;gap:14px}.head strong,.head span{display:block}.head strong{margin-top:4px;font-size:.82rem}.head span{font-size:.71rem;color:#748196;margin-top:3px}.scope{display:flex;align-items:center;gap:5px;color:#8290a3;font-size:.64rem}.scope mat-icon{font-size:14px;width:14px;height:14px}.level{background:#edf2f7;color:#536174;border-radius:9px;height:max-content;padding:7px 9px;font-weight:780}.level.medium{background:#fff5dd;color:#986700}.level.high{background:#ffeded;color:#aa3030}.diagnostic{margin-top:12px;padding:11px 12px;border:1px solid #dfe6f7;background:#f7f9ff;border-radius:10px}.diagnostic.high{background:#fff9f1;border-color:#f0dfca}.diagnostic-title{display:flex;align-items:center;gap:5px;color:#344d77;font-size:.7rem;font-weight:800}.diagnostic-title mat-icon{font-size:17px;width:17px;height:17px}.diagnostic p{margin:6px 0;color:#526177;font-size:.73rem;line-height:1.45}.inspect{display:flex;align-items:flex-start;gap:5px;color:#35506e;font-size:.68rem;font-weight:650}.inspect mat-icon{font-size:15px;width:15px;height:15px;color:#21805a}.metrics{display:grid;grid-template-columns:repeat(4,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5;margin:13px 0}.metrics div{text-align:center;padding:9px 5px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.61rem}.metrics strong{font-size:.79rem}.dates{display:flex;justify-content:space-between;gap:10px;font-size:.65rem;color:#8490a2}.dates span,.dates strong{display:block}.dates strong{color:#4e5c70;margin-top:2px;font-size:.68rem}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.grid{grid-template-columns:1fr}.heading,.dates{display:block}}
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

  maxModification(): number { return this.items().reduce((max, x) => Math.max(max, x.modificationPercent), 0); }
  changedCount(): number { return this.items().filter(x => x.modificationPercent >= 10).length; }
  largeCount(): number { return this.items().filter(x => x.rows >= 1_000_000).length; }
}
