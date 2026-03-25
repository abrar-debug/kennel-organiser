using System.Collections.Generic;

namespace kennel;

public class KennelDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Last saved desktop position. null = not yet positioned (use default offset).
    public double? Left { get; set; }
    public double? Top  { get; set; }

    // Paths are to copied shortcut files stored under AppData for portability.
    public List<ShortcutDefinition> Shortcuts { get; set; } = new();
}

public class ShortcutDefinition
{
    public string DisplayName { get; set; } = string.Empty;

    // For .lnk: path to the copied file stored by this app.
    // For folders: same as OriginalPath (no copy made).
    public string ShortcutPath { get; set; } = string.Empty;

    // Original path the user dropped, used for de-duplication within the kennel.
    public string OriginalPath { get; set; } = string.Empty;

    public bool IsFolder { get; set; }
}

