namespace Workflow.Infrastructure.Services;

using System.Security.Cryptography;
using System.Text;
using System.Xml;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Models;
using Workflow.Domain.Enums;

internal sealed class WorkflowXmlCompiler : IWorkflowXmlCompiler
{
    private const int MaxCharacters = 512 * 1024;
    private const int MaxActivities = 500;
    private const int MaxTransitions = 1000;
    private const int MaxVariables = 200;
    private const int MaxNestingDepth = 10;
    private const string WorkflowNamespace = "https://privora.io/workflow/v1";
    private const string WorkflowNamespaceLegacy = "urn:privora:workflow:v1";

    public Result<WorkflowXmlDocument> Compile(string xmlContent, out string canonicalHash)
    {
        canonicalHash = string.Empty;

        if (string.IsNullOrWhiteSpace(xmlContent))
            return Result.Failure<WorkflowXmlDocument>(
                new Error("Workflow.Version.InvalidXml", "XML content is empty."));

        if (Encoding.UTF8.GetByteCount(xmlContent) > MaxCharacters)
            return Result.Failure<WorkflowXmlDocument>(
                new Error("Workflow.Version.XmlTooLarge", "XML content exceeds the 512 KB limit."));

        if (xmlContent.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            xmlContent.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<WorkflowXmlDocument>(
                new Error("Workflow.Version.DtdDetected", "DTD declarations are not permitted in workflow XML."));

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        try
        {
            var doc = new WorkflowXmlDocument();
            var activities = new List<ActivityXmlNode>();
            var transitions = new List<TransitionXmlNode>();
            var variables = new List<VariableXmlNode>();
            string? workspaceJson = null;

            using var stringReader = new System.IO.StringReader(xmlContent);
            using var reader = XmlReader.Create(stringReader, settings);

            int depth = 0;
            ActivityXmlNode? currentActivity = null;
            var currentRules = new List<AssignmentRuleXmlNode>();
            var currentOutcomes = new List<OutcomeXmlNode>();
            var currentActions = new List<ActionXmlNode>();

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    depth++;
                    if (depth > MaxNestingDepth)
                        return Result.Failure<WorkflowXmlDocument>(
                            new Error("Workflow.Version.InvalidXml",
                                $"XML nesting depth exceeds the maximum of {MaxNestingDepth}."));

                    var localName = reader.LocalName;
                    var ns = reader.NamespaceURI;

                    if (!string.IsNullOrEmpty(ns)
                        && ns != WorkflowNamespace
                        && ns != WorkflowNamespaceLegacy)
                        return Result.Failure<WorkflowXmlDocument>(
                            new Error("Workflow.Version.InvalidXml",
                                $"Unexpected namespace '{ns}'. Expected '{WorkflowNamespace}'."));

                    switch (localName)
                    {
                        case "Workflow":
                        case "WorkflowDefinition":
                            workspaceJson = reader.GetAttribute("workspaceJson");
                            break;
                        case "Activity":
                        {
                            if (activities.Count >= MaxActivities)
                                return Result.Failure<WorkflowXmlDocument>(
                                    new Error("Workflow.Version.InvalidXml",
                                        $"Activity count exceeds maximum of {MaxActivities}."));

                            var node = new ActivityXmlNode
                            {
                                NodeKey = reader.GetAttribute("nodeKey") ?? string.Empty,
                                ActivityTypeName = reader.GetAttribute("type") ?? string.Empty,
                                Name = reader.GetAttribute("name") ?? string.Empty,
                                NameAr = reader.GetAttribute("nameAr"),
                                ActionKey = reader.GetAttribute("actionKey"),
                                AssignmentKey = reader.GetAttribute("assignmentKey"),
                                AssignmentGroupId = reader.GetAttribute("assignmentGroupId"),
                                AssignmentPurpose = reader.GetAttribute("assignmentPurpose"),
                                ConfigurationJson = reader.GetAttribute("configurationJson"),
                                PositionX = TryParseDouble(reader.GetAttribute("positionX")),
                                PositionY = TryParseDouble(reader.GetAttribute("positionY")),
                            };

                            if (reader.IsEmptyElement)
                            {
                                activities.Add(new ActivityXmlNode
                                {
                                    NodeKey = node.NodeKey,
                                    ActivityTypeName = node.ActivityTypeName,
                                    Name = node.Name,
                                    NameAr = node.NameAr,
                                    ActionKey = node.ActionKey,
                                    AssignmentKey = node.AssignmentKey,
                                    AssignmentGroupId = node.AssignmentGroupId,
                                    AssignmentPurpose = node.AssignmentPurpose,
                                    ConfigurationJson = node.ConfigurationJson,
                                    PositionX = node.PositionX,
                                    PositionY = node.PositionY,
                                    AssignmentRules = SynthesizeRules(node, []),
                                    Outcomes = Array.Empty<OutcomeXmlNode>(),
                                    Actions = Array.Empty<ActionXmlNode>(),
                                });
                            }
                            else
                            {
                                currentRules = [];
                                currentOutcomes = [];
                                currentActions = [];
                                currentActivity = node;
                            }
                            break;
                        }

                        case "AssignmentRule" when currentActivity is not null:
                            currentRules.Add(new AssignmentRuleXmlNode
                            {
                                AssigneeTypeName = reader.GetAttribute("assigneeType") ?? string.Empty,
                                AssignmentPurpose = reader.GetAttribute("assignmentPurpose"),
                                AssignmentKey = reader.GetAttribute("assignmentKey"),
                                ReferenceId = reader.GetAttribute("referenceId")
                                    ?? reader.GetAttribute("assignmentGroupId"),
                                Expression = reader.GetAttribute("expression"),
                                Priority = int.TryParse(reader.GetAttribute("priority"), out var rp) ? rp : 0,
                                IsFallback = string.Equals(reader.GetAttribute("isFallback"), "true",
                                    StringComparison.OrdinalIgnoreCase),
                            });
                            break;

                        case "Outcome" when currentActivity is not null:
                            currentOutcomes.Add(new OutcomeXmlNode
                            {
                                OutcomeKey = reader.GetAttribute("key") ?? string.Empty,
                                Name = reader.GetAttribute("name") ?? string.Empty,
                                NameAr = reader.GetAttribute("nameAr"),
                                Description = reader.GetAttribute("description"),
                                DescriptionAr = reader.GetAttribute("descriptionAr"),
                                SortOrder = int.TryParse(reader.GetAttribute("order"), out var so) ? so : 0,
                                RequiresComment = string.Equals(reader.GetAttribute("requiresComment"), "true",
                                    StringComparison.OrdinalIgnoreCase),
                                RequiresAttachment = string.Equals(reader.GetAttribute("requiresAttachment"), "true",
                                    StringComparison.OrdinalIgnoreCase),
                                IsDefault = string.Equals(reader.GetAttribute("isDefault"), "true",
                                    StringComparison.OrdinalIgnoreCase),
                                ResultValue = reader.GetAttribute("resultValue"),
                            });
                            break;

                        case "Action" when currentActivity is not null:
                            currentActions.Add(new ActionXmlNode
                            {
                                ActionKey = reader.GetAttribute("key") ?? string.Empty,
                                ExecutionTriggerName = reader.GetAttribute("trigger") ?? "OnComplete",
                                OutcomeKey = reader.GetAttribute("outcomeKey"),
                                ConditionExpression = reader.GetAttribute("condition"),
                                Sequence = int.TryParse(reader.GetAttribute("sequence"), out var seq) ? seq : 0,
                                InputMappingJson = reader.GetAttribute("inputMapping"),
                                OutputMappingJson = reader.GetAttribute("outputMapping"),
                                FailurePolicyName = reader.GetAttribute("failurePolicy") ?? "Continue",
                                RetryCount = int.TryParse(reader.GetAttribute("retryCount"), out var rc) ? rc : 0,
                                RetryDelaySeconds = int.TryParse(reader.GetAttribute("retryDelaySeconds"), out var rd) ? rd : 0,
                                TimeoutSeconds = int.TryParse(reader.GetAttribute("timeoutSeconds"), out var ts) ? ts : 0,
                            });
                            break;

                        case "Transition":
                            if (transitions.Count >= MaxTransitions)
                                return Result.Failure<WorkflowXmlDocument>(
                                    new Error("Workflow.Version.InvalidXml",
                                        $"Transition count exceeds maximum of {MaxTransitions}."));
                            transitions.Add(new TransitionXmlNode
                            {
                                Key = reader.GetAttribute("key") ?? string.Empty,
                                FromNodeKey = reader.GetAttribute("from") ?? string.Empty,
                                ToNodeKey = reader.GetAttribute("to") ?? string.Empty,
                                ConditionExpression = reader.GetAttribute("condition"),
                                IsDefault = string.Equals(reader.GetAttribute("isDefault"), "true",
                                    StringComparison.OrdinalIgnoreCase),
                                Priority = int.TryParse(reader.GetAttribute("priority"), out var tp) ? tp : 0,
                            });
                            break;

                        case "Variable":
                            if (variables.Count >= MaxVariables)
                                return Result.Failure<WorkflowXmlDocument>(
                                    new Error("Workflow.Version.InvalidXml",
                                        $"Variable count exceeds maximum of {MaxVariables}."));
                            variables.Add(new VariableXmlNode
                            {
                                Key = reader.GetAttribute("key") ?? string.Empty,
                                Name = reader.GetAttribute("name") ?? string.Empty,
                                NameAr = reader.GetAttribute("nameAr"),
                                DataTypeName = reader.GetAttribute("dataType") ?? string.Empty,
                                IsRequired = string.Equals(reader.GetAttribute("isRequired"), "true",
                                    StringComparison.OrdinalIgnoreCase),
                                IsSensitive = string.Equals(reader.GetAttribute("isSensitive"), "true",
                                    StringComparison.OrdinalIgnoreCase),
                                DefaultValue = reader.GetAttribute("defaultValue"),
                                Description = reader.GetAttribute("description"),
                                DescriptionAr = reader.GetAttribute("descriptionAr"),
                            });
                            break;
                    }

                    // Self-closing elements do not fire EndElement. Activity is finalized above,
                    // but it still occupies only the current XML depth and must be released here.
                    if (reader.IsEmptyElement)
                        depth--;
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.LocalName == "Activity" && currentActivity is not null)
                    {
                        if (activities.Count >= MaxActivities)
                            return Result.Failure<WorkflowXmlDocument>(
                                new Error("Workflow.Version.InvalidXml",
                                    $"Activity count exceeds maximum of {MaxActivities}."));

                        var finalized = new ActivityXmlNode
                        {
                            NodeKey = currentActivity.NodeKey,
                            ActivityTypeName = currentActivity.ActivityTypeName,
                            Name = currentActivity.Name,
                            NameAr = currentActivity.NameAr,
                            ActionKey = currentActivity.ActionKey,
                            AssignmentKey = currentActivity.AssignmentKey,
                            AssignmentGroupId = currentActivity.AssignmentGroupId,
                            AssignmentPurpose = currentActivity.AssignmentPurpose,
                            ConfigurationJson = currentActivity.ConfigurationJson,
                            PositionX = currentActivity.PositionX,
                            PositionY = currentActivity.PositionY,
                            AssignmentRules = SynthesizeRules(currentActivity, currentRules),
                            Outcomes = currentOutcomes.AsReadOnly(),
                            Actions = currentActions.AsReadOnly(),
                        };
                        activities.Add(finalized);
                        currentActivity = null;
                        currentRules = [];
                        currentOutcomes = [];
                        currentActions = [];
                    }
                    depth--;
                }
            }

