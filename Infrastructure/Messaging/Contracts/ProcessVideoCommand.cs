namespace CraftoraApi.Infrastructure.Messaging.Contracts;

public sealed record ProcessVideoCommand(
    Guid VideoId,
    string OriginalFileUrl,
    Guid CourseId,
    string TargetType = "CourseLesson",
    bool GenerateThumbnail = false);
