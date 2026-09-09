# SQL Server Advisor

7/24 çalışan, izlenen Microsoft SQL Server sistemlerine **salt-okunur** bağlanan performans/sağlık analiz platformu.

## Teknoloji

- Frontend: Angular 22 + Angular Material
- API: ASP.NET Core 10 Web API
- Collector: .NET 10 Worker Service / Windows Service
- Advisor DB: SQL Server 2025 (17.x), `SQLAdvisor`
- Monitored SQL access: Microsoft.Data.SqlClient + Dapper
- Application DB: EF Core 10
- T-SQL parser: Microsoft ScriptDom

> Kaynak kod bu repository'de `backend/`, `frontend/`, `database/`, `deploy/` ve `docs/` altında tutulacaktır.
