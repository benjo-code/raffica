namespace Raffica.Write.Model;

/// <summary>A picture part a step added to or removed from the package: the relationship id it is reached by, its content type and the file extension it is stored with.</summary>
public sealed record MediaRef(string RelationshipId, string ContentType, string Extension);
