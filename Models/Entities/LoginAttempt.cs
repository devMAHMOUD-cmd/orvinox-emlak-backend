using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class LoginAttempt
{
    public string Email { get; set; } = null!;

    public int? AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }
}
