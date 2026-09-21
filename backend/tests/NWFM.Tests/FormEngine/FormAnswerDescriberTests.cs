namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Common.Schema;

/// <summary>
/// A stored fill read back for a person: labels beside answers, in the form's order, with the
/// machine values a column holds turned into what the crew actually chose.
/// </summary>
public sealed class FormAnswerDescriberTests
{
    private static readonly FormSchema Sectioned = FormSchemaParser.Parse(FormEngineTestData.SectionedSchema);

    private const string MediaSchema = """
    {
      "name_en": "Site visit",
      "name_ar": "زيارة",
      "elements": [
        { "type": "photo", "data_name": "site_photo", "label_en": "Site photo", "label_ar": "صورة الموقع" },
        { "type": "geolocation", "data_name": "meter_location", "label_en": "Meter location", "label_ar": "موقع العداد" },
        { "type": "date", "data_name": "visit_date", "label_en": "Visit date", "label_ar": "تاريخ الزيارة" }
      ]
    }
    """;

    [Fact]
    public void Describe_FollowsTheFormsOrderAndSkipsBlankAnswers()
    {
        var views = FormAnswerDescriber.Describe(Sectioned, new Dictionary<string, object?>
        {
            ["depth_m"] = 4m,
            ["inspector_name"] = "Sara",
            ["pipe_material"] = null,
            ["is_open"] = "",
        });

        views.Select(v => v.DataName).Should().Equal("inspector_name", "depth_m");
        views[0].LabelEn.Should().Be("Inspector");
        views[0].LabelAr.Should().Be("المفتش");
        views[1].DisplayEn.Should().Be("4");
    }

    [Fact]
    public void Describe_ReadsAChoiceAsItsOptionsLabelInEachLanguage()
    {
        var view = FormAnswerDescriber.Describe(Sectioned, new Dictionary<string, object?> { ["pipe_material"] = "pvc" }).Single();

        view.DisplayEn.Should().Be("PVC");
        view.DisplayAr.Should().Be("بلاستيك");
    }

    [Fact]
    public void Describe_FoldsTheOtherTextIntoItsChoice()
    {
        var view = FormAnswerDescriber.Describe(Sectioned, new Dictionary<string, object?>
        {
            ["pipe_material"] = FormChoiceOther.Sentinel,
            [FormChoiceOther.KeyFor("pipe_material")] = "Copper",
        }).Single();

        view.DataName.Should().Be("pipe_material");
        view.DisplayEn.Should().Be("Copper");
    }

    [Fact]
    public void Describe_ReadsAYesNoAnswerAsWords()
    {
        var view = FormAnswerDescriber.Describe(Sectioned, new Dictionary<string, object?> { ["is_open"] = false }).Single();

        view.DisplayEn.Should().Be("No");
        view.DisplayAr.Should().Be("لا");
    }

    [Fact]
    public void Describe_MatchesColumnNamesWhateverTheirCase()
    {
        var views = FormAnswerDescriber.Describe(Sectioned, new Dictionary<string, object?> { ["DEPTH_M"] = 2 });

        views.Should().ContainSingle(v => v.DataName == "depth_m");
    }

    [Fact]
    public void Describe_ReadsMediaAsFileNamesAndAPointForTheMap()
    {
        var schema = FormSchemaParser.Parse(MediaSchema);

        var views = FormAnswerDescriber.Describe(schema, new Dictionary<string, object?>
        {
            ["site_photo"] = """[{"fileId":"6f1c9d2e-0000-0000-0000-000000000001","path":"x","name":"front.jpg"},{"fileId":"6f1c9d2e-0000-0000-0000-000000000002","path":"y","name":"meter.jpg"}]""",
            ["meter_location"] = """{"lat":24.7136,"lng":46.6753,"address":"Riyadh"}""",
            ["visit_date"] = new DateTime(2026, 9, 21),
        });

        views[0].DisplayEn.Should().Be("front.jpg, meter.jpg");
        views[1].Point.Should().NotBeNull();
        views[1].Point!.Latitude.Should().BeApproximately(24.7136, 1e-9);
        views[1].Point!.Address.Should().Be("Riyadh");
        views[2].DisplayEn.Should().Be("2026-09-21");
    }

    [Fact]
    public void Describe_LeavesOutAMediaAnswerWithNoFiles()
    {
        var views = FormAnswerDescriber.Describe(FormSchemaParser.Parse(MediaSchema), new Dictionary<string, object?> { ["site_photo"] = "[]" });

        views.Should().BeEmpty();
    }
}
