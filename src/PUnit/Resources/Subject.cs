namespace PUnit;

/// <summary>Well-known lineage subjects for <c>[References]</c>/<c>[Consumes]</c>.</summary>
public static class Subject
{
    /// <summary>
    /// The step's <c>[Creates]</c>/<c>[Edits]</c> return value, as a lineage subject. The value is a
    /// reserved token no C# parameter can be named. MUST stay identical to
    /// <c>PUnit.Generator.Lowering.AttributeReader.ReturnSubject</c> (separate assembly).
    /// </summary>
    public const string Return = "<return>";
}
