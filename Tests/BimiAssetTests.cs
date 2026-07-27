using System.Xml.Linq;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class BimiAssetTests
{
    [Fact]
    public void Craftora_bimi_logo_uses_safe_tiny_ps_profile()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "wwwroot",
            "email-assets",
            "craftora-bimi.svg"));
        var file = new FileInfo(path);

        Assert.True(file.Exists);
        Assert.InRange(file.Length, 1, 32 * 1024);

        var document = XDocument.Load(path);
        var root = Assert.IsType<XElement>(document.Root);
        var forbiddenElements = new[]
        {
            "script",
            "image",
            "animate",
            "foreignObject"
        };

        Assert.Equal("svg", root.Name.LocalName);
        Assert.Equal("1.2", root.Attribute("version")?.Value);
        Assert.Equal("tiny-ps", root.Attribute("baseProfile")?.Value);
        Assert.Equal("256", root.Attribute("width")?.Value);
        Assert.Equal("256", root.Attribute("height")?.Value);
        Assert.NotNull(root.Elements().FirstOrDefault(
            element => element.Name.LocalName == "title"));
        Assert.NotNull(root.Elements().FirstOrDefault(
            element => element.Name.LocalName == "desc"));
        Assert.DoesNotContain(
            root.Descendants(),
            element => forbiddenElements.Contains(
                element.Name.LocalName,
                StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            root.DescendantsAndSelf().Attributes(),
            attribute =>
                attribute.Name.LocalName is "href" or "xlink:href");
    }
}
