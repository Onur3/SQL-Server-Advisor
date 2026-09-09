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
- `deploy/` Windows Service / IIS kurulum dosyaları

## 1. Gereksinimler

### Development / kaynaktan production kurulumu

- Windows Server 2019 veya üzeri
- .NET SDK 10.x
- Node.js 24.x veya desteklenen daha yeni sürüm
- npm
- SQL Server 2025 (17.x)
- `sqlcmd` (Microsoft SQL command line tool)
- Visual Studio 2026 veya VS Code (development için isteğe bağlı)

`deploy/install.ps1` IIS rolünü ve eksikse .NET 10 Hosting Bundle / ASP.NET Core Module V2 bileşenini otomatik kurar.

## 2. Tek komut otomatik kurulum

PowerShell'i **Run as Administrator** ile açın ve repository kökünde çalıştırın:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force

.\deploy\install.ps1 `
  -SqlInstance "localhost" `
  -HostName "SQLADVISOR-SRV" `
  -HttpPort 8088
```

Windows Authentication kullanılacaksa:

```powershell
.\deploy\install.ps1 `
  -SqlInstance "localhost" `
  -HostName "sqladvisor.kolunsag.local" `
  -HttpPort 8088 `
  -EnableWindowsAuthentication
```

Script aşağıdaki işlemleri otomatik yapar:

1. Administrator ve build toolchain kontrolü.
2. IIS Windows Server rolü / gerekli IIS feature kurulumu.
3. ASP.NET Core Module V2 yoksa .NET 10 Hosting Bundle kurulumu.
4. API için Release `dotnet publish`.
5. Worker için Release `dotnet publish`.
6. Angular production build.
7. Angular çıktısının API `wwwroot` içine yerleştirilmesi.
8. Tek IIS Site + Application Pool oluşturulması veya güncellenmesi.
9. Worker'ın `SQLServerAdvisorWorker` Windows Service olarak kurulması.
10. `database/` altındaki `template` olmayan SQL migration dosyalarının sırasıyla çalıştırılması.
11. `SQLAdvisor` veritabanının oluşturulması/güncellenmesi.
12. IIS AppPool ve Worker sanal servis hesaplarının SQLAdvisor DB izinlerinin verilmesi.
13. Data Protection key klasörü ve NTFS ACL izinlarının oluşturulması.
14. IIS + Worker başlatılması.
15. `/health` endpoint ile kurulum doğrulaması.

Varsayılan kurulum dizini:

```text
C:\Program Files\SqlServerAdvisor
```

Data Protection key ring:

```text
C:\ProgramData\SqlServerAdvisor\Keys
```

Varsayılan servis kimlikleri:

```text
IIS APPPOOL\SQLServerAdvisor
NT SERVICE\SQLServerAdvisorWorker
```

### Uzak SQLAdvisor veritabanı

Otomatik Windows virtual-account SQL grant modeli, SQLAdvisor veritabanının uygulama sunucusuyla aynı Windows Server üzerinde olduğu senaryo için tasarlanmıştır.

SQLAdvisor DB uzak bir SQL Server üzerindeyse uygulama connection string'i açıkça verilebilir:

```powershell
.\deploy\install.ps1 `
  -SqlInstance "SQLDB01" `
  -HostName "sqladvisor.kolunsag.local" `
  -AdvisorConnectionString "Server=SQLDB01;Database=SQLAdvisor;User ID=SqlAdvisorApp;Password=***;Encrypt=True;TrustServerCertificate=True" `
  -SkipIdentityGrants
```

Bu durumda uzak SQL Server üzerindeki login/user izinları DBA tarafından ayrıca tanımlanmalıdır. Parolayı komut satırında bırakmak yerine production ortamında secret/config yönetimi kullanılması önerilir.

Kurulum seçeneklerini görmek için:

```powershell
Get-Help .\deploy\install.ps1 -Full
```

## 3. Manuel SQLAdvisor DB kurulumu

Otomatik installer kullanılmıyorsa SSMS/sqlcmd ile `database/` altındaki `template` olmayan migration dosyalarını dosya adına göre sırayla çalıştırın. İlk kurulum dosyası:

```text
database/001_create_SQLAdvisor.sql
```

API ve Worker'ın `appsettings.json` / `appsettings.Production.json` dosyasındaki `ConnectionStrings:AdvisorDatabase` değerini kendi SQL Server 2025 instance'ınıza göre güncelleyin.

## 4. Data Protection anahtarları

API ve Worker **aynı** key ring klasörünü kullanmalıdır:

```text
C:\ProgramData\SqlServerAdvisor\Keys
```

Bu klasöre yalnızca API App Pool hesabı ve Worker Service hesabı erişebilmelidir. Otomatik installer ACL izinlerini tanımlar. SQL Login parolaları SQLAdvisor DB'de düz metin tutulmaz.

## 5. Monitored SQL Server hesabı

`database/002_monitored_server_readonly_login_template.sql` örneğini inceleyin. Production üzerinde kesinlikle `sysadmin` kullanmayın.

Bu template **otomatik kurulumda çalıştırılmaz**. İzlenen production SQL Server'a verilecek salt-okunur yetkiler bilinçli olarak DBA kontrolünde bırakılmıştır.

Worker bağlantılarında:

```text
Application Name=SQLServerAdvisor.Worker
```

kullanılır.

## 6. Backend geliştirme

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

## 7. Angular geliştirme

```powershell
cd frontend
npm install
npm start
```

Angular:

```text
http://localhost:4200
```

`proxy.conf.json` `/api` ve `/hubs` isteklerini development API adresine yönlendirir.

## Production mimarisi

Production kurulumunda Angular ayrı bir IIS sitesi değildir. Angular browser bundle API publish dizinindeki `wwwroot` altında bulunur. ASP.NET Core aynı IIS uygulamasından:

- Angular statik dosyalarını,
- `/api/...` controller endpoint'lerini,
- `/hubs/...` SignalR endpoint'ini,
- `/health` endpoint'ini

servis eder. Böylece ARR/reverse proxy veya ikinci bir IIS site gereksinimi yoktur.

## Güvenlik sınırı

Bu proje tasarım gereği monitored server üzerinde otomatik düzeltme çalıştırmaz. Recommendation Engine ileride `CREATE INDEX`, `UPDATE STATISTICS` vb. **script önerebilir**, fakat API/Worker üzerinde bu scriptleri çalıştıran bir endpoint bulunmayacaktır.
