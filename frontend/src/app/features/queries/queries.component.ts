import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { switchMap, timer } from 'rxjs';
import { QueryPerformance } from '../../core/models/query.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';
import { QueryPlanDialogComponent } from './query-plan-dialog.component';

@Component({
  selector: 'app-queries',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Pahalı Sorgular</h1>
        <p class="page-subtitle">Plan cache verisini motor yorumlar; yalnız sayı değil, baskın maliyet ve ilk inceleme yönü gösterilir.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> Salt okunur DMV analizi</div>
    </div>

    @if (planError()) {
      <div class="plan-error">
        <mat-icon>error_outline</mat-icon>
        <span>{{ planError() }}</span>
        <button type="button" (click)="planError.set(null)">Kapat</button>
      </div>
    }

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!queries().length) {
      <mat-card class="empty panel">
        <mat-icon>query_stats</mat-icon>
        <h3>Henüz sorgu telemetrisi yok</h3>
        <p>Query Performance collector ilk başarılı örneğini aldıktan sonra sorgular burada görünür.</p>
      </mat-card>
    } @else {
      <div class="summary">
        <mat-card><span>İzlenen sorgu</span><strong>{{ queries().length }}</strong></mat-card>
        <mat-card><span>En yüksek etki</span><strong>{{ queries()[0]?.impactScore | number:'1.0-1' }}</strong></mat-card>
        <mat-card><span>Toplam CPU</span><strong>{{ totalCpu() | number:'1.0-0' }} ms</strong></mat-card>
        <mat-card><span>Toplam logical read</span><strong>{{ totalReads() | number:'1.0-0' }}</strong></mat-card>
      </div>

      <div class="query-list">
        @for (query of queries(); track query.queryId) {
          <mat-card class="query-card">
            <div class="query-head">
              <div class="identity-block">
                <div class="identity">
                  <strong>{{ query.databaseName }}</strong>
                  @if (query.objectName) { <span>{{ query.objectName }}</span> }
                </div>
                <div class="scope"><mat-icon>dns</mat-icon>{{ query.serverName }} <span>·</span> {{ query.diagnosticHeadline }}</div>
              </div>
              <div class="impact" [class.high]="query.impactScore >= 70" [class.medium]="query.impactScore >= 50 && query.impactScore < 70">
                <strong>{{ query.impactScore | number:'1.0-1' }}</strong><span>ETKİ</span>
              </div>
            </div>

            <div class="diagnostic" [class.high]="query.diagnosticLevel === 'High' || query.diagnosticLevel === 'Critical'">
              <div class="diagnostic-icon"><mat-icon>psychology</mat-icon></div>
              <div>
                <strong>Motor yorumu</strong>
                <p>{{ query.diagnosticSummary }}</p>
                <div class="inspect"><mat-icon>checklist</mat-icon><span>{{ query.suggestedInspection }}</span></div>
              </div>
            </div>

            <div class="metrics">
              <div><span>Çalışma sayısı</span><strong>{{ query.executionCount | number }}</strong></div>
              <div><span>Ort. CPU</span><strong>{{ query.averageCpuMs | number:'1.0-1' }} ms</strong></div>
              <div><span>Ort. süre</span><strong>{{ query.averageDurationMs | number:'1.0-1' }} ms</strong></div>
              <div><span>Ort. logical read</span><strong>{{ query.averageLogicalReads | number:'1.0-0' }}</strong></div>
              <div><span>Toplam CPU</span><strong>{{ query.totalCpuMs | number:'1.0-0' }} ms</strong></div>
            </div>

            <details class="sql-box">
              <summary><mat-icon>code</mat-icon> SQL metnini göster</summary>
              <pre class="sql">{{ query.statementText }}</pre>
            </details>

            <div class="footer">
              <div>
                <span>Son çalışma</span>
                <strong>{{ query.lastExecutionTime ? (query.lastExecutionTime | date:'dd.MM.yyyy HH:mm:ss') : '—' }}</strong>
              </div>
              <div class="footer-actions">
                <span class="hash">{{ query.queryHash }}</span>
                @if (query.planId) {
                  <button mat-stroked-button type="button" [disabled]="planLoading() === query.planId" (click)="showPlan(query)">
                    <mat-icon>account_tree</mat-icon>{{ planLoading() === query.planId ? 'Yükleniyor' : 'Execution Planı Gör' }}
                  </button>
                } @else {
                  <span class="no-plan"><mat-icon>info</mat-icon>Plan XML yakalanmadı</span>
                }
              </div>
            </div>
          </mat-card>
        }
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:999px;padding:7px 10px;color:#64748b;font-size:.72rem;background:#fff}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.empty{min-height:240px;display:grid;place-items:center;text-align:center;padding:40px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#64748b}.empty h3,.empty p{margin:0}.empty p{color:#718096}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:16px}.summary mat-card{padding:14px 16px;border:1px solid #e2e7ef;box-shadow:none}.summary span{display:block;color:#778297;font-size:.68rem}.summary strong{display:block;margin-top:5px;font-size:1.13rem}.query-list{display:grid;gap:14px}.query-card{padding:18px;border:1px solid #e1e7f0;border-radius:15px;box-shadow:0 7px 24px rgba(15,23,42,.045)}.query-head{display:flex;justify-content:space-between;gap:16px}.identity{display:flex;gap:8px;align-items:center}.identity span{background:#eef2f7;border-radius:6px;padding:3px 7px;font-size:.7rem;color:#526074}.scope{display:flex;align-items:center;gap:5px;color:#7d899b;font-size:.69rem;margin-top:5px}.scope mat-icon{font-size:15px;width:15px;height:15px}.scope span{color:#c2c9d3}.impact{min-width:70px;text-align:center;border-radius:10px;padding:7px 9px;background:#eef2f7}.impact strong{display:block;font-size:1.2rem}.impact span{font-size:.56rem;letter-spacing:.08em}.impact.medium{background:#fff6de;color:#9b6900}.impact.high{background:#ffeded;color:#aa3030}.diagnostic{display:grid;grid-template-columns:34px 1fr;gap:10px;margin:14px 0 0;padding:12px 13px;border:1px solid #dfe6f7;background:#f7f9ff;border-radius:11px}.diagnostic.high{border-color:#f1dcc8;background:#fffaf3}.diagnostic-icon{width:32px;height:32px;border-radius:8px;display:grid;place-items:center;background:#e7edff;color:#3453b8}.diagnostic-icon mat-icon{font-size:18px;width:18px;height:18px}.diagnostic strong{font-size:.72rem}.diagnostic p{margin:4px 0 7px;color:#526177;font-size:.76rem;line-height:1.45}.inspect{display:flex;align-items:flex-start;gap:5px;color:#35506e;font-size:.7rem;font-weight:650}.inspect mat-icon{font-size:16px;width:16px;height:16px;color:#23805a}.metrics{display:grid;grid-template-columns:repeat(5,1fr);margin:14px 0;border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5}.metrics div{text-align:center;padding:11px 7px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.63rem;margin-bottom:4px}.metrics strong{font-size:.84rem}.sql-box{border:0;margin-top:3px}.sql-box summary{display:flex;align-items:center;gap:5px;width:max-content;cursor:pointer;color:#59687d;font-size:.7rem;font-weight:700}.sql-box summary mat-icon{font-size:16px;width:16px;height:16px}.sql{max-height:190px;overflow:auto;white-space:pre-wrap;background:#0f172a;color:#e5e7eb;border-radius:9px;padding:12px;font-size:.73rem;line-height:1.45}.footer{display:flex;justify-content:space-between;align-items:center;gap:12px;border-top:1px solid #edf0f5;margin-top:12px;padding-top:11px}.footer>div>span,.footer>div>strong{display:block}.footer>div>span{font-size:.61rem;color:#8b96a6}.footer>div>strong{font-size:.7rem;color:#4d5b70;margin-top:2px}.footer-actions{display:flex;align-items:center;gap:9px}.hash{max-width:220px;overflow:hidden;text-overflow:ellipsis;font-family:monospace;font-size:.62rem!important;color:#9aa4b3!important}.no-plan{display:flex!important;align-items:center;gap:4px;color:#8b96a6!important;font-size:.66rem!important}.no-plan mat-icon{font-size:15px;width:15px;height:15px}.plan-error{display:flex;align-items:center;gap:8px;margin:-4px 0 14px;padding:10px 12px;border:1px solid #efcaca;background:#fff3f3;color:#9d2c2c;border-radius:10px;font-size:.76rem}.plan-error mat-icon{font-size:18px;width:18px;height:18px}.plan-error span{flex:1}.plan-error button{border:0;background:transparent;color:#9d2c2c;font-weight:700;cursor:pointer}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.metrics{grid-template-columns:repeat(2,1fr)}.footer,.heading{display:block}.footer-actions{margin-top:10px;justify-content:space-between}.hash{display:none}}
  `]
})
export class QueriesComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  readonly queries = signal<QueryPerformance[]>([]);
  readonly planLoading = signal<number | null>(null);
  readonly planError = signal<string | null>(null);
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
    if (!query.planId) {
      this.planError.set('Bu sorgu için kullanılabilir execution plan XML yakalanmamış. Collector yeni plan yakaladığında tekrar deneyin.');
      return;
    }

    this.planError.set(null);
    this.planLoading.set(query.planId);
    this.api.getQueryPlan(query.planId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: plan => {
        this.planLoading.set(null);
        this.dialog.open(QueryPlanDialogComponent, {
          data: { query, plan },
          width: '1180px',
          maxWidth: '96vw',
          height: '820px',
          maxHeight: '92vh',
          autoFocus: false,
          restoreFocus: true
        });
      },
      error: error => {
        this.planLoading.set(null);
        this.planError.set(error?.error?.detail || 'Execution plan yüklenemedi. Plan cache kaydı artık mevcut olmayabilir.');
      }
    });
  }
}
