# SQL Server Advisor v1.0.0 Final Candidate - Build Status

Bu repository, SQL Server Advisor kaynak ağacının güncel halidir.

## Hedef mimari
- Angular 22 frontend
- ASP.NET Core / .NET 10 Web API
- .NET 10 Windows Worker Service (7/24 collector)
- SQL Server 2025 `SQLAdvisor` veritabanı
- İzlenen SQL Server'lara salt-okunur erişim

## GitHub CI doğrulaması
GitHub Actions üzerinde gerçek build doğrulaması yapılmıştır:

- .NET SDK: 10.0.401
- Backend restore: PASS
- Backend Release build: PASS
- Node.js: 24.20.0
- Angular dependency install: PASS
- Angular 22 production build: PASS

CI workflow: `.github/workflows/ci.yml`

## Güvenlik sınırı
- Monitored SQL Server tarafında otomatik DDL/DML/KILL uygulanmaz.
- `Recommendation.CanExecute` tasarım ve DB constraint gereği `false` kalır.
- Öneri `ScriptText` alanı yalnız inceleme/kopyalama amaçlıdır.
- Collector bağlantıları salt-okunur izleme amacıyla tasarlanmıştır.

## Kalan production doğrulamaları
Kaynak ve derleme doğrulaması geçmiş olsa da production devreye alma öncesinde hedef Windows/IIS + SQL Server 2025 ortamında aşağıdakiler ayrıca uygulanmalıdır:

- SQL Server 2025 fresh-install migration testi
- Gerçek monitored-server permission/capability testi
- Windows Authentication / RBAC entegrasyon testi
- Worker Windows Service uzun süreli çalışma testi
- IIS deployment ve SignalR testi
- SMTP kullanılıyorsa teslimat testi
- Gerçek workload altında collector overhead ve retention testi

## Durum
**Source + CI build verified final candidate.** Production ortamı entegrasyon testleri tamamlandıktan sonra release olarak etiketlenmelidir.
