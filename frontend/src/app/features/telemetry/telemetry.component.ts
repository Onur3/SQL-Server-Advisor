import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin, switchMap, timer } from 'rxjs';
import { BlockingTelemetry, WaitTelemetry } from '../../core/models/telemetry.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-telemetry',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Wait & Blocking</h1>
        <p class="page-subtitle">Son 15 dakikadaki wait delta'ları ve blocking zincirleri. Collector her 30 saniyede günceller.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> Salt okunur</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else {
      <div class="summary-grid">
        <mat-card class="summary-card">
          <span>Wait örneği</span>
          <strong>{{ waits().length }}</strong>
          <small>delta &gt; 0</small>
        </mat-card>
        <mat-card class="summary-card" [class.alert]="blocking().length > 0">
          <span>Blocking kaydı</span>
          <strong>{{ blocking().length }}</strong>
          <small>son 15 dakika</small>
        </mat-card>
        <mat-card class="summary-card">
          <span>En yüksek wait delta</span>
          <strong>{{ formatMs(maxWaitDelta()) }}</strong>
          <small>{{ topWaitType() || '—' }}</small>
        </mat-card>
      </div>

      <mat-card class="panel">
        <div class="panel-head">
          <div><h2>Wait Stats</h2><p>Kümülatif DMV sayacından hesaplanan örnekleme deltaları.</p></div>
        </div>
        @if (!waits().length) {
          <div class="empty"><mat-icon>check_circle</mat-icon> Son 15 dakikada delta wait kaydı yok.</div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead><tr><th>Zaman</th><th>Sunucu</th><th>Wait Type</th><th>Delta</th><th>Signal Delta</th><th>Tasks</th></tr></thead>
              <tbody>
                @for (row of waits(); track row.capturedAt + row.serverProfileId + row.waitType) {
                  <tr>
                    <td>{{ row.capturedAt | date:'dd.MM HH:mm:ss' }}</td>
                    <td>{{ row.serverName }}</td>
                    <td><code>{{ row.waitType }}</code></td>
                    <td><strong>{{ formatMs(row.deltaWaitTimeMs) }}</strong></td>
                    <td>{{ formatMs(row.deltaSignalWaitTimeMs) }}</td>
                    <td>{{ row.waitingTasks }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </mat-card>

      <mat-card class="panel blocking-panel">
        <div class="panel-head">
          <div><h2>Blocking Detail</h2><p>Blocked request → blocker session ilişkisi ve çalışan SQL.</p></div>
        </div>
        @if (!blocking().length) {
          <div class="empty"><mat-icon>check_circle</mat-icon> Son 15 dakikada blocking kaydı yok.</div>
        } @else {
          <div class="blocking-list">
            @for (row of blocking(); track row.capturedAt + '-' + row.sessionId + '-' + row.blockingSessionId) {
              <div class="blocking-row">
                <div class="blocking-top">
                  <div><strong>Session {{ row.sessionId }}</strong> <span>← blocker {{ row.blockingSessionId }}</span></div>
                  <div class="wait">{{ formatMs(row.waitTimeMs) }} · {{ row.waitType || 'wait bilinmiyor' }}</div>
                </div>
                <div class="meta">
                  <span>{{ row.serverName }}</span><span>{{ row.databaseName || 'DB bilinmiyor' }}</span>
                  @if (row.hostName) { <span>{{ row.hostName }}</span> }
                  @if (row.programName) { <span>{{ row.programName }}</span> }
                  <span>{{ row.capturedAt | date:'dd.MM.yyyy HH:mm:ss' }}</span>
                </div>
                @if (row.waitResource) { <div class="resource">{{ row.waitResource }}</div> }
                @if (row.sqlText) { <pre>{{ row.sqlText }}</pre> }
              </div>
            }
          </div>
        }
      </mat-card>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;align-items:flex-start;gap:20px}.readonly{display:flex;align-items:center;gap:6px;color:#526078;border:1px solid #dbe2ec;border-radius:8px;padding:7px 10px;font-size:.75rem}.readonly mat-icon{font-size:17px;width:17px;height:17px}.loading{height:240px;display:grid;place-items:center}
    .summary-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px;margin:18px 0}.summary-card{padding:17px;border:1px solid #e3e7ef;border-radius:12px}.summary-card span,.summary-card small{display:block;color:#718096}.summary-card strong{display:block;font-size:1.55rem;margin:5px 0}.summary-card.alert strong{color:#b42318}
    .panel{margin-top:18px;border:1px solid #e3e7ef;border-radius:14px;overflow:hidden}.panel-head{padding:18px 20px;border-bottom:1px solid #edf0f5}.panel-head h2{margin:0 0 4px;font-size:1.05rem}.panel-head p{margin:0;color:#718096;font-size:.8rem}.empty{padding:28px;display:flex;gap:8px;align-items:center;color:#637083}.empty mat-icon{color:#198754}
    .table-wrap{overflow:auto}table{width:100%;border-collapse:collapse;font-size:.78rem}th,td{text-align:left;padding:11px 14px;border-bottom:1px solid #edf0f5;white-space:nowrap}th{background:#f8fafc;color:#5f6c80;font-size:.7rem;text-transform:uppercase;letter-spacing:.04em}code{font-size:.76rem;color:#263855}
    .blocking-panel{margin-bottom:26px}.blocking-list{padding:0 18px}.blocking-row{padding:16px 2px;border-bottom:1px solid #edf0f5}.blocking-row:last-child{border-bottom:0}.blocking-top{display:flex;justify-content:space-between;gap:14px}.blocking-top span{color:#b42318}.wait{font-weight:700;color:#b42318}.meta{display:flex;flex-wrap:wrap;gap:8px;margin-top:8px}.meta span{background:#f1f4f8;border-radius:6px;padding:4px 7px;font-size:.7rem;color:#5c687b}.resource{font-family:monospace;font-size:.73rem;margin-top:9px;color:#5d6778}pre{white-space:pre-wrap;word-break:break-word;background:#101827;color:#dbe5f5;border-radius:8px;padding:10px;margin:10px 0 0;font-size:.72rem;max-height:180px;overflow:auto}
    @media(max-width:850px){.summary-grid{grid-template-columns:1fr}.heading{display:block}.readonly{width:max-content;margin-top:10px}.blocking-top{display:block}.wait{margin-top:6px}}
  `]
})
export class TelemetryComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly waits = signal<WaitTelemetry[]>([]);
  readonly blocking = signal<BlockingTelemetry[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    timer(0, 15_000).pipe(
      switchMap(() => forkJoin({
        waits: this.api.getWaitTelemetry(15, 100),
        blocking: this.api.getBlockingTelemetry(15, 100)
      })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.waits.set(result.waits);
        this.blocking.set(result.blocking);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  maxWaitDelta(): number {
    return this.waits().reduce((max, x) => Math.max(max, x.deltaWaitTimeMs), 0);
  }

  topWaitType(): string | null {
    const rows = this.waits();
    if (!rows.length) return null;
    return rows.reduce((best, x) => x.deltaWaitTimeMs > best.deltaWaitTimeMs ? x : best).waitType;
  }

  formatMs(value: number): string {
    if (value >= 60_000) return `${(value / 60_000).toFixed(1)} dk`;
    if (value >= 1_000) return `${(value / 1_000).toFixed(1)} sn`;
    return `${value} ms`;
  }
}
