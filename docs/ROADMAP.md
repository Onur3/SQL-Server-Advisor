# Development Roadmap

## Milestone 1 - Foundation (included in current scaffold)
- Angular shell/dashboard/server management
- .NET 10 API
- .NET 10 Windows Worker Service
- SQL Server 2025 SQLAdvisor schema
- Data Protection credentials
- Worker heartbeat
- 15-second server snapshot
- initial blocking rule and health score

## Milestone 2 - Live DBA diagnostics
- permission/capability matrix
- blocking chain/head blocker
- wait stats delta
- file I/O delta
- TempDB usage
- dashboard timelines

## Milestone 3 - Query workload
- Query Store + plan cache collectors
- query catalog / normalized query hashes
- Top CPU / reads / duration / executions
- workload Impact Score
- query regression

## Milestone 4 - Plan and index advisor
- execution-plan parser
- scans/lookups/spills/conversions/cardinality
- missing/duplicate/overlapping/unused indexes
- statistics analysis
- evidence records

## Milestone 5 - Recommendation engine
- root-cause correlation
- recommendation priority/confidence/risk
- safe script rendering (never execution)
- before/after verification
- baseline/trend engine

## Milestone 6 - Operations
- user/role authorization
- retention/downsampling
- reports
- alerting
- production IIS + Worker installer hardening
