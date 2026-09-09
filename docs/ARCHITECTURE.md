# SQL Server Advisor - Architecture v0.1

## Runtime topology

```text
Angular 22
   |
   | REST / later SignalR
   v
ASP.NET Core 10 API --------------------+
                                        |
                                        v
                                SQL Server 2025
                                 SQLAdvisor DB
                                        ^
                                        |
.NET 10 Worker Service -----------------+
   |
   | read-only, internal collector SQL only
   v
Monitored SQL Server(s)
```

## Security boundaries

1. Browser never receives monitored SQL credentials.
2. API protects SQL Login passwords with ASP.NET Core Data Protection.
3. API and Worker share the same protected key ring folder.
4. Worker is the only runtime component that opens monitored SQL connections for collection.
5. No API endpoint accepts arbitrary SQL for execution.
6. Recommendation scripts will be display/copy only; `REC.Recommendation.CanExecute` has a DB CHECK constraint forcing `0`.
7. Monitored SQL logins must not be `sysadmin`.

## Current collector

`ServerSnapshotWorker` runs a server-health cycle every 15 seconds. It collects:

- server name/version/edition/start time
- user/active sessions
- active/blocked requests
- OS/SQL memory
- Page Life Expectancy
- SQL process CPU/System Idle from scheduler monitor ring buffer

Permission-sensitive blocks fail independently. Data coverage is recorded so missing DMV permission is not interpreted as a healthy zero value.

## Current analysis

`BLK-001` detects blocking pressure and emits a finding with severity, impact and confidence.

Initial health scoring uses CPU, memory pressure and blocking. It is intentionally provisional; baseline and workload-normalized scoring will replace hard thresholds as historical data becomes available.

## Next collectors

1. Capability/permission matrix
2. Blocking chain details + head blocker
3. Wait stats delta
4. File I/O delta
5. Query Store runtime collector
6. Plan cache collector
7. Query text normalization/catalog
8. Execution plan XML parsing
9. Index/statistics collector
10. Deadlock collector from system_health
