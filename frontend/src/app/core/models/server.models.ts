export enum ServerAuthenticationType {
  Windows = 0,
  SqlLogin = 1
}

export interface ServerListItem {
  id: string;
  name: string;
  host: string;
  port: number;
  defaultDatabase: string;
  authenticationType: ServerAuthenticationType;
  username?: string | null;
  encrypt: boolean;
  trustServerCertificate: boolean;
  isEnabled: boolean;
  lastConnectedAt?: string | null;
  lastError?: string | null;
}

export interface CreateServerRequest {
  name: string;
  host: string;
  port: number;
  defaultDatabase: string;
  authenticationType: ServerAuthenticationType;
  username?: string | null;
  password?: string | null;
  encrypt: boolean;
  trustServerCertificate: boolean;
}

export interface ConnectionTestResult {
  success: boolean;
  serverName?: string | null;
  productVersion?: string | null;
  edition?: string | null;
  error?: string | null;
}

export interface DatabaseOption {
  name: string;
}

export interface TableOption {
  databaseName: string;
  schemaName: string;
  tableName: string;
  displayName: string;
}

export interface MonitoredTableSelection {
  databaseName: string;
  schemaName: string;
  tableName: string;
}

export interface TableScope {
  serverProfileId: string;
  restricted: boolean;
  tables: MonitoredTableSelection[];
}

export interface UpdateTableScopeRequest {
  tables: MonitoredTableSelection[];
}

export interface DashboardServer {
  id: string;
  name: string;
  host: string;
  online: boolean;
  lastSnapshotAt?: string | null;
  sqlCpuPercent?: number | null;
  availableMemoryMb?: number | null;
  activeSessions: number;
  activeRequests: number;
  blockedRequests: number;
  healthScore?: number | null;
  dataCoveragePercent: number;
  openFindingCount: number;
  highFindingCount: number;
  criticalFindingCount: number;
  topFindingScore?: number | null;
}

export interface WorkerStatus {
  online: boolean;
  machineName?: string | null;
  version?: string | null;
  startedAt?: string | null;
  lastHeartbeatAt?: string | null;
}
