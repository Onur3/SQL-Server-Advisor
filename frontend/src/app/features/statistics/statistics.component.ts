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
        <h1 class="page-title">Statistics Advisor</h1>
        <p class="page-subtitle">Statistics freshness, modification ratio ve sampling görünümü.</p>
      </div>
      <div class="readonly"><mat-icon>lock</mat-icon> UPDATE STATISTICS çalıştırılmaz</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!items().length) {
      <mat-card class="empty"><mat-icon>analytics</mat-icon><h3>Henüz statistics telemetry yok</h3><p>StatisticsAdvisor ilk başarılı örnekten sonra kayıtları gösterecek.</p></mat-card>
    } @else {
      <div class="summary">
        <mat-card><span>Statistics</span><strong>{{ items().length }}</strong></mat-card>
        <mat-card><span>En yüksek değişim</span><strong>{{ maxModification() | number:'1.0-1' }}%</strong></mat-card>
        <mat-card><span>≥ %10 değişen</span><strong>{{ changedCount() }}</strong></mat-card>
        <mat-card><span>1M+ satır stats</span><strong>{{ largeCount() }}</strong></mat-card>
      </div>

      <div class="grid">
        @for (item of items(); track item.id) {
          <mat-card class="card">
            <div class="head">
              <div><strong>{{ item.databaseName }}</strong><span>{{ item.tableName }}</span><small>{{ item.statisticsName }}</small></div>
              <div class="badge" [class.high]="item.modificationPercent >= 20">{{ item.modificationPercent | number:'1.0-1' }}%</div>
            </div>
            <div class="metrics">
              <div><span>Rows</span><strong>{{ item.rows | number }}</strong></div>
              <div><span>Modified</span><strong>{{ item.modificationCounter | number }}</strong></div>
              <div><span>Sample</span><strong>{{ item.samplePercent != null ? ((item.samplePercent | number:'1.0-1') + '%') : '—' }}</strong></div>
              <div><span>Rows Sampled</span><strong>{{ item.rowsSampled | number }}</strong></div>
            </div>
            <div class="dates">
              <span>Last updated: {{ item.lastUpdated ? (item.lastUpdated | date:'dd.MM.yyyy HH:mm') : 'Bilinmiyor' }}</span>
              <span>Captured: {{ item.capturedAt | date:'dd.MM.yyyy HH:mm' }}</span>
            </div>
          </mat-card>
        }
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:8px;padding:7px 10px;color:#64748b;font-size:.75rem}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.empty{min-height:240px;display:grid;place-items:center;text-align:center;padding:40px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#64748b}.empty h3,.empty p{margin:0}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:18px}.summary mat-card{padding:14px 16px}.summary span{display:block;color:#778297;font-size:.72rem}.summary strong{display:block;margin-top:5px;font-size:1.15rem}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(400px,1fr));gap:14px}.card{padding:16px;border:1px solid #e3e7ef;border-radius:14px;box-shadow:0 3px 18px rgba(20,32,55,.05)}.head{display:flex;justify-content:space-between;gap:14px}.head strong,.head span,.head small{display:block}.head span{font-size:.78rem;color:#5d697c;margin-top:3px}.head small{font-size:.7rem;color:#8b95a6;margin-top:3px}.badge{background:#fff6de;color:#986700;border-radius:8px;height:max-content;padding:7px 9px;font-weight:750}.badge.high{background:#ffeded;color:#aa3030}.metrics{display:grid;grid-template-columns:repeat(4,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5;margin:14px 0}.metrics div{text-align:center;padding:10px 5px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.65rem}.metrics strong{font-size:.82rem}.dates{display:flex;justify-content:space-between;gap:10px;font-size:.7rem;color:#7c8799}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.grid{grid-template-columns:1fr}.heading,.dates{display:block}}
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
      next: rows => {
        this.items.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  maxModification(): number {
    return this.items().reduce((max, x) => Math.max(max, x.modificationPercent), 0);
  }

  changedCount(): number {
    return this.items().filter(x => x.modificationPercent >= 10).length;
  }

  largeCount(): number {
    return this.items().filter(x => x.rows >= 1_000_000).length;
  }
}
