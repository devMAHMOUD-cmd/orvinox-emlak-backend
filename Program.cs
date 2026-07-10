using CraftoraApi.Extensions;
using CraftoraApi.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

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
                var dsn = builder.Configuration["Sentry:Dsn"]
                    ?? Environment.GetEnvironmentVariable("SENTRY_DSN");

                if (!string.IsNullOrWhiteSpace(dsn))
                {
                    options.Dsn = dsn;
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
            // UYGULAMA PIPELINE'I
            // ──────────────────────────────────────────────────────────────────────────────
            var app = builder.Build();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
                await storageService.InitializeBucketsAsync();
            }

            // Tüm middleware'ları doğru sırada ekle
            app.UseCraftoraMiddleware();

            Log.Information("✅ Middleware'lar yapılandırıldı");

            // ──────────────────────────────────────────────────────────────────────────────
            // BAŞLATMA LOGLARı
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
            Log.Information("════════════════════════════════════════════════════════════════════════════════════");

            // Uygulamayı çalıştır
            // Uygulamayı çalıştır
            // Uygulamayı çalıştır
            // Uygulamayı çalıştır
            // Uygulamayı çalıştır
            // Uygulamayı çalıştır
            // ──────────────────────────────────────────────────────────────────────────────
            // VERİTABANI İNŞASI VE BAŞLATMA
            // ──────────────────────────────────────────────────────────────────────────────
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
