namespace CraftoraApi.DTOs.Media;

public sealed class CommentDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? UserName { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }
}
