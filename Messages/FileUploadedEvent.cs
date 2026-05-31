namespace CraftoraApi.Messages;

public sealed record FileUploadedEvent(
    Guid UserId,
    string ObjectKey,
    string EntityType,
    Guid EntityId,
    DateTime UploadedAt);
