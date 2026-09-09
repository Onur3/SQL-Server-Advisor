import { Routes } from '@angular/router';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { ServersComponent } from './features/servers/servers.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: DashboardComponent },
  { path: 'servers', component: ServersComponent },
  { path: '**', redirectTo: '' }
];
