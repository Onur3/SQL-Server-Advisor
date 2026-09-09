import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { timer, switchMap } from 'rxjs';
import { FindingListItem } from '../../core/models/analysis.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-findings',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Bulgular</h1>
        <p class="page-subtitle">Collector tarafından üretilen aktif ve geçmiş performans bulguları.</p>
      </div>
      <div class="summary">
        <span><strong>{{ openCount() }}</strong> açık</span>
        <span><strong>{{ findings().length }}</strong> toplam</span>
      </div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else if (!findings().length) {
      <div class="empty">
        <mat-icon>check_circle</mat-icon>
        <h3>Henüz bulgu yok</h3>
        <p>Mevcut sağlık kuralları eşik aşımı tespit ettiğinde burada görünecek.</p>
      </div>
    } @else {
      <div class="list">
        @for (finding of findings(); track finding.id) {
          <mat-card class="finding" [class.resolved]="finding.status !== 'Open'">
            <mat-card-content>
              <div class="top">
                <div>
                  <div class="meta">
                    <span class="severity s{{ finding.severity }}">{{ severityText(finding.severity) }}</span>
                    <span>{{ finding.serverName }}</span>
                    <span>{{ finding.ruleId }}</span>
                    <span>{{ finding.category }}</span>
                  </div>
                  <h3>{{ finding.title }}</h3>
                </div>
                <div class="score"><strong>{{ finding.findingScore | number:'1.0-0' }}</strong><small>SKOR</small></div>
              </div>

              <p class="description">{{ finding.technicalDescription }}</p>

              <div class="metrics">
                <span>Güven <strong>%{{ finding.confidenceScore | number:'1.0-0' }}</strong></span>
                <span>Etki <strong>{{ finding.impactScore | number:'1.0-0' }}</strong></span>
                <span>Tekrar <strong>{{ finding.occurrenceCount }}</strong></span>
                <span>Durum <strong>{{ finding.status }}</strong></span>
                <span>Son tespit <strong>{{ finding.lastDetectedAt | date:'dd.MM.yyyy HH:mm:ss' }}</strong></span>
              </div>
            </mat-card-content>
          </mat-card>
        }
      </div>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;align-items:flex-start;gap:20px;margin-bottom:18px}.summary{display:flex;gap:10px}.summary span{background:#fff;border:1px solid #e2e7ef;border-radius:10px;padding:9px 12px;color:#64748b;font-size:.8rem}.summary strong{color:#172033;margin-right:4px}.loading{height:220px;display:grid;place-items:center}.empty{min-height:260px;display:grid;place-items:center;text-align:center;background:#fff;border:1px solid #e3e7ef;border-radius:14px;padding:40px}.empty mat-icon{font-size:42px;width:42px;height:42px;color:#27875c}.empty h3,.empty p{margin:0}.empty p{color:#718096}.list{display:grid;gap:14px}.finding{border:1px solid #e3e7ef;border-radius:14px;box-shadow:0 3px 18px rgba(20,32,55,.04)}.finding.resolved{opacity:.66}.top{display:flex;justify-content:space-between;gap:18px}.top h3{margin:8px 0 0;font-size:1rem}.meta{display:flex;flex-wrap:wrap;gap:7px;align-items:center;color:#6b778c;font-size:.73rem}.meta span{background:#f4f6f9;border-radius:6px;padding:4px 7px}.meta .severity{font-weight:750}.severity.s4{background:#fee2e2;color:#a91d1d}.severity.s3{background:#ffedd5;color:#a64b00}.severity.s2{background:#fef3c7;color:#8a6300}.severity.s1{background:#e0f2fe;color:#075985}.severity.s0{background:#e9eef5;color:#475569}.score{min-width:64px;text-align:center;background:#f5f7fb;border-radius:10px;padding:8px}.score strong{display:block;font-size:1.2rem}.score small{font-size:.58rem;color:#7b8799;letter-spacing:.08em}.description{color:#4e5a6d;line-height:1.55;margin:14px 0}.metrics{border-top:1px solid #edf0f5;padding-top:11px;display:flex;flex-wrap:wrap;gap:18px;color:#7a8597;font-size:.72rem}.metrics strong{color:#344054;margin-left:3px}@media(max-width:700px){.heading{display:block}.summary{margin-top:12px}.top{display:block}.score{margin-top:10px;width:60px}.metrics{display:grid;grid-template-columns:1fr 1fr}}
  `]
})
export class FindingsComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly findings = signal<FindingListItem[]>([]);
  readonly loading = signal(true);

  readonly openCount = () => this.findings().filter(x => x.status === 'Open').length;

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
}
