export interface StatisticsStatus {
  id: number;
  serverProfileId: string;
  serverName: string;
  databaseName: string;
  tableName: string;
  statisticsName: string;
  rows: number;
  rowsSampled: number;
  modificationCounter: number;
  modificationPercent: number;
  lastUpdated?: string | null;
  samplePercent?: number | null;
  capturedAt: string;
}
