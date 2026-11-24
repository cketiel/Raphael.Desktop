using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Raphael.Desktop.Services.Help;

/// <summary>A topic of the help as plain text, split by its headings.</summary>
public sealed record HelpText(string Id, string Title, IReadOnlyList<HelpTextSection> Sections);

public sealed record HelpTextSection(string Heading, string Text);

/// <summary>
/// RE-086: reads the help shipped with the application as text, for the live map's voice assistant.
/// The same pages people read: one source, never a second redaction. No patient data is in them.
/// </summary>
public static class HelpReader
{
    /// <summary>What one answer may carry back to the model: long answers cost and are cut by it anyway.</summary>
    public const int MaxChars = 6000;

    private static readonly Dictionary<string, HelpText?> Cache = new();

    public static HelpText? Read(string topicId, string language)
    {
        var key = language + "|" + topicId;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var known)) return known;
            return Cache[key] = Load(topicId, language);
        }
    }

    /// <summary>The ids under a folder of the help ("desktop/live-map"), as the bundle has them.</summary>
    public static IReadOnlyList<string> Topics(string folder, string language)
    {
        var root = Path.Combine(HelpService.BundleRoot, language, folder.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(root)
            ? Directory.GetFiles(root, "*.html").Select(f => folder + "/" + Path.GetFileNameWithoutExtension(f)).OrderBy(x => x).ToList()
            : Array.Empty<string>();
    }

    /// <summary>
    /// The sections that answer a question best, across topics: the words of the question counted in
    /// the heading (worth more) and in the text.
    /// </summary>
    public static IReadOnlyList<(HelpText Topic, HelpTextSection Section)> Search(string query, IEnumerable<string> topicIds, string language, int take = 3)
    {
        var words = Regex.Matches(query.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}").Select(m => m.Value).Where(w => !Common.Contains(w)).Distinct().ToList();
        if (words.Count == 0) return Array.Empty<(HelpText, HelpTextSection)>();

        return topicIds.Select(id => Read(id, language)).Where(t => t is not null)
            .SelectMany(t => t!.Sections.Select(s => (Topic: t, Section: s, Score: Score(t, s, words))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(take)
            .Select(x => (x.Topic, x.Section))
            .ToList();
    }

    public static string Cut(string text) => text.Length <= MaxChars ? text : text[..MaxChars] + "…";

    /// <summary>Words every question has, which would make every section match.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "que", "qué", "para", "sirve", "como", "cómo", "cual", "cuál", "cuando", "cuándo", "donde", "dónde", "los", "las", "una", "uno", "del", "con", "por", "este", "esta", "hace", "hacer", "puedo", "significa", "funciona",
        "the", "what", "how", "does", "mean", "for", "and", "this", "that", "with", "when", "where", "which", "can", "you", "use", "used", "work", "works"
    };

    private static int Score(HelpText topic, HelpTextSection s, List<string> words)
    {
        var title = topic.Title.ToLowerInvariant();
        var heading = s.Heading.ToLowerInvariant();
        var text = s.Text.ToLowerInvariant();
        // A section that meets more of the words beats one that repeats a single one.
        // and a short heading that meets the words says more than a long one.
        var headed = words.Count(w => heading.Contains(w));
        return headed * 8 + headed * 8 / Math.Max(1, heading.Count(c => c == ' ') + 1)
            + words.Sum(w => (title.Contains(w) ? 1 : 0) + Math.Min(2, Regex.Matches(text, Regex.Escape(w)).Count) + (text.Contains(w) ? 2 : 0));
    }

    private static HelpText? Load(string topicId, string language)
    {
        // The id comes from the model: it may only name a page of the bundle.
        if (!Regex.IsMatch(topicId, "^[a-z0-9_/-]+$")) return null;
        var path = Path.Combine(HelpService.BundleRoot, language, topicId.Replace('/', Path.DirectorySeparatorChar) + ".html");
        if (!File.Exists(path)) return null;

        var html = File.ReadAllText(path, Encoding.UTF8);
        var article = Regex.Match(html, "<article[^>]*>(.*?)</article>", RegexOptions.Singleline);
        var body = article.Success ? article.Groups[1].Value : Regex.Match(html, "<main[^>]*>(.*?)</main>", RegexOptions.Singleline).Groups[1].Value;
        if (body.Length == 0) return null;
        // The page furniture: breadcrumbs, badges and the # beside each heading.
        body = Regex.Replace(body, "<a[^>]*class=\"[^\"]*(anchor|permalink)[^\"]*\"[^>]*>.*?</a>", "", RegexOptions.Singleline);
        body = Regex.Replace(body, "<div class=\"rf-(meta|crumbs)\">.*?</div>", "", RegexOptions.Singleline);
        body = Regex.Replace(body, "<(script|style|nav|svg|button)[^>]*>.*?</\\1>", "", RegexOptions.Singleline);

        var title = Plain(Regex.Match(body, "<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline).Groups[1].Value);
        var sections = new List<HelpTextSection>();
        var parts = Regex.Split(body, "<h[23][^>]*>(.*?)</h[23]>", RegexOptions.Singleline);
        Add(sections, title, Regex.Replace(parts[0], "^.*?</h1>", "", RegexOptions.Singleline));
        for (var i = 1; i + 1 < parts.Length; i += 2) Add(sections, Plain(parts[i]), parts[i + 1]);
        return new HelpText(topicId, title, sections);
    }

    private static void Add(List<HelpTextSection> sections, string heading, string html)
    {
        var text = Plain(html);
        if (text.Length > 0) sections.Add(new HelpTextSection(heading.TrimEnd('#', ' '), text));
    }

    private static string Plain(string html)
    {
        var s = Regex.Replace(html, "</(td|th)>", " | ");
        s = Regex.Replace(s, "</(tr|p|li|h\\d|div)>|<br ?/?>", "\n");
        s = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", ""));
        s = Regex.Replace(s, "[ \\t]+", " ");
        return Regex.Replace(s, "\\s*\\n\\s*", "\n").Trim();
    }
}
