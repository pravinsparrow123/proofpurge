using System;
using System.IO;
using DataLifecycleEngine.Audit.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DataLifecycleEngine.Audit.Generators;

public class PdfCertificateGenerator
{
    static PdfCertificateGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string Generate(AuditCertificate cert, string outputPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Helvetica").FontColor(Colors.Grey.Darken3));

                page.Header().Element(header => ComposeHeader(header, cert));
                page.Content().Element(content => ComposeContent(content, cert));
                page.Footer().Element(footer => ComposeFooter(footer, cert));
            });
        });

        document.GeneratePdf(outputPath);
        return outputPath;
    }

    private static void ComposeHeader(IContainer container, AuditCertificate cert)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(titleCol =>
                {
                    titleCol.Item().Text("ENTERPRISE DATA LIFECYCLE & RETENTION")
                        .FontSize(15).Bold().FontColor(Colors.Blue.Darken3);
                    titleCol.Item().Text("Cryptographic Proof of Purge & Cold Archival Certificate")
                        .FontSize(10).SemiBold().FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(130).Background(Colors.Green.Lighten5).Border(1).BorderColor(Colors.Green.Medium)
                    .Padding(6).AlignCenter().Column(b =>
                    {
                        b.Item().Text("VERIFIED AUDIT").FontSize(9).Bold().FontColor(Colors.Green.Darken3);
                        b.Item().Text("NON-REPUDIABLE").FontSize(7).FontColor(Colors.Green.Darken2);
                    });
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Colors.Blue.Darken2);
        });
    }

    private static void ComposeContent(IContainer container, AuditCertificate cert)
    {
        container.PaddingVertical(10).Column(col =>
        {
            // Metadata Card
            col.Item().Background(Colors.Grey.Lighten4).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(t => { t.Span("Certificate ID: ").Bold(); t.Span(cert.CertificateId); });
                    c.Item().Text(t => { t.Span("Issued (UTC): ").Bold(); t.Span(cert.IssuedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")); });
                    c.Item().Text(t => { t.Span("Operator: ").Bold(); t.Span(cert.Operator); });
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(t => { t.Span("Retention Policy: ").Bold(); t.Span(cert.PolicyName); });
                    c.Item().Text(t => { t.Span("Target Database: ").Bold(); t.Span(cert.DatabaseTarget); });
                    c.Item().Text(t => { t.Span("Retention Cutoff: ").Bold(); t.Span(cert.RetentionCutoff); });
                });
            });

            // Compliance Standards
            col.Item().PaddingTop(6).Text(text =>
            {
                text.Span("Compliance Frameworks: ").Bold().FontSize(8);
                text.Span(string.Join(" • ", cert.ComplianceFrameworks)).FontSize(8).FontColor(Colors.Blue.Darken3);
            });

            // Summary Metrics Row
            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Background(Colors.Blue.Lighten5).Padding(8).Column(m =>
                {
                    m.Item().Text("TOTAL ROWS PURGED").FontSize(7).Bold().FontColor(Colors.Blue.Darken3);
                    m.Item().Text(cert.TotalRowsPurged.ToString("N0")).FontSize(14).Bold().FontColor(Colors.Blue.Darken3);
                });

                row.RelativeItem().PaddingLeft(6).Background(Colors.Green.Lighten5).Padding(8).Column(m =>
                {
                    m.Item().Text("TOTAL ROWS ARCHIVED").FontSize(7).Bold().FontColor(Colors.Green.Darken3);
                    m.Item().Text(cert.TotalRowsArchived.ToString("N0")).FontSize(14).Bold().FontColor(Colors.Green.Darken3);
                });

                row.RelativeItem().PaddingLeft(6).Background(Colors.Orange.Lighten5).Padding(8).Column(m =>
                {
                    m.Item().Text("STORAGE SAVINGS").FontSize(7).Bold().FontColor(Colors.Orange.Darken3);
                    m.Item().Text($"{cert.OverallCompressionRatio:F1}%").FontSize(14).Bold().FontColor(Colors.Orange.Darken3);
                });

                row.RelativeItem().PaddingLeft(6).Background(Colors.Grey.Lighten4).Padding(8).Column(m =>
                {
                    m.Item().Text("TABLES AUDITED").FontSize(7).Bold().FontColor(Colors.Grey.Darken3);
                    m.Item().Text(cert.TableRecords.Count.ToString()).FontSize(14).Bold().FontColor(Colors.Grey.Darken3);
                });
            });

            // Table Breakdown Section
            col.Item().PaddingTop(14).Text("Cascade Execution & Parquet Cold Archive Manifest").FontSize(11).Bold().FontColor(Colors.Blue.Darken3);
            
            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2); // Table
                    columns.RelativeColumn(1.2f); // Rows Purged
                    columns.RelativeColumn(1.2f); // Rows Archived
                    columns.RelativeColumn(3.5f); // Archive File & SHA-256
                    columns.RelativeColumn(1.2f); // Parquet Size
                    columns.RelativeColumn(1); // Savings
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Table").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Purged").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Archived").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Parquet Hash (SHA-256)").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Size").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Savings").Bold().FontColor(Colors.White).FontSize(8);
                });

                for (int i = 0; i < cert.TableRecords.Count; i++)
                {
                    var rec = cert.TableRecords[i];
                    string bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;

                    table.Cell().Background(bg).Padding(4).Text(rec.TableName).Bold().FontSize(8);
                    table.Cell().Background(bg).Padding(4).Text(rec.RowsPurged.ToString("N0")).FontSize(8);
                    table.Cell().Background(bg).Padding(4).Text(rec.RowsArchived.ToString("N0")).FontSize(8);
                    table.Cell().Background(bg).Padding(4).Column(fileCol =>
                    {
                        fileCol.Item().Text(rec.ArchiveFileName).FontSize(7).SemiBold();
                        string shortHash = rec.ParquetSha256Checksum.Length > 20 ? rec.ParquetSha256Checksum[..20] + "..." : rec.ParquetSha256Checksum;
                        fileCol.Item().Text($"SHA256: {shortHash}").FontSize(6.5f).FontColor(Colors.Grey.Medium);
                    });
                    table.Cell().Background(bg).Padding(4).Text($"{rec.ParquetSizeBytes / 1024.0:F1} KB").FontSize(8);
                    table.Cell().Background(bg).Padding(4).Text($"{rec.CompressionRatio:F1}%").FontColor(Colors.Green.Darken2).FontSize(8);
                }
            });

            // Cryptographic Verification Stamp
            col.Item().PaddingTop(12).Background(Colors.Grey.Lighten5).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(sec =>
            {
                sec.Item().Text("CRYPTOGRAPHIC DIGITAL SEAL & NON-REPUDIATION ATTESTATION").FontSize(8).Bold().FontColor(Colors.Blue.Darken3);
                sec.Item().PaddingTop(2).Text(t =>
                {
                    t.Span("Payload SHA-256: ").Bold().FontSize(7);
                    t.Span(cert.PayloadSha256Hash).FontSize(7).FontColor(Colors.Grey.Darken2);
                });
                sec.Item().Text(t =>
                {
                    t.Span("HMAC-SHA256 Signature: ").Bold().FontSize(7);
                    t.Span(cert.DigitalSignature).FontSize(7).FontColor(Colors.Green.Darken3);
                });
                sec.Item().Text(t =>
                {
                    t.Span("Signing Key ID: ").Bold().FontSize(7);
                    t.Span(cert.SigningKeyId).FontSize(7);
                });
            });

            // Legal Attestation Statement
            col.Item().PaddingTop(10).Text(
                "Legal Compliance Statement: This document certifies that historical database candidate records have been securely " +
                "purged from primary production transactional storage in adherence with GDPR Article 17 ('Right to Erasure'), " +
                "India DPDP Act 2023 Section 12, and organizational retention policies. All records were exported to immutable " +
                "columnar Parquet archives with the cryptographic SHA-256 hashes detailed above prior to destruction.")
                .FontSize(7).Italic().FontColor(Colors.Grey.Darken1);
        });
    }

    private static void ComposeFooter(IContainer container, AuditCertificate cert)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"Safe Purge Engine v1.0 • Certificate: {cert.CertificateId}").FontSize(7).FontColor(Colors.Grey.Medium);
                row.RelativeItem().AlignRight().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });
    }
}
