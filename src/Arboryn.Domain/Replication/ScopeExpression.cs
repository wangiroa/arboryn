using System.Collections.Generic;
using System.Linq;
using Arboryn.Domain.Enums;

namespace Arboryn.Domain.Replication;

/// <summary>
/// Expression composable définissant quels <c>LogicalFile</c> sont dans le périmètre de
/// réplication d'un volume (Inc 10, cf. § 4.3). Arbre pur, évaluable hors-ligne et
/// sérialisable (la (dé)sérialisation JSON vit dans l'Infrastructure). Les feuilles
/// couvrent les cas du brief (<c>category in (…)</c>, <c>subcategory = …</c>,
/// <c>year &gt;= …</c>) et les nœuds <c>And</c>/<c>Or</c>/<c>Not</c> les combinent.
/// </summary>
public abstract record ScopeExpression
{
    /// <summary>Le sujet est-il dans le périmètre défini par cette expression ?</summary>
    public abstract bool Matches(ScopeSubject subject);

    /// <summary>Tout est en scope (<c>category in ('all')</c>).</summary>
    public static ScopeExpression All { get; } = new AllScope();

    /// <summary>Rien n'est en scope — défaut d'un volume sans périmètre défini.</summary>
    public static ScopeExpression None { get; } = new NoneScope();

    public static ScopeExpression Categories(params MediaCategory[] categories)
        => new CategoryScope(categories.ToList());

    public static ScopeExpression Subcategories(params string[] values)
        => new SubcategoryScope(values.ToList());

    public static ScopeExpression Years(int? min, int? max) => new YearRangeScope(min, max);

    public static ScopeExpression And(params ScopeExpression[] operands) => new AndScope(operands.ToList());

    public static ScopeExpression Or(params ScopeExpression[] operands) => new OrScope(operands.ToList());

    public static ScopeExpression Not(ScopeExpression inner) => new NotScope(inner);
}

/// <summary>Tout <c>LogicalFile</c> est en scope.</summary>
public sealed record AllScope : ScopeExpression
{
    public override bool Matches(ScopeSubject subject) => true;
}

/// <summary>Aucun <c>LogicalFile</c> n'est en scope.</summary>
public sealed record NoneScope : ScopeExpression
{
    public override bool Matches(ScopeSubject subject) => false;
}

/// <summary>En scope si la catégorie du sujet figure dans la liste.</summary>
public sealed record CategoryScope(IReadOnlyList<MediaCategory> Values) : ScopeExpression
{
    public override bool Matches(ScopeSubject subject) => Values.Contains(subject.Category);
}

/// <summary>
/// En scope si la sous-catégorie du sujet correspond à l'une des valeurs listées — soit
/// exactement, soit comme préfixe hiérarchique (« Investissements » couvre
/// « Investissements/Appartement Champigny »). Comparaison insensible à la casse.
/// </summary>
public sealed record SubcategoryScope(IReadOnlyList<string> Values) : ScopeExpression
{
    public override bool Matches(ScopeSubject subject)
    {
        if (string.IsNullOrEmpty(subject.Subcategory))
        {
            return false;
        }

        var actual = subject.Subcategory;
        return Values.Any(prefix =>
            actual.Equals(prefix, System.StringComparison.OrdinalIgnoreCase)
            || actual.StartsWith(prefix + "/", System.StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// En scope si l'année du sujet est dans l'intervalle fermé [<paramref name="Min"/>,
/// <paramref name="Max"/>] (bornes optionnelles). Un sujet sans année n'est jamais retenu
/// dès qu'une borne est posée.
/// </summary>
public sealed record YearRangeScope(int? Min, int? Max) : ScopeExpression
{
    public override bool Matches(ScopeSubject subject)
    {
        if (Min is null && Max is null)
        {
            return true;
        }

        if (subject.Year is not { } year)
        {
            return false;
        }

        return (Min is null || year >= Min) && (Max is null || year <= Max);
    }
}

/// <summary>En scope si toutes les sous-expressions le sont (conjonction ; vide ⇒ vrai).</summary>
public sealed record AndScope(IReadOnlyList<ScopeExpression> Operands) : ScopeExpression
{
    public override bool Matches(ScopeSubject subject) => Operands.All(o => o.Matches(subject));
}

/// <summary>En scope si au moins une sous-expression l'est (disjonction ; vide ⇒ faux).</summary>
public sealed record OrScope(IReadOnlyList<ScopeExpression> Operands) : ScopeExpression
{
    public override bool Matches(ScopeSubject subject) => Operands.Any(o => o.Matches(subject));
}

/// <summary>Négation d'une sous-expression.</summary>
public sealed record NotScope(ScopeExpression Inner) : ScopeExpression
{
    public override bool Matches(ScopeSubject subject) => !Inner.Matches(subject);
}
