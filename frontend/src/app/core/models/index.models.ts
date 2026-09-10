export interface FragmentedIndex {
  id: number;
  serverProfileId: string;
  serverName: string;
  databaseName: string;
  tableName: string;
  indexName: string;
  typeDesc: string;
  keyColumns: string;
  includeColumns: string;
  sizeMb: number;
  userSeeks: number;
  userScans: number;
  userLookups: number;
  userUpdates: number;
  hasFilter: boolean;
  filterDefinition?: string | null;
  usageSinceDays: number;
  avgFragmentationPercent?: number | null;
  pageCount?: number | null;
  diagnosticLevel: string;
  diagnosticHeadline: string;
  diagnosticSummary: string;
  suggestedInspection: string;
  capturedAt: string;
}

export interface ExistingIndexSummary {
  indexName: string;
  keyColumns: string;
  includeColumns: string;
  isUnique: boolean;
  isPrimaryKey: boolean;
  hasFilter: boolean;
  filterDefinition?: string | null;
  reads: number;
  writes: number;
  sizeMb: number;
}

export interface MissingIndexCandidate {
  id: number;
  serverProfileId: string;
  serverName: string;
  databaseName: string;
  tableName: string;
  equalityColumns: string;
  inequalityColumns: string;
  includedColumns: string;
  userSeeks: number;
  userScans: number;
  avgTotalUserCost: number;
  avgUserImpact: number;
  improvementMeasure: number;
  coveredByExistingIndex: boolean;
  decisionType: string;
  decisionTitle: string;
  decisionReason: string;
  proposedCreateSql?: string | null;
  existingIndexes: ExistingIndexSummary[];
  matchingWorkloadFiles: string[];
  diagnosticLevel: string;
  diagnosticHeadline: string;
  diagnosticSummary: string;
  suggestedInspection: string;
  capturedAt: string;
}
