# SQL Server Advisor v1.0.0 Final Candidate - Build Status

Bu paket, bu konuşmada erişilebilir olan kaynak ağacının en güncel halidir.

## Hedef mimari
- Angular 22 frontend
- ASP.NET Core / .NET 10 Web API
- .NET 10 Windows Worker Service (7/24 collector)
- SQL Server 2025 SQLAdvisor veritabanı
- İzlenen SQL Server'lara salt-okunur erişim

## Güvenlik sınırı
- Monitored SQL Server tarafında otomatik DDL/DML/KILL uygulanmaz.
- Recommendation.CanExecute varsayılan ve tasarım gereği false'tur.
- ScriptText yalnız inceleme/kopyalama amaçlıdır.

## Bu çalışma ortamındaki doğrulama sınırı
- .NET 10 SDK bu çalışma ortamında yok; dotnet restore/build/test çalıştırılamadı.
- Node mevcut: 22.16.0. Angular 22 production compiler doğrulaması tamamlanmadı.
- Bu nedenle paket production'a alınmadan önce deploy/build-release adımlarının hedef build sunucusunda çalıştırılması gerekir.

## Not
Paket adı "final-candidate"tır; gerçek Release build ve entegrasyon testi geçmeden production-certified olarak değerlendirilmemelidir.
