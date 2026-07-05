using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// (Dé)sérialisation JSON stable de l'arbre <see cref="ScopeExpression"/> (Inc 10). Le format
/// est piloté manuellement — sans coupler le Domaine à System.Text.Json — via un discriminant
/// <c>type</c> par nœud. Les catégories sont stockées par nom d'enum (<c>Audiobook</c>, …).
/// </summary>
public static class ScopeExpressionJson
{
    public static string Serialize(ScopeExpression expression)
        => ToNode(expression).ToJsonString();

    public static ScopeExpression Deserialize(string json)
    {
        var node = JsonNode.Parse(json)
            ?? throw new FormatException("Expression de scope vide.");
        return FromNode(node);
    }

    private static JsonNode ToNode(ScopeExpression expression) => expression switch
    {
        AllScope => new JsonObject { ["type"] = "all" },
        NoneScope => new JsonObject { ["type"] = "none" },
        CategoryScope c => new JsonObject
        {
            ["type"] = "category",
            ["categories"] = new JsonArray(c.Values.Select(x => JsonValue.Create(x.ToString())).ToArray<JsonNode?>()),
        },
        SubcategoryScope s => new JsonObject
        {
            ["type"] = "subcategory",
            ["values"] = new JsonArray(s.Values.Select(x => JsonValue.Create(x)).ToArray<JsonNode?>()),
        },
        YearRangeScope y => new JsonObject
        {
            ["type"] = "year",
            ["min"] = y.Min is { } min ? JsonValue.Create(min) : null,
            ["max"] = y.Max is { } max ? JsonValue.Create(max) : null,
        },
        AndScope a => new JsonObject
        {
            ["type"] = "and",
            ["operands"] = new JsonArray(a.Operands.Select(ToNode).ToArray()),
        },
        OrScope o => new JsonObject
        {
            ["type"] = "or",
            ["operands"] = new JsonArray(o.Operands.Select(ToNode).ToArray()),
        },
        NotScope n => new JsonObject
        {
            ["type"] = "not",
            ["operand"] = ToNode(n.Inner),
        },
        _ => throw new NotSupportedException($"Type d'expression de scope inconnu : {expression.GetType().Name}"),
    };

    private static ScopeExpression FromNode(JsonNode node)
    {
        var obj = node.AsObject();
        var type = (string?)obj["type"]
            ?? throw new FormatException("Nœud d'expression de scope sans discriminant « type ».");

        return type switch
        {
            "all" => ScopeExpression.All,
            "none" => ScopeExpression.None,
            "category" => new CategoryScope(
                Array(obj, "categories").Select(v => Enum.Parse<MediaCategory>((string)v!)).ToList()),
            "subcategory" => new SubcategoryScope(
                Array(obj, "values").Select(v => (string)v!).ToList()),
            "year" => new YearRangeScope((int?)obj["min"], (int?)obj["max"]),
            "and" => new AndScope(Array(obj, "operands").Select(n => FromNode(n!)).ToList()),
            "or" => new OrScope(Array(obj, "operands").Select(n => FromNode(n!)).ToList()),
            "not" => new NotScope(FromNode(obj["operand"]
                ?? throw new FormatException("Nœud « not » sans opérande."))),
            _ => throw new FormatException($"Type d'expression de scope inconnu : {type}"),
        };
    }

    private static JsonArray Array(JsonObject obj, string name)
        => obj[name]?.AsArray() ?? throw new FormatException($"Tableau « {name} » manquant.");
}
