namespace CraftoraApi.DTOs.Media;

public sealed class CommentDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? ParentCommentId { get; set; }

    public string? UserName { get; set; }

    public string? UserEmail { get; set; }

    public string? AvatarPublicUrl { get; set; }

    public Guid? ShopId { get; set; }

    public string? ShopName { get; set; }

    public string? ShopLogoPublicUrl { get; set; }

    public bool IsShopAuthor { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }

    public int ReplyCount { get; set; }

    public IReadOnlyList<CommentDto> Replies { get; set; } = Array.Empty<CommentDto>();
}
