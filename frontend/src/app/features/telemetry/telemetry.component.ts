import { Component, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdvisorApiService } from '../../core/services/advisor-api.service';
import { ServerListItem } from '../../core/models/server.models';
import { Subscription } from 'rxjs';
interface TelemetryRow {
  id: number; waitType: string | null; deltaWaitTimeMs: number; deltaSignalWaitTimeMs: number;
  intervalMs: number; isBaseline: boolean; sessionId: number; requestId: number; blockingSessionId: number;
  databaseName: string | null; waitTimeMs: number; waitResource: string | null;
  blockerStatus: string | null; blockerOpenTransactions: number | null; sqlText: string | null; blockerSqlText: string | null;
}
interface TelemetryResult {
  latest: {status: string; errorMessage: string | null} | null;
  success: {completedAt: string; rowsCollected: number} | null;
  enabled: boolean; stale: boolean; rows: TelemetryRow[];
}
@Component({
  selector: 'app-telemetry', standalone: true, imports: [CommonModule],
  template: `
    <h1>{{kind === 'waits' ? 'Wait Stats' : 'Blocking Detail'}}</h1>
    <p>Salt okunur izleme · {{kind === 'waits' ? 'Bekleme süreleri görevler genelinde toplanır; CPU kullanım yüzdesi değildir.' : 'Anlık örnekleme; iki ölçüm arasında biten blocking görünmeyebilir.'}}</p>
    <label>Sunucu <select [value]="serverId" (change)="selectServer($event)">
      <option value="">Sunucu seçin</option>
      @for (server of servers(); track server.id) { <option [value]="server.id">{{server.name}}</option> }
    </select></label>
    <button (click)="refresh()" [disabled]="!serverId || loading()">Yenile</button>
    @if (loading()) { <p role="status">Yükleniyor…</p> }
    @if (error()) { <p role="alert">{{error()}}</p> }
    @if (result(); as data) {
      @if (!data.enabled) { <p>Bu toplayıcı devre dışı.</p> }
      @if (data.latest?.status === 'Failed') { <p role="alert">Toplama başarısız: {{data.latest.errorMessage}}</p> }
      @if (data.latest?.status === 'Running') { <p>Toplama sürüyor.</p> }
      @if (data.success) { <p>Son başarılı ölçüm: {{data.success.completedAt | date:'medium'}} {{data.stale ? '— Veri güncel değil' : ''}}</p> }
      @else { <p>Henüz başarılı ölçüm yok.</p> }
      @if (kind === 'waits') {
        <label><input type="checkbox" [checked]="showAll()" (change)="showAll.set(!showAll())"> Sistem ve sıfır beklemeleri göster</label>
        @if (data.rows.length && data.rows[0].isBaseline) { <p>Başlangıç ölçümü / sayaç sıfırlanması: sonraki ölçümde fark hesaplanacak.</p> }
        <div class="scroll"><table><thead><tr><th>Bekleme</th><th>Artış (ms)</th><th>Signal (ms)</th><th>Aralık (ms)</th></tr></thead><tbody>
          @for (row of visibleWaits(); track row.id) { <tr><td>{{row.waitType}}</td><td>{{row.deltaWaitTimeMs | number}}</td><td>{{row.deltaSignalWaitTimeMs | number}}</td><td>{{row.intervalMs | number}}</td></tr> }
        </tbody></table></div>
        @if (!visibleWaits().length && data.success) { <p>Bu ölçümde gösterilecek bekleme artışı yok.</p> }
      } @else {
        @if (!data.rows.length && data.success) { <p>Son başarılı ölçümde blocking yok.</p> }
        @if (data.rows.length >= 1000) { <p>En uzun bekleyen 1000 istek gösteriliyor.</p> }
        @for (row of data.rows; track row.id) {
          <article><h2>Oturum {{row.sessionId}} / istek {{row.requestId}} → {{row.blockingSessionId}}</h2>
            <p>{{row.databaseName}} · {{row.waitType}} · {{row.waitTimeMs | number}} ms · {{row.waitResource}}</p>
            <p>Blocker: {{row.blockerStatus || 'Özel sahip / bilgi yok'}} · Açık transaction: {{row.blockerOpenTransactions ?? '—'}}</p>
            @if (row.blockingSessionId < 0) { <p>Negatif kimlik özel bir kaynak sahibidir; kullanıcı oturumu değildir.</p> }
            <details><summary>SQL ayrıntısı (en fazla 4000 karakter)</summary><h3>Bekleyen istek</h3><pre>{{row.sqlText || 'SQL metni yok'}}</pre><h3>Blocker son batch</h3><pre>{{row.blockerSqlText || 'SQL metni yok'}}</pre></details>
          </article>
        }
      }
    }
  `,
  styles: [`select,button{padding:10px;margin:8px}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:12px;border-bottom:1px solid #ddd}.scroll{overflow:auto}article{background:white;padding:16px;margin:12px 0;border:1px solid #ddd;border-radius:8px}pre{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:#a21b1b}`]
})
export class TelemetryComponent {
  private readonly api = inject(AdvisorApiService);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  readonly kind = inject(ActivatedRoute).snapshot.data['kind'] as string;
  readonly servers = signal<ServerListItem[]>([]);
  readonly result = signal<TelemetryResult | null>(null);
  readonly error = signal(''); readonly loading = signal(false); readonly showAll = signal(false);
  serverId = ''; private request?: Subscription;
  constructor() {
    this.api.getServers().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next: rows => this.servers.set(rows), error: () => this.error.set('Sunucular yüklenemedi.')});
  }
  selectServer(event: Event) { this.serverId = (event.target as HTMLSelectElement).value; this.refresh(); }
  visibleWaits() { return (this.result()?.rows ?? []).filter(x => this.showAll() || (x.deltaWaitTimeMs > 0 && /^(LCK_M_|PAGEIOLATCH_|WRITELOG$|RESOURCE_SEMAPHORE$|THREADPOOL$|SOS_SCHEDULER_YIELD$)/.test(x.waitType ?? ''))); }
  refresh() {
    this.request?.unsubscribe(); this.result.set(null); this.error.set(''); this.loading.set(false);
    if (!this.serverId) return;
    this.loading.set(true);
    this.request = this.http.get<TelemetryResult>(`/api/telemetry/${this.serverId}/${this.kind}`).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => { this.result.set(data); this.loading.set(false); },
      error: () => { this.error.set('Veriler yüklenemedi. Yeniden deneyin.'); this.loading.set(false); }
    });
  }
}

