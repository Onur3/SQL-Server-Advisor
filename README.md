# SQL Server Advisor

> **24/7, read-only Microsoft SQL Server monitoring and DBA decision-support platform.**
>
> **Varsayılan arayüz dili Türkçe'dir. Uygulamada TR / EN dil seçeneği vardır.**  
> **The default UI language is Turkish. English is available from the built-in TR / EN language selector.**

[Türkçe](#türkçe) · [English](#english)

---

# Türkçe

## SQL Server Advisor nedir?

SQL Server Advisor, Microsoft SQL Server ortamlarını **salt okunur** olarak izleyen ve toplanan performans verilerini DBA'nın değerlendirebileceği **bulgu ve önerilere** dönüştüren bir karar destek uygulamasıdır.

Amaç yalnızca metrik göstermek değildir. Sistem şu akışı kurar:

```text
Collect → Analyze → Finding → Recommendation → DBA Review
```

Advisor izlenen üretim veritabanlarında **otomatik tuning işlemi çalıştırmaz**. `CREATE/DROP/ALTER INDEX`, `UPDATE STATISTICS`, `KILL`, query hint veya benzeri değişiklikler ancak DBA tarafından ayrıca değerlendirilir.

## Dil desteği

- 🇹🇷 **Türkçe** — varsayılan arayüz dili
- 🇬🇧 **English** — uygulama içindeki `TR / EN` seçicisinden etkinleştirilebilir
- Dil seçimi login ekranında ve ana üst menüde bulunur.
- Seçim tarayıcıda saklanır ve sonraki açılışta korunur.
- SQL metni, execution-plan XML, veritabanı/şema/tablo/index adları ve `IDX-001`, `STATS-001` gibi Rule ID'ler teknik doğruluğu korumak için çevrilmez.

## Başlıca özellikler

| Modül | Ne yapar? |
| --- | --- |
| **Server Health** | SQL CPU, bellek, session, request, blocking ve genel sağlık sinyallerini izler. |
| **Wait & Blocking Advisor** | Wait delta'larını ve blocking zincirlerini anlamlandırır. |
| **Query Performance Advisor** | Plan cache üzerinden pahalı sorguları CPU, duration, reads, writes ve impact ile inceler. |
| **Execution Plan Viewer** | Yakalanan ShowPlan XML'i ve plan içinde kullanılan nesneleri görüntüler. |
| **Index Advisor** | Mevcut indeksler, kullanım/write maliyeti, fragmentation ve missing-index sinyallerini birlikte değerlendirir. |
| **Statistics Advisor** | Statistics freshness, sampling, NORECOMPUTE, persisted sampling ve redundant statistics durumlarını analiz eder. |
| **Table Scope** | Sunucu → veritabanı → tablo seviyesinde whitelist tanımlayarak yalnız seçilen tabloları Index/Statistics analizine dahil eder. |
| **Findings** | Problemin ne olduğunu, neden önemli olduğunu ve ilk neyin kontrol edilmesi gerektiğini gösterir. |
| **Recommendations** | DBA kontrollü aksiyon önerileri, doğrulama SQL'i ve teknik kanıt sunar. |
| **TXT Workload Analysis** | Sorgu dosyalarını çalıştırmadan okuyup tablo/kolon referanslarını destekleyici kanıt olarak kullanır. |
| **Optional Login** | Kurulum sırasında istenirse sabit kullanıcı adı/şifre koruması etkinleştirilebilir. |
| **File Logging** | API ve Worker loglarını günlük dosyalara yazar; unhandled API exception'larında stack trace kaydeder. |

## Güvenlik sınırı

SQL Server Advisor'ın en önemli tasarım kuralı şudur:

> **Advisor izlenen SQL Server üzerinde otomatik değişiklik yapmaz.**

Monitoring bağlantıları minimum yetki prensibiyle kullanılmalıdır. SQL Server 2019 uyumlu izin şablonları `database/` klasöründe bulunur. İzlenen sunucu izin şablonları installer tarafından bilinçli olarak otomatik uygulanmaz; DBA'nın inceleyip ayrıca uygulaması beklenir.

Öneri ekranında bir DDL taslağı gösterilmesi, o komutun uygulama tarafından yürütüleceği anlamına gelmez.

## Mimari

```text
┌──────────────────────────┐
│ Angular 22 Web UI        │
│ Turkish / English        │
└────────────┬─────────────┘
             │ same origin
┌────────────▼─────────────┐
│ ASP.NET Core / .NET 10   │
│ Web API + SignalR        │
└────────────┬─────────────┘
             │
┌────────────▼─────────────┐
│ SQLAdvisor Database      │
│ snapshots/findings/etc.  │
└──────────────────────────┘

┌──────────────────────────┐       read-only       ┌──────────────────────────┐
│ .NET 10 Windows Worker   │ ────────────────────► │ Monitored SQL Servers    │
│ collectors + analysis    │                       │ production / test         │
└──────────────────────────┘                       └──────────────────────────┘
```

Tek IIS uygulaması hem Angular statik dosyalarını hem `/api/...`, `/hubs/...` ve `/health` endpoint'lerini sunar. Worker bağımsız Windows Service olarak çalışır.

## Teknoloji

- Angular 22
- ASP.NET Core / .NET 10 Web API
- .NET 10 Windows Worker Service
- Entity Framework Core 10
- Dapper
- Microsoft.Data.SqlClient
- Microsoft ScriptDom
- Microsoft SQL Server
- IIS / Windows Server

## Repository yapısı

```text
backend/     .NET API, Worker, domain, infrastructure ve analiz motoru
database/    SQLAdvisor migration'ları ve monitored-server izin şablonları
deploy/      Windows Server / IIS kurulum ve HTTPS scriptleri
frontend/    Angular 22 web uygulaması
```

## Hızlı kurulum

Kaynak koddan kurulum için temel gereksinimler:

- Windows Server 2019 veya üzeri
- .NET SDK 10.x
- Node.js 24 veya üzeri
- npm
- Database migration kullanılacaksa Microsoft `sqlcmd`
- IIS

PowerShell'i **Administrator** olarak açın:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\install.ps1
```

Farklı settings dosyası kullanmak için:

```powershell
.\deploy\install.ps1 `
  -SettingsPath 'C:\Config\sql-server-advisor.settings.json'
```

Kurulum değerleri merkezi olarak:

```text
deploy/install.settings.json
```

dosyasından yönetilir. Production'a özel domain, sunucu adı, certificate password veya database password gibi değerler source control'e yazılmamalıdır.

## Opsiyonel uygulama girişi

Installer kurulum sırasında uygulamanın sabit kullanıcı adı/şifre ile korunup korunmayacağını sorabilir.

Login etkinse:

- şifre plain text olarak saklanmaz,
- PBKDF2-SHA256 hash + random salt kullanılır,
- oturum HttpOnly cookie ile tutulur,
- Angular uygulaması login ekranından sonra açılır.

Login kullanılmak istenmezse özellik kapalı bırakılabilir.

## Tablo kapsamı

`SQL Sunucuları → Tablo Kapsamı` ekranından izleme kapsamı sınırlandırılabilir.

```text
Server
  └─ Database
      ├─ dbo.TableA  ✓
      ├─ dbo.TableB  ✓
      └─ dbo.TableC
```

- Hiç tablo seçilmezse mevcut davranış korunur ve tüm erişilebilir tablolar izlenir.
- En az bir tablo seçilirse whitelist devreye girer.
- Index ve Statistics collector/analizleri yalnız seçilen `Database + Schema + Table` kayıtlarını kullanır.

## Log dosyaları

API ve Worker günlük rolling log üretir. Varsayılan production konumu:

```text
C:\ProgramData\SqlServerAdvisor\Keys\Logs
```

Örnek:

```text
sqladvisor-api-20260910.log
sqladvisor-worker-20260910.log
```

API'de yakalanmamış exception oluşursa HTTP method, path, query string, trace identifier ve stack trace dosyaya yazılır. Varsayılan retention süresi 14 gündür.

## HTTPS / certificate

HTTPS ayarları `deploy/install.settings.json` içinden yönetilir. Certificate yenilendiğinde aynı settings dosyasıyla:

```powershell
.\deploy\configure-https.ps1
```

çalıştırılabilir.

`certificate.mode` seçenekleri:

- `auto` — uygun exact-name veya wildcard certificate bulur.
- `thumbprint` — belirtilen certificate thumbprint'i kullanır.
- `pfx` — PFX import eder; şifre environment variable üzerinden okunur.

## Development

Backend:

```powershell
cd backend
dotnet restore
dotnet build SqlServerAdvisor.slnx
dotnet run --project src/SqlServerAdvisor.Api
dotnet run --project src/SqlServerAdvisor.Worker
```

Frontend:

```powershell
cd frontend
npm install
npm start
```

---

# English

## What is SQL Server Advisor?

SQL Server Advisor is a **24/7, read-only monitoring and DBA decision-support platform** for Microsoft SQL Server. It collects operational and performance telemetry and converts it into structured findings and recommendations that a DBA can review.

The core workflow is:

```text
Collect → Analyze → Finding → Recommendation → DBA Review
```

The platform is intentionally advisory. It **does not automatically modify monitored production databases**.

## Languages

- 🇹🇷 **Turkish** — default UI language
- 🇬🇧 **English** — available from the built-in `TR / EN` selector
- The selector is available on both the login screen and the main toolbar.
- The selected language is persisted in the browser.
- SQL text, execution-plan XML, database/schema/table/index names and technical Rule IDs remain unchanged so technical evidence is never rewritten.

## Main features

| Module | Purpose |
| --- | --- |
| **Server Health** | Monitors SQL CPU, memory, sessions, requests, blocking and overall health signals. |
| **Wait & Blocking Advisor** | Explains wait deltas and blocking chains. |
| **Query Performance Advisor** | Reviews expensive plan-cache queries using CPU, duration, reads, writes and impact. |
| **Execution Plan Viewer** | Displays captured ShowPlan XML and objects referenced by the plan. |
| **Index Advisor** | Correlates index inventory, read/write usage, fragmentation and missing-index signals. |
| **Statistics Advisor** | Reviews statistics freshness, sampling, NORECOMPUTE, persisted sampling and redundancy. |
| **Table Scope** | Provides server → database → table whitelisting for Index/Statistics collection and analysis. |
| **Findings** | Explains what was found, why it matters and what to inspect first. |
| **Recommendations** | Produces DBA-controlled actions, read-only validation SQL and supporting evidence. |
| **TXT Workload Analysis** | Parses query files without executing them and uses object/column references as supporting evidence. |
| **Optional Login** | Can enable a fixed username/password during installation. |
| **File Logging** | Writes API and Worker logs to daily files and records stack traces for unhandled API exceptions. |

## Safety boundary

> **SQL Server Advisor never automatically changes the monitored SQL Server.**

No automatic `CREATE/DROP/ALTER INDEX`, `UPDATE STATISTICS`, `KILL`, query hints or similar production tuning actions are executed by the Advisor.

Monitoring connections should follow least privilege. SQL Server 2019-compatible monitored-server permission templates are available under `database/`. They are intentionally not auto-applied by the installer so a DBA can review them first.

## Architecture

```text
Angular 22 Web UI (TR / EN)
          │
          ▼
ASP.NET Core / .NET 10 API + SignalR
          │
          ▼
SQLAdvisor application database

.NET 10 Windows Worker ── read-only ──► Monitored SQL Servers
```

Angular and the API are served by a single IIS application. The Worker runs independently as a Windows Service.

## Technology stack

- Angular 22
- ASP.NET Core / .NET 10
- .NET 10 Windows Worker Service
- Entity Framework Core 10
- Dapper
- Microsoft.Data.SqlClient
- Microsoft ScriptDom
- Microsoft SQL Server
- IIS / Windows Server

## Repository layout

```text
backend/     .NET API, Worker, domain, infrastructure and analysis engine
database/    SQLAdvisor migrations and monitored-server permission templates
deploy/      Windows Server / IIS deployment and HTTPS scripts
frontend/    Angular 22 web application
```

## Quick installation

Source-based deployment requires:

- Windows Server 2019 or later
- .NET SDK 10.x
- Node.js 24 or later
- npm
- Microsoft `sqlcmd` when database migrations are enabled
- IIS

Run PowerShell as **Administrator**:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\install.ps1
```

Or use an external settings file:

```powershell
.\deploy\install.ps1 `
  -SettingsPath 'C:\Config\sql-server-advisor.settings.json'
```

Deployment-specific values are centralized in:

```text
deploy/install.settings.json
```

Do not commit production passwords, certificate passwords or organization-specific secrets to source control.

## Optional application login

The installer can prompt to protect the application with a fixed username/password. When enabled, passwords are stored as PBKDF2-SHA256 hash + random salt and sessions use an HttpOnly cookie. The feature can also be left disabled.

## Table scope

From `SQL Servers → Table Scope`, administrators can define an explicit table whitelist. With no selection, all accessible tables remain in scope. Once at least one table is selected, Index and Statistics collection/analysis are restricted to the selected `Database + Schema + Table` combinations.

## Logs

API and Worker use daily rolling log files. The default production location is:

```text
C:\ProgramData\SqlServerAdvisor\Keys\Logs
```

Example:

```text
sqladvisor-api-20260910.log
sqladvisor-worker-20260910.log
```

Unhandled API exceptions include method, path, query string, trace identifier and stack trace. Default retention is 14 days.

## Development

Backend:

```powershell
cd backend
dotnet restore
dotnet build SqlServerAdvisor.slnx
dotnet run --project src/SqlServerAdvisor.Api
dotnet run --project src/SqlServerAdvisor.Worker
```

Frontend:

```powershell
cd frontend
npm install
npm start
```

---

## Project principle / Proje prensibi

**Measure first. Explain the evidence. Recommend safely. Let the DBA decide.**  
**Önce ölç. Kanıtı açıkla. Güvenli öner. Kararı DBA'ya bırak.**
