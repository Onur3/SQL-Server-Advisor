import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { timer, switchMap } from 'rxjs';
import { FindingListItem } from '../../core/models/analysis.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

type FindingFilter = 'Open' | 'All' | 'Resolved';

@Component({
  selector: 'app-findings',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Bulgular</h1>
        <p class="page-subtitle">Advisor'ın neyi, nerede ve neden problem olarak gördüğünü tek ekranda izleyin.</p>
      </div>
      <div class="summary-grid">
        <div class="summary-card"><span>Açık</span><strong>{{ openCount() }}</strong></div>
        <div class="summary-card danger"><span>Yüksek + Kritik</span><strong>{{ importantCount() }}</strong></div>
        <div class="summary-card"><span>Toplam</span><strong>{{ findings().length }}</strong></div>
      </div>
    </div>

    <div class="toolbar panel">
      <div class="filter-group">
        <button type="button" [class.active]="statusFilter() === 'Open'" (click)="statusFilter.set('Open')">Açık</button>
        <button type="button" [class.active]="statusFilter() === 'All'" (click)="statusFilter.set('All')">Tümü</button>
        <button type="button" [class.active]="statusFilter() === 'Resolved'" (click)="statusFilter.set('Resolved')">Çözülen</button>
      </div>
      <div class="legend"><mat-icon>info</mat-icon> Kartlar öncelik skoruna göre sıralanır.</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!visibleFindings().length) {
      <div class="empty panel">
        <mat-icon>task_alt</mat-icon>
        <h3>{{ statusFilter() === 'Open' ? 'Açık bulgu yok' : 'Bu filtrede kayıt yok' }}</h3>
        <p>Advisor eşik aşımı veya anlamlı performans sinyali tespit ettiğinde burada neden ve hedef bilgisiyle gösterir.</p>
      </div>
    } @else {
      <div class="list">
        @for (finding of visibleFindings(); track finding.id) {
          <mat-card class="finding" [class.resolved]="finding.status !== 'Open'" [class.critical]="finding.severity === 4" [class.high]="finding.severity === 3">
            <mat-card-content>
              <div class="card-head">
                <div class="identity">
                  <div class="icon-wrap s{{ finding.severity }}"><mat-icon>{{ finding.icon }}</mat-icon></div>
                  <div>
                    <div class="eyebrow">
                      <span class="severity s{{ finding.severity }}">{{ severityText(finding.severity) }}</span>
                      <span>{{ finding.categoryLabel }}</span>
                      <span>{{ finding.ruleName }}</span>
                    </div>
                    <h2>{{ finding.title }}</h2>
                    <div class="scope"><mat-icon>my_location</mat-icon>{{ finding.scopeText }}</div>
                  </div>
                </div>
                <div class="score" [attr.title]="'Etki ve güven birleşik skoru'">
                  <strong>{{ finding.findingScore | number:'1.0-0' }}</strong><small>ÖNCELİK</small>
                </div>
              </div>

              <div class="diagnosis-grid">
                <section class="answer primary">
                  <div class="answer-title"><mat-icon>search</mat-icon>Ne bulundu?</div>
                  <p>{{ finding.whatWasFound }}</p>
                </section>
                <section class="answer">
                  <div class="answer-title"><mat-icon>priority_high</mat-icon>Neden önemli?</div>
                  <p>{{ finding.whyItMatters }}</p>
                </section>
                <section class="answer action">
                  <div class="answer-title"><mat-icon>checklist</mat-icon>İlk neyi kontrol etmeliyim?</div>
                  <p>{{ finding.nextCheck }}</p>
                </section>
              </div>

              <div class="context-row">
                <div><span>Sunucu</span><strong>{{ finding.serverName }}</strong></div>
                <div><span>Veritabanı</span><strong>{{ finding.databaseName || 'DB bağlamı çözülemedi' }}</strong></div>
                <div><span>Nesne / tablo</span><strong [title]="finding.objectName || ''">{{ finding.objectName || (finding.queryId ? 'Plan içinden nesne çözümlenemedi' : 'Sunucu geneli') }}</strong></div>
                <div><span>Tekrar</span><strong>{{ finding.occurrenceCount }}</strong></div>
                <div><span>Durum</span><strong>{{ statusText(finding.status) }}</strong></div>
              </div>

              @if (finding.queryId) {
                <section class="query-context">
                  <div class="query-head">
                    <div class="query-title"><mat-icon>code</mat-icon><strong>Problemli sorgu</strong></div>
                    <div class="query-flags">
                      @if (finding.queryHash) { <span>Hash {{ finding.queryHash }}</span> }
                      <span [class.ok]="finding.hasExecutionPlan">{{ finding.hasExecutionPlan ? 'Execution plan mevcut' : 'Plan cache XML yok' }}</span>
                    </div>
                  </div>
                  @if (finding.objectName) {
                    <div class="objects"><span>Kullandığı nesneler</span><strong>{{ finding.objectName }}</strong></div>
                  }
                  @if (finding.queryText) {
                    <pre>{{ finding.queryText }}</pre>
                  } @else {
                    <div class="query-empty">SQL metni bu plan cache örneğinde alınamadı.</div>
                  }
                </section>
              }

              <details class="evidence">
                <summary><mat-icon>science</mat-icon> Teknik kanıtı göster</summary>
                <div class="evidence-body">
                  <p>{{ finding.technicalDescription }}</p>
                  <div class="metrics">
                    <span>Güven <strong>%{{ finding.confidenceScore | number:'1.0-0' }}</strong></span>
                    <span>Etki <strong>{{ finding.impactScore | number:'1.0-0' }}</strong></span>
                    <span>Kural <strong>{{ finding.ruleId }}</strong></span>
                    <span>İlk tespit <strong>{{ finding.firstDetectedAt | date:'dd.MM.yyyy HH:mm:ss' }}</strong></span>
                    <span>Son tespit <strong>{{ finding.lastDetectedAt | date:'dd.MM.yyyy HH:mm:ss' }}</strong></span>
                  </div>
                </div>
              </details>
            </mat-card-content>
          </mat-card>
        }
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;align-items:flex-start;gap:24px;margin-bottom:18px}.summary-grid{display:grid;grid-template-columns:repeat(3,minmax(92px,1fr));gap:9px}.summary-card{min-width:96px;background:#fff;border:1px solid #e1e7f0;border-radius:12px;padding:10px 12px}.summary-card span,.summary-card strong{display:block}.summary-card span{font-size:.65rem;color:#778398}.summary-card strong{margin-top:3px;font-size:1.2rem}.summary-card.danger strong{color:#b3332f}
    .toolbar{display:flex;justify-content:space-between;align-items:center;padding:9px 11px;margin-bottom:16px}.filter-group{display:flex;gap:5px}.filter-group button{border:0;background:transparent;border-radius:8px;padding:8px 12px;color:#68768b;font-size:.76rem;font-weight:700;cursor:pointer}.filter-group button.active{background:#e9eefc;color:#2948b5}.legend{display:flex;align-items:center;gap:5px;color:#8490a2;font-size:.7rem}.legend mat-icon{font-size:16px;width:16px;height:16px}
    .loading{height:230px;display:grid;place-items:center}.empty{min-height:270px;display:grid;place-items:center;text-align:center;padding:40px}.empty mat-icon{font-size:46px;width:46px;height:46px;color:#1d9865}.empty h3,.empty p{margin:0}.empty p{max-width:600px;color:#718096}
    .list{display:grid;gap:16px}.finding{position:relative;overflow:hidden;border:1px solid #e0e6ef;border-radius:16px;box-shadow:0 8px 28px rgba(15,23,42,.05)}.finding:before{content:'';position:absolute;inset:0 auto 0 0;width:4px;background:#9aa7ba}.finding.high:before{background:#e07b24}.finding.critical:before{background:#c63b35}.finding.resolved{opacity:.69}.finding mat-card-content{padding:20px 22px 18px}
    .card-head{display:flex;justify-content:space-between;gap:18px}.identity{display:flex;gap:13px;min-width:0}.icon-wrap{width:42px;height:42px;flex:0 0 42px;border-radius:11px;display:grid;place-items:center;background:#eef2f7;color:#536174}.icon-wrap.s3{background:#fff0df;color:#b45e12}.icon-wrap.s4{background:#ffe8e5;color:#b52e2a}.icon-wrap mat-icon{font-size:21px;width:21px;height:21px}.eyebrow{display:flex;gap:6px;flex-wrap:wrap;align-items:center;color:#748196;font-size:.68rem}.eyebrow span{background:#f3f6f9;border-radius:999px;padding:4px 7px}.eyebrow .severity{font-weight:800}.severity.s4{background:#fee2e2;color:#a91d1d}.severity.s3{background:#ffedd5;color:#a64b00}.severity.s2{background:#fef3c7;color:#8a6300}.severity.s1{background:#e0f2fe;color:#075985}.severity.s0{background:#e9eef5;color:#475569}.card-head h2{margin:8px 0 5px;font-size:1.07rem;line-height:1.35}.scope{display:flex;align-items:center;gap:5px;color:#68778d;font-size:.72rem}.scope mat-icon{font-size:15px;width:15px;height:15px}.score{min-width:70px;height:max-content;text-align:center;background:#f5f7fb;border:1px solid #e8ecf2;border-radius:11px;padding:9px}.score strong{display:block;font-size:1.25rem}.score small{font-size:.56rem;color:#7b8799;letter-spacing:.09em}
    .diagnosis-grid{display:grid;grid-template-columns:1.15fr 1fr 1fr;gap:10px;margin:17px 0 14px}.answer{padding:13px 14px;border:1px solid #e7ebf2;border-radius:11px;background:#fafbfd}.answer.primary{background:#f6f8ff;border-color:#dde4ff}.answer.action{background:#f4faf7;border-color:#dcefe4}.answer-title{display:flex;align-items:center;gap:6px;font-size:.7rem;font-weight:800;color:#344054;text-transform:uppercase;letter-spacing:.035em}.answer-title mat-icon{font-size:17px;width:17px;height:17px;color:#60708a}.answer p{margin:8px 0 0;color:#4d5b70;font-size:.8rem;line-height:1.5}
    .context-row{display:grid;grid-template-columns:1.1fr 1.1fr 1.6fr .55fr .65fr;border:1px solid #e8ecf2;border-radius:10px;overflow:hidden}.context-row div{padding:10px 12px;border-right:1px solid #e8ecf2;min-width:0}.context-row div:last-child{border:0}.context-row span,.context-row strong{display:block}.context-row span{font-size:.62rem;color:#8a96a8;margin-bottom:3px}.context-row strong{font-size:.73rem;color:#344054;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .query-context{margin-top:13px;border:1px solid #d9e1ee;border-radius:12px;overflow:hidden;background:#0f172a;color:#dbe6f4}.query-head{display:flex;justify-content:space-between;gap:12px;align-items:center;padding:10px 12px;background:#162238;border-bottom:1px solid rgba(255,255,255,.08)}.query-title{display:flex;align-items:center;gap:6px;font-size:.75rem}.query-title mat-icon{font-size:17px;width:17px;height:17px}.query-flags{display:flex;flex-wrap:wrap;gap:6px}.query-flags span{font-size:.61rem;background:rgba(255,255,255,.08);padding:4px 7px;border-radius:999px;color:#c5cfdd}.query-flags span.ok{background:rgba(37,183,121,.17);color:#90e3bd}.objects{padding:9px 12px;background:#111d31;border-bottom:1px solid rgba(255,255,255,.07)}.objects span,.objects strong{display:block}.objects span{font-size:.6rem;color:#8391a7;text-transform:uppercase}.objects strong{margin-top:3px;font-size:.7rem;color:#e2e8f0;line-height:1.45}.query-context pre{margin:0;max-height:260px;overflow:auto;padding:13px;white-space:pre-wrap;word-break:break-word;font-size:.72rem;line-height:1.5;color:#dbe6f4}.query-empty{padding:13px;color:#94a3b8;font-size:.72rem}
    .evidence{margin-top:13px;border-top:1px solid #edf0f5;padding-top:11px}.evidence summary{display:flex;align-items:center;gap:6px;width:max-content;cursor:pointer;color:#59687d;font-size:.74rem;font-weight:700}.evidence summary mat-icon{font-size:17px;width:17px;height:17px}.evidence-body{padding:10px 3px 2px}.evidence-body p{margin:0 0 10px;color:#59677a;line-height:1.55;font-size:.77rem}.metrics{display:flex;flex-wrap:wrap;gap:14px;color:#8390a2;font-size:.68rem}.metrics strong{color:#344054;margin-left:3px}
    @media(max-width:1050px){.diagnosis-grid{grid-template-columns:1fr}.context-row{grid-template-columns:1fr 1fr}.context-row div{border-bottom:1px solid #e8ecf2}.context-row div:nth-child(even){border-right:0}}
    @media(max-width:700px){.heading{display:block}.summary-grid{margin-top:12px}.toolbar{display:block}.legend{margin-top:8px}.card-head{display:block}.score{margin-top:10px;width:66px}.context-row{grid-template-columns:1fr}.context-row div{border-right:0}.query-head{display:block}.query-flags{margin-top:8px}}
  `]
})
export class FindingsComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly findings = signal<FindingListItem[]>([]);
  readonly loading = signal(true);
  readonly statusFilter = signal<FindingFilter>('Open');

  readonly openCount = computed(() => this.findings().filter(x => x.status === 'Open').length);
  readonly importantCount = computed(() => this.findings().filter(x => x.status === 'Open' && x.severity >= 3).length);
  readonly visibleFindings = computed(() => {
    const filter = this.statusFilter();
    return this.findings().filter(x => filter === 'All' || x.status === filter);
  });

  ngOnInit(): void {
    timer(0, 15_000).pipe(
      switchMap(() => this.api.getFindings()),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: items => {
        this.findings.set(items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  severityText(value: number): string {
    return ['Bilgi', 'Düşük', 'Orta', 'Yüksek', 'Kritik'][value] ?? `Seviye ${value}`;
  }

  statusText(value: string): string {
    return value === 'Open' ? 'Açık' : value === 'Resolved' ? 'Çözüldü' : value;
  }
}
