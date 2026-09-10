import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConnectionTestResult, CreateServerRequest, DashboardServer, ServerListItem, WorkerStatus } from '../models/server.models';
import { FindingListItem, RecommendationListItem } from '../models/analysis.models';
import { BlockingTelemetry, WaitTelemetry } from '../models/telemetry.models';
import { QueryPerformance, QueryPlan } from '../models/query.models';
import { FragmentedIndex, MissingIndexCandidate } from '../models/index.models';
import { StatisticsStatus } from '../models/statistics.models';
import { CollectorCoverage } from '../models/collector.models';
import { AiPrompt, UpdateWorkloadSettingsRequest, WorkloadFile, WorkloadSettings } from '../models/workload.models';

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

  getRecommendationAiPrompt(id: number): Observable<AiPrompt> {
    return this.http.get<AiPrompt>(`${this.baseUrl}/recommendations/${id}/ai-prompt`);
  }

  getWaitTelemetry(minutes = 15, take = 100): Observable<WaitTelemetry[]> {
    return this.http.get<WaitTelemetry[]>(`${this.baseUrl}/telemetry/waits`, {
      params: { minutes, take }
    });
  }

  getBlockingTelemetry(minutes = 15, take = 100): Observable<BlockingTelemetry[]> {
    return this.http.get<BlockingTelemetry[]>(`${this.baseUrl}/telemetry/blocking`, {
      params: { minutes, take }
    });
  }

  getQueryPerformance(take = 100): Observable<QueryPerformance[]> {
    return this.http.get<QueryPerformance[]>(`${this.baseUrl}/queries`, { params: { take } });
  }

  getQueryPlan(planId: number): Observable<QueryPlan> {
    return this.http.get<QueryPlan>(`${this.baseUrl}/queries/plans/${planId}`);
  }

  getFragmentedIndexes(take = 100): Observable<FragmentedIndex[]> {
    return this.http.get<FragmentedIndex[]>(`${this.baseUrl}/indexes/fragmented`, { params: { take } });
  }

  getMissingIndexCandidates(take = 100): Observable<MissingIndexCandidate[]> {
    return this.http.get<MissingIndexCandidate[]>(`${this.baseUrl}/indexes/missing`, { params: { take } });
  }

  getStatisticsStatus(take = 100): Observable<StatisticsStatus[]> {
    return this.http.get<StatisticsStatus[]>(`${this.baseUrl}/statistics`, { params: { take } });
  }

  getCollectorCoverage(collectorType: string): Observable<CollectorCoverage[]> {
    return this.http.get<CollectorCoverage[]>(`${this.baseUrl}/diagnostics/collector-coverage`, {
      params: { collectorType }
    });
  }

  getWorkloadSettings(): Observable<WorkloadSettings> {
    return this.http.get<WorkloadSettings>(`${this.baseUrl}/admin/workload-settings`);
  }

  updateWorkloadSettings(request: UpdateWorkloadSettingsRequest): Observable<WorkloadSettings> {
    return this.http.put<WorkloadSettings>(`${this.baseUrl}/admin/workload-settings`, request);
  }

  requestWorkloadScan(): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/admin/workload-scan`, {});
  }

  getWorkloadFiles(activeOnly = true, take = 250): Observable<WorkloadFile[]> {
    return this.http.get<WorkloadFile[]>(`${this.baseUrl}/admin/workload-files`, {
      params: { activeOnly, take }
    });
  }
}
