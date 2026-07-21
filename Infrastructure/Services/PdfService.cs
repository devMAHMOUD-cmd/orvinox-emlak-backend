using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.DTOs.Admin;
using CraftoraApi.DTOs.Seller;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CraftoraApi.Infrastructure.Services;

public sealed class PdfService : IPdfService
{
    public Task<byte[]> GenerateCompetitionCertificatePdfAsync(
        CompetitionCertificateData certificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(52);
                page.DefaultTextStyle(style => style.FontFamily("Arial").FontColor("#152235"));
                page.Content().AlignMiddle().Column(column =>
                {
                    column.Spacing(18);
                    column.Item().AlignCenter().Text("CRAFTORA").FontSize(28).Bold().FontColor("#6D4AFF");
                    column.Item().AlignCenter().Text("Yarisma Basari Belgesi").FontSize(24).Bold();
                    column.Item().PaddingTop(18).AlignCenter().Text("Bu belge").FontSize(12);
                    column.Item().AlignCenter().Text(certificate.RecipientName).FontSize(22).Bold().FontColor("#203552");
                    column.Item().AlignCenter().Text("adli kullanicinin").FontSize(12);
                    column.Item().AlignCenter().Text(certificate.CompetitionTitle).FontSize(17).SemiBold();
                    column.Item().AlignCenter().Text($"yarismasinda {certificate.Rank}. dereceyi aldigini onaylar.").FontSize(13);
                    column.Item().PaddingTop(28).AlignCenter().Text($"Duzenlenme tarihi: {certificate.IssuedAt:dd.MM.yyyy}").FontSize(10).FontColor("#58708E");
                    column.Item().AlignCenter().Text("Craftora").FontSize(12).Bold().FontColor("#6D4AFF");
                });
            });
        }).GeneratePdf();

        return Task.FromResult(pdfBytes);
    }

    public Task<byte[]> GenerateInvoicePdfAsync(
        GenerateInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Content().Column(column =>
                {
                    column.Item().Text("Craftora Invoice").FontSize(24).Bold();
                    column.Item().Text($"Order ID: {command.OrderId}");
                    column.Item().Text($"Customer: {command.CustomerName}");
                    column.Item().Text($"Email: {command.CustomerEmail}");
                    column.Item().Text($"Amount: {command.Amount:0.00} USD");
                    column.Item().Text($"Generated At: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                });
            });
        }).GeneratePdf();

        return Task.FromResult(pdfBytes);
    }

    public Task<byte[]> GenerateWeeklySellerReportPdfAsync(
        WeeklySellerReportData report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(style => style.FontFamily("Arial").FontColor("#152235").FontSize(9));

                page.Header().Background("#080A0F").PaddingHorizontal(36).PaddingVertical(28).Column(column =>
                {
                    column.Spacing(5);
                    column.Item().Text("Haftalik magaza raporu")
                        .FontSize(25)
                        .Bold()
                        .FontColor("#FFFFFF");
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text(report.ShopName)
                            .FontSize(12)
                            .SemiBold()
                            .FontColor("#C9D6E8");
                        row.AutoItem().Text($"{report.StartDate:dd MMM} - {report.EndDate:dd MMM yyyy}")
                            .FontSize(9)
                            .FontColor("#A57FFF");
                    });
                });

                page.Content().Background("#F4F7FB").Padding(36).Column(column =>
                {
                    column.Spacing(18);

                    column.Item().Element(container => SectionHeading(container, "HAFTANIN OZETI", "Magazandaki hareketin tek bakista"));
                    column.Item().Row(row =>
                    {
                        MetricCard(row.RelativeItem().PaddingRight(8), "TOPLAM GELIR", report.TotalRevenue.ToString("N2"), "#FF5F5F");
                        MetricCard(row.RelativeItem().PaddingHorizontal(4), "TAMAMLANAN SATIS", report.CompletedSales.ToString(), "#A57FFF");
                        MetricCard(row.RelativeItem().PaddingLeft(8), "TEKIL ZIYARETCI", report.UniqueVisitors.ToString(), "#00B8D4");
                    });

                    column.Item().Row(row =>
                    {
                        MetricCard(row.RelativeItem().PaddingRight(8), "MAGAZA ZIYARETI", report.ShopVisits.ToString(), "#00B8D4");
                        MetricCard(row.RelativeItem().PaddingHorizontal(4), "URUN GORUNTULEME", report.ProductViews.ToString(), "#FF9F43");
                        MetricCard(row.RelativeItem().PaddingLeft(8), "REELS GORUNTULEME", report.MediaViews.ToString(), "#A57FFF");
                    });

                    column.Item().Border(1).BorderColor("#DCE6F1").Background("#FFFFFF").Padding(16).Column(summary =>
                    {
                        summary.Spacing(7);
                        summary.Item().Text("ETKILESIM OZETI").FontSize(9).Bold().LetterSpacing(1.1f).FontColor("#58708E");
                        summary.Item().Row(row =>
                        {
                            SummaryPill(row.RelativeItem().PaddingRight(6), "Yeni like", report.NewLikes, "#00E5FF");
                            SummaryPill(row.RelativeItem().PaddingHorizontal(3), "Yeni yorum", report.NewComments, "#A57FFF");
                            SummaryPill(row.RelativeItem().PaddingLeft(6), "Kurs izlenme", report.CourseViews, "#FF5F5F");
                        });
                    });

                    column.Item().Element(container => SectionHeading(container, "EN IYI URUNLER", "Goruntulenme, satis ve gelire gore siralandi"));
                    column.Item().Border(1).BorderColor("#DCE6F1").Background("#FFFFFF").Padding(12).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4.4f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.5f);
                        });

                        table.Header(header =>
                        {
                            HeaderCell(header.Cell(), "URUN");
                            HeaderCell(header.Cell(), "GOR.");
                            HeaderCell(header.Cell(), "SATIS");
                            HeaderCell(header.Cell(), "GELIR");
                        });

                        if (report.TopProducts.Count == 0)
                        {
                            table.Cell().ColumnSpan(4).PaddingVertical(14).Text("Bu hafta siralanacak yeterli urun verisi yok.").FontColor("#74869B");
                        }
                        else
                        {
                            var rank = 1;
                            foreach (var product in report.TopProducts.Take(3))
                            {
                                ProductCell(table.Cell(), $"{rank}. {product.Title}", true);
                                ProductCell(table.Cell(), product.Views.ToString(), false);
                                ProductCell(table.Cell(), product.Sales.ToString(), false);
                                ProductCell(table.Cell(), product.Revenue.ToString("N2"), false);
                                rank++;
                            }
                        }
                    });

                    column.Item().Element(container => SectionHeading(container, "GUNLUK HAREKET", "Ziyaret, satis ve gelir ritmi"));
                    column.Item().Border(1).BorderColor("#DCE6F1").Background("#FFFFFF").Padding(12).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.8f);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn(1.6f);
                            columns.RelativeColumn(1.1f);
                        });

                        table.Header(header =>
                        {
                            HeaderCell(header.Cell(), "TARIH");
                            HeaderCell(header.Cell(), "GOR.");
                            HeaderCell(header.Cell(), "SATIS");
                            HeaderCell(header.Cell(), "GELIR");
                            HeaderCell(header.Cell(), "SEVIYE");
                        });

                        var maxViews = Math.Max(1, report.DailyPoints.Count == 0 ? 1 : report.DailyPoints.Max(point => point.Views));
                        foreach (var point in report.DailyPoints)
                        {
                            var activityLevel = Math.Clamp((int)Math.Round(point.Views * 100d / maxViews), 0, 100);
                            DailyCell(table.Cell(), point.Date);
                            DailyCell(table.Cell(), point.Views.ToString());
                            DailyCell(table.Cell(), point.Sales.ToString());
                            DailyCell(table.Cell(), point.Revenue.ToString("N2"));
                            DailyCell(table.Cell(), $"{activityLevel}%", activityLevel > 0 ? "#00B8D4" : "#95A5B8");
                        }
                    });
                });

                page.Footer().Background("#080A0F").PaddingHorizontal(36).PaddingVertical(16).Row(row =>
                {
                    row.RelativeItem().Text("Craftora seller intelligence / Haftalik karar destegi")
                        .FontSize(8)
                        .FontColor("#95A5B8");
                    row.AutoItem().Text($"Olusturuldu: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                        .FontSize(8)
                        .FontColor("#95A5B8");
                });
            });
        }).GeneratePdf();

        return Task.FromResult(pdfBytes);
    }

    private static IContainer SectionHeading(IContainer container, string title, string subtitle)
    {
        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(title).FontSize(10).Bold().LetterSpacing(1.2f).FontColor("#203552");
            column.Item().Text(subtitle).FontSize(9).FontColor("#73869C");
        });

        return container;
    }

    private static void MetricCard(IContainer container, string label, string value, string accentColor)
    {
        container.Border(1).BorderColor("#DCE6F1").Background("#FFFFFF").Padding(12).Column(column =>
        {
            column.Spacing(5);
            column.Item().Height(3).Background(accentColor);
            column.Item().PaddingTop(4).Text(label).FontSize(7).Bold().LetterSpacing(0.8f).FontColor("#6A7F96");
            column.Item().Text(value).FontSize(17).Bold().FontColor("#152235");
        });
    }

    private static void SummaryPill(IContainer container, string label, int value, string accentColor)
    {
        container.Background("#F4F7FB").Padding(10).Row(row =>
        {
            row.ConstantItem(5).Height(24).Background(accentColor);
            row.RelativeItem().PaddingLeft(8).Column(column =>
            {
                column.Item().Text(label).FontSize(8).FontColor("#647A92");
                column.Item().Text(value.ToString()).FontSize(14).Bold().FontColor("#152235");
            });
        });
    }

    private static void HeaderCell(IContainer container, string label)
    {
        container.Background("#EDF3F8").PaddingVertical(8).PaddingHorizontal(6).Text(label).FontSize(7).Bold().LetterSpacing(0.6f).FontColor("#55708D");
    }

    private static void ProductCell(IContainer container, string value, bool isTitle)
    {
        container.BorderBottom(1).BorderColor("#EDF2F7").PaddingVertical(9).PaddingHorizontal(6).Text(value)
            .FontSize(8)
            .FontColor(isTitle ? "#1C2D43" : "#526B84")
            .SemiBold();
    }

    private static void DailyCell(IContainer container, string value, string color = "#526B84")
    {
        container.BorderBottom(1).BorderColor("#EDF2F7").PaddingVertical(7).PaddingHorizontal(6).Text(value)
            .FontSize(8)
            .FontColor(color);
    }
}