            var result = new WorkflowXmlDocument
            {
                WorkspaceJson = workspaceJson,
                Activities = activities.AsReadOnly(),
                Transitions = transitions.AsReadOnly(),
                Variables = variables.AsReadOnly(),
            };

            canonicalHash = ComputeHash(activities, transitions, variables);
            if (workspaceJson is not null) canonicalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalHash + workspaceJson)));
            return Result.Success(result);
        }
        catch (XmlException ex)
        {
            return Result.Failure<WorkflowXmlDocument>(
                new Error("Workflow.Version.InvalidXml", $"XML parse error: {ex.Message}"));
        }
    }

    /// <summary>
    /// Designer stores assignmentGroupId (org group) and/or assignmentKey on the Activity.
    /// Runtime projection requires AssignmentRule children — synthesize a rule when either
    /// attribute is present and no explicit rules were parsed.
    /// </summary>
    private static IReadOnlyList<AssignmentRuleXmlNode> SynthesizeRules(
        ActivityXmlNode node, List<AssignmentRuleXmlNode> parsed)
    {
        if (parsed.Count > 0)
            return parsed.AsReadOnly();

        if (string.IsNullOrWhiteSpace(node.AssignmentKey)
            && string.IsNullOrWhiteSpace(node.AssignmentGroupId))
            return Array.Empty<AssignmentRuleXmlNode>();

        return
        [
            new AssignmentRuleXmlNode
            {
                AssigneeTypeName = nameof(AssigneeType.AssignmentGroup),
                AssignmentPurpose = node.AssignmentPurpose,
                AssignmentKey = node.AssignmentKey,
                ReferenceId = node.AssignmentGroupId,
                Priority = 0,
                IsFallback = false,
            }
        ];
    }

    private static double? TryParseDouble(string? value)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string ComputeHash(
        IEnumerable<ActivityXmlNode> activities,
        IEnumerable<TransitionXmlNode> transitions,
        IEnumerable<VariableXmlNode> variables)
    {
        var sb = new StringBuilder();
        foreach (var a in activities.OrderBy(x => x.NodeKey))
        {
            sb.Append($"A:{a.NodeKey}:{a.ActivityTypeName}:{a.Name}:{a.AssignmentKey}:{a.AssignmentGroupId};");
            foreach (var r in a.AssignmentRules)
                sb.Append($"R:{r.AssignmentPurpose}:{r.AssignmentKey}:{r.ReferenceId};");
            foreach (var o in a.Outcomes.OrderBy(x => x.SortOrder))
                sb.Append($"O:{o.OutcomeKey}:{o.Name};");
            foreach (var ac in a.Actions.OrderBy(x => x.Sequence))
                sb.Append($"AC:{ac.ActionKey}:{ac.ExecutionTriggerName}:{ac.Sequence};");
        }
        foreach (var t in transitions.OrderBy(x => x.Key))
            sb.Append($"T:{t.Key}:{t.FromNodeKey}:{t.ToNodeKey}:{t.ConditionExpression};");
        foreach (var v in variables.OrderBy(x => x.Key))
            sb.Append($"V:{v.Key}:{v.DataTypeName}:{v.IsRequired};");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
