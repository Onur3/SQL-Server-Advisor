export interface WorkloadSettings {
  enabled: boolean;
  folderPath: string;
  recursive: boolean;
  defaultServerProfileId?: string | null;
  defaultDatabaseName?: string | null;
  scanIntervalSeconds: number;
  maxFileSizeKb: number;
  lastStatus: string;
  lastMessage?: string | null;
  lastScanAt?: string | null;
  activeFiles: number;
}

export interface UpdateWorkloadSettingsRequest {
  enabled: boolean;
  folderPath: string;
  recursive: boolean;
  defaultServerProfileId?: string | null;
  defaultDatabaseName?: string | null;
  scanIntervalSeconds: number;
  maxFileSizeKb: number;
}

export interface WorkloadFile {
  id: number;
  serverProfileId?: string | null;
  filePath: string;
  fileName: string;
  databaseName?: string | null;
  referencedObjects: string;
  referencedColumns: string;
  lastWriteTimeUtc: string;
  lastScannedAt: string;
  isActive: boolean;
  parseMessage?: string | null;
}

export interface AiPrompt {
  prompt: string;
  generatedAt: string;
}
