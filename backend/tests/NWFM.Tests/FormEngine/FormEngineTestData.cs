namespace NWFM.Tests.Modules.FormEngine;

using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;

/// <summary>Shared fixtures for the FormEngine tests: schema JSON and an in-memory context.</summary>
internal static class FormEngineTestData
{
    /// <summary>
    /// A context backed by the in-memory provider. Transactions are a no-op there, so the warning
    /// that would otherwise throw is ignored — the code under test opens one on every publish.
    /// </summary>
    public static FormEngineDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FormEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new FormEngineDbContext(options);
    }

    /// <summary>A one-field schema, for tests that only need something publishable.</summary>
    public static string SimpleSchema(string dataName = "meter_reading", string type = FormElementTypes.Numeric) =>
        $$"""
        {
          "name_en": "Simple Form",
          "name_ar": "نموذج بسيط",
          "elements": [
            { "type": "{{type}}", "data_name": "{{dataName}}", "label_en": "Reading", "label_ar": "القراءة",
              "required": false, "hidden": false, "disabled": false }
          ]
        }
        """;

    /// <summary><see cref="SimpleSchema"/>'s field plus a second — what a republish adding a field looks like.</summary>
    public const string TwoFieldSchema = """
    {
      "name_en": "Simple Form",
      "name_ar": "نموذج بسيط",
      "elements": [
        { "type": "numeric", "data_name": "meter_reading", "label_en": "Reading", "label_ar": "القراءة" },
        { "type": "numeric", "data_name": "depth_m", "label_en": "Depth", "label_ar": "العمق" }
      ]
    }
    """;

    /// <summary>
    /// A section holding a required text field, a choice with "Other", and a hidden field — enough to
    /// exercise flattening, companions and inherited visibility.
    /// </summary>
    public const string SectionedSchema = """
    {
      "name_en": "Inspection",
      "name_ar": "فحص",
      "elements": [
        {
          "type": "section",
          "data_name": "details",
          "label_en": "Details",
          "label_ar": "التفاصيل",
          "hidden": false,
          "visible_conditions": { "match": "all", "conditions": [ { "field": "is_open", "operator": "equal", "value": "yes" } ], "preserve_data": false },
          "elements": [
            { "type": "text", "data_name": "inspector_name", "label_en": "Inspector", "label_ar": "المفتش",
              "required": true, "hidden": false, "disabled": false, "min_length": 3, "max_length": 20,
              "pattern": { "regex": "^[A-Za-z ]+$", "description": "letters only" } },
            { "type": "single_choice", "data_name": "pipe_material", "label_en": "Material", "label_ar": "المادة",
              "required": false, "hidden": false, "disabled": false, "allow_other": true,
              "choices": [ { "value": "steel", "label_en": "Steel", "label_ar": "صلب" },
                           { "value": "pvc", "label_en": "PVC", "label_ar": "بلاستيك" } ] }
          ]
        },
        { "type": "yes_no", "data_name": "is_open", "label_en": "Open", "label_ar": "مفتوح",
          "required": false, "hidden": false, "disabled": false },
        { "type": "numeric", "data_name": "depth_m", "label_en": "Depth", "label_ar": "العمق",
          "required": false, "hidden": false, "disabled": false, "format": "integer", "min": 1, "max": 10 }
      ]
    }
    """;
}
