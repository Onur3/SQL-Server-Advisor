import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AdvisorApiService } from '../../core/services/advisor-api.service';
import { CreateServerRequest, ServerAuthenticationType, ServerListItem } from '../../core/models/server.models';

@Component({
  selector: 'app-servers',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatTableModule, MatProgressSpinnerModule],
  template: `
    <h1 class="page-title">SQL Sunucuları</h1>
    <p class="page-subtitle">İzlenecek SQL Server bağlantılarını yönetin. Monitored bağlantılar yalnızca okuma/performans izinleriyle kullanılmalıdır.</p>

    <div class="layout">
      <mat-card class="form-card">
        <mat-card-header><mat-card-title>Yeni SQL Server</mat-card-title></mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" class="form-grid">
            <mat-form-field appearance="outline"><mat-label>Profil adı</mat-label><input matInput formControlName="name" placeholder="SQL-PROD"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Host / IP</mat-label><input matInput formControlName="host" placeholder="192.168.1.20"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Port</mat-label><input matInput type="number" formControlName="port"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Başlangıç DB</mat-label><input matInput formControlName="defaultDatabase"></mat-form-field>
            <mat-form-field appearance="outline" class="wide">
              <mat-label>Kimlik doğrulama</mat-label>
              <mat-select formControlName="authenticationType">
                <mat-option [value]="auth.Windows">Windows Authentication</mat-option>
                <mat-option [value]="auth.SqlLogin">SQL Login</mat-option>
              </mat-select>
            </mat-form-field>
            @if (form.controls.authenticationType.value === auth.SqlLogin) {
              <mat-form-field appearance="outline"><mat-label>Kullanıcı</mat-label><input matInput formControlName="username" autocomplete="off"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Parola</mat-label><input matInput type="password" formControlName="password" autocomplete="new-password"></mat-form-field>
            }
            <div class="checks wide">
              <mat-checkbox formControlName="encrypt">Encrypt</mat-checkbox>
              <mat-checkbox formControlName="trustServerCertificate">Trust Server Certificate</mat-checkbox>
            </div>
          </form>
          @if (message()) { <div class="message" [class.error]="messageError()">{{ message() }}</div> }
          <div class="actions">
            <button mat-stroked-button type="button" (click)="test()" [disabled]="form.invalid || busy()">Bağlantıyı Test Et</button>
            <button mat-flat-button type="button" (click)="save()" [disabled]="form.invalid || busy()">Sunucuyu Ekle</button>
            @if (busy()) { <mat-spinner diameter="24" /> }
          </div>
        </mat-card-content>
      </mat-card>

      <div class="panel list-panel">
        <div class="list-title"><strong>İzlenen Sunucular</strong><span>{{ servers().length }} kayıt</span></div>
        @if (!servers().length) {
          <div class="no-data">Henüz sunucu eklenmedi.</div>
        } @else {
          <table mat-table [dataSource]="servers()">
            <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Sunucu</th><td mat-cell *matCellDef="let s"><strong>{{ s.name }}</strong><small>{{ s.host }}:{{ s.port }}</small></td></ng-container>
            <ng-container matColumnDef="auth"><th mat-header-cell *matHeaderCellDef>Auth</th><td mat-cell *matCellDef="let s">{{ s.authenticationType === auth.Windows ? 'Windows' : 'SQL Login' }}</td></ng-container>
            <ng-container matColumnDef="last"><th mat-header-cell *matHeaderCellDef>Son Bağlantı</th><td mat-cell *matCellDef="let s">{{ s.lastConnectedAt ? (s.lastConnectedAt | date:'dd.MM.yyyy HH:mm:ss') : '—' }}</td></ng-container>
            <ng-container matColumnDef="enabled"><th mat-header-cell *matHeaderCellDef>Aktif</th><td mat-cell *matCellDef="let s"><mat-slide-toggle [checked]="s.isEnabled" (change)="toggle(s,$event.checked)"></mat-slide-toggle></td></ng-container>
            <tr mat-header-row *matHeaderRowDef="columns"></tr><tr mat-row *matRowDef="let row; columns: columns"></tr>
          </table>
        }
      </div>
    </div>
  `,
  styles: [`
    .layout{display:grid;grid-template-columns:440px 1fr;gap:20px;align-items:start}.form-card,.list-panel{border:1px solid #e3e7ef;border-radius:14px;box-shadow:0 3px 18px rgba(20,32,55,.05)}.form-card mat-card-header{padding:20px 20px 6px}.form-card mat-card-content{padding:16px 20px 20px}.form-grid{display:grid;grid-template-columns:1fr 1fr;gap:2px 12px}.wide{grid-column:1/-1}.checks{display:flex;gap:18px;margin:0 0 16px}.actions{display:flex;gap:10px;align-items:center}.actions button[mat-flat-button]{background:#244fc5;color:#fff}.message{margin:4px 0 14px;padding:10px 12px;border-radius:8px;background:#eaf7f0;color:#146b48;font-size:.82rem}.message.error{background:#ffeded;color:#a12e2e}
    .list-panel{overflow:hidden}.list-title{display:flex;justify-content:space-between;padding:18px;border-bottom:1px solid #e8ebf1}.list-title span{color:#748095;font-size:.8rem}.no-data{padding:50px;text-align:center;color:#7a8597}table{width:100%}td small{display:block;color:#818b9c;margin-top:3px}th{color:#667085;font-size:.72rem}
    @media(max-width:1000px){.layout{grid-template-columns:1fr}}@media(max-width:600px){.form-grid{grid-template-columns:1fr}.wide{grid-column:auto}}
  `]
})
export class ServersComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(AdvisorApiService);

  readonly auth = ServerAuthenticationType;
  readonly servers = signal<ServerListItem[]>([]);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly messageError = signal(false);
  readonly columns = ['name', 'auth', 'last', 'enabled'];

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    host: ['', Validators.required],
    port: [1433, [Validators.required, Validators.min(1), Validators.max(65535)]],
    defaultDatabase: ['master', Validators.required],
    authenticationType: [ServerAuthenticationType.Windows, Validators.required],
    username: [''],
    password: [''],
    encrypt: [true],
    trustServerCertificate: [true]
  });

  ngOnInit(): void { this.load(); }

  test(): void {
    this.busy.set(true); this.message.set('');
    this.api.testServer(this.toRequest()).subscribe({
      next: r => { this.busy.set(false); this.messageError.set(false); this.message.set(`Bağlantı başarılı: ${r.serverName} · ${r.productVersion} · ${r.edition}`); },
      error: e => { this.busy.set(false); this.messageError.set(true); this.message.set(e?.error?.error ?? e?.error ?? 'Bağlantı testi başarısız.'); }
    });
  }

  save(): void {
    this.busy.set(true); this.message.set('');
    this.api.createServer(this.toRequest()).subscribe({
      next: () => { this.busy.set(false); this.messageError.set(false); this.message.set('Sunucu eklendi. Worker en geç 15 saniye içinde snapshot toplamaya başlayacak.'); this.form.patchValue({name:'', host:'', username:'', password:''}); this.load(); },
      error: e => { this.busy.set(false); this.messageError.set(true); this.message.set(e?.error?.error ?? e?.error ?? 'Sunucu eklenemedi.'); }
    });
  }

  toggle(server: ServerListItem, enabled: boolean): void {
    this.api.setServerEnabled(server.id, enabled).subscribe({ next: () => this.load() });
  }

  private load(): void {
    this.api.getServers().subscribe({ next: x => this.servers.set(x) });
  }

  private toRequest(): CreateServerRequest {
    const v = this.form.getRawValue();
    return { ...v, username: v.username || null, password: v.password || null };
  }
}
