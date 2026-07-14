using CraftoraApi.Data;
using CraftoraApi.DTOs.Interaction;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class ProductQaService : IProductQaService
{
    private readonly AppDbContext _dbContext;

    public ProductQaService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<QaResponseDto> AskQuestionAsync(Guid userId, CreateQuestionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var productExists = await _dbContext.Products.AnyAsync(product =>
            product.Id == dto.ProductId &&
            product.IsActive == true);
        if (!productExists)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var question = new ProductQa
        {
            ProductId = dto.ProductId,
            UserId = userId,
            Message = PlainTextInputValidator.Require(dto.QuestionText, "Soru metni", 500),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProductQas.Add(question);
        await _dbContext.SaveChangesAsync();

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
}
