using SmartX.Api.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Recursively validates a nested device deployment configuration (Facility -> Zone -> SubZone -> ...).
/// PART 1 requirement (Recursion): works for a tree of any depth, so the same code validates
/// "Sub-Zone B -> Zone 1 -> Facility A" or a deeper hierarchy without modification.
/// </summary>
public static class RecursiveConfigValidator
{
    // Guards against a pathological configuration causing runaway recursion.
    private const int MaxDepth = 6;

    /// <summary>Walks the tree, collecting an error per invalid node. True only if every node passed.</summary>
    public static bool Validate(DeviceConfigNode node, List<string> errors, int depth = 0)
    {
        // Base case 1: deeper than any real facility hierarchy -> malformed tree.
        if (depth > MaxDepth)
        {
            errors.Add($"Configuration depth exceeded ({depth}) at node '{node.Name}'.");
            return false;
        }

        bool isValid = true;

        if (string.IsNullOrWhiteSpace(node.Name))
        {
            errors.Add($"Empty node name found at depth {depth}.");
            isValid = false;
        }
        else if (node.Name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != ' '))
        {
            errors.Add($"Node '{node.Name}' at depth {depth} contains invalid characters.");
            isValid = false;
        }

        // Base case 2 (implicit): a leaf has no children, so the loop does not run.
        foreach (var child in node.Children)
        {
            bool childValid = Validate(child, errors, depth + 1);
            isValid = isValid && childValid;
        }

        return isValid;
    }
}
