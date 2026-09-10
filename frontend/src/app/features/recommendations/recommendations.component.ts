import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { timer, switchMap } from 'rxjs';
import { RecommendationListItem } from '../../core/models/analysis.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

type RecommendationFilter = 'New' | 'All' | 'Resolved';

@Component({
  selector: 'app-recommendations',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Öneriler</h1>
        <p class="page-subtitle">Her önerinin hangi bulguya, hangi SQL'e, hangi nesneye ve hangi riske karşı üretildiğini görün.</p>
      </div>
      <div class="guard"><mat-icon>verified_user</mat-icon><div><strong>DBA kontrollü</strong><small>Advisor otomatik SQL çalıştırmaz.</small></div></div>
    </div>

    <div class="toolbar panel">
      <div class="filter-group">
        <button type="button" [class.active]="statusFilter() === 'New'" (click)="statusFilter.set('New')">Aktif</button>
        <button type="button" [class.active]="statusFilter() === 'All'" (click)="statusFilter.set('All')">Tümü</button>
        <button type="button" [class.active]="statusFilter() === 'Resolved'" (click)="statusFilter.set('Resolved')">Çözülen</button>
      </div>
      <div class="summary"><span><strong>{{ activeCount() }}</strong> aktif</span><span><strong>{{ highPriorityCount() }}</strong> yüksek öncelik</span></div>
    </div>

    @if (aiError()) {
      <div class="ai-error"><mat-icon>error_outline</mat-icon><span>{{ aiError() }}</span><button type="button" (click)="aiError.set(null)">Kapat</button></div>
    }

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!visibleRecommendations().length) {
      <div class="empty panel">
        <mat-icon>tips_and_updates</mat-icon>
        <h3>{{ statusFilter() === 'New' ? 'Aktif öneri yok' : 'Bu filtrede kayıt yok' }}</h3>
        <p>Bir bulgu aksiyon gerektirdiğinde kaynak problemi ve hedefiyle birlikte burada gösterilir.</p>
      </div>
    } @else {
      <div class="list">
        @for (item of visibleRecommendations(); track item.id) {
          <mat-card class="recommendation" [class.resolved]="item.status === 'Resolved'">
            <mat-card-content>
              <div class="card-head">
                <div class="identity">
                  <div class="icon-wrap s{{ item.severity }}"><mat-icon>{{ item.icon }}</mat-icon></div>
                  <div>
                    <div class="eyebrow">
                      <span class="severity s{{ item.severity }}">{{ severityText(item.severity) }}</span>
                      <span>{{ item.categoryLabel }}</span>
                      <span>{{ item.ruleName }}</span>
                    </div>
                    <h2>{{ item.title }}</h2>
                    <div class="scope"><mat-icon>my_location</mat-icon>{{ item.scopeText }}</div>
                  </div>
                </div>
                <div class="priority"><strong>{{ item.priorityScore | number:'1.0-0' }}</strong><small>ÖNCELİK</small></div>
              </div>

              <section class="source-box">
                <div class="source-title"><mat-icon>link</mat-icon>Bu öneri hangi probleme ait?</div>
                <strong>{{ item.findingTitle }}</strong>
                <p>{{ item.whatWasFound }}</p>
                <div class="source-meta">
                  <span>Sunucu <b>{{ item.serverName }}</b></span>
                  <span>Veritabanı <b>{{ item.databaseName || 'DB bağlamı çözülemedi' }}</b></span>
                  <span>Nesne / tablo <b>{{ item.objectName || (item.queryId ? 'Plan içinden nesne çözümlenemedi' : 'Sunucu geneli') }}</b></span>
                  <span>Bulgu #<b>{{ item.findingId }}</b></span>
                </div>
              </section>

              @if (item.queryId) {
                <section class="query-context">
                  <div class="query-head">
                    <div class="query-title"><mat-icon>code</mat-icon><strong>Bu öneriye neden olan SQL</strong></div>
                    <div class="query-flags">
                      @if (item.queryHash) { <span>Hash {{ item.queryHash }}</span> }
                      <span [class.ok]="item.hasExecutionPlan">{{ item.hasExecutionPlan ? 'Execution plan mevcut' : 'Plan cache XML yok' }}</span>
                    </div>
                  </div>
                  @if (item.objectName) {
                    <div class="objects"><span>Kullandığı nesneler</span><strong>{{ item.objectName }}</strong></div>
                  }
                  @if (item.queryText) {
                    <pre class="problem-sql">{{ item.queryText }}</pre>
                  } @else {
                    <div class="query-empty">SQL metni bu plan cache örneğinde alınamadı.</div>
                  }
                </section>
              }

              <div class="recommend-grid">
                <section class="answer action">
                  <div class="answer-title"><mat-icon>task_alt</mat-icon>Önerilen aksiyon</div>
                  <p>{{ item.recommendedAction }}</p>
                </section>
                <section class="answer">
                  <div class="answer-title"><mat-icon>psychology_alt</mat-icon>Neden bu aksiyon?</div>
                  <p>{{ item.explanation }}</p>
                </section>
                <section class="answer">
                  <div class="answer-title"><mat-icon>warning_amber</mat-icon>Problem neden önemli?</div>
                  <p>{{ item.whyItMatters }}</p>
                </section>
              </div>

              <div class="decision-bar">
                <div><span>Beklenen fayda</span><strong>{{ benefitText(item.expectedBenefit) }}</strong></div>
                <div><span>Risk</span><strong>{{ riskText(item.riskLevel) }}</strong></div>
                <div><span>Güven</span><strong>%{{ item.confidenceScore | number:'1.0-0' }}</strong></div>
                <div><span>Uygulama</span><strong class="safe">DBA onayı gerekli</strong></div>
                <div><span>Durum</span><strong>{{ statusText(item.status) }}</strong></div>
              </div>

              <section class="ai-box">
                <div class="ai-copy">
                  <div class="ai-icon"><mat-icon>smart_toy</mat-icon></div>
                  <div>
                    <strong>AI ile ikinci görüş al</strong>
                    <p>Advisor; bulgu, SQL, execution plan, mevcut indeksler, statistics, missing-index ve TXT workload bağlamını tek bir danışma metnine dönüştürür.</p>
                  </div>
                </div>
                <button mat-flat-button type="button" (click)="copyAiPrompt(item)" [disabled]="aiLoadingId() === item.id">
                  <mat-icon>{{ aiCopiedId() === item.id ? 'check' : 'content_copy' }}</mat-icon>
                  {{ aiLoadingId() === item.id ? 'Hazırlanıyor' : aiCopiedId() === item.id ? 'AI metni kopyalandı' : 'AI’ye Sor Metnini Kopyala' }}
                </button>
              </section>

              <details class="evidence">
                <summary><mat-icon>science</mat-icon> Kaynak bulgunun teknik kanıtı</summary>
                <p>{{ item.findingTechnicalDescription }}</p>
              </details>

              @if (item.scriptText) {
                <details class="script-box">
                  <summary><mat-icon>terminal</mat-icon> Salt-okunur tanı sorgusu</summary>
                  <div class="script-head">
                    <span>Bu sorgu yalnız teşhis amacıyla verilir.</span>
                    <button mat-stroked-button type="button" (click)="copyScript(item.id, item.scriptText)">
                      <mat-icon>{{ copiedId() === item.id ? 'check' : 'content_copy' }}</mat-icon>
                      {{ copiedId() === item.id ? 'Kopyalandı' : 'Kopyala' }}
                    </button>
                  </div>
                  <pre>{{ item.scriptText }}</pre>
                </details>
              }
            </mat-card-content>
          </mat-card>
        }
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:22px;align-items:flex-start;margin-bottom:18px}.guard{display:flex;align-items:center;gap:9px;background:#eaf8f1;color:#116c47;border:1px solid #c9ead9;border-radius:12px;padding:9px 12px}.guard mat-icon{font-size:20px;width:20px;height:20px}.guard strong,.guard small{display:block}.guard strong{font-size:.74rem}.guard small{font-size:.64rem;margin-top:2px;color:#4d8b70}
    .toolbar{display:flex;justify-content:space-between;align-items:center;padding:9px 11px;margin-bottom:16px}.filter-group{display:flex;gap:5px}.filter-group button{border:0;background:transparent;border-radius:8px;padding:8px 12px;color:#68768b;font-size:.76rem;font-weight:700;cursor:pointer}.filter-group button.active{background:#e9eefc;color:#2948b5}.summary{display:flex;gap:8px}.summary span{font-size:.7rem;color:#7d899c;background:#f6f8fb;border-radius:8px;padding:7px 9px}.summary strong{color:#344054;margin-right:3px}.ai-error{display:flex;align-items:center;gap:7px;margin-bottom:14px;padding:10px 12px;background:#fff0f0;color:#9d2e2e;border:1px solid #efcccc;border-radius:10px;font-size:.74rem}.ai-error span{flex:1}.ai-error mat-icon{font-size:18px;width:18px;height:18px}.ai-error button{border:0;background:transparent;color:#9d2e2e;font-weight:700;cursor:pointer}
    .loading{height:230px;display:grid;place-items:center}.empty{min-height:270px;display:grid;place-items:center;text-align:center;padding:40px}.empty mat-icon{font-size:46px;width:46px;height:46px;color:#b17a00}.empty h3,.empty p{margin:0}.empty p{max-width:600px;color:#718096}
    .list{display:grid;gap:16px}.recommendation{border:1px solid #e0e6ef;border-radius:16px;box-shadow:0 8px 28px rgba(15,23,42,.05);overflow:hidden}.recommendation.resolved{opacity:.69}.recommendation mat-card-content{padding:20px 22px 18px}
    .card-head{display:flex;justify-content:space-between;gap:18px}.identity{display:flex;gap:13px;min-width:0}.icon-wrap{width:42px;height:42px;flex:0 0 42px;border-radius:11px;display:grid;place-items:center;background:#eef2f7;color:#536174}.icon-wrap.s3{background:#fff0df;color:#b45e12}.icon-wrap.s4{background:#ffe8e5;color:#b52e2a}.icon-wrap mat-icon{font-size:21px;width:21px;height:21px}.eyebrow{display:flex;gap:6px;flex-wrap:wrap;align-items:center;color:#748196;font-size:.68rem}.eyebrow span{background:#f3f6f9;border-radius:999px;padding:4px 7px}.eyebrow .severity{font-weight:800}.severity.s4{background:#fee2e2;color:#a91d1d}.severity.s3{background:#ffedd5;color:#a64b00}.severity.s2{background:#fef3c7;color:#8a6300}.severity.s1{background:#e0f2fe;color:#075985}.severity.s0{background:#e9eef5;color:#475569}.card-head h2{margin:8px 0 5px;font-size:1.07rem;line-height:1.35}.scope{display:flex;align-items:center;gap:5px;color:#68778d;font-size:.72rem}.scope mat-icon{font-size:15px;width:15px;height:15px}.priority{min-width:70px;height:max-content;text-align:center;background:#f5f7fb;border:1px solid #e8ecf2;border-radius:11px;padding:9px}.priority strong{display:block;font-size:1.25rem}.priority small{font-size:.56rem;color:#7b8799;letter-spacing:.09em}
    .source-box{margin:17px 0 12px;padding:13px 14px;border:1px solid #dce4f5;border-radius:12px;background:#f6f8ff}.source-title{display:flex;align-items:center;gap:6px;color:#3550a5;font-size:.68rem;font-weight:800;text-transform:uppercase;letter-spacing:.04em}.source-title mat-icon{font-size:17px;width:17px;height:17px}.source-box>strong{display:block;margin-top:7px;font-size:.86rem;color:#263754}.source-box p{margin:5px 0 9px;color:#53627a;font-size:.78rem;line-height:1.45}.source-meta{display:flex;flex-wrap:wrap;gap:8px}.source-meta span{background:#fff;border:1px solid #e1e6f1;border-radius:7px;padding:5px 7px;color:#7e899a;font-size:.64rem}.source-meta b{color:#344054;margin-left:3px}
    .query-context{margin:0 0 12px;border:1px solid #d9e1ee;border-radius:12px;overflow:hidden;background:#0f172a;color:#dbe6f4}.query-head{display:flex;justify-content:space-between;gap:12px;align-items:center;padding:10px 12px;background:#162238;border-bottom:1px solid rgba(255,255,255,.08)}.query-title{display:flex;align-items:center;gap:6px;font-size:.75rem}.query-title mat-icon{font-size:17px;width:17px;height:17px}.query-flags{display:flex;flex-wrap:wrap;gap:6px}.query-flags span{font-size:.61rem;background:rgba(255,255,255,.08);padding:4px 7px;border-radius:999px;color:#c5cfdd}.query-flags span.ok{background:rgba(37,183,121,.17);color:#90e3bd}.objects{padding:9px 12px;background:#111d31;border-bottom:1px solid rgba(255,255,255,.07)}.objects span,.objects strong{display:block}.objects span{font-size:.6rem;color:#8391a7;text-transform:uppercase}.objects strong{margin-top:3px;font-size:.7rem;color:#e2e8f0;line-height:1.45}.problem-sql{margin:0;border-radius:0;background:#0f172a}.query-empty{padding:13px;color:#94a3b8;font-size:.72rem}
    .recommend-grid{display:grid;grid-template-columns:1.2fr 1fr 1fr;gap:10px}.answer{padding:13px 14px;border:1px solid #e7ebf2;border-radius:11px;background:#fafbfd}.answer.action{background:#f4faf7;border-color:#dcefe4}.answer-title{display:flex;align-items:center;gap:6px;font-size:.68rem;font-weight:800;color:#344054;text-transform:uppercase;letter-spacing:.035em}.answer-title mat-icon{font-size:17px;width:17px;height:17px;color:#60708a}.answer p{margin:8px 0 0;color:#4d5b70;font-size:.79rem;line-height:1.52}
    .decision-bar{display:grid;grid-template-columns:repeat(5,1fr);margin-top:12px;border:1px solid #e8ecf2;border-radius:10px;overflow:hidden}.decision-bar div{padding:10px 11px;border-right:1px solid #e8ecf2}.decision-bar div:last-child{border:0}.decision-bar span,.decision-bar strong{display:block}.decision-bar span{font-size:.61rem;color:#8a96a8}.decision-bar strong{margin-top:3px;font-size:.72rem;color:#344054}.decision-bar .safe{color:#16734b}.ai-box{display:flex;justify-content:space-between;align-items:center;gap:14px;margin-top:12px;padding:12px 13px;border:1px solid #dce5fa;background:#f7f9ff;border-radius:11px}.ai-copy{display:flex;gap:9px;align-items:flex-start}.ai-icon{width:32px;height:32px;flex:0 0 32px;display:grid;place-items:center;border-radius:8px;background:#e9efff;color:#3855b6}.ai-icon mat-icon{font-size:18px;width:18px;height:18px}.ai-copy strong{display:block;font-size:.73rem;color:#344767}.ai-copy p{margin:3px 0 0;color:#697891;font-size:.65rem;line-height:1.4}.ai-box button{white-space:nowrap}
    details{margin-top:12px;border-top:1px solid #edf0f5;padding-top:11px}summary{display:flex;align-items:center;gap:6px;width:max-content;cursor:pointer;font-weight:700;color:#59687d;font-size:.73rem}summary mat-icon{font-size:17px;width:17px;height:17px}.evidence p{font-size:.76rem;line-height:1.5;color:#5c687a}.script-head{display:flex;justify-content:space-between;align-items:center;margin:10px 0 6px;font-size:.7rem;color:#758196}pre{white-space:pre-wrap;word-break:break-word;background:#0f172a;color:#dbe6f4;border-radius:10px;padding:14px;max-height:330px;overflow:auto;font-size:.73rem;line-height:1.5}
    @media(max-width:1100px){.recommend-grid{grid-template-columns:1fr}.decision-bar{grid-template-columns:1fr 1fr}.decision-bar div{border-bottom:1px solid #e8ecf2}.ai-box{align-items:flex-start;flex-direction:column}}
    @media(max-width:700px){.heading{display:block}.guard{margin-top:12px;width:max-content}.toolbar{display:block}.summary{margin-top:8px}.card-head{display:block}.priority{margin-top:10px;width:66px}.decision-bar{grid-template-columns:1fr}.script-head{align-items:flex-start;gap:10px}.query-head{display:block}.query-flags{margin-top:8px}}
  `]
})
export class RecommendationsComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly recommendations = signal<RecommendationListItem[]>([]);
  readonly loading = signal(true);
  readonly statusFilter = signal<RecommendationFilter>('New');
  readonly copiedId = signal<number | null>(null);
  readonly aiLoadingId = signal<number | null>(null);
  readonly aiCopiedId = signal<number | null>(null);
  readonly aiError = signal<string | null>(null);

  readonly activeCount = computed(() => this.recommendations().filter(x => x.status === 'New').length);
  readonly highPriorityCount = computed(() => this.recommendations().filter(x => x.status === 'New' && x.priorityScore >= 70).length);
  readonly visibleRecommendations = computed(() => {
    const filter = this.statusFilter();
    return this.recommendations().filter(x => filter === 'All' || x.status === filter);
  });

  ngOnInit(): void {
    timer(0, 15_000).pipe(
      switchMap(() => this.api.getRecommendations()),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: items => {
        this.recommendations.set(items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  severityText(value: number): string {
    return ['Bilgi', 'Düşük', 'Orta', 'Yüksek', 'Kritik'][value] ?? `Seviye ${value}`;
  }

  statusText(value: string): string {
    return value === 'New' ? 'Aktif' : value === 'Resolved' ? 'Çözüldü' : value;
  }

  benefitText(value: string): string {
    return value === 'High' ? 'Yüksek' : value === 'Medium' ? 'Orta' : value === 'Low' ? 'Düşük' : value;
  }

  riskText(value: string): string {
    return value === 'High' ? 'Yüksek' : value === 'Medium' ? 'Orta' : value === 'Low' ? 'Düşük' : value;
  }

  async copyScript(id: number, script: string): Promise<void> {
    await navigator.clipboard.writeText(script);
    this.copiedId.set(id);
    setTimeout(() => this.copiedId.set(null), 1600);
  }

  copyAiPrompt(item: RecommendationListItem): void {
    this.aiError.set(null);
    this.aiLoadingId.set(item.id);
    this.api.getRecommendationAiPrompt(item.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: async result => {
        try {
          await navigator.clipboard.writeText(result.prompt);
          this.aiCopiedId.set(item.id);
          setTimeout(() => this.aiCopiedId.set(null), 2200);
        } catch {
          this.aiError.set('AI danışma metni hazırlandı ancak panoya kopyalanamadı. Tarayıcı clipboard iznini kontrol edin.');
        } finally {
          this.aiLoadingId.set(null);
        }
      },
      error: error => {
        this.aiLoadingId.set(null);
        this.aiError.set(error?.error?.detail || 'AI danışma metni hazırlanamadı.');
      }
    });
  }
}
