import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin, switchMap, timer } from 'rxjs';
import { CollectorCoverage } from '../../core/models/collector.models';
import { FragmentedIndex, MissingIndexCandidate } from '../../core/models/index.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-indexes',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">İndeks Analizi</h1>
        <p class="page-subtitle">Motor mevcut indeks kataloğu, kullanım/write maliyeti, missing-index DMV ve TXT workload kanıtını birlikte değerlendirir.</p>
      </div>
      <div class="readonly"><mat-icon>visibility</mat-icon> Taslaklar otomatik çalıştırılmaz</div>
    </div>

    @if (coverageWarnings().length) {
      <div class="coverage-warning">
        <mat-icon>database</mat-icon>
        <div>
          <strong>İndeks analizi tüm veritabanlarını kapsamıyor</strong>
          <p>Monitoring hesabının bazı veritabanlarında metadata/DMV yetkisi yok. Bu veritabanları sonuçlardan eksik olabilir.</p>
          @for (row of coverageWarnings(); track row.serverProfileId) {
            <details><summary>{{ row.serverName }} · ayrıntıyı göster</summary><pre>{{ row.warningMessage }}</pre></details>
          }
        </div>
      </div>
    }

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else {
      <div class="summary">
        <mat-card><span>Bakım inceleme kaydı</span><strong>{{ maintenanceCandidates() }}</strong><small>kullanılan + fragmented</small></mat-card>
        <mat-card><span>Yeni indeks kararı</span><strong>{{ createCandidates() }}</strong><small>kapsanmayan DMV</small></mat-card>
        <mat-card><span>Konsolidasyon kararı</span><strong>{{ consolidateCandidates() }}</strong><small>mevcut indeksle örtüşüyor</small></mat-card>
        <mat-card><span>Yeni indeks açma</span><strong>{{ coveredMissing() }}</strong><small>mevcut indeks kapsıyor</small></mat-card>
      </div>

      <section>
        <div class="section-title">
          <div><h2>Mevcut İndekslerin Sağlığı ve Değeri</h2><p>Fragmentation tek başına bakım kararı değildir; gözlem süresi ve read/write değeriyle birlikte yorumlanır.</p></div>
          <span>güncel snapshot · bakım eşiği: page_count ≥ 1000 / frag ≥ %30</span>
        </div>
        @if (!fragmented().length) {
          <mat-card class="empty"><mat-icon>task_alt</mat-icon><p>Güncel indeks snapshot kaydı yok.</p></mat-card>
        } @else {
          <div class="grid">
            @for (item of fragmented(); track item.id) {
              <mat-card class="card">
                <div class="head">
                  <div>
                    <div class="scope"><mat-icon>dns</mat-icon>{{ item.serverName }}</div>
                    <strong>{{ item.databaseName }} › {{ item.tableName }}</strong>
                    <span>{{ item.indexName }}</span>
                    <div class="badges"><span>{{ item.usageSinceDays }} gün gözlem</span><span>Read {{ readCount(item) | number }}</span><span>Write {{ item.userUpdates | number }}</span>@if (item.hasFilter) { <span class="special">FILTERED</span> }</div>
                  </div>
                  <div class="level" [class.high]="item.diagnosticLevel === 'High'">{{ item.avgFragmentationPercent | number:'1.0-1' }}%</div>
                </div>
                <div class="diagnostic" [class.high]="item.diagnosticLevel === 'High'">
                  <div class="diagnostic-title"><mat-icon>psychology</mat-icon>{{ item.diagnosticHeadline }}</div>
                  <p>{{ item.diagnosticSummary }}</p>
                  <div class="inspect"><mat-icon>checklist</mat-icon>{{ item.suggestedInspection }}</div>
                </div>
                <div class="metrics six">
                  <div><span>Page</span><strong>{{ item.pageCount | number }}</strong></div><div><span>Boyut</span><strong>{{ item.sizeMb | number:'1.0-1' }} MB</strong></div><div><span>Seek</span><strong>{{ item.userSeeks | number }}</strong></div><div><span>Scan</span><strong>{{ item.userScans | number }}</strong></div><div><span>Lookup</span><strong>{{ item.userLookups | number }}</strong></div><div><span>Update</span><strong>{{ item.userUpdates | number }}</strong></div>
                </div>
                <details><summary>İndeks tanımını göster</summary><div class="columns"><span>Key</span><code>{{ item.keyColumns || '—' }}</code></div>@if (item.includeColumns) { <div class="columns"><span>Include</span><code>{{ item.includeColumns }}</code></div> }@if (item.filterDefinition) { <div class="columns"><span>Filter</span><code>{{ item.filterDefinition }}</code></div> }</details>
              </mat-card>
            }
          </div>
        }
      </section>

      <section>
        <div class="section-title">
          <div><h2>İndeks Aksiyon Planı</h2><p>SQL Server missing-index sinyali önce mevcut indekslerle karşılaştırılır. Workload klasörü tanımlıysa ilgili TXT sorgu dosyaları da destekleyici kanıt olur.</p></div>
          <span>DMV + mevcut indeks + workload</span>
        </div>
        @if (!missing().length) {
          <mat-card class="empty"><mat-icon>task_alt</mat-icon><p>Şu anda missing-index DMV kaydı yok.</p></mat-card>
        } @else {
          <div class="action-list">
            @for (item of missing(); track item.id) {
              <mat-card class="action-card" [class.safe]="item.decisionType === 'UseExisting'" [class.create]="item.decisionType === 'CreateIndexCandidate'">
                <div class="action-head">
                  <div>
                    <div class="scope"><mat-icon>dns</mat-icon>{{ item.serverName }}</div>
                    <h3>{{ item.databaseName }} › {{ item.tableName }}</h3>
                    <div class="decision" [class.safe]="item.decisionType === 'UseExisting'" [class.create]="item.decisionType === 'CreateIndexCandidate'"><mat-icon>{{ decisionIcon(item) }}</mat-icon><strong>{{ item.decisionTitle }}</strong></div>
                  </div>
                  <div class="impact"><strong>{{ item.avgUserImpact | number:'1.0-1' }}%</strong><span>DMV ETKİ</span></div>
                </div>

                <p class="decision-reason">{{ item.decisionReason }}</p>

                @if (item.matchingWorkloadFiles.length) {
                  <div class="workload-box">
                    <div class="box-title"><mat-icon>folder_open</mat-icon>TXT workload kanıtı</div>
                    <p>Bu tablo tanımlı sorgu dosyalarında kullanılıyor:</p>
                    <div class="file-badges">@for (file of item.matchingWorkloadFiles; track file) { <span>{{ file }}</span> }</div>
                  </div>
                }

                <div class="metrics four">
                  <div><span>Seek</span><strong>{{ item.userSeeks | number }}</strong></div><div><span>Scan</span><strong>{{ item.userScans | number }}</strong></div><div><span>Ort. maliyet</span><strong>{{ item.avgTotalUserCost | number:'1.0-1' }}</strong></div><div><span>Improvement</span><strong>{{ item.improvementMeasure | number:'1.0-0' }}</strong></div>
                </div>

                <div class="candidate-columns">
                  <div><span>Equality key</span><code>{{ item.equalityColumns || '—' }}</code></div>
                  <div><span>Inequality key</span><code>{{ item.inequalityColumns || '—' }}</code></div>
                  <div><span>Include</span><code>{{ item.includedColumns || '—' }}</code></div>
                </div>

                <details class="existing">
                  <summary><mat-icon>account_tree</mat-icon> Karşılaştırılan mevcut indeksler ({{ item.existingIndexes.length }})</summary>
                  @if (!item.existingIndexes.length) {
                    <p>Bu tablo için mevcut indeks snapshot'ı bulunamadı.</p>
                  } @else {
                    <div class="index-table">
                      @for (idx of item.existingIndexes; track idx.indexName) {
                        <div class="index-row">
                          <div><strong>{{ idx.indexName }}</strong><small>@if (idx.isPrimaryKey) { PK } @if (idx.isUnique) { UNIQUE } @if (idx.hasFilter) { FILTERED }</small></div>
                          <div><span>Key</span><code>{{ idx.keyColumns || '—' }}</code></div>
                          <div><span>Include</span><code>{{ idx.includeColumns || '—' }}</code></div>
                          <div class="rw"><span>Read <b>{{ idx.reads | number }}</b></span><span>Write <b>{{ idx.writes | number }}</b></span></div>
                        </div>
                      }
                    </div>
                  }
                </details>

                @if (item.proposedCreateSql) {
                  <details class="sql-plan" open>
                    <summary><mat-icon>add_circle</mat-icon> CREATE INDEX taslağı</summary>
                    <div class="sql-head"><span>Önce test ortamı + execution plan + write maliyeti doğrulanmalı.</span><button mat-stroked-button type="button" (click)="copySql(item)"><mat-icon>{{ copiedSqlId() === item.id ? 'check' : 'content_copy' }}</mat-icon>{{ copiedSqlId() === item.id ? 'Kopyalandı' : 'SQL Kopyala' }}</button></div>
                    <pre>{{ item.proposedCreateSql }}</pre>
                  </details>
                }
              </mat-card>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.readonly{display:flex;align-items:center;gap:6px;border:1px solid #dbe2ec;border-radius:999px;padding:7px 10px;color:#64748b;font-size:.72rem;background:#fff}.readonly mat-icon{font-size:16px;width:16px;height:16px}.coverage-warning{display:flex;gap:11px;margin:-5px 0 18px;padding:13px 15px;border:1px solid #f2d5a2;background:#fff8e8;border-radius:12px;color:#744b00}.coverage-warning>mat-icon{margin-top:1px}.coverage-warning strong{font-size:.8rem}.coverage-warning p{margin:4px 0 7px;font-size:.7rem;color:#81632a}.coverage-warning details{margin-top:5px}.coverage-warning summary{cursor:pointer;font-size:.68rem;font-weight:700}.coverage-warning pre{white-space:pre-wrap;margin:6px 0 0;padding:8px 9px;background:#fff;border:1px solid #f1dfbd;border-radius:7px;color:#6a542d;font-size:.65rem}.loading{height:240px;display:grid;place-items:center}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:22px}.summary mat-card{padding:14px 16px;border:1px solid #e2e7ef;box-shadow:none}.summary span{display:block;color:#778297;font-size:.68rem}.summary strong{display:block;margin-top:5px;font-size:1.13rem}.summary small{display:block;margin-top:3px;color:#9aa4b4;font-size:.6rem}section{margin-top:27px}.section-title{display:flex;justify-content:space-between;align-items:flex-end;margin-bottom:10px}.section-title h2{font-size:1.02rem;margin:0}.section-title p{margin:4px 0 0;color:#7c899b;font-size:.68rem;max-width:900px}.section-title>span{font-size:.67rem;color:#8792a3;background:#f0f3f7;border-radius:999px;padding:5px 8px}.empty{padding:24px;display:flex;gap:10px;align-items:center;color:#667085}.empty mat-icon{color:#279063}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(430px,1fr));gap:14px}.card,.action-card{padding:17px;border:1px solid #e1e7f0;border-radius:15px;box-shadow:0 7px 24px rgba(15,23,42,.045)}.head,.action-head{display:flex;justify-content:space-between;gap:14px}.head strong,.head span{display:block}.head strong{margin-top:4px;font-size:.82rem}.head>div>span{font-size:.71rem;color:#748196;margin-top:3px}.scope{display:flex;align-items:center;gap:5px;color:#8290a3;font-size:.64rem}.scope mat-icon{font-size:14px;width:14px;height:14px}.badges{display:flex;gap:6px;flex-wrap:wrap;margin-top:8px}.badges span{display:flex;align-items:center;gap:3px;background:#f0f3f7;color:#5f6e82;border-radius:999px;padding:4px 7px;font-size:.6rem}.badges .special{background:#e9efff;color:#3157a4}.level{background:#fff5dd;color:#986700;border-radius:9px;height:max-content;padding:7px 9px;font-weight:780}.level.high{background:#ffeded;color:#aa3030}.diagnostic{margin-top:12px;padding:11px 12px;border:1px solid #dfe6f7;background:#f7f9ff;border-radius:10px}.diagnostic.high{background:#fff9f1;border-color:#f0dfca}.diagnostic-title{display:flex;align-items:center;gap:5px;color:#344d77;font-size:.7rem;font-weight:800}.diagnostic-title mat-icon{font-size:17px;width:17px;height:17px}.diagnostic p{margin:6px 0;color:#526177;font-size:.73rem;line-height:1.45}.inspect{display:flex;align-items:flex-start;gap:5px;color:#35506e;font-size:.68rem;font-weight:650}.inspect mat-icon{font-size:15px;width:15px;height:15px;color:#21805a}.metrics{display:grid;border-top:1px solid #edf0f5;border-bottom:1px solid #edf0f5;margin:13px 0}.metrics.six{grid-template-columns:repeat(6,1fr)}.metrics.four{grid-template-columns:repeat(4,1fr)}.metrics div{text-align:center;padding:9px 5px;border-right:1px solid #edf0f5}.metrics div:last-child{border:0}.metrics span{display:block;color:#778297;font-size:.61rem}.metrics strong{font-size:.79rem}details{font-size:.7rem;color:#59687d}summary{cursor:pointer;font-weight:700}.columns{display:grid;grid-template-columns:70px 1fr;gap:8px;margin-top:7px;font-size:.7rem}.columns span{color:#778297}.columns code{white-space:normal;word-break:break-word;color:#334155}.action-list{display:grid;gap:14px}.action-card{position:relative;border-left:4px solid #d3922a}.action-card.create{border-left-color:#3157d5}.action-card.safe{border-left-color:#1e9364;background:#fcfffd}.action-head h3{margin:5px 0 8px;font-size:.93rem}.impact{text-align:center;background:#f4f6fa;border-radius:10px;padding:7px 9px;height:max-content;min-width:72px}.impact strong{display:block;font-size:1rem}.impact span{font-size:.54rem;color:#778297}.decision{display:flex;align-items:center;gap:6px;width:max-content;max-width:100%;padding:6px 9px;border-radius:8px;background:#fff4df;color:#915c00;font-size:.7rem}.decision.create{background:#eaf0ff;color:#294db7}.decision.safe{background:#e9f8f0;color:#157249}.decision mat-icon{font-size:17px;width:17px;height:17px}.decision-reason{font-size:.76rem;line-height:1.5;color:#526176;margin:12px 0}.workload-box{padding:10px 11px;border:1px solid #d8e7df;background:#f3faf6;border-radius:10px}.box-title{display:flex;align-items:center;gap:5px;color:#237151;font-size:.68rem;font-weight:800}.box-title mat-icon{font-size:16px;width:16px;height:16px}.workload-box p{margin:5px 0;color:#607366;font-size:.67rem}.file-badges{display:flex;gap:5px;flex-wrap:wrap}.file-badges span{background:#fff;border:1px solid #d9e9df;border-radius:999px;padding:4px 7px;font-size:.61rem;color:#486455}.candidate-columns{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin:11px 0}.candidate-columns div{padding:9px;background:#f7f9fc;border-radius:8px;min-width:0}.candidate-columns span,.candidate-columns code{display:block}.candidate-columns span{font-size:.6rem;color:#8792a3;margin-bottom:4px}.candidate-columns code{font-size:.67rem;white-space:normal;word-break:break-word}.existing{margin-top:10px;border-top:1px solid #edf0f5;padding-top:9px}.existing summary,.sql-plan summary{display:flex;align-items:center;gap:5px;width:max-content}.existing summary mat-icon,.sql-plan summary mat-icon{font-size:16px;width:16px;height:16px}.index-table{display:grid;gap:6px;margin-top:9px}.index-row{display:grid;grid-template-columns:1.1fr 1.6fr 1.4fr .7fr;gap:8px;align-items:start;padding:9px;border:1px solid #e7ebf1;border-radius:8px;background:#fbfcfe}.index-row strong,.index-row small{display:block}.index-row strong{font-size:.68rem}.index-row small{margin-top:3px;color:#8a95a5;font-size:.55rem}.index-row span{font-size:.58rem;color:#8792a3}.index-row code{display:block;margin-top:3px;font-size:.62rem;white-space:normal;word-break:break-word}.rw{display:flex;gap:7px;justify-content:flex-end}.rw b{color:#344054}.sql-plan{margin-top:12px;border-top:1px solid #edf0f5;padding-top:10px}.sql-head{display:flex;justify-content:space-between;align-items:center;gap:10px;margin:9px 0 6px}.sql-head span{font-size:.65rem;color:#7d899a}.sql-plan pre{margin:0;white-space:pre-wrap;word-break:break-word;background:#0f172a;color:#dbe7f3;border-radius:9px;padding:12px;font-size:.69rem;line-height:1.45}@media(max-width:1000px){.summary{grid-template-columns:repeat(2,1fr)}.grid{grid-template-columns:1fr}.heading{display:block}.metrics.six{grid-template-columns:repeat(3,1fr)}.index-row{grid-template-columns:1fr 1fr}.candidate-columns{grid-template-columns:1fr}.sql-head{align-items:flex-start}}@media(max-width:650px){.summary{grid-template-columns:1fr}.metrics.six,.metrics.four{grid-template-columns:repeat(2,1fr)}.action-head{display:block}.impact{margin-top:8px;width:max-content}.index-row{grid-template-columns:1fr}.rw{justify-content:flex-start}}
  `]
})
export class IndexesComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly fragmented = signal<FragmentedIndex[]>([]);
  readonly missing = signal<MissingIndexCandidate[]>([]);
  readonly coverage = signal<CollectorCoverage[]>([]);
  readonly loading = signal(true);
  readonly copiedSqlId = signal<number | null>(null);

  readonly coverageWarnings = () => this.coverage().filter(x => x.status === 'Warning' || x.status === 'Failed');

  ngOnInit(): void {
    timer(0, 300_000).pipe(
      switchMap(() => forkJoin({
        fragmented: this.api.getFragmentedIndexes(200),
        missing: this.api.getMissingIndexCandidates(100),
        coverage: this.api.getCollectorCoverage('IndexAdvisor')
      })),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.fragmented.set(result.fragmented);
        this.missing.set(result.missing);
        this.coverage.set(result.coverage);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  readCount(item: FragmentedIndex): number { return item.userSeeks + item.userScans + item.userLookups; }
  maintenanceCandidates(): number {
    return this.fragmented().filter(x =>
      (x.pageCount ?? 0) >= 1000 && (x.avgFragmentationPercent ?? 0) >= 30
    ).length;
  }
  createCandidates(): number { return this.missing().filter(x => x.decisionType === 'CreateIndexCandidate').length; }
  consolidateCandidates(): number { return this.missing().filter(x => x.decisionType === 'ConsolidateOrCreate').length; }
  coveredMissing(): number { return this.missing().filter(x => x.decisionType === 'UseExisting').length; }

  decisionIcon(item: MissingIndexCandidate): string {
    if (item.decisionType === 'UseExisting') return 'shield';
    if (item.decisionType === 'CreateIndexCandidate') return 'add_circle';
    return 'merge_type';
  }

  async copySql(item: MissingIndexCandidate): Promise<void> {
    if (!item.proposedCreateSql) return;
    await navigator.clipboard.writeText(item.proposedCreateSql);
    this.copiedSqlId.set(item.id);
    setTimeout(() => this.copiedSqlId.set(null), 1600);
  }
}
