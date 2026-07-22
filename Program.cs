using CraftoraApi.Extensions;
using CraftoraApi.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Microsoft.EntityFrameworkCore;
using CraftoraApi.Data; // AppDbContext'i görmesi için eklendi

namespace CraftoraApi;

/// <summary>
/// Craftora - TikTok + Shopify + Udemy benzeri sosyal ticaret platformu
/// Production-ready .NET 9 uygulaması
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            // ──────────────────────────────────────────────────────────────────────────────
            // SERILOG YAPILANDIRMASI
            // ──────────────────────────────────────────────────────────────────────────────
            var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)

            // Enrichment - her log'a context bilgisi ekle
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "CraftoraApi")
            .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
            .Enrich.WithProperty("MachineName", Environment.MachineName)

            // Development: Console'a renkli yaz
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")

            // Production: Dosyaya da JSON formatında yaz
            .WriteTo.File(
                path: "logs/craftora-.log",
                formatter: new CompactJsonFormatter(),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 104857600, // 100MB
                rollOnFileSizeLimit: true)

            // Hata logları ayrı dosyaya
            .WriteTo.File(
                path: "logs/errors/craftora-errors-.log",
                formatter: new CompactJsonFormatter(),
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Error,
                retainedFileCountLimit: 90,
                fileSizeLimitBytes: 104857600)

            .CreateLogger();

            Log.Information("════════════════════════════════════════════════════════════════════════════════════");
            Log.Information("🚀 Craftora API başlatılıyor...");
            Log.Information("════════════════════════════════════════════════════════════════════════════════════");

            // ──────────────────────────────────────────────────────────────────────────────
            // BUILDER YAPILANDIRMASI
            // ──────────────────────────────────────────────────────────────────────────────
            DotNetEnv.Env.Load(); // .env dosyasını bulup sistem değişkenlerine yükler
            var builder = WebApplication.CreateBuilder(args);

            // Serilog'u Host'a bağla
            builder.Host.UseSerilog();
            builder.WebHost.UseSentry(options =>
            {
                var configuredDsn = builder.Configuration["Sentry:Dsn"];
                var dsn = string.IsNullOrWhiteSpace(configuredDsn)
                    ? Environment.GetEnvironmentVariable("SENTRY_DSN")
                    : configuredDsn;

                if (Uri.TryCreate(dsn?.Trim(), UriKind.Absolute, out var dsnUri) &&
                    (dsnUri.Scheme == Uri.UriSchemeHttp || dsnUri.Scheme == Uri.UriSchemeHttps))
                {
                    options.Dsn = dsnUri.AbsoluteUri;
                }
                else if (!string.IsNullOrWhiteSpace(dsn))
                {
                    Log.Warning("Invalid Sentry DSN configured. Sentry disabled for this process.");
                }

                options.Environment = builder.Environment.EnvironmentName;
                options.Release = builder.Configuration["Sentry:Release"];
                options.Debug = builder.Environment.IsDevelopment();
                options.TracesSampleRate = builder.Configuration.GetValue<double?>("Sentry:TracesSampleRate") ?? 0.0;
            });

            // Kestrel server yapılandırması
            builder.WebHost.ConfigureKestrel(options =>
            {
                // Server header'ını gizle (security)
                options.AddServerHeader = false;

                // Max request body size: 10MB (presigned URL ve file upload için)
                options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB

                // Request timeout: 30 saniye
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
            });

            // Tüm Craftora servislerini kaydet
            builder.Services.AddCraftoraServices(
                builder.Configuration,
                builder.Environment);

            Log.Information("✅ Servisler kayıt edildi");
            // ──────────────────────────────────────────────────────────────────────────────
            // UYGULAMA PIPELINE'I VE BAŞLATMA
            // ──────────────────────────────────────────────────────────────────────────────
            var app = builder.Build();

            // Scope açarak Başlangıç Servislerini ve Veritabanı Migration'ını çalıştır
            await using (var scope = app.Services.CreateAsyncScope())
            {
                // 1. VERİTABANI TABLOLARINI OLUŞTUR
                try
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    Log.Information("🔍 Veritabanı durumu kontrol ediliyor...");

                    var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

                    if (pendingMigrations.Any())
                    {
                        Log.Information("⏳ {Count} adet migration uygulanıyor...", pendingMigrations.Count());
                        await dbContext.Database.MigrateAsync();
                        Log.Information("✅ Veritabanı migration'ları başarıyla uygulandı.");
                    }
                    else
                    {
                        // Eğer projede hiç Migration dosyası yoksa tabloları direkt DbContext modellerinden oluştur
                        Log.Information("🔨 Migration bulunamadı, tablolar modellerden direkt oluşturuluyor...");
                        await dbContext.Database.EnsureCreatedAsync();
                        Log.Information("✅ Veritabanı tabloları başarıyla oluşturuldu.");
                    }

                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Veritabanı tabloları oluşturulurken HATA meydana geldi!");
                }

                // 2. STORAGE (MinIO) BUCKET'LARINI HAZIRLA
                try
                {
                    var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
                    await storageService.InitializeBucketsAsync();
                    Log.Information("✅ MinIO Bucket'ları hazırlandı.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "⚠️ MinIO başlatılırken uyarı alındı.");
                }
            }

            // Tüm middleware'ları doğru sırada ekle
            app.UseCraftoraMiddleware();

            Log.Information("✅ Middleware'lar yapılandırıldı");

            // ──────────────────────────────────────────────────────────────────────────────
            // BAŞLATMA LOGLARI
            // ──────────────────────────────────────────────────────────────────────────────
            var environment = app.Environment.EnvironmentName;
            var url = app.Urls.FirstOrDefault() ?? "http://+:5000";
            Log.Information("🌐 API URL: {Url}", url);

            if (app.Environment.IsDevelopment())
            {
                Log.Information("📚 Swagger UI: {Url}/api-docs", url);
                Log.Information("🏥 Health Check: {Url}/health", url);
            }

            Log.Information("════════════════════════════════════════════════════════════════════════════════════");
            Log.Information("✨ Craftora API başarıyla başlatıldı!");
            Log.Information("════════════════════════════════════════════════════════────────────────────");

            // Uygulamayı çalıştır
            await app.RunAsync();
        }
        catch (HostAbortedException)
        {
            // EF Core migration komutlari host'u bilerek durdurdugu icin susturuyoruz.
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "❌ Uygulama başlatılırken kritik hata oluştu!");
            Environment.Exit(1);
        }
        finally
        {
            Log.Information("🛑 Craftora API kapatılıyor...");
            await Log.CloseAndFlushAsync();
        }
    }

}
