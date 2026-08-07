using CraftoraApi.Data;
using CraftoraApi.DTOs.Interaction;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class ProductQaService : IProductQaService
{
    private readonly AppDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ProductQaService> _logger;

    public ProductQaService(
        AppDbContext dbContext,
        INotificationService notificationService,
        ILogger<ProductQaService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<QaResponseDto> AskQuestionAsync(Guid userId, CreateQuestionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.Id == dto.ProductId &&
                product.IsActive == true &&
                product.Status == ProductStatus.Published &&
                product.Shop.IsActive == true)
            .Select(product => new
            {
                product.Id,
                product.Title,
                product.ShopId,
                OwnerUserId = product.Shop.UserId
            })
            .FirstOrDefaultAsync();
        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var actor = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.FullName,
                user.AvatarUrl,
                ShopId = (Guid?)user.Shop!.Id,
                ShopName = user.Shop != null ? user.Shop.ShopName : null,
                ShopLogoUrl = user.Shop != null ? user.Shop.LogoUrl : null
            })
            .FirstAsync();

        var question = new ProductQa
        {
            ProductId = dto.ProductId,
            UserId = userId,
            Message = PlainTextInputValidator.Require(dto.QuestionText, "Soru metni", 500),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProductQas.Add(question);
        await _dbContext.SaveChangesAsync();

        if (product.OwnerUserId != userId)
        {
            try
            {
                await _notificationService.SendActorNotificationAsync(
                    product.OwnerUserId,
                    "Urunune yeni bir soru geldi",
                    $"{DisplayName(actor.FullName)} {product.Title} icin sordu: {question.Message}",
                    NotificationType.NewQuestion,
                    product.Id,
                    userId,
                    actor.FullName,
                    actor.AvatarUrl,
                    actor.ShopId,
                    actor.ShopName,
                    actor.ShopLogoUrl,
                    "product");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Product question notification failed after question was saved. QuestionId: {QuestionId}",
                    question.Id);
            }
        }

        return await GetQaResponseAsync(question.Id);
    }

    public async Task<QaResponseDto> AnswerQuestionAsync(Guid questionId, Guid sellerUserId, AnswerQuestionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var question = await _dbContext.ProductQas
            .Include(question => question.Product)
            .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(question =>
                question.Id == questionId &&
                question.ParentId == null);

        if (question is null)
        {
            throw new NotFoundException("Soru bulunamadi.");
        }

        if (question.Product.Shop.UserId != sellerUserId)
        {
            throw new ForbiddenException("Bu soruya cevap verme yetkiniz yok.");
        }

        var answer = new ProductQa
        {
            ProductId = question.ProductId,
            UserId = sellerUserId,
            ParentId = question.Id,
            Message = PlainTextInputValidator.Require(dto.AnswerText, "Cevap metni", 1000),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProductQas.Add(answer);
        await _dbContext.SaveChangesAsync();

        if (question.UserId != sellerUserId)
        {
            try
            {
                await _notificationService.SendProductQuestionAnswerNotificationAsync(
                    question.UserId,
                    question.ProductId,
                    question.Id,
                    question.Product.ShopId,
                    question.Product.Shop.ShopName,
                    question.Product.Shop.LogoUrl,
                    answer.Message);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Question answer notification failed after answer was saved. QuestionId: {QuestionId}, AnswerId: {AnswerId}",
                    question.Id,
                    answer.Id);
            }
        }

        return await GetQaResponseAsync(answer.Id);
    }

    public async Task DeleteQuestionAsync(Guid questionId, Guid userId)
    {
        var question = await _dbContext.ProductQas.FirstOrDefaultAsync(question =>
            question.Id == questionId &&
            question.UserId == userId);

        if (question is null)
        {
            throw new NotFoundException("Soru bulunamadi.");
        }

        _dbContext.ProductQas.Remove(question);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<QaResponseDto>> GetProductQuestionsAsync(Guid productId)
    {
        var productExists = await _dbContext.Products.AnyAsync(product =>
            product.Id == productId &&
            product.IsActive == true);
        if (!productExists)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var questions = await _dbContext.ProductQas
            .AsNoTracking()
            .Include(question => question.User)
            .Include(question => question.InverseParent)
            .ThenInclude(answer => answer.User)
            .Where(question =>
                question.ProductId == productId &&
                question.ParentId == null)
            .OrderByDescending(question => question.CreatedAt)
            .ToListAsync();

        return questions.Select(MapToResponse).ToList();
    }

    private async Task<QaResponseDto> GetQaResponseAsync(Guid qaId)
    {
        var qa = await _dbContext.ProductQas
            .AsNoTracking()
            .Include(question => question.User)
            .Include(question => question.InverseParent)
            .ThenInclude(answer => answer.User)
            .FirstOrDefaultAsync(question => question.Id == qaId);

        if (qa is null)
        {
            throw new NotFoundException("Soru bulunamadi.");
        }

        return MapToResponse(qa);
    }

    private static QaResponseDto MapToResponse(ProductQa qa)
    {
        return new QaResponseDto(
            Id: qa.Id,
            ProductId: qa.ProductId,
            UserId: qa.UserId,
            UserFullName: qa.User?.FullName,
            ParentId: qa.ParentId,
            Text: qa.Message,
            CreatedAt: qa.CreatedAt,
            Answers: qa.InverseParent
                .OrderBy(answer => answer.CreatedAt)
                .Select(MapToResponse)
                .ToList());
    }

    private static string DisplayName(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "Bir kullanici" : fullName.Trim();
}
