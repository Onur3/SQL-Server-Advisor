export interface CollectorCoverage {
  serverProfileId: string;
  serverName: string;
  collectorType: string;
  status: string;
  rowsCollected: number;
  warningMessage?: string | null;
  startedAt: string;
  completedAt?: string | null;
}
