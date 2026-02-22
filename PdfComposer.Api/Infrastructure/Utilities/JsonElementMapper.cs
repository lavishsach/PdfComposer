using System.Collections;
using System.Dynamic;
using System.Text.Json;

namespace PdfComposer.Api.Infrastructure.Utilities;

public static class JsonElementMapper
{
    public static object? ToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ToExpando(element),
            JsonValueKind.Array => ToList(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static ExpandoObject ToExpando(JsonElement element)
    {
        IDictionary<string, object?> expando = new ExpandoObject();
        foreach (var property in element.EnumerateObject())
        {
            expando[property.Name] = ToObject(property.Value);
        }

        return (ExpandoObject)expando;
    }

    private static List<object?> ToList(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ToObject(item));
        }

        return list;
    }
}
