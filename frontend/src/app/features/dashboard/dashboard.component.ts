import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { timer, switchMap, forkJoin } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdvisorApiService } from '../../core/services/advisor-api.service';
import { DashboardServer, WorkerStatus } from '../../core/models/server.models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatProgressSpinnerModule, MatIconModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Dashboard</h1>
        <p class="page-subtitle">SQL Server ortamının canlı sağlık ve collector durumu.</p>
      </div>
      @if (worker(); as w) {
        <div class="worker" [class.offline]="!w.online">
          <span class="status-dot" [class.online]="w.online" [class.offline]="!w.online"></span>
          Collector {{ w.online ? 'ONLINE' : 'OFFLINE' }}
          @if (w.machineName) { <small>{{ w.machineName }} · {{ w.version }}</small> }
        </div>
      }
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!servers().length) {
      <div class="panel empty">
        <mat-icon>dns</mat-icon>
        <h3>Henüz izlenen SQL Server yok</h3>
        <p>SQL Sunucuları ekranından ilk salt-okunur bağlantıyı ekleyin.</p>
      </div>
    } @else {
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
                  <strong>{{ server.healthScore ?? '—' }}</strong><span>HEALTH · DATA {{ server.dataCoveragePercent }}%</span>
                </div>
              </div>
            </mat-card-header>
            <mat-card-content>
              <div class="metrics">
                <div><span>SQL CPU</span><strong>{{ server.sqlCpuPercent ?? '—' }}{{ server.sqlCpuPercent != null ? '%' : '' }}</strong></div>
                <div><span>Boş RAM</span><strong>{{ formatMemory(server.availableMemoryMb) }}</strong></div>
                <div><span>Aktif Session</span><strong>{{ server.activeSessions }}</strong></div>
                <div><span>Aktif Request</span><strong>{{ server.activeRequests }}</strong></div>
                <div class="blocking" [class.has-blocking]="server.blockedRequests > 0"><span>Blocking</span><strong>{{ server.blockedRequests }}</strong></div>
              </div>
              <div class="last">Son snapshot: {{ server.lastSnapshotAt ? (server.lastSnapshotAt | date:'dd.MM.yyyy HH:mm:ss') : 'Henüz yok' }}</div>
            </mat-card-content>
          </mat-card>
        }
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start}.worker{background:#eaf7f0;color:#137348;border:1px solid #c6ead7;border-radius:10px;padding:9px 12px;font-size:.78rem;font-weight:700}.worker.offline{background:#fff0f0;color:#a52a2a;border-color:#f1caca}.worker small{display:block;margin:4px 0 0 16px;font-weight:500;opacity:.75}
    .loading{height:240px;display:grid;place-items:center}.empty{min-height:260px;display:grid;place-items:center;text-align:center;padding:45px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#64748b}.empty h3{margin:0}.empty p{margin:0;color:#718096}
    .server-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(380px,1fr));gap:18px}.server-card{border-radius:14px;border:1px solid #e3e7ef;box-shadow:0 3px 18px rgba(20,32,55,.05)}.server-card mat-card-header{display:block;padding:18px 18px 12px}.server-head{display:flex;justify-content:space-between;align-items:center}.server-name{font-weight:750;font-size:1.08rem}.host{color:#758196;font-size:.82rem;margin:4px 0 0 16px}.health{text-align:center;border-radius:9px;min-width:62px;padding:7px 8px;background:#eef2f7}.health strong{font-size:1.25rem;display:block}.health span{font-size:.58rem;letter-spacing:.08em}.health.good{background:#e9f8ef;color:#137348}.health.warn{background:#fff6de;color:#a16c00}.health.bad{background:#ffeded;color:#aa3030}
    .metrics{display:grid;grid-template-columns:repeat(5,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5}.metrics div{padding:14px 8px;text-align:center;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.68rem;margin-bottom:5px}.metrics strong{font-size:1rem}.metrics .has-blocking strong{color:#c33333}.last{font-size:.72rem;color:#8993a4;padding-top:12px;text-align:right}
    @media(max-width:700px){.heading{display:block}.worker{margin-bottom:18px}.server-grid{grid-template-columns:1fr}.metrics{grid-template-columns:repeat(2,1fr)}.metrics div{border-bottom:1px solid #edf0f5}}
  `]
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly servers = signal<DashboardServer[]>([]);
  readonly worker = signal<WorkerStatus | null>(null);
  readonly loading = signal(true);

  ngOnInit(): void {
    timer(0, 15_000).pipe(
      switchMap(() => forkJoin({ servers: this.api.getDashboardServers(), worker: this.api.getWorkerStatus() })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.servers.set(result.servers);
        this.worker.set(result.worker);
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
