import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { LanguageService } from './core/i18n/language.service';
import { AppLanguage } from './core/i18n/ui-translations';

interface AuthStatus {
  enabled: boolean;
  authenticated: boolean;
  username?: string | null;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatButtonModule,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  template: `
    @if (authLoading()) {
      <div class="auth-loading"><mat-spinner diameter="42" /></div>
    } @else if (authEnabled() && !authenticated()) {
      <div class="login-page">
        <div class="login-language language-switch" data-no-translate aria-label="Dil / Language">
          <button type="button" [class.active]="language() === 'tr'" (click)="setLanguage('tr')" aria-label="Türkçe">TR</button>
          <button type="button" [class.active]="language() === 'en'" (click)="setLanguage('en')" aria-label="English">EN</button>
        </div>
        <form class="login-card" (ngSubmit)="login()">
          <div class="login-mark"><mat-icon>storage</mat-icon></div>
          <h1>SQL Server Advisor</h1>
          <p>Performans Karar Destek Sistemi</p>
          <label>Kullanıcı adı</label>
          <input name="username" [(ngModel)]="username" autocomplete="username" required autofocus />
          <label>Şifre</label>
          <input name="password" [(ngModel)]="password" type="password" autocomplete="current-password" required />
          @if (loginError()) { <div class="login-error"><mat-icon>error_outline</mat-icon>{{ loginError() }}</div> }
          <button mat-flat-button type="submit" [disabled]="loginBusy() || !username.trim() || !password">
            @if (loginBusy()) { <mat-spinner diameter="18" /> } @else { <mat-icon>login</mat-icon> }
            Giriş Yap
          </button>
          <small><mat-icon>shield</mat-icon> Oturum sunucu tarafında HttpOnly cookie ile korunur.</small>
        </form>
      </div>
    } @else {
      <mat-sidenav-container class="shell">
        <mat-sidenav mode="side" opened class="sidebar">
          <a class="brand" routerLink="/">
            <div class="brand-mark"><mat-icon>storage</mat-icon></div>
            <div>
              <strong>SQL Server Advisor</strong>
              <small>Performans Karar Destek Sistemi</small>
            </div>
          </a>

          <div class="nav-section">GENEL</div>
          <mat-nav-list>
            <a mat-list-item routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{exact:true}">
              <mat-icon matListItemIcon>space_dashboard</mat-icon><span matListItemTitle>Genel Bakış</span>
            </a>
            <a mat-list-item routerLink="/servers" routerLinkActive="active">
              <mat-icon matListItemIcon>dns</mat-icon><span matListItemTitle>SQL Sunucuları</span>
            </a>
          </mat-nav-list>

          <div class="nav-section">PERFORMANS ANALİZİ</div>
          <mat-nav-list>
            <a mat-list-item routerLink="/telemetry" routerLinkActive="active">
              <mat-icon matListItemIcon>monitor_heart</mat-icon><span matListItemTitle>Beklemeler & Kilitler</span>
            </a>
            <a mat-list-item routerLink="/queries" routerLinkActive="active">
              <mat-icon matListItemIcon>query_stats</mat-icon><span matListItemTitle>Pahalı Sorgular</span>
            </a>
            <a mat-list-item routerLink="/indexes" routerLinkActive="active">
              <mat-icon matListItemIcon>account_tree</mat-icon><span matListItemTitle>İndeks Analizi</span>
            </a>
            <a mat-list-item routerLink="/statistics" routerLinkActive="active">
              <mat-icon matListItemIcon>analytics</mat-icon><span matListItemTitle>İstatistikler</span>
            </a>
          </mat-nav-list>

          <div class="nav-section">AKSİYON MERKEZİ</div>
          <mat-nav-list>
            <a mat-list-item routerLink="/findings" routerLinkActive="active">
              <mat-icon matListItemIcon>problem</mat-icon><span matListItemTitle>Bulgular</span>
            </a>
            <a mat-list-item routerLink="/recommendations" routerLinkActive="active">
              <mat-icon matListItemIcon>tips_and_updates</mat-icon><span matListItemTitle>Öneriler</span>
            </a>
          </mat-nav-list>

          <div class="nav-section">YÖNETİM</div>
          <mat-nav-list>
            <a mat-list-item routerLink="/admin" routerLinkActive="active">
              <mat-icon matListItemIcon>settings</mat-icon><span matListItemTitle>Workload Kaynakları</span>
            </a>
          </mat-nav-list>

          <div class="readonly-card">
            <mat-icon>shield</mat-icon>
            <div><strong>Salt okunur izleme</strong><small>Advisor üretim SQL'inde otomatik değişiklik yapmaz.</small></div>
          </div>
        </mat-sidenav>

        <mat-sidenav-content>
          <mat-toolbar class="topbar">
            <div>
              <strong>SQL Server Advisor</strong>
              <small>Veri → Bulgu → Öneri → DBA Aksiyonu</small>
            </div>
            <span class="spacer"></span>
            <div class="language-switch topbar-language" data-no-translate aria-label="Dil / Language">
              <button type="button" [class.active]="language() === 'tr'" (click)="setLanguage('tr')" aria-label="Türkçe">TR</button>
              <button type="button" [class.active]="language() === 'en'" (click)="setLanguage('en')" aria-label="English">EN</button>
            </div>
            <span class="read-only"><mat-icon>visibility</mat-icon> READ ONLY</span>
            @if (authEnabled()) {
              <button mat-stroked-button type="button" class="logout" (click)="logout()"><mat-icon>logout</mat-icon>Çıkış</button>
            }
          </mat-toolbar>
          <main class="content"><router-outlet /></main>
        </mat-sidenav-content>
      </mat-sidenav-container>
    }
  `,
  styles: [`
    .auth-loading{height:100vh;display:grid;place-items:center;background:#f3f6fb}.login-page{height:100vh;display:grid;place-items:center;background:radial-gradient(circle at top,#eef3ff 0,#f3f6fb 48%,#edf1f7 100%);padding:20px;position:relative}.login-card{width:min(390px,calc(100vw - 40px));padding:30px;background:#fff;border:1px solid #dfe5ef;border-radius:20px;box-shadow:0 22px 65px rgba(15,23,42,.12)}.login-mark{width:52px;height:52px;display:grid;place-items:center;border-radius:14px;background:#3157d5;color:#fff;margin-bottom:16px}.login-mark mat-icon{font-size:28px;width:28px;height:28px}.login-card h1{font-size:1.25rem;margin:0;color:#172033}.login-card>p{margin:5px 0 22px;color:#7c899b;font-size:.76rem}.login-card label{display:block;margin:12px 0 6px;color:#445066;font-size:.7rem;font-weight:750}.login-card input{box-sizing:border-box;width:100%;height:44px;border:1px solid #ccd5e2;border-radius:10px;padding:0 12px;outline:none;font:inherit}.login-card input:focus{border-color:#3157d5;box-shadow:0 0 0 3px rgba(49,87,213,.1)}.login-card button{width:100%;height:44px;margin-top:18px}.login-card button mat-spinner{display:inline-block;margin-right:6px}.login-card>small{display:flex;align-items:center;justify-content:center;gap:5px;margin-top:14px;color:#8792a3;font-size:.61rem}.login-card>small mat-icon{font-size:14px;width:14px;height:14px}.login-error{display:flex;align-items:center;gap:5px;margin-top:12px;padding:9px 10px;background:#fff0f0;border:1px solid #f2cece;border-radius:8px;color:#9b2c2c;font-size:.68rem}.login-error mat-icon{font-size:16px;width:16px;height:16px}
    .language-switch{display:flex;align-items:center;gap:2px;padding:3px;border:1px solid #dce3ed;border-radius:9px;background:#f7f9fc}.language-switch button{border:0;background:transparent;color:#6b778a;border-radius:6px;padding:5px 8px;font-size:.65rem;font-weight:850;letter-spacing:.04em;cursor:pointer}.language-switch button.active{background:#3157d5;color:#fff;box-shadow:0 2px 7px rgba(49,87,213,.22)}.login-language{position:absolute;right:24px;top:24px;background:#fff}.topbar-language{margin-right:9px}
    .shell{height:100vh;background:#f3f6fb}.sidebar{width:278px;border:0;background:#0f172a;color:#fff;padding-bottom:18px}.brand{display:flex;gap:12px;align-items:center;padding:22px 18px 20px;border-bottom:1px solid rgba(255,255,255,.08);color:#fff}.brand-mark{width:44px;height:44px;border-radius:12px;display:grid;place-items:center;background:linear-gradient(145deg,#4568e7,#273fb3);box-shadow:0 8px 22px rgba(49,87,213,.28)}.brand-mark mat-icon{color:#fff}.brand strong,.brand small{display:block}.brand strong{font-size:.96rem}.brand small{color:#8fa0bc;margin-top:3px;font-size:.68rem;line-height:1.3}.nav-section{padding:20px 18px 7px;color:#64748b;font-size:.62rem;font-weight:800;letter-spacing:.12em}mat-nav-list{padding-top:0}.sidebar a[mat-list-item]{margin:3px 10px;border-radius:10px;min-height:46px}.sidebar a.active{background:rgba(80,104,230,.18)}.readonly-card{margin:22px 14px 0;padding:12px;display:flex;gap:10px;align-items:flex-start;border:1px solid rgba(148,163,184,.18);border-radius:12px;background:rgba(15,23,42,.42)}.readonly-card mat-icon{font-size:19px;width:19px;height:19px;color:#5ee3aa}.readonly-card strong,.readonly-card small{display:block}.readonly-card strong{font-size:.72rem;color:#dce6f5}.readonly-card small{margin-top:3px;font-size:.64rem;line-height:1.4;color:#7f91ad}.topbar{height:68px;background:#fff;border-bottom:1px solid #e2e8f0;color:#172033;padding:0 28px}.topbar strong,.topbar small{display:block}.topbar strong{font-size:.95rem}.topbar small{margin-top:2px;color:#8591a4;font-size:.67rem}.spacer{flex:1}.read-only{display:flex;align-items:center;gap:6px;font-size:.68rem;font-weight:800;letter-spacing:.07em;color:#16734b;background:#eaf8f1;border:1px solid #caebda;padding:7px 10px;border-radius:999px}.read-only mat-icon{font-size:16px;width:16px;height:16px}.logout{margin-left:9px}.content{padding:28px;max-width:1680px;margin:0 auto}@media(max-width:900px){.sidebar{width:230px}.content{padding:18px}.topbar{padding:0 18px}.topbar small,.read-only{display:none}.topbar-language{margin-right:0}}@media(max-width:520px){.login-language{right:14px;top:14px}}
  `]
})
export class AppComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly languageService = inject(LanguageService);

  readonly language = this.languageService.language;
  readonly authLoading = signal(true);
  readonly authEnabled = signal(false);
  readonly authenticated = signal(false);
  readonly loginBusy = signal(false);
  readonly loginError = signal('');

  username = '';
  password = '';

  ngOnInit(): void {
    this.languageService.start();
    this.refreshAuth();
  }

  setLanguage(language: AppLanguage): void {
    this.languageService.setLanguage(language);
  }

  login(): void {
    if (this.loginBusy() || !this.username.trim() || !this.password) return;
    this.loginBusy.set(true);
    this.loginError.set('');
    this.http.post<AuthStatus>('/api/auth/login', { username: this.username.trim(), password: this.password }).subscribe({
      next: result => {
        this.authEnabled.set(result.enabled);
        this.authenticated.set(result.authenticated);
        this.password = '';
        this.loginBusy.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loginError.set(error.error?.message ?? 'Giriş yapılamadı.');
        this.password = '';
        this.loginBusy.set(false);
      }
    });
  }

  logout(): void {
    this.http.post<void>('/api/auth/logout', {}).subscribe({
      next: () => {
        this.authenticated.set(false);
        this.username = '';
        this.password = '';
      },
      error: () => this.authenticated.set(false)
    });
  }

  private refreshAuth(): void {
    this.http.get<AuthStatus>('/api/auth/status').subscribe({
      next: result => {
        this.authEnabled.set(result.enabled);
        this.authenticated.set(result.authenticated);
        this.username = result.username ?? '';
        this.authLoading.set(false);
      },
      error: () => {
        this.authEnabled.set(false);
        this.authenticated.set(false);
        this.authLoading.set(false);
      }
    });
  }
}
