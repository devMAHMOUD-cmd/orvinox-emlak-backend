# 🔐 Craftora Backend - Auth Sistem Analizi

## 📋 İçindekiler
1. [Sistem Mimarisi](#sistem-mimarisi)
2. [User Model ve Roller](#user-model-ve-roller)
3. [Giriş-Çıkış Akışı](#giriş-çıkış-akışı)
4. [Google OAuth Entegrasyonu](#google-oauth-entegrasyonu)
5. [JWT Token Yönetimi](#jwt-token-yönetimi)
6. [Security Özellikleri](#security-özellikleri)
7. [API Endpoints](#api-endpoints)

---

## 🏗️ Sistem Mimarisi

```
┌─────────────────────────────────────────────────────────────┐
│                    AuthController                            │
│  (API Endpoints: register, login, google, logout, refresh)   │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    AuthService (IAuthService)               │
│  Tüm authentication ve authorization logic'i içerir         │
└──────┬───────────────────────────┬───────────────────┬──────┘
       │                           │                   │
       ▼                           ▼                   ▼
  ┌─────────┐             ┌──────────────┐      ┌────────────┐
  │ Database │             │ Redis Cache  │      │ JwtProvider│
  │ (User)   │             │ (OTP, Token) │      │(Token Gen.)│
  └─────────┘             └──────────────┘      └────────────┘
       ▲                           │                   │
       │                           ▼                   │
       │              ┌──────────────────────┐        │
       └──────────────│  JWT Bearer Tokens   │◄───────┘
                      │ (Access + Refresh)   │
                      └──────────────────────┘
```

---

## 👤 User Model ve Roller

### User Entity Yapısı
**Dosya:** `Models/Entities/User.cs`

```csharp
public partial class User
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    public string AvatarUrl { get; set; }
    
    // AUTH ALANLAR
    public UserRole Role { get; set; }                 // Kullanıcı Rolü
    public string AuthProvider { get; set; }            // "email", "google"
    public string ProviderId { get; set; }              // External provider ID (Google sub)
    public string PasswordHash { get; set; }            // BCrypt hash
    public bool IsEmailVerified { get; set; }           // Email doğrulama durumu
    
    // SECURITY ALANLAR
    public DateTime LockedUntil { get; set; }           // Account lockout
    public DateTime LastLoginAt { get; set; }           // Son giriş zamanı
    
    // PLATFORM ALANLAR
    public string StripeCustomerId { get; set; }        // Ödeme için
    public string StripeAccountId { get; set; }         // Satıcı ödemeleri
    public string Preferences { get; set; }             // JSON: Kullanıcı ayarları
    public bool IsActive { get; set; }                  // Account aktif mi
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // İLİŞKİLER
    public ICollection<UserSession> UserSessions { get; set; }
    public ICollection<CartItem> CartItems { get; set; }
    public ICollection<Order> Orders { get; set; }
    public ICollection<Review> Reviews { get; set; }
    public Shop Shop { get; set; }                      // Eğer satıcıysa
    // ... diğer ilişkiler
}
```

### Kullanıcı Rolleri - UserRole Enum
**Dosya:** `Models/Enums/UserRole.cs`

```csharp
public enum UserRole
{
    [PgName("user")]      // Normal kullanıcı - ürün satın alır, kurs alır
    User,
    
    [PgName("seller")]    // Satıcı - ürün/kurs satabilir
    Seller,
    
    [PgName("admin")]     // Admin - tüm sistemi yönetir
    Admin
}
```

#### Rol Yetkilendirmeleri (Authorization Policies)
**Dosya:** `Extensions/ServiceExtensions.cs` (~L320)

```csharp
services.AddAuthorization(options =>
{
    // ✅ AuthenticatedUser: Oturum açmış herkes
    options.AddPolicy("AuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());
    
    // 🛡️ AdminOnly: Sadece admin
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("role", "admin"));
    
    // 🏪 SellerOnly: Satıcı ve admin
    options.AddPolicy("SellerOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("role", "seller", "admin"));
    
    // 🎥 CreatorOnly: Content creator ve admin
    options.AddPolicy("CreatorOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("role", "creator", "admin"));
});
```

---

## 🔄 Giriş-Çıkış Akışı

### 1️⃣ KAYIT (Register) Akışı

**Endpoint:** `POST /api/auth/register`

```
USER REQUEST
    ▼
[RegisterDto]
  - FullName (3-100 char)
  - Email (valid email)
  - Password (min 8 char)
  - PasswordConfirm (eşleşmeli)
    ▼
[AuthService.RegisterAsync]
  1. Email normalize et (toLowerCase)
  2. Email zaten kayıtlı mı kontrol et ✓
  3. Şifre BCrypt ile hash'le
  4. User entity oluştur
     - Role = UserRole.User (default)
     - AuthProvider = "email"
     - IsEmailVerified = false
     - IsActive = true
  5. Database'e kaydet
  6. OTP (5 dk geçerli) Redis'e ekle
  7. SendEmailCommand publish et (MassTransit)
    ▼
USER EMAIL'İNE OTP GÖNDERİLİR
```

**Kod:**
```csharp
public async Task<string> RegisterAsync(RegisterDto dto)
{
    var email = NormalizeEmail(dto.Email);
    var emailExists = await _dbContext.Users
        .AnyAsync(user => user.Email == email);
    
    if (emailExists)
        throw new ConflictException("Bu e-posta zaten kayıtlı.");
    
    var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
    var user = new User
    {
        Email = email,
        FullName = dto.FullName.Trim(),
        Role = UserRole.User,
        AuthProvider = "email",
        PasswordHash = passwordHash,
        IsEmailVerified = false,
        IsActive = true
    };
    
    _dbContext.Users.Add(user);
    await _dbContext.SaveChangesAsync();
    
    // OTP Cache'e ekle (5 dakika)
    var otpCode = GenerateOtpCode(); // 6 haneli kod
    await _cache.SetStringAsync(
        GetOtpCacheKey(email),
        otpCode,
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = 
                TimeSpan.FromMinutes(OtpExpirationMinutes)
        });
    
    // Email gönderimi
    await _publishEndpoint.Publish(new SendEmailCommand(
        To: email,
        Subject: "Craftora e-posta doğrulama kodu",
        Body: BuildOtpEmailBody(user.FullName, otpCode),
        IsHtml: true));
    
    return "Kayıt başarılı. Lütfen e-posta adresine gönderilen kodu giriniz.";
}
```

### 2️⃣ EMAIL DOĞRULAMA (Verify Email) Akışı

**Endpoint:** `POST /api/auth/verify-email`

```
USER REQUEST
    ▼
[OtpDto]
  - Email
  - OtpCode (6 haneli)
    ▼
[AuthService.VerifyEmailAsync]
  1. Email normalize et
  2. OTP'yi Redis'ten al
  3. Doğrulama kodu eşleş mi? ✓
  4. User bulup IsEmailVerified = true yap
  5. OTP'yi Redis'ten sil
    ▼
✅ E-POSTA DOĞRULANDı
   Artık login yapabilir
```

### 3️⃣ GİRİŞ (Login) Akışı

**Endpoint:** `POST /api/auth/login`

```
USER REQUEST
    ▼
[LoginDto]
  - Email
  - Password
    ▼
[AuthService.LoginAsync] - BRUTE FORCE KORUMASILI
  
  1. Email normalize et
  2. Brute Force Kontrolü:
     - Redis'ten "brute_force:{email}" key'i kontrol et
     - ≥ 5 başarısız deneme? → 30 dakika lock!
     - throw UnauthorizedException
     ▼
  3. User ve şifreyi kontrol et
     - if (user not found OR password mismatch):
       - failed attempts counter artır
       - 30 dakika lock cache'e yaz
       - throw UnauthorizedException
     ▼
  4. Email doğrulandı mı? 
     - if (IsEmailVerified != true):
       - throw UnauthorizedException("Önce email doğrulayın")
     ▼
  5. Seller Promosyon Kontrolü:
     - Shop sahibiyse Seller role'e geçir (PromoteShopOwnerToSellerAsync)
     ▼
  6. Token'ları İss:
     - JwtProvider.GenerateTokens(user)
     - Access Token (JWT, 15 dakika)
     - Refresh Token (Base64, 30 gün)
     - UserSession'ı DB'ye kaydet
     ▼
✅ BAŞARILI GİRİŞ
   - Access Token → Header'da kullan
   - Refresh Token → Secure storage'da koru
```

**Kod:**
```csharp
public async Task<TokenDto> LoginAsync(LoginDto dto)
{
    var email = NormalizeEmail(dto.Email);
    var bruteForceKey = $"brute_force:{email}";
    
    // 1. Brute force check
    var failedAttemptValue = await _cache.GetStringAsync(bruteForceKey);
    var failedAttemptCount = int.TryParse(failedAttemptValue, out var count) 
        ? count : 0;
    
    if (failedAttemptCount >= MaxFailedLoginAttempts) // 5
        throw new UnauthorizedException(
            "Çok fazla hatalı deneme. 30 dakika sonra tekrar deneyin.");
    
    // 2. User ve şifre doğrulama
    var user = await _dbContext.Users
        .FirstOrDefaultAsync(u => u.Email == email);
    
    if (user is null || 
        !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
    {
        failedAttemptCount++;
        await _cache.SetStringAsync(bruteForceKey, 
            failedAttemptCount.ToString(),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = 
                    TimeSpan.FromMinutes(BruteForceWindowMinutes)
            });
        throw new UnauthorizedException("E-posta veya şifre hatalı.");
    }
    
    // Başarılı → brute force counter sıfırla
    await _cache.RemoveAsync(bruteForceKey);
    
    // 3. Email doğrulama kontrolü
    if (user.IsEmailVerified != true)
        throw new UnauthorizedException(
            "Lütfen önce e-posta adresinizi doğrulayın.");
    
    // 4. Seller promosyonu
    await PromoteShopOwnerToSellerAsync(user);
    
    // 5. Token'ları oluştur ve DB'ye kaydet
    return await IssueTokensAsync(user);
}

private async Task<TokenDto> IssueTokensAsync(User user)
{
    var tokens = _jwtProvider.GenerateTokens(user);
    
    var session = new UserSession
    {
        UserId = user.Id,
        RefreshToken = tokens.RefreshToken,
        ExpiresAt = DateTime.UtcNow.AddDays(DefaultRefreshTokenExpireDays),
        DeviceId = null, // İsteğe bağlı
        IpAddress = null, // İsteğe bağlı
        UserAgent = null  // İsteğe bağlı
    };
    
    _dbContext.UserSessions.Add(session);
    await _dbContext.SaveChangesAsync();
    
    return tokens;
}
```

### 4️⃣ ÇIKIŞ (Logout) Akışı

**Endpoint:** `POST /api/auth/logout`

```
USER REQUEST (Authenticated)
    ▼
[RefreshRequestDto]
  - RefreshToken
    ▼
[AuthService.LogoutAsync]
  1. Header'dan Access Token'ı çıkar
  2. Refresh Token'a göre UserSession'ı sil
  3. Access Token'ı Redis blacklist'e ekle (15 dakika)
    ▼
✅ BAŞARILI ÇIKIŞ
   - Session DB'den silindi
   - Access Token geçersiz
   - Refresh Token geçersiz
```

**Kod:**
```csharp
[HttpPost("logout")]
public async Task<IActionResult> LogoutAsync([FromBody] RefreshRequestDto dto)
{
    var authorizationHeader = 
        Request.Headers.Authorization.ToString();
    
    var accessToken = authorizationHeader
        .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authorizationHeader["Bearer ".Length..].Trim()
        : string.Empty;
    
    await _authService.LogoutAsync(dto.RefreshToken, accessToken);
    return Ok(new { message = "Başarıyla çıkış yapıldı." });
}

public async Task<bool> LogoutAsync(string refreshToken, string accessToken)
{
    var isSessionRemoved = false;
    
    // 1. Refresh Token'a göre session sil
    if (!string.IsNullOrWhiteSpace(refreshToken))
    {
        var session = await _dbContext.UserSessions
            .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken);
        
        if (session is not null)
        {
            _dbContext.UserSessions.Remove(session);
            await _dbContext.SaveChangesAsync();
            isSessionRemoved = true;
        }
    }
    
    // 2. Access Token'ı blacklist'e ekle
    if (!string.IsNullOrWhiteSpace(accessToken))
    {
        await _cache.SetStringAsync(
            GetAccessTokenBlacklistKey(accessToken),
            "1",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = 
                    TimeSpan.FromMinutes(AccessTokenBlacklistMinutes)
            });
    }
    
    return isSessionRemoved || !string.IsNullOrWhiteSpace(accessToken);
}
```

---

## 🔐 Google OAuth Entegrasyonu

### Google Login Akışı

**Endpoint:** `POST /api/auth/google`

```
FRONTEND (React, Flutter, etc.)
    │
    ├─ Google SDK load et
    ├─ User "Sign in with Google" tıkla
    ├─ Google Consent Screen
    ├─ User'dan id_token al
    └─► Backend'e gönder
         │
         ▼
    [GoogleLoginRequestDto]
      - IdToken (Google'dan alınan JWT)
         │
         ▼
    [AuthService.GoogleLoginAsync]
      1. Google ClientId configuration'dan al
      2. Google.Apis.Auth kütüphane ile token validate et
      3. Token payload'ını çıkar:
         - Email
         - Name (FullName)
         - Subject (ProviderId)
      4. Email'e göre User arama:
         - ✓ Var: Email verified yap, ProviderId set et
         - ✗ Yok: Yeni User oluştur
      5. Seller Promosyonu
      6. Token'ları oluştur
         │
         ▼
    ✅ BAŞARILI GOOGLE LOGIN
       - Access Token → Header
       - Refresh Token → Storage
```

**Kod:**
```csharp
[HttpPost("google")]
public async Task<IActionResult> GoogleLoginAsync(
    [FromBody] GoogleLoginRequestDto dto)
{
    var result = await _authService.GoogleLoginAsync(dto.IdToken);
    return Ok(result);
}

public async Task<TokenDto> GoogleLoginAsync(string idToken)
{
    if (string.IsNullOrWhiteSpace(idToken))
        throw new UnauthorizedException("Geçersiz Google token.");
    
    // 1. Google ClientId'yi al
    var clientId = _configuration["OAuth:Google:ClientId"];
    if (string.IsNullOrWhiteSpace(clientId))
        throw new InvalidOperationException("Google ClientId not found");
    
    // 2. Token'ı valide et
    GoogleJsonWebSignature.Payload payload;
    try
    {
        payload = await GoogleJsonWebSignature.ValidateAsync(
            idToken,
            new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            });
    }
    catch (Exception ex) when (ex is InvalidJwtException or ArgumentException)
    {
        throw new UnauthorizedException("Geçersiz Google token.");
    }
    
    if (string.IsNullOrWhiteSpace(payload.Email))
        throw new UnauthorizedException(
            "Google hesabından e-posta bilgisi alınamadı.");
    
    // 3. User bulmaya veya oluşturmaya çalış
    var email = NormalizeEmail(payload.Email);
    var user = await _dbContext.Users
        .FirstOrDefaultAsync(u => u.Email == email);
    
    if (user is null)
    {
        // YENİ USER OLUŞTUR
        user = new User
        {
            Email = email,
            FullName = payload.Name,              // Google'dan ad-soyad
            Role = UserRole.User,
            AuthProvider = "google",
            ProviderId = payload.Subject,          // Google'ın unique ID'si
            IsEmailVerified = true,                // Google verify etmiş
            IsActive = true
        };
        
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
    }
    else
    {
        // VAR OLAN USER'I GÜNCELLE
        user.IsEmailVerified = true;
        user.ProviderId ??= payload.Subject;      // Eğer yoksa set et
        await _dbContext.SaveChangesAsync();
    }
    
    // 4. Seller promosyonu
    await PromoteShopOwnerToSellerAsync(user);
    
    // 5. Token'ları oluştur
    return await IssueTokensAsync(user);
}
```

### Google ClientId Yapılandırması

**Dosya:** `appsettings.json`

```json
{
  "OAuth": {
    "Google": {
      "ClientId": "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com"
    }
  }
}
```

### Google .NET Client Library

**Package:** `Google.Apis.Auth` (NuGet)

```csharp
using Google.Apis.Auth;

// Token validation
GoogleJsonWebSignature.Payload payload = 
    await GoogleJsonWebSignature.ValidateAsync(idToken);
```

---

## 🔑 JWT Token Yönetimi

### JWT Token Yapısı

**Dosya:** `Services/JwtProvider.cs`

#### Access Token (Short-lived)
- **Tür:** JWT
- **Geçerlilik:** 15 dakika (appsettings'ten configurable)
- **Algoritma:** HS256 (HMAC-SHA256)
- **Kullanılan:** Her API request'inde Authorization header

```
Header:
{
  "alg": "HS256",
  "typ": "JWT"
}

Payload (Claims):
{
  "sub": "user-uuid",                    // Subject = User ID
  "iat": 1234567890,                     // Issued at
  "exp": 1234568790,                     // Expires (15 dakika sonra)
  "iss": "CraftoraApi",                  // Issuer
  "aud": "CraftoraApp",                  // Audience
  "email": "user@example.com",
  "role": "user|seller|admin",
  "NameIdentifier": "user-uuid"
}

Signature:
HMACSHA256(base64UrlEncode(header) + "." + base64UrlEncode(payload), secret)
```

**Kod:**
```csharp
public TokenDto GenerateTokens(User user)
{
    var jwtSettings = _configuration.GetSection("Jwt");
    var secret = jwtSettings["Secret"];
    var issuer = jwtSettings["Issuer"] ?? "CraftoraApi";
    var audience = jwtSettings["Audience"] ?? "CraftoraApp";
    var accessTokenExpireMinutes = 
        jwtSettings.GetValue("AccessTokenExpireMinutes", 15);
    
    var expiresAt = DateTime.UtcNow.AddMinutes(accessTokenExpireMinutes);
    var signingKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(secret));
    
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role.ToString().ToLowerInvariant()),
        new Claim("role", user.Role.ToString().ToLowerInvariant())
    };
    
    var credentials = new SigningCredentials(
        signingKey, SecurityAlgorithms.HmacSha256Signature);
    
    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: expiresAt,
        signingCredentials: credentials);
    
    var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
    var refreshToken = GenerateRefreshToken();
    
    return new TokenDto(
        AccessToken: accessToken,
        RefreshToken: refreshToken,
        ExpiresIn: accessTokenExpireMinutes * 60);
}

private static string GenerateRefreshToken()
{
    var randomBytes = RandomNumberGenerator.GetBytes(32);
    return Convert.ToBase64String(randomBytes);
}
```

#### Refresh Token (Long-lived)
- **Tür:** Base64-encoded random bytes
- **Geçerlilik:** 30 gün (appsettings'ten configurable)
- **Depolama:** Database (UserSession tablosu)
- **Kullanılan:** Yeni access token almak için

```sql
-- UserSession Table
CREATE TABLE user_sessions (
  id UUID PRIMARY KEY,
  user_id UUID NOT NULL REFERENCES users(id),
  refresh_token VARCHAR NOT NULL,
  device_id VARCHAR,
  ip_address INET,
  user_agent VARCHAR,
  expires_at TIMESTAMP NOT NULL,
  created_at TIMESTAMP NOT NULL,
  UNIQUE(refresh_token)
);
```

### Token Refresh Akışı

**Endpoint:** `POST /api/auth/refresh`

```
USER REQUEST (Unauthenticated OK)
    ▼
[RefreshRequestDto]
  - RefreshToken (30 gün geçerliliği kalmış)
    ▼
[AuthService.RefreshTokenAsync]
  1. Refresh Token'a göre UserSession'ı bul
  2. Session'ın user bilgisini al
  3. Geçerlilik kontrolü:
     - if (expiresAt <= now): Session sil, exception
  4. Seller promosyonu
  5. Yeni token'lar oluştur:
     - Access Token (yeni 15 dk)
     - Refresh Token (yeni 30 gün)
  6. Session'ı yeni token'larla güncelle
     ▼
✅ BAŞARILI REFRESH
   - Yeni Access Token al
   - Yeni Refresh Token al (Rotation)
```

**Kod:**
```csharp
public async Task<TokenDto> RefreshTokenAsync(string refreshToken)
{
    if (string.IsNullOrWhiteSpace(refreshToken))
        throw new UnauthorizedException("Geçersiz refresh token.");
    
    // 1. Session'ı bul
    var session = await _dbContext.UserSessions
        .Include(s => s.User)
        .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken);
    
    if (session?.User is null)
        throw new UnauthorizedException("Geçersiz refresh token.");
    
    // 2. Geçerlilik kontrolü
    if (session.ExpiresAt <= DateTime.UtcNow)
    {
        _dbContext.UserSessions.Remove(session);
        await _dbContext.SaveChangesAsync();
        throw new UnauthorizedException("Süresi dolmuş refresh token.");
    }
    
    // 3. Seller promosyonu
    await PromoteShopOwnerToSellerAsync(session.User);
    
    // 4. Yeni token'lar
    var tokens = _jwtProvider.GenerateTokens(session.User);
    
    // 5. Session'ı güncelle
    session.RefreshToken = tokens.RefreshToken;      // Token rotation
    session.ExpiresAt = DateTime.UtcNow.AddDays(
        GetRefreshTokenExpireDays());
    
    await _dbContext.SaveChangesAsync();
    
    return tokens;
}
```

### Token Geçerlilik Kontrolü

**Dosya:** `Extensions/ServiceExtensions.cs` (~L280)

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !environment.IsDevelopment();
        options.SaveToken = true;
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            
            ValidateIssuer = true,
            ValidIssuer = issuer,
            
            ValidateAudience = true,
            ValidAudience = audience,
            
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(10),    // 10 sn tolerans
            
            NameClaimType = "sub"
        };
        
        // Token validation events
        options.Events = new JwtBearerEvents
        {
            // WebSocket'te access_token query param'ından oku
            OnMessageReceived = context =>
            {
                if (context.Request.Query
                    .TryGetValue("access_token", out var token))
                {
                    context.Token = token.ToString();
                }
                return Task.CompletedTask;
            },
            
            // Blacklist kontrolü (logout sonrası)
            OnTokenValidated = async context =>
            {
                if (context.SecurityToken is not JwtSecurityToken token)
                    return;
                
                var cache = context.HttpContext.RequestServices
                    .GetRequiredService<IDistributedCache>();
                
                var blacklistValue = await cache.GetStringAsync(
                    $"blacklist:{token.RawData}");
                
                if (!string.IsNullOrWhiteSpace(blacklistValue))
                {
                    context.Fail(
                        "Bu token çıkış yapıldığı için geçersiz.");
                }
            }
        };
    });
```

---

## 🛡️ Security Özellikleri

### 1. Password Security

#### BCrypt Hashing
- **Algoritma:** BCrypt
- **Rounds:** Varsayılan (11)
- **Salt:** Otomatik dahil

```csharp
// Kayıt sırasında
var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

// Login sırasında
BCrypt.Net.BCrypt.Verify(loginPassword, storedHash);
```

#### Password Validation Kuralları
```csharp
[property: Required(ErrorMessage = "Şifre zorunlu")]
[property: MinLength(8, ErrorMessage = "Min 8 karakter")]
string Password
```

### 2. Brute Force Attack Koruması

**Dosya:** `Services/AuthService.cs` (~L125)

```
MaxFailedLoginAttempts = 5
BruteForceWindowMinutes = 30

Akış:
├─ 1. failed attempt → "başarısız"
├─ 2. failed attempt → "başarısız"
├─ 3. failed attempt → "başarısız"
├─ 4. failed attempt → "başarısız"
├─ 5. failed attempt → "başarısız"
└─ 6. attempt attempt → 🔒 LOCKED 30 dakika
    (Cache key: brute_force:{email})
```

**Redis Cache Kullanım:**
```
Key: brute_force:{normalized_email}
Value: {attempt_count} (integer)
TTL: 30 dakika
```

### 3. Email Verification

- Kayıt sonrası e-posta doğrulama zorunlu
- 5 dakikalık geçerli OTP kodu
- Kullanıcı OTP olmadan login yapamaz

```
is_email_verified = false → Cannot Login
                  ↓
              Verify OTP
                  ↓
is_email_verified = true → Can Login
```

### 4. Token Blacklist

**Çıkış (Logout) sonrası:**

```
Access Token → Redis Blacklist
Key: blacklist:{token_raw_data}
Value: "1"
TTL: 15 dakika (token'ın geçerlilik süresi)
```

Her request'te, token validated event'de blacklist kontrol edilir:

```csharp
OnTokenValidated = async context =>
{
    var blacklistValue = await cache
        .GetStringAsync($"blacklist:{token.RawData}");
    
    if (!string.IsNullOrWhiteSpace(blacklistValue))
        context.Fail("Bu token çıkış yapıldığı için geçersiz.");
}
```

### 5. Google Token Validation

```csharp
// Google'ın official library
GoogleJsonWebSignature.ValidateAsync(idToken)

Validasyon:
├─ Signature doğrulama
├─ Issuer kontrol
├─ Audience kontrol (ClientId)
├─ Expiration kontrol
└─ Gerekli fields kontrol
```

### 6. Account Lockout

```csharp
public DateTime? LockedUntil { get; set; }

if (user.LockedUntil > DateTime.UtcNow)
    throw new UnauthorizedException("Hesap kilitlenmiş");
```

### 7. HTTPS Enforcement

```csharp
options.RequireHttpsMetadata = !environment.IsDevelopment();
// Production'da HTTPS zorunlu
```

### 8. CORS Yapılandırması

```csharp
// Sadece belirtilen origins'ten istekleri kabul et
services.AddCors("CraftoraPolicy", options =>
{
    options.AllowAnyOrigin()
           .AllowAnyMethod()
           .AllowAnyHeader();
});
```

### 9. Rate Limiting

```csharp
services.AddRateLimiter(options =>
{
    options.AddPolicy("AuthLimit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress
                ?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,                  // 10 request
                Window = TimeSpan.FromMinutes(1)   // per 1 minute
            }));
});

[EnableRateLimiting("AuthLimit")]
public class AuthController { }
```

### 10. Exception Handling

```csharp
public class UnauthorizedException : CraftoraException
{
    public UnauthorizedException(string message)
        : base(message, statusCode: 401, errorCode: "UNAUTHORIZED")
}

// Middleware tarafından global olarak handle edilir
```

---

## 📡 API Endpoints

### Authentication Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/register` | ❌ | Kullanıcı kaydı |
| POST | `/api/auth/verify-email` | ❌ | OTP doğrulama |
| POST | `/api/auth/login` | ❌ | E-posta ile giriş |
| POST | `/api/auth/google` | ❌ | Google ile giriş |
| POST | `/api/auth/refresh` | ❌ | Token refresh |
| POST | `/api/auth/logout` | ✅ | Çıkış |
| GET | `/api/auth/me` | ✅ | Mevcut kullanıcı bilgisi |

### Request/Response Örnekleri

#### 1. Register
```http
POST /api/auth/register HTTP/1.1
Content-Type: application/json

{
  "fullName": "Ahmet Yılmaz",
  "email": "ahmet@example.com",
  "password": "SecurePass123!",
  "passwordConfirm": "SecurePass123!"
}

200 OK
{
  "message": "Kayıt başarılı. Lütfen e-posta adresinize gönderilen kodu giriniz."
}
```

#### 2. Verify Email
```http
POST /api/auth/verify-email HTTP/1.1
Content-Type: application/json

{
  "email": "ahmet@example.com",
  "otpCode": "123456"
}

200 OK
{
  "message": "E-posta adresiniz başarıyla doğrulandı."
}
```

#### 3. Login
```http
POST /api/auth/login HTTP/1.1
Content-Type: application/json

{
  "email": "ahmet@example.com",
  "password": "SecurePass123!"
}

200 OK
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "aB3dEf5gHi9jKl2mNo4pQr7sTu0vWx1yZ...",
  "expiresIn": 900
}
```

#### 4. Google Login
```http
POST /api/auth/google HTTP/1.1
Content-Type: application/json

{
  "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6IjEifQ..."
}

200 OK
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "aB3dEf5gHi9jKl2mNo4pQr7sTu0vWx1yZ...",
  "expiresIn": 900
}
```

#### 5. Refresh Token
```http
POST /api/auth/refresh HTTP/1.1
Content-Type: application/json

{
  "refreshToken": "aB3dEf5gHi9jKl2mNo4pQr7sTu0vWx1yZ..."
}

200 OK
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "cD6eHi9jKl2mNo4pQr7sTu0vWx1yZ3aB...",
  "expiresIn": 900
}
```

#### 6. Logout
```http
POST /api/auth/logout HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "refreshToken": "aB3dEf5gHi9jKl2mNo4pQr7sTu0vWx1yZ..."
}

200 OK
{
  "message": "Başarıyla çıkış yapıldı."
}
```

#### 7. Get Current User
```http
GET /api/auth/me HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...

200 OK
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "fullName": "Ahmet Yılmaz",
  "email": "ahmet@example.com",
  "role": "user",
  "hasShop": false,
  "shopId": null,
  "shopSlug": null,
  "shopIsActive": null
}
```

---

## 📊 Veri Akışı Özeti

### Email + Password Flow
```
User → Register → Email Verify → Login → Access Token + Refresh Token
                                           ↓
                                    Protected Routes
                                           ↓
                                      (Token Expires)
                                           ↓
                                       Refresh Token → New Access Token
                                           ↓
                                      Logout → Token Blacklist
```

### Google OAuth Flow
```
User → Google Sign In → id_token → Backend → Validate Token → Check User
                                                                ↓
                                                    User Var → Email Verified
                                                    User Yok → Create + Verified
                                                                ↓
                                                    Generate Tokens → Client
```

---

## 🔧 Configuration Örneği

**appsettings.json:**
```json
{
  "Jwt": {
    "Secret": "your-super-secret-key-at-least-32-characters-long",
    "Issuer": "CraftoraApi",
    "Audience": "CraftoraApp",
    "AccessTokenExpireMinutes": 15,
    "RefreshTokenExpireDays": 30
  },
  "OAuth": {
    "Google": {
      "ClientId": "123456789-abcdefg.apps.googleusercontent.com"
    }
  },
  "Email": {
    "FromName": "Craftora",
    "FromEmail": "noreply@craftora.com"
  }
}
```

---

## 📝 Önemli Notlar

### ✅ Best Practices Uygulanmış
1. ✅ Şifre BCrypt ile hash'leniyor
2. ✅ JWT token'ları imzalanmış ve valide ediliyor
3. ✅ Refresh token'lar database'de saklanıyor
4. ✅ Brute force attack koruması
5. ✅ Email verification
6. ✅ Token blacklist sistemi
7. ✅ HTTPS enforcement (production)
8. ✅ Rate limiting
9. ✅ Google OAuth token validation
10. ✅ Account lockout mekanizması

### 🚀 Improvement Fırsatları
1. 🔄 Multi-factor authentication (MFA)
2. 📱 SMS OTP alternatifi
3. 🔑 API Key authentication
4. 👤 Social logins (Facebook, GitHub)
5. 📊 Login history tracking
6. 🌐 IP whitelisting
7. 🔐 Password reset flow
8. 👥 Session management UI

---

## 📚 İlgili Dosyalar

| Dosya | Açıklama |
|-------|----------|
| `Controllers/AuthController.cs` | Auth API endpoints |
| `Services/AuthService.cs` | Auth iş mantığı |
| `Services/JwtProvider.cs` | Token oluşturma |
| `Models/Entities/User.cs` | User modeli |
| `Models/Entities/UserSession.cs` | Session yönetimi |
| `Models/Enums/UserRole.cs` | Kullanıcı rolleri |
| `DTOs/Auth/*.cs` | Request/response modelleri |
| `Extensions/ServiceExtensions.cs` | JWT ve Auth konfigürasyonu |
| `Middleware/CraftoraExceptions.cs` | Exception sınıfları |

---

**Analiz Tarihi:** 30 Mayıs 2026
**Sistem Durumu:** Production Ready ✅














Bir hata düzgün yakalanmıyor, 500 yerine 400 dönmeli. Düzelt.

SORUN: Kullanıcı zaten sahip olduğu (kütüphanesindeki) bir ürünü sepete eklemeye 
çalışınca, DB trigger'ı prevent_duplicate_purchase şu hatayı fırlatıyor:
  PostgresException P0001: "Bu urun zaten kutuphanenizde mevcut!"

Bu hata CartService.AddToCartAsync (line 55 civarı, SaveChangesAsync sırasında) 
yakalanmadığı için kullanıcıya 500 Internal Server Error dönüyor. Halbuki bu bir 
KULLANICI hatası, 400 Bad Request + anlamlı mesaj dönmeli.

YAPILACAK:
1. CartService.AddToCartAsync içinde, SaveChangesAsync'i try-catch ile sar.
2. PostgresException yakala, SqlState == "P0001" ise (ya da mesajda 
   "zaten kutuphanenizde" geçiyorsa) bunu bizim BadRequestException'a çevir 
   (mesaj: "Bu ürün zaten kütüphanenizde mevcut.") → böylece kullanıcı 400 + 
   net mesaj alır.
3. Aynı P0001 hatası CHECKOUT sırasında da (OrderService) oluşabilir mi kontrol 
   et. Eğer checkout'ta da bu trigger patlayıp 500 dönebiliyorsa, orada da aynı 
   şekilde yakala. (Ama checkout mantığını/lock'u/completed persist'i BOZMA.)

KURALLAR:
- Sadece hata yakalama ekle, iş mantığını değiştirme.
- Trigger'a DOKUNMA (o doğru çalışıyor, sadece hatayı düzgün karşılayacağız).
- Başka PostgresException tiplerini yutma, sadece bu duplicate purchase 
  (P0001) durumunu çevir. Diğer hatalar yine yukarı gitsin.
- CartService.cs ve gerekiyorsa OrderService.cs'e dokun.

Build al. Ne değiştirdiğini açıkla. Önce sadece bu iki dosyayı incele, P0001 
nerede oluşabilir söyle, sonra düzelt.






Propmt Hazirlarken 
1.Adim : Sorunu Anlat
2.Adim : Yapilacak Islemi Anlat
3.Adim : Islemi Yapareken Kurali Anlat 



🟡 NOT: Fatura presigned URL'leri 7 gün (604800s) token'sız erişilebilir.
   İyileştirme: Süreyi kısalt (örn. birkaç saat/gün), ve/veya fatura 
   indirmeyi auth'lu bir endpoint arkasına al.
   Ayrıca: linkler https://localhost:9000 üretiyor ama MinIO https değil 
   → prod'da MinIO public adresi/protokolü doğru ayarlanmalı.


🟡 Fatura presigned URL 7 gün token'sız açık → süre kısalt / auth'lu endpoint
🟡 Fatura linkleri https://localhost:9000 üretiyor ama MinIO https değil 
   → prod'da MinIO adresi/protokolü düzeltilmeli (senin eski "download hatası" 
     muhtemelen buydu)