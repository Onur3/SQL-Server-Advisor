export interface StatisticsStatus {
  id: number;
  serverProfileId: string;
  serverName: string;
  databaseName: string;
  tableName: string;
  statisticsName: string;
  statisticsType: string;
  rows: number;
  rowsSampled: number;
  modificationCounter: number;
  modificationPercent: number;
  lastUpdated?: string | null;
  samplePercent?: number | null;
  autoCreated: boolean;
  userCreated: boolean;
  noRecompute: boolean;
  hasFilter: boolean;
  filterDefinition?: string | null;
  diagnosticLevel: string;
  diagnosticHeadline: string;
  diagnosticSummary: string;
  suggestedInspection: string;
  capturedAt: string;
}
