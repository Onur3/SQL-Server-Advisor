import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin, switchMap, timer } from 'rxjs';
import { FragmentedIndex, MissingIndexCandidate } from '../../core/models/index.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-indexes',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">İndeks Analizi</h1>
        <p class="page-subtitle">Fragmentation ve missing-index verilerini motor yorumlar; otomatik CREATE/ALTER INDEX çalıştırılmaz.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> Salt okunur analiz</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else {
      <div class="summary">
        <mat-card><span>Fragmentation kaydı</span><strong>{{ fragmented().length }}</strong></mat-card>
        <mat-card><span>Eksik indeks adayı</span><strong>{{ missing().length }}</strong></mat-card>
        <mat-card><span>En yüksek fragmentation</span><strong>{{ maxFragmentation() | number:'1.0-1' }}%</strong></mat-card>
        <mat-card><span>En yüksek improvement</span><strong>{{ maxImprovement() | number:'1.0-0' }}</strong></mat-card>
      </div>

      <section>
        <div class="section-title"><div><h2>Fiziksel İndeks Sağlığı</h2><p>1.000 page üzerindeki indeksler; bakım ihtiyacı kullanım ile birlikte yorumlanır.</p></div><span>page_count ≥ 1000</span></div>
        @if (!fragmented().length) {
          <mat-card class="empty"><mat-icon>task_alt</mat-icon><p>Şu anda anlamlı fragmentation kaydı yok.</p></mat-card>
        } @else {
          <div class="grid">
            @for (item of fragmented(); track item.id) {
              <mat-card class="card">
                <div class="head">
                  <div><div class="scope"><mat-icon>dns</mat-icon>{{ item.serverName }}</div><strong>{{ item.databaseName }} › {{ item.tableName }}</strong><span>{{ item.indexName }}</span></div>
                  <div class="level" [class.high]="item.diagnosticLevel === 'High'">{{ item.avgFragmentationPercent | number:'1.0-1' }}%</div>
                </div>

                <div class="diagnostic" [class.high]="item.diagnosticLevel === 'High'">
                  <div class="diagnostic-title"><mat-icon>psychology</mat-icon>{{ item.diagnosticHeadline }}</div>
                  <p>{{ item.diagnosticSummary }}</p>
                  <div class="inspect"><mat-icon>checklist</mat-icon>{{ item.suggestedInspection }}</div>
                </div>

                <div class="metrics">
                  <div><span>Page</span><strong>{{ item.pageCount | number }}</strong></div>
                  <div><span>Boyut</span><strong>{{ item.sizeMb | number:'1.0-1' }} MB</strong></div>
                  <div><span>Seek</span><strong>{{ item.userSeeks | number }}</strong></div>
                  <div><span>Scan</span><strong>{{ item.userScans | number }}</strong></div>
                  <div><span>Update</span><strong>{{ item.userUpdates | number }}</strong></div>
                </div>
                <details><summary>İndeks kolonlarını göster</summary><div class="columns"><span>Key</span><code>{{ item.keyColumns || '—' }}</code></div>@if (item.includeColumns) { <div class="columns"><span>Include</span><code>{{ item.includeColumns }}</code></div> }</details>
              </mat-card>
            }
          </div>
        }
      </section>

      <section>
        <div class="section-title"><div><h2>Eksik İndeks Adayları</h2><p>DMV önerileri doğrudan indeks emri değildir; overlap ve write maliyeti kontrol edilmelidir.</p></div><span>SQL Server DMV</span></div>
        @if (!missing().length) {
          <mat-card class="empty"><mat-icon>task_alt</mat-icon><p>Şu anda missing-index adayı yok.</p></mat-card>
        } @else {
          <div class="grid">
            @for (item of missing(); track item.id) {
              <mat-card class="card">
                <div class="head">
                  <div><div class="scope"><mat-icon>dns</mat-icon>{{ item.serverName }}</div><strong>{{ item.databaseName }} › {{ item.tableName }}</strong></div>
                  <div class="impact"><strong>{{ item.avgUserImpact | number:'1.0-1' }}%</strong><span>TAHMİNİ ETKİ</span></div>
                </div>

                <div class="diagnostic" [class.high]="item.diagnosticLevel === 'High'">
                  <div class="diagnostic-title"><mat-icon>psychology</mat-icon>{{ item.diagnosticHeadline }}</div>
                  <p>{{ item.diagnosticSummary }}</p>
                  <div class="inspect"><mat-icon>checklist</mat-icon>{{ item.suggestedInspection }}</div>
                </div>

                <div class="metrics four">
                  <div><span>Seek</span><strong>{{ item.userSeeks | number }}</strong></div>
                  <div><span>Scan</span><strong>{{ item.userScans | number }}</strong></div>
                  <div><span>Ort. maliyet</span><strong>{{ item.avgTotalUserCost | number:'1.0-1' }}</strong></div>
                  <div><span>Improvement</span><strong>{{ item.improvementMeasure | number:'1.0-0' }}</strong></div>
                </div>
                <details><summary>Aday kolonları göster</summary><div class="columns"><span>Equality</span><code>{{ item.equalityColumns || '—' }}</code></div><div class="columns"><span>Inequality</span><code>{{ item.inequalityColumns || '—' }}</code></div><div class="columns"><span>Include</span><code>{{ item.includedColumns || '—' }}</code></div></details>
              </mat-card>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:999px;padding:7px 10px;color:#64748b;font-size:.72rem;background:#fff}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:22px}.summary mat-card{padding:14px 16px;border:1px solid #e2e7ef;box-shadow:none}.summary span{display:block;color:#778297;font-size:.68rem}.summary strong{display:block;margin-top:5px;font-size:1.13rem}section{margin-top:27px}.section-title{display:flex;justify-content:space-between;align-items:flex-end;margin-bottom:10px}.section-title h2{font-size:1.02rem;margin:0}.section-title p{margin:4px 0 0;color:#7c899b;font-size:.68rem}.section-title>span{font-size:.67rem;color:#8792a3;background:#f0f3f7;border-radius:999px;padding:5px 8px}.empty{padding:24px;display:flex;gap:10px;align-items:center;color:#667085}.empty mat-icon{color:#279063}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(430px,1fr));gap:14px}.card{padding:17px;border:1px solid #e1e7f0;border-radius:15px;box-shadow:0 7px 24px rgba(15,23,42,.045)}.head{display:flex;justify-content:space-between;gap:14px}.head strong,.head span{display:block}.head strong{margin-top:4px;font-size:.82rem}.head span{font-size:.71rem;color:#748196;margin-top:3px}.scope{display:flex;align-items:center;gap:5px;color:#8290a3;font-size:.64rem}.scope mat-icon{font-size:14px;width:14px;height:14px}.level{background:#fff5dd;color:#986700;border-radius:9px;height:max-content;padding:7px 9px;font-weight:780}.level.high{background:#ffeded;color:#aa3030}.impact{text-align:center}.impact strong{display:block;font-size:1rem}.impact span{font-size:.54rem;color:#778297;letter-spacing:.03em}.diagnostic{margin-top:12px;padding:11px 12px;border:1px solid #dfe6f7;background:#f7f9ff;border-radius:10px}.diagnostic.high{background:#fff9f1;border-color:#f0dfca}.diagnostic-title{display:flex;align-items:center;gap:5px;color:#344d77;font-size:.7rem;font-weight:800}.diagnostic-title mat-icon{font-size:17px;width:17px;height:17px}.diagnostic p{margin:6px 0;color:#526177;font-size:.73rem;line-height:1.45}.inspect{display:flex;align-items:flex-start;gap:5px;color:#35506e;font-size:.68rem;font-weight:650}.inspect mat-icon{font-size:15px;width:15px;height:15px;color:#21805a}.metrics{display:grid;grid-template-columns:repeat(5,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5;margin:13px 0}.metrics.four{grid-template-columns:repeat(4,1fr)}.metrics div{text-align:center;padding:9px 5px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.61rem}.metrics strong{font-size:.79rem}details{font-size:.7rem;color:#59687d}summary{cursor:pointer;font-weight:700}.columns{display:grid;grid-template-columns:70px 1fr;gap:8px;margin-top:7px;font-size:.7rem}.columns span{color:#778297}.columns code{white-space:normal;word-break:break-word;color:#334155}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.grid{grid-template-columns:1fr}.heading{display:block}}
  `]
})
export class IndexesComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly fragmented = signal<FragmentedIndex[]>([]);
  readonly missing = signal<MissingIndexCandidate[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    timer(0, 300_000).pipe(
      switchMap(() => forkJoin({ fragmented: this.api.getFragmentedIndexes(100), missing: this.api.getMissingIndexCandidates(100) })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => { this.fragmented.set(result.fragmented); this.missing.set(result.missing); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  maxFragmentation(): number { return this.fragmented().reduce((max, x) => Math.max(max, x.avgFragmentationPercent ?? 0), 0); }
  maxImprovement(): number { return this.missing().reduce((max, x) => Math.max(max, x.improvementMeasure), 0); }
}
