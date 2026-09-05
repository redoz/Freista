namespace Raun;

/// <summary>Well-known lineage targets for a producer's <c>References</c>/<c>Consumes</c>.</summary>
public static class Subject
{
    /// <summary>
    /// The step's own return value, as a lineage <i>target</i> — used when a <c>[Edited]</c> parameter
    /// references/consumes the resource the step returns. The value is a reserved token no C# parameter
    /// can be named. MUST stay identical to
    /// <c>Raun.Generator.Lowering.AttributeReader.ReturnSubject</c> (separate assembly).
    /// </summary>
    public const string Return = "<return>";
}
