using System;
using System.Collections.Generic;

public static class IfcTypeRegistry
{
    // Case-insensitive, holds the distinct ifc_type_final values we see at runtime.
    private static readonly HashSet<string> _types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> Types => _types;

    public static void Clear() => _types.Clear();

    public static void Register(string ifcType)
    {
        if (!string.IsNullOrWhiteSpace(ifcType))
            _types.Add(ifcType.Trim());
    }
}
