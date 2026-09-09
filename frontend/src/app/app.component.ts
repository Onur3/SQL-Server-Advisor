import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatSidenavModule, MatListModule, MatIconModule],
  template: `
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
          <span class="read-only"><mat-icon>visibility</mat-icon> READ ONLY</span>
        </mat-toolbar>
        <main class="content"><router-outlet /></main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: [`
    .shell{height:100vh;background:#f3f6fb}.sidebar{width:278px;border:0;background:#0f172a;color:#fff;padding-bottom:18px}
    .brand{display:flex;gap:12px;align-items:center;padding:22px 18px 20px;border-bottom:1px solid rgba(255,255,255,.08);color:#fff}
    .brand-mark{width:44px;height:44px;border-radius:12px;display:grid;place-items:center;background:linear-gradient(145deg,#4568e7,#273fb3);box-shadow:0 8px 22px rgba(49,87,213,.28)}
    .brand-mark mat-icon{color:#fff}.brand strong,.brand small{display:block}.brand strong{font-size:.96rem}.brand small{color:#8fa0bc;margin-top:3px;font-size:.68rem;line-height:1.3}
    .nav-section{padding:20px 18px 7px;color:#64748b;font-size:.62rem;font-weight:800;letter-spacing:.12em}
    mat-nav-list{padding-top:0}.sidebar a[mat-list-item]{margin:3px 10px;border-radius:10px;min-height:46px}.sidebar a.active{background:rgba(80,104,230,.18)}
    .readonly-card{margin:22px 14px 0;padding:12px;display:flex;gap:10px;align-items:flex-start;border:1px solid rgba(148,163,184,.18);border-radius:12px;background:rgba(15,23,42,.42)}
    .readonly-card mat-icon{font-size:19px;width:19px;height:19px;color:#5ee3aa}.readonly-card strong,.readonly-card small{display:block}.readonly-card strong{font-size:.72rem;color:#dce6f5}.readonly-card small{margin-top:3px;font-size:.64rem;line-height:1.4;color:#7f91ad}
    .topbar{height:68px;background:#fff;border-bottom:1px solid #e2e8f0;color:#172033;padding:0 28px}.topbar strong,.topbar small{display:block}.topbar strong{font-size:.95rem}.topbar small{margin-top:2px;color:#8591a4;font-size:.67rem}.spacer{flex:1}
    .read-only{display:flex;align-items:center;gap:6px;font-size:.68rem;font-weight:800;letter-spacing:.07em;color:#16734b;background:#eaf8f1;border:1px solid #caebda;padding:7px 10px;border-radius:999px}.read-only mat-icon{font-size:16px;width:16px;height:16px}
    .content{padding:28px;max-width:1680px;margin:0 auto}
    @media(max-width:900px){.sidebar{width:230px}.content{padding:18px}.topbar{padding:0 18px}.topbar small{display:none}}
  `]
})
export class AppComponent {}
