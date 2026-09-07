using System;
using System.Collections.Generic;

namespace SimpleGit11.Presentation.Navigation;

internal static class NavigationSelectionResolver
{
    public static Type? ResolveTopLevelPage(
        Type currentPageType,
        IEnumerable<Type> backStackPageTypes,
        IReadOnlySet<Type> topLevelPageTypes)
    {
        ArgumentNullException.ThrowIfNull(currentPageType);
        ArgumentNullException.ThrowIfNull(backStackPageTypes);
        ArgumentNullException.ThrowIfNull(topLevelPageTypes);

        if (topLevelPageTypes.Contains(currentPageType))
        {
            return currentPageType;
        }

        Type? owningPageType = null;
        foreach (Type pageType in backStackPageTypes)
        {
            if (topLevelPageTypes.Contains(pageType))
            {
                owningPageType = pageType;
            }
        }

        return owningPageType;
    }
}
