using DevToys.Api;

using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace DevToys.JsonQuery;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(JsonQueryAssemblyIdentifier))]
internal sealed class JsonQueryAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = "DevToys.JsonQuery.Assets.jq-icon.ttf";

        Stream stream = assembly.GetManifestResourceStream(resourceName)!;

        return new ValueTask<FontDefinition[]>([new("jq-icon", stream)]);
    }
}