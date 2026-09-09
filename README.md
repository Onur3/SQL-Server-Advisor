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

## Şu an çalışan ilk milestone

1. Angular'da SQL Server profili ekleme ekranı.
2. Bağlantıyı kaydetmeden önce test etme.
3. SQL Login parolasını ASP.NET Core Data Protection ile koruma.
4. Worker heartbeat (10 sn).
5. Worker server-health snapshot (15 sn).
6. Server version/edition/start time toplama.
7. Yetki varsa CPU, RAM, PLE, session/request/blocking metrikleri toplama.
8. İlk Rule Engine kuralı: `BLK-001` blocking pressure.
9. Dashboard'da Worker durumu, Health Score ve canlı metrikler.
10. Monitored server tarafında hiçbir write/DDL/kill endpoint'i yoktur.

## Dizinler

- `backend/` .NET 10 solution
- `frontend/` Angular uygulama
- `database/` SQL Server 2025 kurulum ve izin scriptleri
- `deploy/` Windows Service / IIS yardımcı dosyaları

## 1. Gereksinimler

### Development

- .NET SDK 10.x
- Node.js: Angular 22 için **22.22.3+**, **24.15.0+** veya desteklenen daha yeni hat
- npm
- SQL Server 2025 (17.x)
- Visual Studio 2026 veya VS Code (isteğe bağlı)

## 2. SQLAdvisor DB kurulumu

SSMS ile sırasıyla:

```text
database/001_create_SQLAdvisor.sql
```

çalıştırın.

API ve Worker'ın `appsettings.json` dosyasındaki `ConnectionStrings:AdvisorDatabase` değerini kendi SQL Server 2025 instance'ınıza göre güncelleyin.

## 3. Data Protection anahtarları

API ve Worker **aynı** key ring klasörünü kullanmalıdır:

```text
C:\ProgramData\SqlServerAdvisor\Keys
```

Bu klasöre yalnızca API App Pool hesabı ve Worker Service hesabı erişebilmelidir. SQL Login parolaları SQLAdvisor DB'de düz metin tutulmaz.

## 4. Monitored SQL Server hesabı

`database/002_monitored_server_readonly_login_template.sql` örneğini inceleyin. Production üzerinde kesinlikle `sysadmin` kullanmayın.

Worker bağlantılarında:

```text
Application Name=SQLServerAdvisor.Worker
```

kullanılır. Böylece ileride Advisor'ın kendi sorguları workload analizinden dışlanabilir.

## 5. Backend geliştirme

```powershell
cd backend
dotnet restore
dotnet build SqlServerAdvisor.slnx

dotnet run --project src/SqlServerAdvisor.Api
dotnet run --project src/SqlServerAdvisor.Worker
```

API development endpoint'i:

```text
https://localhost:7140
```

## 6. Angular geliştirme

```powershell
cd frontend
npm install
npm start
```

Angular:

```text
http://localhost:4200
```

`proxy.conf.json` `/api` ve `/hubs` isteklerini `https://localhost:7140` adresine yönlendirir.

## 7. Production Worker

Örnek publish:

```powershell
dotnet publish backend/src/SqlServerAdvisor.Worker/SqlServerAdvisor.Worker.csproj -c Release -r win-x64 --self-contained false -o C:\SqlServerAdvisor\Worker
```

Ardından:

```powershell
.\deploy\install-worker.ps1 -PublishPath C:\SqlServerAdvisor\Worker
```

Windows Service otomatik başlar ve sistem reboot sonrası tekrar ayağa kalkar.

## Güvenlik sınırı

Bu proje tasarım gereği monitored server üzerinde otomatik düzeltme çalıştırmaz. Recommendation Engine ileride `CREATE INDEX`, `UPDATE STATISTICS` vb. **script önerebilir**, fakat API/Worker üzerinde bu scriptleri çalıştıran bir endpoint bulunmayacaktır.

## Sonraki milestone

- Capability Matrix (izin/sürüm/Query Store özellik tespiti)
- Blocking chain collector
- Wait stats delta collector
- Query Store / plan-cache workload collector
- Query Impact Score
- Execution plan XML parser
- Finding Evidence
- Recommendation Engine
