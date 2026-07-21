using System.Net;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Seller;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SellerNotificationPreferenceService : ISellerNotificationPreferenceService
{
    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly IWeeklySellerReportService _weeklySellerReportService;

    public SellerNotificationPreferenceService(
        AppDbContext dbContext,
        IRabbitMqPublisher rabbitMqPublisher,
        IWeeklySellerReportService weeklySellerReportService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _weeklySellerReportService = weeklySellerReportService ?? throw new ArgumentNullException(nameof(weeklySellerReportService));
    }

    public async Task<SellerNotificationPreferencesDto> GetAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveSellerAsync(sellerUserId, cancellationToken);
        var preference = await GetOrCreatePreferenceAsync(sellerUserId, cancellationToken);

        return MapToDto(preference);
    }

    public async Task<SellerNotificationPreferencesDto> UpdateAsync(
        Guid sellerUserId,
        SellerNotificationPreferencesDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await EnsureActiveSellerAsync(sellerUserId, cancellationToken);
        var preference = await GetOrCreatePreferenceAsync(sellerUserId, cancellationToken);

        preference.OrderEmails = dto.OrderEmails;
        preference.WeeklyReportEmails = dto.WeeklyReportEmails;
        preference.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(preference);
    }

    public async Task QueueTestOrderEmailAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var seller = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Shop)
            .FirstOrDefaultAsync(
                user => user.Id == sellerUserId &&
                    user.IsActive == true &&
                    user.DeletedAt == null &&
                    user.Shop != null &&
                    user.Shop.IsActive == true,
                cancellationToken);

        if (seller is null || seller.Shop is null)
        {
            throw new NotFoundException("Aktif magaza bulunamadi.");
        }

        await _rabbitMqPublisher.PublishSendEmailCommand(
            new SendEmailCommand(
                seller.Email,
                "Yeni sipariş aldınız",
                BuildOrderEmailBody(
                    seller.Shop.ShopName,
                    "Test urunu",
                    "TEST-ORDER",
                    "Test Alici",
                    123.45m,
                    "TRY",
                    Guid.Empty),
                true),
            cancellationToken);
    }

    public async Task<WeeklySellerReportPreviewResponseDto> QueueWeeklyReportPreviewAsync(
        Guid sellerUserId,
        WeeklySellerReportPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureActiveSellerAsync(sellerUserId, cancellationToken);

        var end = (request.EndDate ?? DateTime.UtcNow).ToUniversalTime();
        if (request.EndDate.HasValue && end.TimeOfDay == TimeSpan.Zero)
        {
            end = end.Date.AddDays(1).AddTicks(-1);
        }

        var start = (request.StartDate ?? end.AddDays(-7)).ToUniversalTime();
        if (start > end)
        {
            throw new BadRequestException("Baslangic tarihi bitis tarihinden buyuk olamaz.");
        }

        return await _weeklySellerReportService.GenerateAndQueueWeeklyReportAsync(
            sellerUserId,
            start,
            end,
            cancellationToken);
    }

    public async Task<bool> AreOrderEmailsEnabledAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var preference = await _dbContext.SellerNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == sellerUserId, cancellationToken);

        return preference?.OrderEmails ?? true;
    }

    public static string BuildOrderEmailBody(
        string shopName,
        string productTitle,
        string orderNumber,
        string buyerName,
        decimal amount,
        string? currency,
        Guid orderId)
    {
        var safeShopName = WebUtility.HtmlEncode(shopName);
        var safeProductTitle = WebUtility.HtmlEncode(productTitle);
        var safeOrderNumber = WebUtility.HtmlEncode(orderNumber);
        var safeBuyerName = WebUtility.HtmlEncode(buyerName);
        var safeCurrency = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(currency) ? "USD" : currency);

        return $"""
            <p>Merhaba {safeShopName},</p>
            <p>Yeni bir siparis aldiniz.</p>
            <ul>
              <li><strong>Urun:</strong> {safeProductTitle}</li>
              <li><strong>Siparis No:</strong> {safeOrderNumber}</li>
              <li><strong>Alici:</strong> {safeBuyerName}</li>
              <li><strong>Tutar:</strong> {amount:0.00} {safeCurrency}</li>
            </ul>
            <p>Seller panelindeki siparis detayindan kontrol edebilirsiniz.</p>
            <p>Referans: {orderId:D}</p>
            """;
    }

    private async Task<Shop> EnsureActiveSellerAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.UserId == sellerUserId && item.IsActive == true,
                cancellationToken);

        if (shop is null)
        {
            throw new NotFoundException("Aktif magaza bulunamadi.");
        }

        return shop;
    }

    private async Task<SellerNotificationPreference> GetOrCreatePreferenceAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken)
    {
        var preference = await _dbContext.SellerNotificationPreferences
            .FirstOrDefaultAsync(item => item.UserId == sellerUserId, cancellationToken);

        if (preference is not null)
        {
            return preference;
        }

        preference = new SellerNotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = sellerUserId,
            OrderEmails = true,
            WeeklyReportEmails = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.SellerNotificationPreferences.Add(preference);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return preference;
    }

    private static SellerNotificationPreferencesDto MapToDto(SellerNotificationPreference preference)
    {
        return new SellerNotificationPreferencesDto(
            preference.OrderEmails,
            preference.WeeklyReportEmails);
    }
}
