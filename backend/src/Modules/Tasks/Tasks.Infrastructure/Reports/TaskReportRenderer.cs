using System.Globalization;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tasks.Application.Common.Interfaces;

namespace Tasks.Infrastructure.Reports;

/// <summary>
/// The task report, laid out like the reference survey report: a header naming the task, its
/// facts in grouped tables, the latest fill's answers, then the signatures, photos and a list of
/// every file. Arabic reports run right to left.
/// </summary>
internal sealed class TaskReportRenderer(ILogger<TaskReportRenderer> logger) : ITaskReportRenderer
{
    public byte[] Render(TaskReport report)
    {
        TaskReportFonts.EnsureRegistered();

        var images = DecodeImages(report);
        try
        {
            return Document.Create(container => new TaskReportDocument(report, images).Compose(container)).GeneratePdf();
        }
        finally
        {
            foreach (var image in images.Values)
            {
                (image as IDisposable)?.Dispose();
            }
        }
    }

    /// <summary>
    /// Decodes each embeddable image up front. An upload is whatever the device produced — a HEIC
    /// photo, a truncated file — and one that will not decode must not take the whole report down;
    /// it is listed as not embedded instead.
    /// </summary>
    private Dictionary<TaskReportFile, Image> DecodeImages(TaskReport report)
    {
        var images = new Dictionary<TaskReportFile, Image>(ReferenceEqualityComparer.Instance);

        foreach (var file in report.Files.Where(f => f.IsImage && f.Bytes is { Length: > 0 }))
        {
            try
            {
                images[file] = Image.FromBinaryData(file.Bytes!);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Task {TaskNumber}: image '{FileName}' could not be decoded for the report.", report.TaskNumber, file.FileName);
            }
        }

        return images;
    }
}

internal sealed class TaskReportDocument(TaskReport report, IReadOnlyDictionary<TaskReportFile, Image> images)
{
    private const float ImageMaxHeight = 260f;
    private const float SignatureMaxWidth = 220f;
    private const float SignatureMaxHeight = 110f;
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    private const string Missing = "-";

    /// <summary>Room a heading needs below it before it may start a section, so it never ends a page alone.</summary>
    private const float SectionLeadSpace = 170f;

    /// <summary>Unicode's left-to-right isolate and its closer. See <see cref="Ltr"/>.</summary>
    private const char LeftToRightIsolate = '\u2066';
    private const char PopDirectionalIsolate = '\u2069';

    private static readonly string Accent = Colors.Blue.Darken2;
    private static readonly string Rule = Colors.Grey.Lighten2;

    private bool IsArabic => report.Language == "ar";

    private string T(string english, string arabic) => IsArabic ? arabic : english;

    /// <summary>
    /// A date, code, number or Latin name kept in its own order inside Arabic text. Without the
    /// isolate the bidi algorithm reorders its parts — "2026-09-21 08:00" prints as "08:00 2026-09-21".
    /// </summary>
    private string Ltr(string value) =>
        IsArabic && !value.Any(IsArabicLetter) ? $"{LeftToRightIsolate}{value}{PopDirectionalIsolate}" : value;

    private static bool IsArabicLetter(char c) => c is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F';

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(style =>
            {
                var text = style.FontSize(10).FontFamily(TaskReportFonts.LatinFamily, TaskReportFonts.ArabicFamily);
                return IsArabic ? text.DirectionFromRightToLeft() : text;
            });

