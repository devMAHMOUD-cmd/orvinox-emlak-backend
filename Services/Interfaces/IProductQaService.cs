using CraftoraApi.DTOs.Interaction;

namespace CraftoraApi.Services.Interfaces;

public interface IProductQaService
{
    Task<QaResponseDto> AskQuestionAsync(Guid userId, CreateQuestionDto dto);

    Task<QaResponseDto> AnswerQuestionAsync(Guid questionId, Guid sellerUserId, AnswerQuestionDto dto);

    Task DeleteQuestionAsync(Guid questionId, Guid userId);

    Task<List<QaResponseDto>> GetProductQuestionsAsync(Guid productId);
}
