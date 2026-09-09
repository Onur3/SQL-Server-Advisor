import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { timer, switchMap } from 'rxjs';
import { QueryPerformance, QueryPlan } from '../../core/models/query.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-queries',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Sorgu Performansı</h1>
        <p class="page-subtitle">Plan cache üzerinden en maliyetli sorgular. Liste impact score ile sıralanır.</p>
      </div>
      <div class="readonly"><mat-icon>lock</mat-icon> Salt okunur DMV analizi</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!queries().length) {
      <mat-card class="empty">
        <mat-icon>query_stats</mat-icon>
        <h3>Henüz query telemetry yok</h3>
        <p>QueryPerformance collector ilk başarılı örneğini aldıktan sonra sorgular burada görünecek.</p>
      </mat-card>
    } @else {
      <div class="summary">
        <mat-card><span>İzlenen sorgu</span><strong>{{ queries().length }}</strong></mat-card>
        <mat-card><span>En yüksek impact</span><strong>{{ queries()[0]?.impactScore | number:'1.0-1' }}</strong></mat-card>
        <mat-card><span>Toplam CPU</span><strong>{{ totalCpu() | number:'1.0-0' }} ms</strong></mat-card>
        <mat-card><span>Toplam logical read</span><strong>{{ totalReads() | number:'1.0-0' }}</strong></mat-card>
      </div>

      <div class="query-list">
        @for (query of queries(); track query.queryId) {
          <mat-card class="query-card">
            <div class="query-head">
              <div>
                <div class="identity">
                  <strong>{{ query.databaseName }}</strong>
                  @if (query.objectName) { <span>{{ query.objectName }}</span> }
                </div>
                <div class="hash">{{ query.queryHash }}</div>
              </div>
              <div class="impact" [class.high]="query.impactScore >= 70" [class.medium]="query.impactScore >= 50 && query.impactScore < 70">
                <strong>{{ query.impactScore | number:'1.0-1' }}</strong><span>IMPACT</span>
              </div>
            </div>

            <div class="metrics">
              <div><span>Execution</span><strong>{{ query.executionCount | number }}</strong></div>
              <div><span>Avg CPU</span><strong>{{ query.averageCpuMs | number:'1.0-1' }} ms</strong></div>
              <div><span>Avg Süre</span><strong>{{ query.averageDurationMs | number:'1.0-1' }} ms</strong></div>
              <div><span>Avg Read</span><strong>{{ query.averageLogicalReads | number:'1.0-0' }}</strong></div>
              <div><span>Total CPU</span><strong>{{ query.totalCpuMs | number:'1.0-0' }} ms</strong></div>
            </div>

            <pre class="sql">{{ query.statementText }}</pre>

            <div class="footer">
              <span>Son çalışma: {{ query.lastExecutionTime ? (query.lastExecutionTime | date:'dd.MM.yyyy HH:mm:ss') : '—' }}</span>
              <button mat-stroked-button [disabled]="!query.planId || planLoading() === query.queryId" (click)="showPlan(query)">
                <mat-icon>account_tree</mat-icon>
                {{ planLoading() === query.queryId ? 'Yükleniyor' : 'Execution Plan' }}
              </button>
            </div>
          </mat-card>
        }
      </div>
    }

    @if (selectedPlan(); as plan) {
      <mat-card class="plan-panel">
        <div class="plan-head">
          <div><strong>Execution Plan XML</strong><span>{{ plan.planHash }}</span></div>
          <button mat-button (click)="selectedPlan.set(null)">Kapat</button>
        </div>
        <pre>{{ plan.planXml }}</pre>
      </mat-card>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:8px;padding:7px 10px;color:#64748b;font-size:.75rem}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.empty{min-height:240px;display:grid;place-items:center;text-align:center;padding:40px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#64748b}.empty h3,.empty p{margin:0}.empty p{color:#718096}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:16px}.summary mat-card{padding:14px 16px}.summary span{display:block;color:#778297;font-size:.72rem}.summary strong{display:block;margin-top:5px;font-size:1.15rem}.query-list{display:grid;gap:14px}.query-card{padding:18px;border:1px solid #e3e7ef;border-radius:14px;box-shadow:0 3px 18px rgba(20,32,55,.05)}.query-head{display:flex;justify-content:space-between;gap:16px}.identity{display:flex;gap:8px;align-items:center}.identity span{background:#eef2f7;border-radius:6px;padding:3px 7px;font-size:.72rem;color:#526074}.hash{font-family:monospace;font-size:.7rem;color:#8a95a7;margin-top:6px}.impact{min-width:70px;text-align:center;border-radius:9px;padding:7px 9px;background:#eef2f7}.impact strong{display:block;font-size:1.2rem}.impact span{font-size:.58rem;letter-spacing:.08em}.impact.medium{background:#fff6de;color:#9b6900}.impact.high{background:#ffeded;color:#aa3030}.metrics{display:grid;grid-template-columns:repeat(5,1fr);margin:15px 0;border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5}.metrics div{text-align:center;padding:11px 7px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.67rem;margin-bottom:4px}.metrics strong{font-size:.88rem}.sql{max-height:180px;overflow:auto;white-space:pre-wrap;background:#111827;color:#e5e7eb;border-radius:9px;padding:12px;font-size:.76rem;line-height:1.45}.footer{display:flex;justify-content:space-between;align-items:center;gap:12px;color:#7c8799;font-size:.72rem;margin-top:12px}.plan-panel{margin-top:18px;padding:16px}.plan-head{display:flex;justify-content:space-between;gap:14px;align-items:center}.plan-head span{display:block;color:#7c8799;font-family:monospace;font-size:.72rem;margin-top:4px}.plan-panel pre{max-height:520px;overflow:auto;background:#0f172a;color:#e2e8f0;padding:14px;border-radius:9px;font-size:.72rem;white-space:pre-wrap}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.metrics{grid-template-columns:repeat(2,1fr)}.footer,.heading{display:block}.footer button{margin-top:10px}}
  `]
})
export class QueriesComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly queries = signal<QueryPerformance[]>([]);
  readonly selectedPlan = signal<QueryPlan | null>(null);
  readonly planLoading = signal<number | null>(null);
  readonly loading = signal(true);

  ngOnInit(): void {
    timer(0, 60_000).pipe(
      switchMap(() => this.api.getQueryPerformance(100)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: rows => {
        this.queries.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  totalCpu(): number {
    return this.queries().reduce((sum, x) => sum + x.totalCpuMs, 0);
  }

  totalReads(): number {
    return this.queries().reduce((sum, x) => sum + x.totalLogicalReads, 0);
  }

  showPlan(query: QueryPerformance): void {
    if (!query.planId) return;
    this.planLoading.set(query.queryId);
    this.api.getQueryPlan(query.queryId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: plan => {
        this.selectedPlan.set(plan);
        this.planLoading.set(null);
      },
      error: () => this.planLoading.set(null)
    });
  }
}