            if (IsArabic)
            {
                page.ContentFromRightToLeft();
            }

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span(T("Page ", "صفحة "));
                text.CurrentPageNumber();
                text.Span(T(" of ", " من "));
                text.TotalPages();
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(12).BorderBottom(2).BorderColor(Accent).PaddingBottom(8).Column(column =>
        {
            column.Item().Text(T("NWFM — Field Task Report", "NWFM — تقرير مهمة ميدانية")).FontSize(20).SemiBold().FontColor(Accent);
            column.Item().PaddingTop(4).Text(text =>
            {
                text.Span(Ltr(report.TaskNumber)).FontSize(14).SemiBold();
                text.Span("   ");
                text.Span(TaskReportText.Status(report.Status, IsArabic)).FontSize(12).FontColor(Colors.Grey.Darken2);
            });
            column.Item().Text($"{T("Generated", "تاريخ الإصدار")}: {Ltr(report.GeneratedAt.ToString(DateTimeFormat, CultureInfo.InvariantCulture) + " UTC")}")
                .FontSize(9).FontColor(Colors.Grey.Medium);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(8).Column(column =>
        {
            column.Spacing(14);

            column.Item().Element(ComposeTaskInformation);
            column.Item().Element(ComposeOrganisation);

            if (!string.IsNullOrWhiteSpace(report.Notes))
            {
                column.Item().Element(c => ComposeCallout(c, T("Notes", "ملاحظات"), report.Notes!, Colors.Blue.Lighten5, Accent));
            }

            if (report.ReturnReasonCode is not null)
            {
                var heading = $"{T("Returned", "أعيدت")}: {TaskReportText.ReturnReason(report.ReturnReasonCode, IsArabic)}"
                    + $" · {Date(report.ReturnedDate, DateTimeFormat)} · {T("Times returned", "مرات الإعادة")}: {Ltr(report.ReturnCount.ToString(CultureInfo.InvariantCulture))}";
                column.Item().Element(c => ComposeCallout(c, heading, report.ReturnReason ?? Missing, Colors.Red.Lighten5, Colors.Red.Darken2));
            }

            column.Item().Element(ComposeAnswers);

            var signatures = report.Files.Where(f => f.IsSignature && f.IsFromLatestFill).ToList();
            if (signatures.Count > 0)
            {
                column.Item().Element(c => ComposeSignatures(c, signatures));
            }

            var photos = report.Files.Where(f => !f.IsSignature && images.ContainsKey(f)).ToList();
            if (photos.Count > 0)
            {
                column.Item().Element(c => ComposeImages(c, photos));
            }

            if (report.Files.Count > 0)
            {
                column.Item().Element(ComposeFileList);
            }
        });
    }

    private void ComposeTaskInformation(IContainer container)
    {
        ComposeSection(container, T("Task information", "معلومات المهمة"), table =>
        {
            FactRow(table, 0, T("Task type", "نوع المهمة"), report.TaskType, T("Form", "النموذج"), $"{report.Form} (v{report.FormVersionNo})");
            FactRow(table, 1, T("Title", "العنوان"), report.Title ?? Missing, T("External reference", "مرجع خارجي"), report.ExternalReference ?? Missing);
            FactRow(table, 2, T("Priority", "الأولوية"), TaskReportText.Priority(report.Priority, IsArabic), T("Source", "المصدر"), TaskReportText.Source(report.Source, IsArabic));
            FactRow(table, 3, T("Created", "تاريخ الإنشاء"), Date(report.CreatedAt, DateTimeFormat), T("Team", "الفريق"), report.Team ?? Missing);
            FactRow(table, 4, T("Fill due", "موعد التعبئة"), Date(report.DueDate, DateTimeFormat), T("Review due", "موعد المراجعة"), Date(report.CompletionDueDate, DateTimeFormat));
            FactRow(table, 5, T("Assigned", "الإسناد"), Date(report.AssignedDate, DateTimeFormat), T("Filled", "التعبئة"), Date(report.SubmittedDate, DateTimeFormat));
            FactRow(table, 6, T("Fills", "التعبئات"), report.SubmissionCount.ToString(CultureInfo.InvariantCulture), T("Completed", "الإكمال"), Date(report.CompletedDate, DateTimeFormat));
        });
    }

    private void ComposeOrganisation(IContainer container)
    {
        var coordinates = Ltr(string.Create(CultureInfo.InvariantCulture, $"{report.Latitude:0.######}, {report.Longitude:0.######}"));

        ComposeSection(container, T("Organisation and location", "الجهة والموقع"), table =>
        {
            FactRow(table, 0, T("Cluster", "المجموعة"), report.Cluster, T("CBU", "وحدة الأعمال"), report.Cbu);
            FactRow(table, 1, T("Branch", "الفرع"), report.Branch, T("Operation area", "منطقة العمليات"), report.OperationArea);
            FactRow(table, 2, T("Department", "الإدارة"), report.Department, T("Coordinates", "الإحداثيات"), coordinates);

            var background = Colors.Grey.Lighten4;
            table.Cell().Element(c => LabelCell(c, background)).Text(T("Address", "العنوان"));
            table.Cell().ColumnSpan(3).Element(c => ValueCell(c, background)).Text(Ltr(report.Address ?? Missing));
        });
    }

    private void ComposeAnswers(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(5);
            column.Item().EnsureSpace(SectionLeadSpace).Text(T("Last submitted data", "آخر البيانات المقدمة")).FontSize(14).SemiBold().FontColor(Accent);

            if (report.LatestFill is not { } fill)
            {
                column.Item().Text(T("The task has not been filled yet.", "لم تتم تعبئة المهمة بعد.")).Italic().FontColor(Colors.Grey.Darken1);
                return;
            }

            column.Item().Text(
                    $"{T("Filled by", "عبأها")}: {Ltr(fill.FilledBy ?? Missing)} · {Date(fill.FilledAt?.UtcDateTime, DateTimeFormat)} · {T("Form version", "إصدار النموذج")} {Ltr(fill.VersionNo.ToString(CultureInfo.InvariantCulture))}")
                .FontSize(9).FontColor(Colors.Grey.Darken1);

            if (report.Answers.Count == 0)
            {
                column.Item().Text(T("No answers were recorded.", "لم تُسجل أي إجابات.")).Italic().FontColor(Colors.Grey.Darken1);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(3);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text(T("Field", "الحقل"));
                    header.Cell().Element(HeaderCell).Text(T("Answer", "الإجابة"));
                });

                foreach (var answer in report.Answers)
                {
                    table.Cell().Element(PlainCell).Text(answer.Label).SemiBold();
                    table.Cell().Element(PlainCell).Text(Ltr(answer.Value));
                }
            });
        });
    }

    private void ComposeSignatures(IContainer container, IReadOnlyList<TaskReportFile> signatures)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().EnsureSpace(SectionLeadSpace + SignatureMaxHeight).Text(T("Signature", "التوقيع")).FontSize(14).SemiBold().FontColor(Accent);

            foreach (var signature in signatures)
            {
                column.Item().ShowEntire().Column(item =>
                {
                    if (images.TryGetValue(signature, out var image))
                    {
                        item.Item()
                            .MaxWidth(SignatureMaxWidth + 16)
                            .Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8)
                            .MaxHeight(SignatureMaxHeight)
                            .Image(image).FitArea();
                    }
                    else
                    {
                        item.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten4).Padding(8)
                            .Text(T("Signature image not available", "صورة التوقيع غير متوفرة")).Italic().FontColor(Colors.Red.Darken1);
                    }

                    item.Item().PaddingTop(3).Text($"{signature.Field}: {Ltr(signature.FileName)}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }
        });
    }

    private void ComposeImages(IContainer container, IReadOnlyList<TaskReportFile> photos)
    {
        container.Column(column =>
        {
            column.Spacing(10);
            column.Item().EnsureSpace(SectionLeadSpace + ImageMaxHeight).Text(T("Attached images", "الصور المرفقة")).FontSize(14).SemiBold().FontColor(Accent);

            foreach (var photo in photos)
            {
                column.Item().ShowEntire().Column(item =>
                {
                    item.Item().MaxHeight(ImageMaxHeight).Image(images[photo]).FitArea();
                    item.Item().PaddingTop(2).Text($"{photo.Field}: {Ltr(photo.FileName)}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }
        });
    }

    private void ComposeFileList(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(5);
            column.Item().EnsureSpace(SectionLeadSpace).Text(T("Attached files", "الملفات المرفقة")).FontSize(14).SemiBold().FontColor(Accent);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(65);
                    columns.ConstantColumn(100);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text(T("File name", "اسم الملف"));
                    header.Cell().Element(HeaderCell).Text(T("Field", "الحقل"));
                    header.Cell().Element(HeaderCell).Text(T("Size (KB)", "الحجم (ك.ب)"));
                    header.Cell().Element(HeaderCell).Text(T("Uploaded", "تاريخ الرفع"));
                    header.Cell().Element(HeaderCell).Text(T("In report", "في التقرير"));
                });

                foreach (var file in report.Files)
                {
                    var embedded = images.ContainsKey(file);
                    var state = embedded
                        ? T("Embedded", "مضمّنة")
                        : file.IsImage && file.IsFromLatestFill
                            ? T("Unavailable", "غير متوفرة")
                            : file.IsFromLatestFill ? T("Listed", "مدرج") : T("Earlier fill", "تعبئة سابقة");

                    table.Cell().Element(PlainCell).Text(Ltr(file.FileName));
                    table.Cell().Element(PlainCell).Text(file.Field);
                    table.Cell().Element(PlainCell).Text(Ltr(Math.Ceiling(file.SizeBytes / 1024d).ToString(CultureInfo.InvariantCulture)));
                    table.Cell().Element(PlainCell).Text(Date(file.CreatedAt, DateTimeFormat));
                    table.Cell().Element(PlainCell).Text(state)
                        .FontColor(file.IsImage && file.IsFromLatestFill && !embedded ? Colors.Red.Darken1 : Colors.Black);
                }
            });
        });
    }

    /// <summary>A titled four-column table of label/value pairs, two pairs to a row.</summary>
    private void ComposeSection(IContainer container, string title, Action<TableDescriptor> rows)
    {
        container.Column(column =>
        {
            column.Spacing(5);
            column.Item().EnsureSpace(SectionLeadSpace).Text(title).FontSize(14).SemiBold().FontColor(Accent);
            column.Item().Border(1).BorderColor(Rule).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(95);
                    columns.RelativeColumn();
                    columns.ConstantColumn(95);
                    columns.RelativeColumn();
                });

                rows(table);
            });
        });
    }

    private void FactRow(TableDescriptor table, int index, string label1, string value1, string label2, string value2)
    {
        var background = index % 2 == 1 ? Colors.Grey.Lighten4 : Colors.White;
        table.Cell().Element(c => LabelCell(c, background)).Text(label1);
        table.Cell().Element(c => ValueCell(c, background)).Text(Ltr(value1));
        table.Cell().Element(c => LabelCell(c, background)).Text(label2);
        table.Cell().Element(c => ValueCell(c, background)).Text(Ltr(value2));
    }

    /// <summary>A note set off from the tables, its accent rule on the side the reader starts from.</summary>
    private void ComposeCallout(IContainer container, string heading, string body, string background, string accent)
    {
        var edged = IsArabic ? container.Background(background).BorderRight(3) : container.Background(background).BorderLeft(3);

        edged.BorderColor(accent).Padding(8).Column(column =>
        {
            column.Spacing(3);
            column.Item().Text(heading).SemiBold().FontColor(accent);
            column.Item().Text(body);
        });
    }

    private static IContainer LabelCell(IContainer container, string background) =>
        container.Background(background).BorderBottom(1).BorderColor(Rule).Padding(5).DefaultTextStyle(x => x.SemiBold());

    private static IContainer ValueCell(IContainer container, string background) =>
        container.Background(background).BorderBottom(1).BorderColor(Rule).Padding(5);

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Accent).Padding(5).DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold());

    private static IContainer PlainCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Rule).Padding(5);

    private string Date(DateTime? value, string format) =>
        value is null ? Missing : Ltr(value.Value.ToString(format, CultureInfo.InvariantCulture));
}

