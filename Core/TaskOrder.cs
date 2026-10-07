namespace IdleDash.Core;

/// <summary>Volgorde van taken: wat het eerst af moet bovenaan.</summary>
public static class TaskOrder
{
    /// <summary>
    /// Te laat eerst, dan vandaag, morgen enzovoort; taken zonder datum onderaan (in hun oude volgorde).
    /// Subtaken blijven onder hun hoofdtaak. Een hoofdtaak telt met de vroegste datum van zichzelf en zijn subtaken.
    /// </summary>
    public static List<T> ByDue<T>(IReadOnlyList<T> items, Func<T, DateTime?> due, Func<T, bool> isSubtask)
    {
        var groups = new List<List<T>>();
        foreach (var item in items)
        {
            if (isSubtask(item) && groups.Count > 0) groups[^1].Add(item);
            else groups.Add(new List<T> { item });
        }
        return groups
            .Select((group, index) => (Group: group, Index: index, Key: group.Select(due).Where(d => d != null).Min()))
            .OrderBy(x => x.Key == null)
            .ThenBy(x => x.Key)
            .ThenBy(x => x.Index)
            .SelectMany(x => x.Group.Take(1).Concat(x.Group.Skip(1).OrderBy(s => due(s) == null).ThenBy(s => due(s))))
            .ToList();
    }
}
