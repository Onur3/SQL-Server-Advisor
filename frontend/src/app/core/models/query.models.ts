export interface QueryPerformance {
  queryId: number;
  serverProfileId: string;
  serverName: string;
  databaseName: string;
  queryHash: string;
  objectName?: string | null;
  statementText: string;
  source: string;
  executionCount: number;
  totalCpuMs: number;
  averageCpuMs: number;
  totalDurationMs: number;
  averageDurationMs: number;
  totalLogicalReads: number;
  averageLogicalReads: number;
  totalLogicalWrites: number;
  impactScore: number;
  diagnosticLevel: string;
  diagnosticHeadline: string;
  diagnosticSummary: string;
  suggestedInspection: string;
  lastExecutionTime?: string | null;
  capturedAt: string;
  planId?: number | null;
  planHash?: string | null;
}

export interface QueryPlan {
  queryId: number;
  planId: number;
  planHash: string;
  source: string;
  hasActualRuntimeCounters: boolean;
  planXml: string;
  firstSeenAt: string;
  lastSeenAt: string;
}
