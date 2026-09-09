export interface WaitTelemetry {
  serverProfileId: string;
  serverName: string;
  capturedAt: string;
  waitType: string;
  waitingTasks: number;
  waitTimeMs: number;
  deltaWaitTimeMs: number;
  deltaSignalWaitTimeMs: number;
}

export interface BlockingTelemetry {
  serverProfileId: string;
  serverName: string;
  capturedAt: string;
  sessionId: number;
  blockingSessionId: number;
  databaseName?: string | null;
  waitType?: string | null;
  waitTimeMs: number;
  waitResource?: string | null;
  sqlText?: string | null;
  hostName?: string | null;
  programName?: string | null;
  loginName?: string | null;
}
