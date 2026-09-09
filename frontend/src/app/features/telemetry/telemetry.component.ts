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
        <h1 class="page-title">Beklemeler & Kilitler</h1>
        <p class="page-subtitle">Son 15 dakikadaki wait delta'larını ve blocking zincirlerini anlamlarıyla birlikte görün.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> Salt okunur · 30 sn collector</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else {
      <div class="summary-grid">
        <mat-card class="summary-card"><span>Wait örneği</span><strong>{{ waits().length }}</strong><small>delta &gt; 0</small></mat-card>
        <mat-card class="summary-card" [class.alert]="blocking().length > 0"><span>Blocking kaydı</span><strong>{{ blocking().length }}</strong><small>son 15 dakika</small></mat-card>
        <mat-card class="summary-card"><span>En yüksek wait delta</span><strong>{{ formatMs(maxWaitDelta()) }}</strong><small>{{ topWaitType() || '—' }}</small></mat-card>
      </div>

      <mat-card class="panel">
        <div class="panel-head"><div><h2>Wait Analizi</h2><p>Wait type tek başına kök neden değildir; aşağıdaki açıklama hangi kaynağa bakmanız gerektiğini gösterir.</p></div></div>
        @if (!waits().length) {
          <div class="empty"><mat-icon>task_alt</mat-icon> Son 15 dakikada delta wait kaydı yok.</div>
        } @else {
          <div class="wait-list">
            @for (row of waits(); track row.capturedAt + row.serverProfileId + row.waitType) {
              <div class="wait-row">
                <div class="wait-main">
                  <div class="wait-icon"><mat-icon>{{ waitIcon(row.waitType) }}</mat-icon></div>
                  <div class="wait-copy"><div class="wait-title"><code>{{ row.waitType }}</code><span>{{ waitCategory(row.waitType) }}</span></div><strong>{{ waitMeaning(row.waitType) }}</strong><p>{{ waitSuggestion(row.waitType) }}</p></div>
                </div>
                <div class="wait-numbers"><div><span>Delta</span><strong>{{ formatMs(row.deltaWaitTimeMs) }}</strong></div><div><span>Signal</span><strong>{{ formatMs(row.deltaSignalWaitTimeMs) }}</strong></div><div><span>Tasks</span><strong>{{ row.waitingTasks }}</strong></div></div>
                <div class="wait-foot"><span>{{ row.serverName }}</span><span>{{ row.capturedAt | date:'dd.MM.yyyy HH:mm:ss' }}</span></div>
              </div>
            }
          </div>
        }
      </mat-card>

      <mat-card class="panel blocking-panel">
        <div class="panel-head"><div><h2>Blocking Zincirleri</h2><p>Blocked request → blocker session ilişkisi. Amaç önce head blocker ve transaction davranışını bulmaktır.</p></div></div>
        @if (!blocking().length) {
          <div class="empty"><mat-icon>task_alt</mat-icon> Son 15 dakikada blocking kaydı yok.</div>
        } @else {
          <div class="blocking-list">
            @for (row of blocking(); track row.capturedAt + '-' + row.sessionId + '-' + row.blockingSessionId) {
              <div class="blocking-row">
                <div class="blocking-head">
                  <div class="blocking-icon"><mat-icon>lock</mat-icon></div>
                  <div><strong>Session {{ row.sessionId }} bekliyor</strong><p>Session <b>{{ row.blockingSessionId }}</b> bu isteğin ilerlemesini engelliyor.</p></div>
                  <div class="wait-time">{{ formatMs(row.waitTimeMs) }}</div>
                </div>
                <div class="diagnostic"><mat-icon>checklist</mat-icon><span>Önce blocker session'ın açık transaction süresini, SQL metnini, wait resource'u ve ilgili indeks erişim yolunu kontrol edin.</span></div>
                <div class="meta"><span>Sunucu <b>{{ row.serverName }}</b></span><span>DB <b>{{ row.databaseName || 'Bilinmiyor' }}</b></span><span>Wait <b>{{ row.waitType || 'Bilinmiyor' }}</b></span>@if (row.hostName) { <span>Host <b>{{ row.hostName }}</b></span> }@if (row.programName) { <span>Uygulama <b>{{ row.programName }}</b></span> }</div>
                @if (row.waitResource) { <div class="resource"><span>Wait resource</span><code>{{ row.waitResource }}</code></div> }
                @if (row.sqlText) { <details><summary>Blocked SQL metnini göster</summary><pre>{{ row.sqlText }}</pre></details> }
              </div>
            }
          </div>
        }
      </mat-card>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;align-items:flex-start;gap:20px}.readonly{display:flex;align-items:center;gap:6px;color:#526078;border:1px solid #dbe2ec;border-radius:999px;padding:7px 10px;font-size:.7rem;background:#fff}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.summary-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin:18px 0}.summary-card{padding:15px;border:1px solid #e2e7ef;border-radius:12px;box-shadow:none}.summary-card span,.summary-card small{display:block;color:#718096;font-size:.66rem}.summary-card strong{display:block;font-size:1.4rem;margin:4px 0}.summary-card.alert strong{color:#b42318}.panel{margin-top:18px;border:1px solid #e1e7f0;border-radius:15px;overflow:hidden;box-shadow:0 7px 24px rgba(15,23,42,.045)}.panel-head{padding:16px 18px;border-bottom:1px solid #edf0f5}.panel-head h2{margin:0 0 4px;font-size:1rem}.panel-head p{margin:0;color:#718096;font-size:.72rem}.empty{padding:26px;display:flex;gap:8px;align-items:center;color:#637083}.empty mat-icon{color:#198754}.wait-list{display:grid}.wait-row{padding:14px 17px;border-bottom:1px solid #edf0f5}.wait-row:last-child{border:0}.wait-main{display:grid;grid-template-columns:36px 1fr;gap:10px}.wait-icon{width:34px;height:34px;border-radius:9px;display:grid;place-items:center;background:#eef2f7;color:#50647d}.wait-icon mat-icon{font-size:18px;width:18px;height:18px}.wait-title{display:flex;gap:7px;align-items:center}.wait-title code{font-size:.72rem;color:#2e405b}.wait-title span{background:#eef2f7;border-radius:999px;padding:3px 6px;font-size:.58rem;color:#6f7c90}.wait-copy>strong{display:block;margin-top:4px;font-size:.77rem;color:#344054}.wait-copy p{margin:3px 0 0;color:#6c798c;font-size:.68rem;line-height:1.4}.wait-numbers{display:grid;grid-template-columns:repeat(3,100px);gap:7px;margin:10px 0 0 46px}.wait-numbers div{background:#f8fafc;border:1px solid #edf0f5;border-radius:8px;padding:6px 8px}.wait-numbers span,.wait-numbers strong{display:block}.wait-numbers span{font-size:.57rem;color:#8a96a8}.wait-numbers strong{font-size:.71rem;margin-top:2px}.wait-foot{display:flex;justify-content:space-between;margin:7px 0 0 46px;color:#8994a5;font-size:.61rem}.blocking-panel{margin-bottom:26px}.blocking-list{padding:0 17px}.blocking-row{padding:16px 1px;border-bottom:1px solid #edf0f5}.blocking-row:last-child{border:0}.blocking-head{display:grid;grid-template-columns:36px 1fr auto;gap:10px;align-items:start}.blocking-icon{width:34px;height:34px;border-radius:9px;display:grid;place-items:center;background:#ffe9e6;color:#b5322c}.blocking-icon mat-icon{font-size:18px;width:18px;height:18px}.blocking-head strong{font-size:.8rem}.blocking-head p{margin:3px 0 0;color:#68768a;font-size:.7rem}.wait-time{background:#fff0ed;color:#aa302b;border-radius:8px;padding:6px 8px;font-size:.72rem;font-weight:800}.diagnostic{display:flex;align-items:flex-start;gap:6px;margin:10px 0 9px 46px;padding:8px 10px;background:#f5faf7;border:1px solid #dcefe4;border-radius:8px;color:#3d6253;font-size:.68rem}.diagnostic mat-icon{font-size:16px;width:16px;height:16px;color:#21805a}.meta{display:flex;flex-wrap:wrap;gap:6px;margin-left:46px}.meta span{background:#f1f4f8;border-radius:6px;padding:4px 7px;font-size:.62rem;color:#7a8799}.meta b{color:#445267;margin-left:2px}.resource{display:flex;gap:7px;margin:9px 0 0 46px;font-size:.65rem}.resource span{color:#8792a3}.resource code{color:#435169}details{margin:9px 0 0 46px;font-size:.68rem;color:#59687d}summary{cursor:pointer;font-weight:700}pre{white-space:pre-wrap;word-break:break-word;background:#0f172a;color:#dbe5f5;border-radius:8px;padding:10px;margin:8px 0 0;font-size:.7rem;max-height:190px;overflow:auto}@media(max-width:850px){.summary-grid{grid-template-columns:1fr}.heading{display:block}.readonly{width:max-content;margin-top:10px}.wait-numbers{grid-template-columns:repeat(3,1fr)}.blocking-head{grid-template-columns:36px 1fr}.wait-time{grid-column:2;width:max-content}.diagnostic,.meta,.resource,details{margin-left:0}}
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
      switchMap(() => forkJoin({ waits: this.api.getWaitTelemetry(15, 100), blocking: this.api.getBlockingTelemetry(15, 100) })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => { this.waits.set(result.waits); this.blocking.set(result.blocking); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  maxWaitDelta(): number { return this.waits().reduce((max, x) => Math.max(max, x.deltaWaitTimeMs), 0); }
  topWaitType(): string | null { const rows = this.waits(); return rows.length ? rows.reduce((best, x) => x.deltaWaitTimeMs > best.deltaWaitTimeMs ? x : best).waitType : null; }

  waitCategory(waitType: string): string {
    if (waitType.startsWith('LCK_M_')) return 'Kilit';
    if (waitType.startsWith('PAGEIOLATCH_')) return 'Disk I/O';
    if (waitType === 'WRITELOG') return 'Log I/O';
    if (waitType === 'RESOURCE_SEMAPHORE') return 'Bellek';
    if (waitType === 'THREADPOOL') return 'Worker Thread';
    if (waitType === 'SOS_SCHEDULER_YIELD') return 'CPU';
    return 'SQL Wait';
  }

  waitMeaning(waitType: string): string {
    if (waitType.startsWith('LCK_M_')) return 'Başka bir transaction gerekli lock kaynağını tutuyor.';
    if (waitType.startsWith('PAGEIOLATCH_')) return 'SQL Server data page’in diskten belleğe gelmesini bekliyor.';
    if (waitType === 'WRITELOG') return 'Transaction log flush işlemi disk yazmasını bekliyor.';
    if (waitType === 'RESOURCE_SEMAPHORE') return 'Sorgular execution memory grant bekliyor.';
    if (waitType === 'THREADPOOL') return 'Yeni işler için kullanılabilir worker thread baskısı oluşuyor.';
    if (waitType === 'SOS_SCHEDULER_YIELD') return 'Runnable sorgular CPU scheduler üzerinde zaman bekliyor.';
    return 'SQL Server bu kaynak/işlem tamamlanana kadar bekleme kaydetti.';
  }

  waitSuggestion(waitType: string): string {
    if (waitType.startsWith('LCK_M_')) return 'Blocking ekranında head blocker ve transaction süresini kontrol edin.';
    if (waitType.startsWith('PAGEIOLATCH_')) return 'Dosya latency, physical reads ve yüksek logical read üreten sorguları karşılaştırın.';
    if (waitType === 'WRITELOG') return 'Log disk latency, log growth ve yüksek transaction hacmini inceleyin.';
    if (waitType === 'RESOURCE_SEMAPHORE') return 'Memory grant isteyen sorguları, max server memory ve spill işaretlerini inceleyin.';
    if (waitType === 'THREADPOOL') return 'Uzun süren/blocking session sayısını ve worker thread kullanımını kontrol edin.';
    if (waitType === 'SOS_SCHEDULER_YIELD') return 'CPU tüketen pahalı sorgular ve paralellik davranışını inceleyin.';
    return 'Aynı zaman aralığındaki sorgu, CPU, I/O ve blocking verileriyle korele edin.';
  }

  waitIcon(waitType: string): string {
    if (waitType.startsWith('LCK_M_')) return 'lock';
    if (waitType.startsWith('PAGEIOLATCH_') || waitType === 'WRITELOG') return 'storage';
    if (waitType === 'RESOURCE_SEMAPHORE') return 'memory';
    if (waitType === 'THREADPOOL') return 'groups';
    if (waitType === 'SOS_SCHEDULER_YIELD') return 'memory';
    return 'hourglass_top';
  }

  formatMs(value: number): string {
    if (value >= 60_000) return `${(value / 60_000).toFixed(1)} dk`;
    if (value >= 1_000) return `${(value / 1_000).toFixed(1)} sn`;
    return `${value} ms`;
  }
}
