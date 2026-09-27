namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Common.Schema;

/// <summary>
/// The C2M metadata the builder writes on a closing form: the parameter a field is sent under, and
/// the outcome each Action Taken option closes the field activity with.
/// </summary>
public sealed class FormSchemaC2mTests
{
    private const string ClosingForm = """
    {
      "name_en": "Closing",
      "name_ar": "إغلاق",
      "elements": [
        { "type": "single_choice", "data_name": "wfm_action_taken", "label_en": "Action", "label_ar": "الإجراء",
          "choices": [
            { "value": "OCUL01", "label_en": "Done", "label_ar": "تم", "c2m_fa_status": " c ", "c2m_reason": " ALL-GOOD " },
            { "value": "MMFCNR1", "label_en": "No contract", "label_ar": "لا عقد" }
          ] },
        { "type": "numeric", "data_name": "wfm_building_units", "label_en": "Units", "label_ar": "الوحدات",
          "c2m_parameter_name": " CM_BUNIT " },
        { "type": "text", "data_name": "note", "label_en": "Note", "label_ar": "ملاحظة", "c2m_parameter_name": "  " }
      ]
    }
    """;

    [Fact]
    public void Parse_ReadsTheOutcomeEachActionTakenOptionClosesWith()
    {
        var choices = FormSchemaParser.Parse(ClosingForm).Fields[0].Choices;

        choices[0].C2mFaStatus.Should().Be("C");
        choices[0].C2mReason.Should().Be("ALL-GOOD");
        choices[1].C2mFaStatus.Should().BeNull();
        choices[1].C2mReason.Should().BeNull();
    }

    [Fact]
    public void Parse_ReadsTheParameterAFieldIsSentUnder_AndTreatsBlankAsNone()
    {
        var fields = FormSchemaParser.Parse(ClosingForm).Fields;

        fields.Single(f => f.DataName == "wfm_building_units").C2mParameterName.Should().Be("CM_BUNIT");
        fields.Single(f => f.DataName == "note").C2mParameterName.Should().BeNull();
    }
}
