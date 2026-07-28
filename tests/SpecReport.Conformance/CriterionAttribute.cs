namespace SpecReport.Conformance;

/// <summary>
/// Marks a test as evidence for a spec criterion from docs/criteria.json.
///
/// The point of the tag is the INVERSE direction: <see cref="GateTests"/> walks the registry
/// and fails when an in-scope executable criterion has no test carrying its id. Without that,
/// a criterion nobody implemented looks identical to one that passes.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class CriterionAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
