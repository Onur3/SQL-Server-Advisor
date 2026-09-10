import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { forkJoin, map, Observable } from 'rxjs';
import {
  ConnectionTestResult,
  CreateServerRequest,
  DashboardServer,
  DatabaseOption,
  MonitoredTableSelection,
  ServerListItem,
  TableOption,
  TableScope,
  UpdateTableScopeRequest,
  WorkerStatus
} from '../models/server.models';
import { FindingListItem, RecommendationListItem } from '../models/analysis.models';
import { BlockingTelemetry, WaitTelemetry } from '../models/telemetry.models';
import { QueryPerformance, QueryPlan } from '../models/query.models';
import { FragmentedIndex, MissingIndexCandidate } from '../models/index.models';
import { StatisticsStatus } from '../models/statistics.models';
import { CollectorCoverage } from '../models/collector.models';
import { AiPrompt, UpdateWorkloadSettingsRequest, WorkloadFile, WorkloadSettings } from '../models/workload.models';

type ServerTableScope = MonitoredTableSelection & { serverProfileId: string };

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

  getServerDatabases(id: string): Observable<DatabaseOption[]> {
    return this.http.get<DatabaseOption[]>(`${this.baseUrl}/servers/${id}/databases`);
  }

  getServerTables(id: string, databaseName: string): Observable<TableOption[]> {
    return this.http.get<TableOption[]>(`${this.baseUrl}/servers/${id}/tables`, {
      params: { databaseName }
    });
  }

  getTableScope(id: string): Observable<TableScope> {
    return this.http.get<TableScope>(`${this.baseUrl}/servers/${id}/table-scope`);
  }

  updateTableScope(id: string, request: UpdateTableScopeRequest): Observable<TableScope> {
    return this.http.put<TableScope>(`${this.baseUrl}/servers/${id}/table-scope`, request);
  }

  getAllTableScopes(): Observable<ServerTableScope[]> {
    return this.http.get<ServerTableScope[]>(`${this.baseUrl}/servers/table-scopes`);
  }

  getDashboardServers(): Observable<DashboardServer[]> {
    return this.http.get<DashboardServer[]>(`${this.baseUrl}/dashboard/servers`);
  }

  getWorkerStatus(): Observable<WorkerStatus> {
    return this.http.get<WorkerStatus>(`${this.baseUrl}/dashboard/worker`);
  }

  getFindings(status = 'All'): Observable<FindingListItem[]> {
    return forkJoin({
      rows: this.http.get<FindingListItem[]>(`${this.baseUrl}/findings`, { params: { status } }),
      scopes: this.getAllTableScopes()
    }).pipe(map(({ rows, scopes }) => rows.filter(x =>
      !this.isTableScopedCategory(x.category) || this.scopeAllows(scopes, x.serverProfileId, x.databaseName, x.objectName)
    )));
  }

  getRecommendations(status = 'All'): Observable<RecommendationListItem[]> {
    return forkJoin({
      rows: this.http.get<RecommendationListItem[]>(`${this.baseUrl}/recommendations`, { params: { status } }),
      scopes: this.getAllTableScopes()
    }).pipe(map(({ rows, scopes }) => rows.filter(x =>
      !this.isTableScopedCategory(x.category) || this.scopeAllows(scopes, x.serverProfileId, x.databaseName, x.objectName)
    )));
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
    return forkJoin({
      rows: this.http.get<FragmentedIndex[]>(`${this.baseUrl}/indexes/fragmented`, { params: { take } }),
      scopes: this.getAllTableScopes()
    }).pipe(map(({ rows, scopes }) => rows.filter(x =>
      this.scopeAllows(scopes, x.serverProfileId, x.databaseName, x.tableName)
    )));
  }

  getMissingIndexCandidates(take = 100): Observable<MissingIndexCandidate[]> {
    return forkJoin({
      rows: this.http.get<MissingIndexCandidate[]>(`${this.baseUrl}/indexes/missing`, { params: { take } }),
      scopes: this.getAllTableScopes()
    }).pipe(
      map(({ rows, scopes }) => rows.filter(x =>
        this.scopeAllows(scopes, x.serverProfileId, x.databaseName, x.tableName)
      )),
      map(rows => this.consolidateMissingIndexCandidates(rows))
    );
  }

  getStatisticsStatus(take = 100): Observable<StatisticsStatus[]> {
    return forkJoin({
      rows: this.http.get<StatisticsStatus[]>(`${this.baseUrl}/statistics`, { params: { take } }),
      scopes: this.getAllTableScopes()
    }).pipe(map(({ rows, scopes }) => rows.filter(x =>
      this.scopeAllows(scopes, x.serverProfileId, x.databaseName, x.tableName)
    )));
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

  private scopeAllows(
    scopes: ServerTableScope[],
    serverProfileId: string,
    databaseName?: string | null,
    objectName?: string | null
  ): boolean {
    const serverScope = scopes.filter(x => x.serverProfileId.toLowerCase() === serverProfileId.toLowerCase());
    if (!serverScope.length) return true;
    if (!databaseName?.trim() || !objectName?.trim()) return false;

    const parsed = this.parseObjectName(objectName);
    return serverScope.some(x =>
      x.databaseName.toLowerCase() === databaseName.toLowerCase() &&
      x.tableName.toLowerCase() === parsed.tableName.toLowerCase() &&
      (!parsed.schemaName || x.schemaName.toLowerCase() === parsed.schemaName.toLowerCase())
    );
  }

  private parseObjectName(value: string): { schemaName: string; tableName: string } {
    const parts = value
      .split('.')
      .map(x => x.trim().replace(/^\[|\]$/g, '').replace(/^"|"$/g, '').replace(/^`|`$/g, ''))
      .filter(Boolean);
    return {
      schemaName: parts.length >= 2 ? parts[parts.length - 2] : '',
      tableName: parts.length ? parts[parts.length - 1] : ''
    };
  }

  private isTableScopedCategory(category: string): boolean {
    const normalized = category?.trim().toLowerCase();
    return normalized === 'index' || normalized === 'indexes' || normalized === 'statistics';
  }

  private consolidateMissingIndexCandidates(rows: MissingIndexCandidate[]): MissingIndexCandidate[] {
    const groups = new Map<string, MissingIndexCandidate[]>();

    for (const row of rows) {
      const key = [
        row.serverProfileId.toLowerCase(),
        row.databaseName.toLowerCase(),
        row.tableName.toLowerCase(),
        this.normalizeColumnSequence(row.equalityColumns),
        this.normalizeColumnSequence(row.inequalityColumns)
      ].join('\u001e');

      const group = groups.get(key) ?? [];
      group.push(row);
      groups.set(key, group);
    }

    const consolidated: MissingIndexCandidate[] = [];

    for (const group of groups.values()) {
      const remaining = group
        .map(row => ({ row, includes: new Set(this.parseColumns(row.includedColumns).map(x => x.toLowerCase())) }))
        .sort((a, b) => b.includes.size - a.includes.size || b.row.improvementMeasure - a.row.improvementMeasure);

      while (remaining.length) {
        const leader = remaining[0];
        consolidated.push(leader.row);

        for (let index = remaining.length - 1; index >= 0; index--) {
          const candidate = remaining[index];
          if ([...candidate.includes].every(column => leader.includes.has(column))) {
            remaining.splice(index, 1);
          }
        }
      }
    }

    return consolidated.sort((a, b) =>
      Number(a.coveredByExistingIndex) - Number(b.coveredByExistingIndex) ||
      b.improvementMeasure - a.improvementMeasure
    );
  }

  private normalizeColumnSequence(value: string): string {
    return this.parseColumns(value).map(x => x.toLowerCase()).join('\u001f');
  }

  private parseColumns(value: string): string[] {
    if (!value?.trim()) return [];

    return value
      .split(',')
      .map(x => x.trim().replace(/^\[|\]$/g, '').replace(/^"|"$/g, ''))
      .filter(Boolean);
  }
}
