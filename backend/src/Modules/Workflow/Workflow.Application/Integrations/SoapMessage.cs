using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Workflow.Application.Integrations;

public static class SoapMessage
{
    public static XDocument Parse(string xml)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    public static string Render(string template, Dictionary<string, JsonElement> variables)
    {
        var xml = Parse(template);
        foreach (var text in xml.DescendantNodes().OfType<XText>()) text.Value = IntegrationValueMapper.Render(text.Value, variables);
        foreach (var attribute in xml.Descendants().Attributes().Where(a => !a.IsNamespaceDeclaration)) attribute.Value = IntegrationValueMapper.Render(attribute.Value, variables);
        return xml.ToString(SaveOptions.DisableFormatting);
    }

    public static bool HasFault(string xml) => Parse(xml).Descendants().Any(e => e.Name.LocalName == "Fault"
        && e.Name.NamespaceName is "http://schemas.xmlsoap.org/soap/envelope/" or "http://www.w3.org/2003/05/soap-envelope");

    public static Dictionary<string, object?> Map(string xml, HttpActivityConfiguration configuration)
    {
        var document = Parse(xml);
        var namespaces = new XmlNamespaceManager(new NameTable());
        namespaces.AddNamespace("soap", configuration.SoapVersion == "1.2" ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/");
        foreach (var (prefix, uri) in configuration.XmlNamespaces) namespaces.AddNamespace(prefix, uri);
        var values = new Dictionary<string, object?>();
        foreach (var (key, xpath) in configuration.XmlOutputMappings)
        {
            var value = document.XPathEvaluate(xpath, namespaces);
            values[key] = value is System.Collections.IEnumerable nodes and not string
                ? nodes.Cast<object>().Select(n => n switch { XElement e => e.Value, XAttribute a => a.Value, XText t => t.Value, _ => n.ToString() }).FirstOrDefault()
                : value;
            if (values[key] is null) throw new InvalidOperationException($"SOAP response mapping for '{key}' did not match a value.");
        }
        return values;
    }
}
