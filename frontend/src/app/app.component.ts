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
        <div class="brand">
          <div class="brand-mark">SQL</div>
          <div>
            <strong>Server Advisor</strong>
            <small>Performance & Health</small>
          </div>
        </div>
        <mat-nav-list>
          <a mat-list-item routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{exact:true}">
            <mat-icon matListItemIcon>dashboard</mat-icon><span matListItemTitle>Dashboard</span>
          </a>
          <a mat-list-item routerLink="/servers" routerLinkActive="active">
            <mat-icon matListItemIcon>dns</mat-icon><span matListItemTitle>SQL Sunucuları</span>
          </a>
          <a mat-list-item class="disabled"><mat-icon matListItemIcon>query_stats</mat-icon><span matListItemTitle>Sorgular</span></a>
          <a mat-list-item class="disabled"><mat-icon matListItemIcon>rule</mat-icon><span matListItemTitle>Bulgular</span></a>
          <a mat-list-item class="disabled"><mat-icon matListItemIcon>tips_and_updates</mat-icon><span matListItemTitle>Öneriler</span></a>
        </mat-nav-list>
      </mat-sidenav>
      <mat-sidenav-content>
        <mat-toolbar class="topbar">
          <span>SQL Server Advisor</span>
          <span class="spacer"></span>
          <span class="read-only">READ ONLY MONITORING</span>
        </mat-toolbar>
        <main class="content"><router-outlet /></main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: [`
    .shell{height:100vh}.sidebar{width:250px;border-right:1px solid #e4e8f0;background:#111827;color:#fff}
    .brand{display:flex;gap:12px;align-items:center;padding:22px 18px;border-bottom:1px solid rgba(255,255,255,.09)}
    .brand-mark{width:42px;height:42px;border-radius:10px;display:grid;place-items:center;background:#3157d5;font-weight:800}
    .brand strong,.brand small{display:block}.brand small{color:#93a0b8;margin-top:2px;font-size:.72rem}
    .sidebar a{color:#cad2e2;margin:5px 8px;border-radius:9px}.sidebar a.active{background:#25324a;color:#fff}.sidebar a.disabled{opacity:.5}
    .topbar{height:64px;background:#fff;border-bottom:1px solid #e4e8f0;color:#1d2738}.spacer{flex:1}.read-only{font-size:.72rem;letter-spacing:.09em;color:#64748b;border:1px solid #dbe2ec;padding:6px 9px;border-radius:7px}
    .content{padding:26px;max-width:1600px;margin:0 auto}
  `]
})
export class AppComponent {}
