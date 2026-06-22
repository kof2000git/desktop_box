using System.Collections.Generic;
using DesktopBox.Models;

namespace DesktopBox.Controls;

public static class FirstLetterNavigator
{
    public static int FindNextIndex(IReadOnlyList<BoxItem> items, char key, int currentIndex)
    {
        if (items.Count == 0)
            return -1;

        var target = char.ToUpperInvariant(key);
        if (!char.IsLetterOrDigit(target))
            return -1;

        var start = currentIndex >= 0 && currentIndex < items.Count ? currentIndex + 1 : 0;
        for (var offset = 0; offset < items.Count; offset++)
        {
            var index = (start + offset) % items.Count;
            if (StartsWith(items[index], target))
                return index;
        }

        return -1;
    }

    private static bool StartsWith(BoxItem item, char target)
    {
        var name = item.DisplayName?.TrimStart();
        if (string.IsNullOrEmpty(name))
            return false;

        return char.ToUpperInvariant(name[0]) == target;
    }
}
