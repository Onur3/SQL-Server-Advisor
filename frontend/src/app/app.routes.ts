import { Routes } from '@angular/router';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { FindingsComponent } from './features/findings/findings.component';
import { RecommendationsComponent } from './features/recommendations/recommendations.component';
import { ServersComponent } from './features/servers/servers.component';
import { TelemetryComponent } from './features/telemetry/telemetry.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: DashboardComponent },
  { path: 'servers', component: ServersComponent },
  { path: 'telemetry', component: TelemetryComponent },
  { path: 'findings', component: FindingsComponent },
  { path: 'recommendations', component: RecommendationsComponent },
  { path: '**', redirectTo: '' }
];
