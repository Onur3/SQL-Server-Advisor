import { Routes } from '@angular/router';
import { AdminComponent } from './features/admin/admin.component';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { FindingsComponent } from './features/findings/findings.component';
import { IndexesComponent } from './features/indexes/indexes.component';
import { QueriesComponent } from './features/queries/queries.component';
import { RecommendationsComponent } from './features/recommendations/recommendations.component';
import { ServersComponent } from './features/servers/servers.component';
import { StatisticsComponent } from './features/statistics/statistics.component';
import { TelemetryComponent } from './features/telemetry/telemetry.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: DashboardComponent },
  { path: 'servers', component: ServersComponent },
  { path: 'telemetry', component: TelemetryComponent },
  { path: 'queries', component: QueriesComponent },
  { path: 'indexes', component: IndexesComponent },
  { path: 'statistics', component: StatisticsComponent },
  { path: 'findings', component: FindingsComponent },
  { path: 'recommendations', component: RecommendationsComponent },
  { path: 'admin', component: AdminComponent },
  { path: '**', redirectTo: '' }
];
