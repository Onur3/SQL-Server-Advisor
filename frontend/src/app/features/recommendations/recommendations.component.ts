import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { timer, switchMap } from 'rxjs';
import { RecommendationListItem } from '../../core/models/analysis.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-recommendations',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Öneriler</h1>
        <p class="page-subtitle">Bulgulara göre oluşturulan DBA inceleme ve iyileştirme önerileri.</p>
      </div>
      <div class="guard"><mat-icon>verified_user</mat-icon> Otomatik SQL çalıştırılmaz</div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!recommendations().length) {
      <div class="empty">
        <mat-icon>tips_and_updates</mat-icon>
        <h3>Henüz öneri yok</h3>
        <p>Aktif bir performans bulgusu oluştuğunda güvenli inceleme önerileri burada üretilecek.</p>
      </div>
    } @else {
      <div class="list">
        @for (item of recommendations(); track item.id) {
          <mat-card class="recommendation" [class.resolved]="item.status === 'Resolved'">
            <mat-card-content>
              <div class="top">
                <div>
                  <div class="meta">
                    <span class="severity s{{ item.severity }}">{{ severityText(item.severity) }}</span>
                    <span>{{ item.serverName }}</span>
                    <span>{{ item.ruleId }}</span>
                    <span>{{ item.status }}</span>
                  </div>
                  <h3>{{ item.title }}</h3>
                  <div class="finding-title">Kaynak bulgu: {{ item.findingTitle }}</div>
                </div>
                <div class="score"><strong>{{ item.priorityScore | number:'1.0-0' }}</strong><small>ÖNCELİK</small></div>
              </div>

              <p>{{ item.explanation }}</p>
              <div class="action"><strong>Önerilen aksiyon</strong><span>{{ item.recommendedAction }}</span></div>

              <div class="tags">
                <span>Beklenen fayda: <strong>{{ item.expectedBenefit }}</strong></span>
                <span>Risk: <strong>{{ item.riskLevel }}</strong></span>
                <span>Güven: <strong>%{{ item.confidenceScore | number:'1.0-0' }}</strong></span>
                <span>CanExecute: <strong>{{ item.canExecute ? 'true' : 'false' }}</strong></span>
              </div>

              @if (item.scriptText) {
                <details>
                  <summary>Salt-okunur tanı sorgusu</summary>
                  <div class="script-head">
                    <span>DBA incelemesi için</span>
                    <button mat-stroked-button type="button" (click)="copyScript(item.scriptText)">
                      <mat-icon>content_copy</mat-icon> Kopyala
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
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:18px}.guard{display:flex;align-items:center;gap:7px;background:#eaf7f0;color:#137348;border:1px solid #c6ead7;border-radius:10px;padding:9px 12px;font-size:.76rem;font-weight:700}.guard mat-icon{font-size:18px;width:18px;height:18px}.loading{height:220px;display:grid;place-items:center}.empty{min-height:260px;display:grid;place-items:center;text-align:center;background:#fff;border:1px solid #e3e7ef;border-radius:14px;padding:40px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#b17900}.empty h3,.empty p{margin:0}.empty p{color:#718096}.list{display:grid;gap:14px}.recommendation{border:1px solid #e3e7ef;border-radius:14px;box-shadow:0 3px 18px rgba(20,32,55,.04)}.recommendation.resolved{opacity:.67}.top{display:flex;justify-content:space-between;gap:18px}.top h3{margin:8px 0 4px;font-size:1.05rem}.finding-title{font-size:.74rem;color:#758196}.meta{display:flex;gap:7px;flex-wrap:wrap;color:#6b778c;font-size:.72rem}.meta span{background:#f4f6f9;border-radius:6px;padding:4px 7px}.meta .severity{font-weight:750}.severity.s4{background:#fee2e2;color:#a91d1d}.severity.s3{background:#ffedd5;color:#a64b00}.severity.s2{background:#fef3c7;color:#8a6300}.severity.s1{background:#e0f2fe;color:#075985}.severity.s0{background:#e9eef5;color:#475569}.score{min-width:68px;text-align:center;background:#f5f7fb;border-radius:10px;padding:8px}.score strong{display:block;font-size:1.2rem}.score small{font-size:.58rem;color:#7b8799;letter-spacing:.08em}.recommendation p{line-height:1.55;color:#4e5a6d}.action{display:grid;gap:4px;background:#f8fafc;border-left:3px solid #3157d5;border-radius:6px;padding:11px 13px;color:#425066;font-size:.82rem}.action strong{color:#26354e}.tags{display:flex;flex-wrap:wrap;gap:14px;margin-top:13px;font-size:.72rem;color:#7a8597}.tags strong{color:#344054}details{margin-top:14px;border-top:1px solid #edf0f5;padding-top:12px}summary{cursor:pointer;font-weight:650;color:#344054}.script-head{display:flex;justify-content:space-between;align-items:center;margin:10px 0 6px;font-size:.72rem;color:#758196}pre{white-space:pre-wrap;word-break:break-word;background:#111827;color:#d7e0ef;border-radius:9px;padding:14px;max-height:320px;overflow:auto;font-size:.74rem;line-height:1.45}@media(max-width:700px){.heading{display:block}.guard{margin-top:12px;width:max-content}.top{display:block}.score{margin-top:10px;width:60px}.script-head{align-items:flex-start;gap:10px}}
  `]
})
export class RecommendationsComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly recommendations = signal<RecommendationListItem[]>([]);
  readonly loading = signal(true);

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

  async copyScript(script: string): Promise<void> {
    await navigator.clipboard.writeText(script);
  }
}
