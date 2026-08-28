using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Raphael.Desktop.Helpers;

/// <summary>
/// Declares which help topic a piece of the interface answers for.
/// </summary>
/// <remarks>
/// Same shape as MaterialDesign's own attached helpers, so it reads like the rest of the XAML:
///
/// <code>
/// &lt;Window ... helpers:HelpAssist.TopicId="desktop/notifications/alert-preferences"&gt;
/// </code>
///
/// <para>
/// F1 resolves the <b>deepest</b> declaration above whatever has focus. A dialog opened over the
/// Dispatch tab opens the dialog's topic, not Dispatch's. Without that, the alert preferences
/// window — which is exactly where a dispatcher gets lost — could only ever open the generic help
/// for the tab behind it.
/// </para>
/// </remarks>
public static class HelpAssist
{
    public static readonly DependencyProperty TopicIdProperty =
        DependencyProperty.RegisterAttached(
            "TopicId",
            typeof(string),
            typeof(HelpAssist),
            new FrameworkPropertyMetadata(null));

    public static void SetTopicId(DependencyObject element, string value) =>
        element.SetValue(TopicIdProperty, value);

    public static string GetTopicId(DependencyObject element) =>
        (string)element.GetValue(TopicIdProperty);

    /// <summary>
    /// Walks up from the focused element looking for the first declared topic.
    /// </summary>
    /// <returns>The topic id, or <c>null</c> when nothing on the way up declares one.</returns>
    public static string ResolveFromFocus() =>
        ResolveUpwards(Keyboard.FocusedElement as DependencyObject);

    /// <summary>
    /// Walks up from <paramref name="start"/> looking for the first declared topic.
    /// </summary>
    /// <remarks>
    /// Climbs the visual tree and falls back to the logical one. The fallback is not decoration:
    /// the contents of a Popup or a ContextMenu live in their own visual tree, so a visual-only
    /// walk stops at the popup root and loses the window that declared the topic.
    /// </remarks>
    public static string ResolveUpwards(DependencyObject start)
    {
        var current = start;

        while (current is not null)
        {
            var topic = GetTopicId(current);
            if (!string.IsNullOrWhiteSpace(topic))
                return topic;

            current = GetParent(current);
        }

        return null;
    }

    /// <summary>
    /// Looks *down* from <paramref name="root"/> for the nearest declared topic.
    /// </summary>
    /// <remarks>
    /// Walking up is not enough on its own. A tab's content is a plain Grid that holds the view,
    /// and the view is the thing that declares the topic — a child, not an ancestor. Worse, the
    /// keyboard focus is often nowhere near it: clicking the bell leaves focus on a button up in
    /// the title bar, outside every view, and an upward walk from there finds nothing at all.
    ///
    /// <para>
    /// Breadth-first on purpose: the shallowest declaration wins, which is the view root rather
    /// than some nested control that happens to declare a finer topic of its own.
    /// </para>
    /// </remarks>
    public static string ResolveInSubtree(DependencyObject root)
    {
        if (root is null)
            return null;

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        // A tab's content is shallow; the cap is only there so a pathological tree cannot turn
        // pressing F1 into a visible pause.
        var visited = 0;

        while (queue.Count > 0 && visited++ < 2000)
        {
            var current = queue.Dequeue();

            var topic = GetTopicId(current);
            if (!string.IsNullOrWhiteSpace(topic))
                return topic;

            if (current is not Visual and not System.Windows.Media.Media3D.Visual3D)
                continue;

            var children = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < children; index++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, index));
        }

        return null;
    }

    private static DependencyObject GetParent(DependencyObject element)
    {
        if (element is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(element);
            if (visualParent is not null)
                return visualParent;
        }

        return LogicalTreeHelper.GetParent(element);
    }
}
