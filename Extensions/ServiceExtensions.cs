using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using FluentValidation.AspNetCore;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Minio;
using Elastic.Clients.Elasticsearch;
using Npgsql;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;
using CraftoraApi.Configuration;
using CraftoraApi.Data; // ← yeni namespace
using CraftoraApi.HostedServices;
using CraftoraApi.Hubs;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Services;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services;
using CraftoraApi.Services.Discovery;
using CraftoraApi.Services.Interfaces;
using CraftoraApi.Validators;
using FluentValidation;
using CraftoraApi.Data.Interceptors;
using CraftoraApi.Infrastructure.Security;

namespace CraftoraApi.Extensions;

/// <summary>
/// Craftora platformu için servisler kaydı yapan extension class
/// TikTok + Shopify + Udemy benzeri platformun tüm bağımlılıklarını yapılandırır
/// 
/// KULLANIM ÖRNEĞI - Program.cs'te:
/// 
/// using CraftoraApi.Extensions;
/// 
/// var builder = WebApplication.CreateBuilder(args);
/// 
/// // Tüm Craftora servislerini kaydet
/// builder.Services.AddCraftoraServices(builder.Configuration, builder.Environment);
/// 
/// var app = builder.Build();
/// 
/// // CORS'u kullan
/// app.UseCors("CraftoraPolicy");
/// 
/// // Rate Limiting'i kullan
/// app.UseRateLimiter();
/// 
/// // Health Checks endpoint'i
/// app.MapHealthChecks("/health");
/// 
/// // Swagger (dev ortamında)
/// if (app.Environment.IsDevelopment())
/// {
///     app.UseSwagger();
///     app.UseSwaggerUI();
/// }
/// 
/// app.UseAuthentication();
/// app.UseAuthorization();
/// app.MapControllers();
/// app.Run();
/// </summary>
public static class ServiceExtensions
{
    public static IServiceCollection AddStorageService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var storageSettings = GetStorageSettings(configuration);
        var s3Config = new AmazonS3Config
        {
            ServiceURL = storageSettings.ServiceUrl,
            ForcePathStyle = storageSettings.ForcePathStyle,
            UseHttp = storageSettings.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        };

        var credentials = new BasicAWSCredentials(
            storageSettings.AccessKey,
            storageSettings.SecretKey);

        services.AddSingleton<IAmazonS3>(new AmazonS3Client(credentials, s3Config));
        services.AddScoped<IStorageService, S3StorageService>();
        services.AddScoped<IUploadService, UploadService>();
        services.Configure<StorageSettings>(options =>
        {
            options.ServiceUrl = storageSettings.ServiceUrl;
            options.PublicServiceUrl = storageSettings.PublicServiceUrl;
            options.AccessKey = storageSettings.AccessKey;
            options.SecretKey = storageSettings.SecretKey;
            options.ForcePathStyle = storageSettings.ForcePathStyle;
            options.CorsAllowedOrigins = storageSettings.CorsAllowedOrigins;
        });

