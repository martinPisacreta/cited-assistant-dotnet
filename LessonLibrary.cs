using System.Text.RegularExpressions;

namespace AskChefSample;

public sealed class LessonLibrary
{
    private static readonly HashSet<string> StopWords = new(
        ("a an the and or but of to in on at for from with without " +
         "i me my we our you your it its this that these those " +
         "is are was were be been do does did can could would " +
         "should will how what when where why which who please")
        .Split(' '), StringComparer.Ordinal);
    private readonly List<IndexedLesson> lessons = [];

    public LessonLibrary(string directory)
    {
        // Fixed paths only: a question cannot select a filesystem path.
        var definitions = new[]
        {
            (Id: "roasted-vegetables", Title: "Roasting vegetables"),
            (Id: "pasta-water", Title: "Finishing pasta with cooking water"),
            (Id: "vinaigrette", Title: "Making a simple vinaigrette")
        };
        foreach (var definition in definitions)
        {
            var text = File.ReadAllText(
                Path.Combine(directory, definition.Id + ".md")).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("A synthetic lesson is empty.");
            var passage = new LessonPassage(definition.Id, definition.Title,
                "content/" + definition.Id + ".md", text);
            lessons.Add(new IndexedLesson(passage,
                Words(definition.Title), Words(text)));
        }
    }

    public LessonPassage? Find(
        string question, IReadOnlyList<ChefTurn> history)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(history);
        var normalized = question.Trim().TrimEnd('?', '.', '!')
            .ToLowerInvariant();
        if (normalized is "can you repeat that lesson" or
            "please repeat that lesson")
        {
            // History selects evidence; it never becomes answer evidence.
            var previousSource = history.LastOrDefault()?.Source;
            return previousSource is null ? null : lessons
                .FirstOrDefault(item => string.Equals(
                    item.Passage.Source, previousSource,
                    StringComparison.Ordinal))?.Passage;
        }

        var words = Words(question);
        LessonPassage? best = null;
        var bestScore = 0;
        var tied = false;
        foreach (var lesson in lessons)
        {
            // Count distinct query words, so repetition cannot boost rank.
            var matches = words.Count(word =>
                lesson.Title.Contains(word) || lesson.Text.Contains(word));
            if (matches < 2) continue;
            // Title matches receive two points; body matches receive one.
            var score = words.Sum(word =>
                (lesson.Title.Contains(word) ? 2 : 0) +
                (lesson.Text.Contains(word) ? 1 : 0));
            if (score > bestScore)
            {
                best = lesson.Passage;
                bestScore = score;
                tied = false;
            }
            else if (score == bestScore) tied = true;
        }
        return tied ? null : best;
    }

    private static HashSet<string> Words(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
            text.ToLowerInvariant(), @"[a-z0-9]+"))
        {
            var word = match.Value;
            if (StopWords.Contains(word)) continue;
            // Small English suffix rules, not semantic or vector search.
            if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal))
                word = word[..^3];
            else if (word.Length > 5 && word.EndsWith("ed", StringComparison.Ordinal))
                word = word[..^2];
            else if (word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) &&
                !word.EndsWith("ss", StringComparison.Ordinal))
                word = word[..^1];
            result.Add(word);
        }
        return result;
    }

    private sealed record IndexedLesson(
        LessonPassage Passage, HashSet<string> Title, HashSet<string> Text);
}
