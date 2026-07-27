using System.Net;

namespace CraftoraApi.Services;

public static class SupportReplyEmailTemplate
{
    private const string LogoUrl =
        "https://api.craftoramedya.com/email-assets/craftora-email-logo.png";

    public static string Build(
        string? fullName,
        string ticketSubject,
        string reply)
    {
        var safeName = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(fullName)
                ? "Craftora kullanicisi"
                : fullName.Trim());
        var safeSubject = WebUtility.HtmlEncode(ticketSubject.Trim());
        var safeReply = WebUtility.HtmlEncode(reply.Trim())
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

        return $$"""
            <!doctype html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Craftora destek yaniti</title>
            </head>
            <body style="margin:0;background:#f3f5f7;font-family:Arial,sans-serif;color:#111827">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f5f7;padding:32px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:#ffffff;border:1px solid #e5e7eb">
                    <tr>
                      <td style="padding:24px 36px;border-bottom:1px solid #e5e7eb">
                        <table role="presentation" cellspacing="0" cellpadding="0">
                          <tr>
                            <td style="padding-right:12px">
                              <img src="{{LogoUrl}}" width="42" height="42" alt="Craftora" style="display:block;border:0">
                            </td>
                            <td>
                              <div style="font-size:24px;font-weight:700;color:#00677a">CRAFTORA</div>
                              <div style="margin-top:4px;font-size:13px;color:#6b7280">Destek ekibi yaniti</div>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:32px 36px">
                        <h1 style="margin:0 0 18px;font-size:21px;line-height:1.35">Merhaba {{safeName}},</h1>
                        <p style="margin:0 0 8px;font-size:13px;color:#6b7280">Destek talebiniz</p>
                        <p style="margin:0 0 22px;font-size:16px;font-weight:700;color:#111827">{{safeSubject}}</p>
                        <div style="padding:20px;background:#f8fafc;border-left:4px solid #00677a;font-size:16px;line-height:1.7;color:#374151">{{safeReply}}</div>
                        <p style="margin:26px 0 0;font-size:14px;line-height:1.6;color:#6b7280">
                          Bu e-postaya tekrar yazarak veya Craftora destek ekranini kullanarak konusmaya devam edebilirsiniz.
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:18px 36px;background:#f9fafb;border-top:1px solid #e5e7eb;font-size:12px;color:#6b7280">
                        Craftora Destek Ekibi<br>
                        &copy; {{DateTime.UtcNow.Year}} Craftora &middot; craftoramedya.com
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