/// <summary>
/// Report labels for the lifecycle codes, in step with <c>tasks.status.*</c>, <c>tasks.priority.*</c>,
/// <c>tasks.source.*</c> and <c>tasks.returnReason.*</c> in the web client's translations.
/// </summary>
internal static class TaskReportText
{
    private static readonly Dictionary<string, (string En, string Ar)> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CREATED"] = ("New", "جديدة"),
        ["ASSIGNED"] = ("Assigned", "مسندة"),
        ["IN_PROGRESS"] = ("In progress", "قيد التنفيذ"),
        ["SUBMITTED"] = ("Filled", "معبأة"),
        ["APPROVED"] = ("Completed", "مكتملة"),
        ["RETURNED"] = ("Returned", "معادة"),
        ["EXPIRED"] = ("Expired", "منتهية"),
    };

    private static readonly Dictionary<string, (string En, string Ar)> Priorities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LOW"] = ("Low", "منخفضة"),
        ["NORMAL"] = ("Normal", "عادية"),
        ["HIGH"] = ("High", "عالية"),
        ["URGENT"] = ("Urgent", "عاجلة"),
    };

    private static readonly Dictionary<string, (string En, string Ar)> Sources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MANUAL"] = ("Manual", "يدوي"),
        ["API"] = ("API", "واجهة برمجية"),
        ["IMPORT"] = ("Import", "استيراد"),
    };

    private static readonly Dictionary<string, (string En, string Ar)> ReturnReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INCOMPLETE_DATA"] = ("Incomplete data", "بيانات ناقصة"),
        ["WRONG_LOCATION"] = ("Wrong location", "موقع خاطئ"),
        ["POOR_MEDIA"] = ("Poor photos or media", "صور أو وسائط غير واضحة"),
        ["NEEDS_REVISIT"] = ("Needs a revisit", "تحتاج زيارة أخرى"),
        ["OTHER"] = ("Other", "أخرى"),
    };

    public static string Status(string code, bool arabic) => Label(Statuses, code, arabic);

    public static string Priority(string code, bool arabic) => Label(Priorities, code, arabic);

    public static string Source(string code, bool arabic) => Label(Sources, code, arabic);

    public static string ReturnReason(string code, bool arabic) => Label(ReturnReasons, code, arabic);

    /// <summary>An unknown code prints as itself rather than as nothing.</summary>
    private static string Label(Dictionary<string, (string En, string Ar)> labels, string code, bool arabic) =>
        labels.TryGetValue(code, out var label) ? (arabic ? label.Ar : label.En) : code;
}
