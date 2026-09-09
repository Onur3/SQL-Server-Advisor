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
        <h1 class="page-title">Index Advisor</h1>
        <p class="page-subtitle">Fragmentation ve missing-index telemetry. Otomatik indeks değişikliği uygulanmaz.</p>
      </div>
      <div class="readonly"><mat-icon>lock</mat-icon> Read-only analysis</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else {
      <div class="summary">
        <mat-card><span>Fragmented</span><strong>{{ fragmented().length }}</strong></mat-card>
        <mat-card><span>Missing aday</span><strong>{{ missing().length }}</strong></mat-card>
        <mat-card><span>En yüksek frag.</span><strong>{{ maxFragmentation() | number:'1.0-1' }}%</strong></mat-card>
        <mat-card><span>En yüksek impact</span><strong>{{ maxImprovement() | number:'1.0-0' }}</strong></mat-card>
      </div>

      <section>
        <div class="section-title"><h2>Fragmented Indexes</h2><span>page_count ≥ 1000</span></div>
        @if (!fragmented().length) {
          <mat-card class="empty"><mat-icon>check_circle</mat-icon><p>Şu anda anlamlı fragmentation kaydı yok.</p></mat-card>
        } @else {
          <div class="grid">
            @for (item of fragmented(); track item.id) {
              <mat-card class="card">
                <div class="head">
                  <div><strong>{{ item.databaseName }}</strong><span>{{ item.tableName }}</span><small>{{ item.indexName }}</small></div>
                  <div class="badge" [class.high]="(item.avgFragmentationPercent ?? 0) >= 50">{{ item.avgFragmentationPercent | number:'1.0-1' }}%</div>
                </div>
                <div class="metrics">
                  <div><span>Page</span><strong>{{ item.pageCount | number }}</strong></div>
                  <div><span>Boyut</span><strong>{{ item.sizeMb | number:'1.0-1' }} MB</strong></div>
                  <div><span>Seek</span><strong>{{ item.userSeeks | number }}</strong></div>
                  <div><span>Scan</span><strong>{{ item.userScans | number }}</strong></div>
                  <div><span>Update</span><strong>{{ item.userUpdates | number }}</strong></div>
                </div>
                <div class="columns"><span>Key</span><code>{{ item.keyColumns || '—' }}</code></div>
                @if (item.includeColumns) { <div class="columns"><span>Include</span><code>{{ item.includeColumns }}</code></div> }
              </mat-card>
            }
          </div>
        }
      </section>

      <section>
        <div class="section-title"><h2>Missing Index Candidates</h2><span>DMV tuning candidates</span></div>
        @if (!missing().length) {
          <mat-card class="empty"><mat-icon>check_circle</mat-icon><p>Şu anda missing-index adayı yok.</p></mat-card>
        } @else {
          <div class="grid">
            @for (item of missing(); track item.id) {
              <mat-card class="card">
                <div class="head">
                  <div><strong>{{ item.databaseName }}</strong><span>{{ item.tableName }}</span></div>
                  <div class="impact"><strong>{{ item.avgUserImpact | number:'1.0-1' }}%</strong><span>USER IMPACT</span></div>
                </div>
                <div class="metrics four">
                  <div><span>Seek</span><strong>{{ item.userSeeks | number }}</strong></div>
                  <div><span>Scan</span><strong>{{ item.userScans | number }}</strong></div>
                  <div><span>Avg Cost</span><strong>{{ item.avgTotalUserCost | number:'1.0-1' }}</strong></div>
                  <div><span>Improvement</span><strong>{{ item.improvementMeasure | number:'1.0-0' }}</strong></div>
                </div>
                <div class="columns"><span>Equality</span><code>{{ item.equalityColumns || '—' }}</code></div>
                <div class="columns"><span>Inequality</span><code>{{ item.inequalityColumns || '—' }}</code></div>
                <div class="columns"><span>Include</span><code>{{ item.includedColumns || '—' }}</code></div>
              </mat-card>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:8px;padding:7px 10px;color:#64748b;font-size:.75rem}.readonly mat-icon{font-size:16px;width:16px;height:16px}.loading{height:240px;display:grid;place-items:center}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:22px}.summary mat-card{padding:14px 16px}.summary span{display:block;color:#778297;font-size:.72rem}.summary strong{display:block;margin-top:5px;font-size:1.15rem}section{margin-top:26px}.section-title{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px}.section-title h2{font-size:1.05rem;margin:0}.section-title span{font-size:.72rem;color:#8792a3}.empty{padding:26px;display:flex;gap:10px;align-items:center;color:#667085}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(420px,1fr));gap:14px}.card{padding:16px;border:1px solid #e3e7ef;border-radius:14px;box-shadow:0 3px 18px rgba(20,32,55,.05)}.head{display:flex;justify-content:space-between;gap:14px}.head strong,.head span,.head small{display:block}.head span{font-size:.78rem;color:#5d697c;margin-top:3px}.head small{font-size:.7rem;color:#8b95a6;margin-top:3px}.badge{background:#fff6de;color:#986700;border-radius:8px;height:max-content;padding:7px 9px;font-weight:750}.badge.high{background:#ffeded;color:#aa3030}.impact{text-align:center}.impact strong{display:block;font-size:1rem}.impact span{font-size:.58rem;color:#778297}.metrics{display:grid;grid-template-columns:repeat(5,1fr);border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5;margin:14px 0}.metrics.four{grid-template-columns:repeat(4,1fr)}.metrics div{text-align:center;padding:10px 5px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.65rem}.metrics strong{font-size:.82rem}.columns{display:grid;grid-template-columns:70px 1fr;gap:8px;margin-top:7px;font-size:.72rem}.columns span{color:#778297}.columns code{white-space:normal;word-break:break-word;color:#334155}@media(max-width:900px){.summary{grid-template-columns:repeat(2,1fr)}.grid{grid-template-columns:1fr}.heading{display:block}}
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
      switchMap(() => forkJoin({
        fragmented: this.api.getFragmentedIndexes(100),
        missing: this.api.getMissingIndexCandidates(100)
      })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.fragmented.set(result.fragmented);
        this.missing.set(result.missing);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  maxFragmentation(): number {
    return this.fragmented().reduce((max, x) => Math.max(max, x.avgFragmentationPercent ?? 0), 0);
  }

  maxImprovement(): number {
    return this.missing().reduce((max, x) => Math.max(max, x.improvementMeasure), 0);
  }
}
