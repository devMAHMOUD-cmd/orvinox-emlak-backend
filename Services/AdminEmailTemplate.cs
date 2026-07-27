using System.Net;

namespace CraftoraApi.Services;

public static class AdminEmailTemplate
{
    public static string Build(string? fullName, string message)
    {
        var displayName = string.IsNullOrWhiteSpace(fullName)
            ? "Craftora kullanıcısı"
            : fullName.Trim();
        var safeName = WebUtility.HtmlEncode(displayName);
        var safeMessage = WebUtility.HtmlEncode(message.Trim())
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

        return $$"""
            <!doctype html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Craftora</title>
            </head>
            <body style="margin:0;background:#f3f5f7;font-family:Arial,sans-serif;color:#111827">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f5f7;padding:32px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:#ffffff;border:1px solid #e5e7eb">
                    <tr>
                      <td style="padding:28px 36px;border-bottom:1px solid #e5e7eb">
                        <div style="font-size:26px;font-weight:700;color:#00677a">CRAFTORA</div>
                        <div style="margin-top:6px;font-size:14px;color:#6b7280">Craftora duyurusu</div>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:34px 36px">
                        <h1 style="margin:0 0 20px;font-size:22px;line-height:1.35">Merhaba {{safeName}},</h1>
                        <div style="font-size:16px;line-height:1.7;color:#374151">{{safeMessage}}</div>
                        <p style="margin:28px 0 0;font-size:15px;color:#374151">Craftora Ekibi</p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:20px 36px;background:#f9fafb;border-top:1px solid #e5e7eb;font-size:12px;color:#6b7280">
                        Bu mesaj Craftora hesabınızla ilgili bir platform duyurusudur.<br>
                        © {{DateTime.UtcNow.Year}} Craftora · craftoramedya.com
                      </td>
                    </tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
