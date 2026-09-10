import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin } from 'rxjs';
import { ServerListItem } from '../../core/models/server.models';
import { UpdateWorkloadSettingsRequest, WorkloadFile, WorkloadSettings } from '../../core/models/workload.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="heading">
      <div>
        <h1 class="page-title">Yönetim & Workload Kaynakları</h1>
        <p class="page-subtitle">Uygulamada kullanılan TXT sorgu klasörünü tanımlayın. Worker dosyaları yalnız okur ve analiz eder; SQL çalıştırmaz.</p>
      </div>
      <div class="guard"><mat-icon>verified_user</mat-icon><div><strong>Salt okunur dosya analizi</strong><small>TXT içeriği execute edilmez.</small></div></div>
    </div>

    @if (loading()) {
      <div class="loading"><mat-spinner diameter="38" /></div>
    } @else {
      @if (message()) {
        <div class="message" [class.error]="messageError()"><mat-icon>{{ messageError() ? 'error_outline' : 'check_circle' }}</mat-icon><span>{{ message() }}</span></div>
      }

      <div class="layout">
        <mat-card class="settings-card">
          <h2>TXT Sorgu Klasörü</h2>
          <p class="intro">Örneğin ERP veya uygulama tarafından kullanılan sorgu dosyalarının bulunduğu klasörü girin. Yol SQLAdvisor veritabanında saklanır; GitHub'a yazılmaz.</p>

          <label class="switch-row">
            <input type="checkbox" [(ngModel)]="draft.enabled" />
            <div><strong>Workload dosya analizini aktif et</strong><span>Worker belirlenen periyotta *.txt dosyalarını tarar.</span></div>
          </label>

          <div class="field full">
            <label>Klasör yolu</label>
            <input [(ngModel)]="draft.folderPath" placeholder="D:\Uygulama\SqlQueries" />
            <small>Yerel disk veya Worker servis hesabının okuyabildiği UNC yol kullanılabilir.</small>
          </div>

          <div class="two-col">
            <div class="field">
              <label>Varsayılan SQL Server</label>
              <select [(ngModel)]="draft.defaultServerProfileId">
                <option [ngValue]="null">Otomatik / tek aktif sunucu</option>
                @for (server of servers(); track server.id) {
                  <option [ngValue]="server.id">{{ server.name }} · {{ server.host }}</option>
                }
              </select>
            </div>
            <div class="field">
              <label>Varsayılan veritabanı</label>
              <input [(ngModel)]="draft.defaultDatabaseName" placeholder="Örn. ERP_DB" />
              <small>TXT içinde USE varsa o değer önceliklidir.</small>
            </div>
          </div>

          <div class="three-col">
            <label class="switch-row compact">
              <input type="checkbox" [(ngModel)]="draft.recursive" />
              <div><strong>Alt klasörleri tara</strong><span>Recursive *.txt</span></div>
            </label>
            <div class="field">
              <label>Tarama aralığı (sn)</label>
              <input type="number" min="60" max="86400" [(ngModel)]="draft.scanIntervalSeconds" />
            </div>
            <div class="field">
              <label>Maksimum dosya (KB)</label>
              <input type="number" min="16" max="10240" [(ngModel)]="draft.maxFileSizeKb" />
            </div>
          </div>

          <div class="actions">
            <button mat-flat-button type="button" (click)="save()" [disabled]="saving()"><mat-icon>save</mat-icon>{{ saving() ? 'Kaydediliyor' : 'Ayarları Kaydet' }}</button>
            <button mat-stroked-button type="button" (click)="scanNow()" [disabled]="scanning()"><mat-icon>manage_search</mat-icon>{{ scanning() ? 'İstek gönderildi' : 'Şimdi Tara' }}</button>
            <button mat-button type="button" (click)="reload()"><mat-icon>refresh</mat-icon>Durumu Yenile</button>
          </div>
        </mat-card>

        <mat-card class="status-card">
          <h2>Tarama Durumu</h2>
          <div class="status" [class.ok]="settings()?.lastStatus === 'Success'" [class.warn]="settings()?.lastStatus === 'Warning'">
            <mat-icon>{{ statusIcon() }}</mat-icon>
            <div><strong>{{ statusText() }}</strong><span>{{ settings()?.lastMessage || 'Henüz tarama yapılmadı.' }}</span></div>
          </div>
          <div class="status-grid">
            <div><span>Aktif TXT</span><strong>{{ settings()?.activeFiles || 0 }}</strong></div>
            <div><span>Son tarama</span><strong>{{ settings()?.lastScanAt ? (settings()?.lastScanAt | date:'dd.MM.yyyy HH:mm:ss') : '—' }}</strong></div>
            <div><span>Periyot</span><strong>{{ draft.scanIntervalSeconds }} sn</strong></div>
            <div><span>Recursive</span><strong>{{ draft.recursive ? 'Evet' : 'Hayır' }}</strong></div>
          </div>
          <div class="security-note"><mat-icon>shield</mat-icon><p>Worker dosyalardaki SELECT/UPDATE/DELETE ifadelerini çalıştırmaz. Metin yalnız tablo, alias ve kolon referansı çıkarmak; Advisor kanıtına eklemek için okunur.</p></div>
        </mat-card>
      </div>

      <section class="files-section">
        <div class="section-head"><div><h2>Taranan Workload Dosyaları</h2><p>Motorun indeks tavsiyelerinde destekleyici kanıt olarak kullanabileceği aktif dosyalar.</p></div><span>{{ files().length }} kayıt</span></div>
        @if (!files().length) {
          <mat-card class="empty"><mat-icon>folder_off</mat-icon><p>Henüz aktif TXT dosyası yok. Ayarları kaydedip “Şimdi Tara” kullanın.</p></mat-card>
        } @else {
          <div class="file-grid">
            @for (file of files(); track file.id) {
              <mat-card class="file-card">
                <div class="file-head"><mat-icon>description</mat-icon><div><strong>{{ file.fileName }}</strong><span>{{ file.databaseName || 'DB bağlamı belirtilmemiş' }}</span></div></div>
                <div class="path">{{ file.filePath }}</div>
                <div class="meta"><span>Son değişiklik <b>{{ file.lastWriteTimeUtc | date:'dd.MM.yyyy HH:mm' }}</b></span><span>Tarama <b>{{ file.lastScannedAt | date:'dd.MM.yyyy HH:mm' }}</b></span></div>
                <details><summary>Çıkarılan SQL bağlamı</summary><div class="context"><span>Nesneler</span><code>{{ file.referencedObjects || 'Çıkarılamadı' }}</code></div><div class="context"><span>Kolonlar</span><code>{{ file.referencedColumns || 'Çıkarılamadı' }}</code></div><p>{{ file.parseMessage }}</p></details>
              </mat-card>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    .heading{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:20px}.guard{display:flex;align-items:center;gap:9px;background:#eaf8f1;color:#116c47;border:1px solid #c9ead9;border-radius:12px;padding:9px 12px}.guard mat-icon{font-size:20px;width:20px;height:20px}.guard strong,.guard small{display:block}.guard strong{font-size:.74rem}.guard small{font-size:.64rem;margin-top:2px;color:#4d8b70}.loading{height:260px;display:grid;place-items:center}.message{display:flex;align-items:center;gap:7px;margin-bottom:14px;padding:10px 12px;background:#edf9f3;color:#16704b;border:1px solid #d0ebdd;border-radius:10px;font-size:.75rem}.message.error{background:#fff0f0;color:#9f2f2f;border-color:#efcccc}.message mat-icon{font-size:18px;width:18px;height:18px}.layout{display:grid;grid-template-columns:minmax(0,1.55fr) minmax(320px,.75fr);gap:16px}.settings-card,.status-card{padding:20px;border:1px solid #e1e7f0;border-radius:15px;box-shadow:0 7px 24px rgba(15,23,42,.045)}h2{margin:0;font-size:1rem}.intro{color:#6f7d91;font-size:.75rem;line-height:1.5;margin:6px 0 17px}.field{display:grid;gap:5px}.field label{font-size:.68rem;font-weight:800;color:#46556b}.field input,.field select{width:100%;height:39px;border:1px solid #d8e0ea;border-radius:8px;background:#fff;padding:0 10px;color:#243146}.field small{font-size:.61rem;color:#8b96a5}.field.full{margin:16px 0}.two-col{display:grid;grid-template-columns:1fr 1fr;gap:12px}.three-col{display:grid;grid-template-columns:1.1fr .8fr .8fr;gap:12px;align-items:end;margin-top:14px}.switch-row{display:flex;align-items:flex-start;gap:9px;padding:11px 12px;border:1px solid #e1e7ef;border-radius:10px;background:#fafbfd}.switch-row input{margin-top:3px}.switch-row strong,.switch-row span{display:block}.switch-row strong{font-size:.73rem}.switch-row span{font-size:.62rem;color:#8390a2;margin-top:3px}.switch-row.compact{padding:9px 10px}.actions{display:flex;gap:8px;flex-wrap:wrap;margin-top:18px}.status{display:flex;gap:10px;align-items:flex-start;margin-top:14px;padding:12px;border:1px solid #e1e7ef;border-radius:11px;background:#f7f9fc}.status.ok{background:#edf9f3;border-color:#d1ebdd}.status.warn{background:#fff8e9;border-color:#efdfb9}.status>mat-icon{color:#65758a}.status.ok>mat-icon{color:#14835a}.status.warn>mat-icon{color:#a36e00}.status strong,.status span{display:block}.status strong{font-size:.76rem}.status span{margin-top:3px;color:#66758a;font-size:.67rem;line-height:1.45}.status-grid{display:grid;grid-template-columns:1fr 1fr;border:1px solid #e7ebf1;border-radius:10px;overflow:hidden;margin-top:13px}.status-grid div{padding:10px;border-right:1px solid #e7ebf1;border-bottom:1px solid #e7ebf1}.status-grid div:nth-child(even){border-right:0}.status-grid div:nth-last-child(-n+2){border-bottom:0}.status-grid span,.status-grid strong{display:block}.status-grid span{font-size:.61rem;color:#8792a2}.status-grid strong{margin-top:3px;font-size:.72rem}.security-note{display:flex;gap:8px;margin-top:13px;color:#607087}.security-note mat-icon{font-size:18px;width:18px;height:18px;color:#287b5c}.security-note p{margin:0;font-size:.65rem;line-height:1.48}.files-section{margin-top:25px}.section-head{display:flex;justify-content:space-between;align-items:flex-end;margin-bottom:10px}.section-head h2{font-size:1rem}.section-head p{margin:4px 0 0;color:#7c899b;font-size:.68rem}.section-head>span{font-size:.65rem;color:#7d8999;background:#edf1f6;border-radius:999px;padding:5px 8px}.empty{padding:24px;display:flex;align-items:center;gap:9px;color:#68768b}.file-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(360px,1fr));gap:12px}.file-card{padding:15px;border:1px solid #e2e7ef;box-shadow:none}.file-head{display:flex;gap:8px;align-items:center}.file-head>mat-icon{color:#526ec9}.file-head strong,.file-head span{display:block}.file-head strong{font-size:.78rem}.file-head span{font-size:.62rem;color:#8490a1;margin-top:2px}.path{margin:9px 0;padding:7px 8px;background:#f5f7fa;border-radius:7px;color:#5f6d80;font-family:monospace;font-size:.62rem;word-break:break-all}.meta{display:flex;gap:12px;color:#8591a1;font-size:.61rem}.meta b{color:#46556b;margin-left:3px}details{margin-top:10px;border-top:1px solid #edf0f4;padding-top:8px;font-size:.66rem;color:#59687d}summary{cursor:pointer;font-weight:700}.context{display:grid;grid-template-columns:65px 1fr;gap:7px;margin-top:7px}.context span{color:#8792a2}.context code{white-space:normal;word-break:break-word}.file-card details p{margin:7px 0 0;color:#8490a2;line-height:1.45}@media(max-width:1000px){.layout{grid-template-columns:1fr}.heading{display:block}.guard{margin-top:10px;width:max-content}}@media(max-width:700px){.two-col,.three-col{grid-template-columns:1fr}.file-grid{grid-template-columns:1fr}.section-head{display:block}.section-head>span{display:inline-block;margin-top:7px}}
  `]
})
export class AdminComponent implements OnInit {
  private readonly api = inject(AdvisorApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly settings = signal<WorkloadSettings | null>(null);
  readonly servers = signal<ServerListItem[]>([]);
  readonly files = signal<WorkloadFile[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly scanning = signal(false);
  readonly message = signal<string | null>(null);
  readonly messageError = signal(false);

  draft: UpdateWorkloadSettingsRequest = {
    enabled: false,
    folderPath: '',
    recursive: false,
    defaultServerProfileId: null,
    defaultDatabaseName: '',
    scanIntervalSeconds: 300,
    maxFileSizeKb: 2048
  };

  ngOnInit(): void { this.reload(); }

  reload(): void {
    this.loading.set(true);
    forkJoin({
      settings: this.api.getWorkloadSettings(),
      servers: this.api.getServers(),
      files: this.api.getWorkloadFiles(true, 250)
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.settings.set(result.settings);
        this.servers.set(result.servers);
        this.files.set(result.files);
        this.draft = {
          enabled: result.settings.enabled,
          folderPath: result.settings.folderPath,
          recursive: result.settings.recursive,
          defaultServerProfileId: result.settings.defaultServerProfileId ?? null,
          defaultDatabaseName: result.settings.defaultDatabaseName ?? '',
          scanIntervalSeconds: result.settings.scanIntervalSeconds,
          maxFileSizeKb: result.settings.maxFileSizeKb
        };
        this.loading.set(false);
      },
      error: error => {
        this.loading.set(false);
        this.showMessage(error?.error || 'Yönetim ayarları yüklenemedi.', true);
      }
    });
  }

  save(): void {
    this.saving.set(true);
    this.api.updateWorkloadSettings(this.draft).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: settings => {
        this.settings.set(settings);
        this.saving.set(false);
        this.showMessage('Workload ayarları kaydedildi ve yeni tarama isteği gönderildi.', false);
      },
      error: error => {
        this.saving.set(false);
        this.showMessage(error?.error || 'Ayarlar kaydedilemedi.', true);
      }
    });
  }

  scanNow(): void {
    this.scanning.set(true);
    this.api.requestWorkloadScan().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.scanning.set(false);
        this.showMessage('Tarama isteği gönderildi. Worker en geç yaklaşık 30 saniye içinde isteği alır; Durumu Yenile ile sonucu görebilirsiniz.', false);
      },
      error: error => {
        this.scanning.set(false);
        this.showMessage(error?.error || 'Tarama isteği gönderilemedi.', true);
      }
    });
  }

  statusText(): string {
    const status = this.settings()?.lastStatus;
    if (status === 'Success') return 'Başarılı';
    if (status === 'Warning') return 'Uyarı';
    if (status === 'Disabled') return 'Kapalı';
    return 'Henüz taranmadı';
  }

  statusIcon(): string {
    const status = this.settings()?.lastStatus;
    if (status === 'Success') return 'check_circle';
    if (status === 'Warning') return 'warning_amber';
    if (status === 'Disabled') return 'pause_circle';
    return 'schedule';
  }

  private showMessage(value: unknown, isError: boolean): void {
    const text = typeof value === 'string' ? value : JSON.stringify(value);
    this.message.set(text);
    this.messageError.set(isError);
  }
}
