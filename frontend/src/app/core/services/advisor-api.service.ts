import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConnectionTestResult, CreateServerRequest, DashboardServer, ServerListItem, WorkerStatus } from '../models/server.models';
import { FindingListItem, RecommendationListItem } from '../models/analysis.models';

@Injectable({ providedIn: 'root' })
export class AdvisorApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  getServers(): Observable<ServerListItem[]> {
    return this.http.get<ServerListItem[]>(`${this.baseUrl}/servers`);
  }

  testServer(request: CreateServerRequest): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(`${this.baseUrl}/servers/test`, request);
  }

  createServer(request: CreateServerRequest): Observable<ServerListItem> {
    return this.http.post<ServerListItem>(`${this.baseUrl}/servers`, request);
  }

  setServerEnabled(id: string, enabled: boolean): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/servers/${id}/enabled`, enabled);
  }

  getDashboardServers(): Observable<DashboardServer[]> {
    return this.http.get<DashboardServer[]>(`${this.baseUrl}/dashboard/servers`);
  }

  getWorkerStatus(): Observable<WorkerStatus> {
    return this.http.get<WorkerStatus>(`${this.baseUrl}/dashboard/worker`);
  }

  getFindings(status = 'All'): Observable<FindingListItem[]> {
    return this.http.get<FindingListItem[]>(`${this.baseUrl}/findings`, { params: { status } });
  }

  getRecommendations(status = 'All'): Observable<RecommendationListItem[]> {
    return this.http.get<RecommendationListItem[]>(`${this.baseUrl}/recommendations`, { params: { status } });
  }
}