        return services;
    }

    /// <summary>
    /// Tüm Craftora servislerini Dependency Injection container'ına kaydetmek için extension method
    /// </summary>
    public static IServiceCollection AddCraftoraServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        #region Controllers

        // MVC controller'larını kaydı
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(
                    new SafeStringJsonConverter());
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        // FluentValidation - yeni syntax'ı kullan
        services.AddFluentValidationAutoValidation()
            .AddFluentValidationClientsideAdapters();
        services.AddValidatorsFromAssemblyContaining<CreateProductDtoValidator>();

        services.AddSignalR();

        #endregion

        #region Swagger / OpenAPI

        // Swagger/OpenAPI documentation - Sadece Development ortamında aktif
        if (environment.IsDevelopment())
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Craftora API",
                    Version = "v1",
                    Description = "TikTok + Shopify + Udemy benzeri sosyal ticaret platformu API",
                    Contact = new OpenApiContact
                    {
                        Name = "Craftora Dev Team",
                        Email = "dev@craftora.com"
                    }
                });

                // JWT Bearer scheme'ini Swagger'a ekle
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "JWT Bearer token ile authorization örneği: Bearer {token}"
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] { }
                    }
                });
            });
        }

        #endregion

        #region PostgreSQL + Entity Framework Core

        // PostgreSQL bağlantı string'i
        var postgresConnection = GetPostgresConnectionString(configuration);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(postgresConnection);
        dataSourceBuilder.MapEnum<UserRole>("user_role");
        dataSourceBuilder.MapEnum<ProductType>("product_type");
        dataSourceBuilder.MapEnum<MediaStatus>("media_status");
        dataSourceBuilder.MapEnum<OrderStatus>("order_status");
        dataSourceBuilder.MapEnum<PaymentStatusType>("payment_status_type");
        dataSourceBuilder.MapEnum<SubStatus>("sub_status");
        dataSourceBuilder.MapEnum<AnalyticsEventType>("analytics_event_type");
        dataSourceBuilder.MapEnum<SupportTicketStatus>("support_ticket_status");
        dataSourceBuilder.MapEnum<SupportMessageSenderRole>("support_message_sender_role");

        var postgresDataSource = dataSourceBuilder.Build();
        services.AddSingleton(postgresDataSource);
        services.AddSingleton<RlsInterceptor>();

        // DbContext kaydı - AppDbContext henüz yazılmadı, hazırlık aşamasında
        // DbContext kaydı - AppDbContext henüz yazılmadı, hazırlık aşamasında
        services.AddDbContextFactory<AppDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(
                postgresDataSource,
                npgsqlOptions =>
                {
                    npgsqlOptions.MapEnum<UserRole>("user_role");
                    npgsqlOptions.MapEnum<ProductType>("product_type");
                    npgsqlOptions.MapEnum<MediaStatus>("media_status");
                    npgsqlOptions.MapEnum<OrderStatus>("order_status");
                    npgsqlOptions.MapEnum<PaymentStatusType>("payment_status_type");
                    npgsqlOptions.MapEnum<SubStatus>("sub_status");
                    npgsqlOptions.MapEnum<AnalyticsEventType>("analytics_event_type");
                    npgsqlOptions.MapEnum<SupportTicketStatus>("support_ticket_status");
                    npgsqlOptions.MapEnum<SupportMessageSenderRole>("support_message_sender_role");

                    // Entity Framework Core için gereken timeout'ları ayarla
                    npgsqlOptions.CommandTimeout(60);
                })
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)); // 👈 İŞTE FİŞİ ÇEKTİĞİMİZ YER BURASI


            options.AddInterceptors(serviceProvider.GetRequiredService<RlsInterceptor>());

            // Development ortamında SQL sorgularını loglama
            if (environment.IsDevelopment())
            {
                options.LogTo(Console.WriteLine)
                    .EnableDetailedErrors()
                    .EnableSensitiveDataLogging();
            }
        });

        // Scoped DbContext - dependency injection üzerinden kullanılacak
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        #endregion

        #region Application Services

        services.Configure<EmailSettings>(configuration.GetSection("Email"));
        services.PostConfigure<EmailSettings>(ApplyEmailEnvironmentFallbacks);
        services.Configure<ResendInboundSettings>(
            configuration.GetSection("ResendInbound"));
        services.PostConfigure<ResendInboundSettings>(settings =>
        {
            settings.ApiKey = GetFirstNonWhiteSpace(
                Environment.GetEnvironmentVariable("RESEND_INBOUND_API_KEY"),
                settings.ApiKey);
            settings.WebhookSecret = GetFirstNonWhiteSpace(
                Environment.GetEnvironmentVariable("RESEND_WEBHOOK_SECRET"),
                settings.WebhookSecret);
            settings.SupportAddress = GetFirstNonWhiteSpace(
                    Environment.GetEnvironmentVariable("RESEND_SUPPORT_ADDRESS"),
                    settings.SupportAddress,
                    "support@craftoramedya.com")
                ?? "support@craftoramedya.com";
        });
        services.AddHttpClient("Resend", client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("ResendInbound", client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IShopService, ShopService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<ICourseSectionService, CourseSectionService>();
        services.AddScoped<ICourseLessonService, CourseLessonService>();
        services.AddScoped<ILessonResourceService, LessonResourceService>();
        services.AddScoped<ICourseQuizService, CourseQuizService>();
        services.AddScoped<ICourseProgressService, CourseProgressService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IProductQaService, ProductQaService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IPaymentService, MockPaymentService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<IGamificationService, GamificationService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IAdminEmailCampaignService, AdminEmailCampaignService>();
        services.AddScoped<IAdminCampaignEmailDeliveryService, AdminCampaignEmailDeliveryService>();
        services.AddScoped<IResendInboundService, ResendInboundService>();
        services.AddScoped<ISupportTicketService, SupportTicketService>();
        services.AddScoped<IAnalyticsEventService, AnalyticsEventService>();
        services.AddScoped<ICompetitionService, CompetitionService>();
        services.AddScoped<IPublicCourseService, PublicCourseService>();
        services.AddScoped<IMyCourseService, MyCourseService>();
        services.AddScoped<ISellerAnalyticsService, SellerAnalyticsService>();
        services.AddScoped<ISellerCourseService, SellerCourseService>();
        services.AddScoped<ISellerCustomerService, SellerCustomerService>();
        services.AddScoped<ISellerOrderService, SellerOrderService>();
        services.AddScoped<ISellerNotificationPreferenceService, SellerNotificationPreferenceService>();
        services.AddScoped<IWeeklySellerReportService, WeeklySellerReportService>();
        services.AddSingleton<IDiscoveryTrackingTokenService, DiscoveryTrackingTokenService>();
        services.AddSingleton<IDiscoveryFeedCursorService, DiscoveryFeedCursorService>();
        services.AddScoped<IDiscoveryEventService, DiscoveryEventService>();
        services.AddScoped<IDiscoveryRankingService, DiscoveryRankingService>();
        services.AddScoped<IDiscoveryFeedService, DiscoveryFeedService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddScoped<IVideoProcessingService, VideoProcessingService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
        services.AddSingleton<IJwtProvider, JwtProvider>();
        services.AddStorageService(configuration);
        services.AddHostedService<ElasticsearchSyncWorker>();
        services.AddHostedService<MediaViewCountSyncWorker>();
        services.AddHostedService<SubscriptionMonitorWorker>();
        services.AddHostedService<WeeklySellerReportWorker>();

        #endregion

        #region JWT Authentication

        // JWT konfigürasyonunu appsettings'ten oku
        var jwtSettings = configuration.GetSection("Jwt");
        var secretKey = jwtSettings["Secret"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            secretKey = Environment.GetEnvironmentVariable("JWT_SECRET");
        }

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("JWT Secret not found in configuration.");
        }
        var issuer = jwtSettings["Issuer"] ?? "CraftoraApi";
        var audience = jwtSettings["Audience"] ?? "CraftoraApp";

        var key = Encoding.UTF8.GetBytes(secretKey);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.SaveToken = true;
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),

                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audience,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(10),

                // Keep JWT claim names stable for policy and role authorization checks.
                NameClaimType = "sub",
                RoleClaimType = "role"
            };

            // JWT Bearer token'ı WebSocket bağlantılarında da destekle (real-time features için)
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // WebSocket handshake'de access_token query parameter'ından token oku
                    if (context.Request.Query.TryGetValue("access_token", out var token))
                    {
                        context.Token = token.ToString();
                    }
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = new
                        {
                            code = "UNAUTHORIZED",
                            message = "Kimlik dogrulamasi gerekli.",
                            statusCode = StatusCodes.Status401Unauthorized,
                            requestId = context.HttpContext.TraceIdentifier
                        }
                    });
                },
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = new
                        {
                            code = "FORBIDDEN",
                            message = "Bu isleme yetkiniz yok.",
                            statusCode = StatusCodes.Status403Forbidden,
                            requestId = context.HttpContext.TraceIdentifier
                        }
                    });
                },
                OnTokenValidated = async context =>
                {
                    var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();
                    var rawToken = context.SecurityToken is JwtSecurityToken token
                        ? token.RawData
                        : context.Request.Query.TryGetValue("access_token", out var queryToken)
                            ? queryToken.ToString()
                            : context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                                ? context.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim()
                                : string.Empty;

                    var blacklistValue = string.IsNullOrWhiteSpace(rawToken)
                        ? null
                        : await cache.GetStringAsync($"blacklist:{rawToken}");

                    if (!string.IsNullOrWhiteSpace(blacklistValue))
                    {
                        context.Fail("Bu token cikis yapildigi icin gecersiz kilinmistir.");
                        return;
                    }

                    var userIdValue = context.Principal?.FindFirst("sub")?.Value
                        ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? context.Principal?.FindFirst("nameid")?.Value
                        ?? context.Principal?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
                    if (!Guid.TryParse(userIdValue, out var userId))
                    {
                        context.Fail("Gecersiz kullanici token'i.");
                        return;
                    }

                    var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var user = await dbContext.Users
                        .AsNoTracking()
                        .Where(item => item.Id == userId)
                        .Select(item => new
                        {
                            item.IsActive,
                            item.LockedUntil,
                            item.DeletedAt
                        })
                        .FirstOrDefaultAsync();

                    if (user is null || user.DeletedAt is not null)
                    {
                        context.Fail("Hesap kullanima kapatildi.");
                        return;
                    }

                    if (user.IsActive != true)
                    {
                        context.Fail("Hesap askiya alindi.");
                        return;
                    }

                    if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
                    {
                        context.Fail($"Hesabiniz {user.LockedUntil.Value:O} tarihine kadar kilitli.");
                    }
                }
            };
        });

        #endregion

        #region Authorization

        // Policy-based authorization konfigürasyonu
        services.AddAuthorization(options =>
        {
            // Temel authenticated user policy
            options.AddPolicy("AuthenticatedUser", policy =>
            {
                policy.RequireAuthenticatedUser();
            });

            // Admin özel yetkisi
            options.AddPolicy("AdminOnly", policy =>
            {
                policy.RequireAuthenticatedUser()
                    .RequireClaim("role", "admin");
            });

            // Satıcı yetkisi
            options.AddPolicy("SellerOnly", policy =>
            {
                policy.RequireAuthenticatedUser()
                    .RequireClaim("role", "seller", "admin");
            });

            // Content Creator yetkisi
            options.AddPolicy("CreatorOnly", policy =>
            {
                policy.RequireAuthenticatedUser()
                    .RequireClaim("role", "creator", "admin");
            });
        });

        #endregion

        #region Redis Cache

        // Redis bağlantı yapılandırması
        var redisConnection = GetRedisConnectionString(configuration);

        var redisOptions = ConfigurationOptions.Parse(redisConnection);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectTimeout = 5000;
        redisOptions.SyncTimeout = 5000;

        // IConnectionMultiplexer'i singleton olarak kaydet
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisOptions));

        // Redis için distributed cache'i kaydet
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "craftora_";
        });

        #endregion

        #region MassTransit + RabbitMQ

        // RabbitMQ bağlantı string'i
        var rabbitMqConnection = GetRabbitMqConnectionString(configuration);

        services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
        {
            Uri = new Uri(rabbitMqConnection),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        });

        services.AddMassTransit(x =>
        {
            // Tüm Consumer'ları otomatik olarak kaydet (Consumers klasöründen)
            x.AddConsumers(typeof(Program).Assembly);

            // RabbitMQ transport'unu yapılandır
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqConnection);

                // Retry politikası - başarısız mesajlar otomatik tekrar denesin
                cfg.UseMessageRetry(retry =>
                {
                    retry.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
                });

                // Dead letter queue politikası
                cfg.UsePublishMessageScheduler();

                // Endpoint'leri yapılandır
                cfg.ConfigureEndpoints(context);
            });
        });

        #endregion

        #region MinIO Object Storage

        // MinIO yapılandırması
        var minioSettings = configuration.GetSection("MinIO");
        var minioEndpoint = minioSettings["Endpoint"];
        if (string.IsNullOrWhiteSpace(minioEndpoint))
        {
            minioEndpoint = Environment.GetEnvironmentVariable("MINIO_ENDPOINT") ?? "localhost:9000";
        }

        var minioAccessKey = minioSettings["AccessKey"];
        if (string.IsNullOrWhiteSpace(minioAccessKey))
        {
            minioAccessKey = Environment.GetEnvironmentVariable("MINIO_ROOT_USER");
        }

        var minioSecretKey = minioSettings["SecretKey"];
        if (string.IsNullOrWhiteSpace(minioSecretKey))
        {
            minioSecretKey = Environment.GetEnvironmentVariable("MINIO_ROOT_PASSWORD");
        }
        if (string.IsNullOrWhiteSpace(minioAccessKey) || string.IsNullOrWhiteSpace(minioSecretKey))
        {
            throw new InvalidOperationException("MinIO credentials are missing in configuration.");
        }
        var minioUseSSL = bool.TryParse(minioSettings["UseSSL"], out var useSSL) && useSSL;

        // MinIO client'ı singleton olarak kaydet
        services.AddSingleton<IMinioClient>(sp =>
            new MinioClient()
                .WithEndpoint(minioEndpoint)
                .WithCredentials(minioAccessKey, minioSecretKey)
                .WithSSL(minioUseSSL)
                .Build());

        #endregion

        #region Elasticsearch NEST

        // Elasticsearch bağlantı string'i
        var elasticsearchUrl = configuration.GetConnectionString("Elasticsearch");
        if (string.IsNullOrWhiteSpace(elasticsearchUrl))
        {
            elasticsearchUrl = Environment.GetEnvironmentVariable("ELASTICSEARCH_URL") ?? "http://localhost:9200";
        }

        // ✅ Elastic.Clients.Elasticsearch 8.x ile
        services.AddSingleton<ElasticsearchClient>(sp =>
        {
            var settings = new ElasticsearchClientSettings(new Uri(elasticsearchUrl))
                .DefaultIndex("craftora_");

            if (environment.IsDevelopment())
            {
                settings = settings.EnableDebugMode();
            }

            return new ElasticsearchClient(settings);
        });

        #endregion

        #region CORS

        // CORS politikasi - appsettings'ten AllowedOrigins'i oku
        var corsSettings = configuration.GetSection("Cors");
        var allowedOrigins = corsSettings.GetSection("AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        allowedOrigins = allowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedOrigins.Length == 0)
        {
            Log.Warning("Cors:AllowedOrigins is empty. CORS will reject all browser origins until it is configured.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy("CraftoraCorsPolicy", builder =>
            {
                builder
                    .WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithExposedHeaders("X-Total-Count", "X-Page-Number");
            });
        });

        #endregion

        #region Rate Limiting

        // Rate Limiting politikaları - dakika başına istek limitleri
        var rateLimitSettings = configuration.GetSection("RateLimit");
        var generalLimit = int.Parse(rateLimitSettings["General"] ?? "100");
        var authLimit = int.Parse(rateLimitSettings["Auth"] ?? "10");
        // One asset uses both presign and completion requests. A product or
        // course can legitimately upload multiple assets in a short burst.
        var uploadLimit = Math.Max(int.Parse(rateLimitSettings["Upload"] ?? "60"), 60);
        var searchLimit = int.Parse(rateLimitSettings["Search"] ?? "30");

        services.AddRateLimiter(options =>
        {
            options.AddPolicy("GlobalLimit", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("AuthLimit", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            // Varsayılan - Genel API endpoint'leri için
            options.AddPolicy("general", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = generalLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            // Authentication endpoint'leri - brute force koruması
            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            // Dosya yükleme endpoint'leri
            options.AddPolicy("upload", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = uploadLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            // Arama endpoint'leri
            options.AddPolicy("search", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = searchLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("support-ticket-create", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("support-ticket-message", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(10),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            // Rate limit aşıldığında 429 Too Many Requests dön
            options.AddPolicy("seller-email-test", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit aşıldı",
                    message = "Lütfen biraz sonra tekrar deneyin",
                    retryAfter = context.HttpContext.Response.Headers["Retry-After"]
                });
            };
        });

        #endregion

        #region FluentValidation

        // FluentValidation - validators otomatik olarak kaydedilir
        // AddFluentValidationAutoValidation() ve AddFluentValidationClientsideAdapters()
        // zaten Controllers bölümünde çağrıldı

        #endregion

        #region HttpContext Accessor

        // Http context'ine erişim - middleware ve service'ler için
        services.AddHttpContextAccessor();

        #endregion

        #region Health Checks

        // Uygulama sağlık kontrolleri
        // Simple health checks - PostgreSQL connection kontrolü yapılır
        var healthPostgresConnection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(healthPostgresConnection))
        {
            healthPostgresConnection = postgresConnection;
        }

        services.AddHealthChecks()
            .AddNpgSql(healthPostgresConnection, name: "PostgreSQL")
            .AddRedis(redisConnection, name: "Redis")
            .AddRabbitMQ(
                serviceProvider => serviceProvider
                    .GetRequiredService<IConnectionFactory>()
                    .CreateConnectionAsync()
                    .GetAwaiter()
                    .GetResult(),
                name: "RabbitMQ");

        #endregion

        return services;
    }

    private static StorageSettings GetStorageSettings(IConfiguration configuration)
    {
        var settings = configuration.GetSection("Storage").Get<StorageSettings>() ?? new StorageSettings();
        var minioSettings = configuration.GetSection("MinIO");
        var useSsl = bool.TryParse(
            Environment.GetEnvironmentVariable("MINIO_USE_SSL") ?? minioSettings["UseSSL"],
            out var parsedUseSsl) && parsedUseSsl;
        var scheme = useSsl ? "https" : "http";

        if (string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            var endpoint = Environment.GetEnvironmentVariable("MINIO_INTERNAL_ENDPOINT")
                ?? minioSettings["InternalEndpoint"]
                ?? minioSettings["Endpoint"];
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                settings.ServiceUrl = NormalizeStorageEndpoint(endpoint, scheme);
            }

            settings.AccessKey = minioSettings["AccessKey"] ?? settings.AccessKey;
            settings.SecretKey = minioSettings["SecretKey"] ?? settings.SecretKey;
            settings.ForcePathStyle = true;
        }

        var publicEndpoint = GetFirstNonWhiteSpace(
            settings.PublicEndpoint,
            Environment.GetEnvironmentVariable("MINIO_PUBLIC_ENDPOINT"),
            minioSettings["PublicEndpoint"]);
        if (string.IsNullOrWhiteSpace(settings.PublicServiceUrl) &&
            !string.IsNullOrWhiteSpace(publicEndpoint))
        {
            settings.PublicEndpoint = publicEndpoint;
            var publicUseSsl = GetOptionalBoolean(Environment.GetEnvironmentVariable("MINIO_PUBLIC_USE_SSL"))
                ?? GetOptionalBoolean(minioSettings["PublicUseSSL"])
                ?? settings.PublicUseSSL
                ?? useSsl;

            settings.PublicUseSSL = publicUseSsl;

            var publicScheme = publicUseSsl
                ? "https"
                : "http";

            settings.PublicServiceUrl = NormalizeStorageEndpoint(publicEndpoint, publicScheme);
        }

        if (string.IsNullOrWhiteSpace(settings.PublicServiceUrl))
        {
            settings.PublicServiceUrl = settings.ServiceUrl;
        }

        settings.AccessKey = string.IsNullOrWhiteSpace(settings.AccessKey)
            ? Environment.GetEnvironmentVariable("MINIO_ROOT_USER") ?? settings.AccessKey
            : settings.AccessKey;
        settings.SecretKey = string.IsNullOrWhiteSpace(settings.SecretKey)
            ? Environment.GetEnvironmentVariable("MINIO_ROOT_PASSWORD") ?? settings.SecretKey
            : settings.SecretKey;

        if (string.IsNullOrWhiteSpace(settings.ServiceUrl) ||
            string.IsNullOrWhiteSpace(settings.AccessKey) ||
            string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException("Storage settings are missing in appsettings.");
        }

        return settings;
    }

    private static string NormalizeStorageEndpoint(string endpoint, string scheme)
    {
        return endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? endpoint.TrimEnd('/')
            : $"{scheme}://{endpoint.TrimEnd('/')}";
    }

    private static bool? GetOptionalBoolean(string? value)
    {
        return bool.TryParse(value, out var parsedValue)
            ? parsedValue
            : null;
    }

    private static string? GetFirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static void ApplyEmailEnvironmentFallbacks(EmailSettings settings)
    {
        settings.Provider = "resend";

        settings.ApiKey = GetValueOrFallback(
            Environment.GetEnvironmentVariable("RESEND_API_KEY"),
            Environment.GetEnvironmentVariable("Email__Resend__ApiKey"),
            Environment.GetEnvironmentVariable("Resend__ApiKey"),
            settings.ApiKey);

        settings.Resend.ApiKey = GetValueOrFallback(
            Environment.GetEnvironmentVariable("RESEND_API_KEY"),
            Environment.GetEnvironmentVariable("Email__Resend__ApiKey"),
            Environment.GetEnvironmentVariable("Resend__ApiKey"),
            settings.Resend.ApiKey,
            settings.ApiKey);

        settings.FromEmail = GetValueOrFallback(
            Environment.GetEnvironmentVariable("EMAIL_FROM"),
            settings.FromEmail,
            "onboarding@resend.dev");

        settings.FromName = GetValueOrFallback(
            Environment.GetEnvironmentVariable("EMAIL_FROM_NAME"),
            settings.FromName,
            "Craftora");

        settings.ReplyTo = GetFirstNonWhiteSpace(
            Environment.GetEnvironmentVariable("EMAIL_REPLY_TO"),
            Environment.GetEnvironmentVariable("Email__ReplyTo"),
            settings.ReplyTo);
    }

    private static string GetValueOrFallback(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string GetPostgresConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("PostgreSQL")
            ?? configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "CraftoraMobile";
        var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "admin";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("PostgreSQL connection string or POSTGRES_PASSWORD is missing.");
        }

        return $"Host=localhost;Database={database};Username={username};Password={password}";
    }

    private static string GetRateLimitPartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ipAddress}";
    }

    private static string GetRedisConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var password = Environment.GetEnvironmentVariable("REDIS_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Redis connection string or REDIS_PASSWORD is missing.");
        }

        return $"localhost:6379,password={password},abortConnect=false";
    }

    private static string GetRabbitMqConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("RabbitMQ");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var username = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER") ?? "admin";
        var password = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS");

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("RabbitMQ connection string or RABBITMQ_DEFAULT_PASS is missing.");
        }

        return $"amqp://{username}:{password}@localhost:5672";
    }
}
