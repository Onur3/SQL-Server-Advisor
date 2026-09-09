export interface FindingListItem {
  id: number;
  serverProfileId: string;
  serverName: string;
  ruleId: string;
  category: string;
  severity: number;
  title: string;
  technicalDescription: string;
  confidenceScore: number;
  impactScore: number;
  findingScore: number;
  firstDetectedAt: string;
  lastDetectedAt: string;
  resolvedAt?: string | null;
  occurrenceCount: number;
  status: string;
}

export interface RecommendationListItem {
  id: number;
  findingId: number;
  serverProfileId: string;
  serverName: string;
  ruleId: string;
  severity: number;
  findingTitle: string;
  priorityScore: number;
  title: string;
  explanation: string;
  expectedBenefit: string;
  riskLevel: string;
  confidenceScore: number;
  recommendedAction: string;
  scriptText?: string | null;
  canExecute: boolean;
  status: string;
  createdAt: string;
}
