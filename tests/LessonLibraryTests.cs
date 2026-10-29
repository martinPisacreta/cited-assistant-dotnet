using AskChefSample;
using Xunit;

namespace AskChefTests;

public sealed class LessonLibraryTests
{
    private static LessonLibrary Create() => new(
        Path.Combine(AppContext.BaseDirectory, "content"));

    [Theory]
    [InlineData("How long should I roast vegetables?", "roasted-vegetables")]
    [InlineData("Can you explain roasting vegetables?", "roasted-vegetables")]
    [InlineData("Explain finishing pasta with cooking water.", "pasta-water")]
    [InlineData("What ratio of oil and vinegar makes vinaigrette?", "vinaigrette")]
    public void Paraphrases_find_the_expected_local_source(
        string question, string lesson)
    {
        var passage = Create().Find(question, Array.Empty<ChefTurn>());
        Assert.NotNull(passage);
        Assert.Equal("content/" + lesson + ".md", passage!.Source);
        Assert.Equal(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "content", lesson + ".md")).Trim(),
            passage.Text);
    }

    [Theory]
    [InlineData("Who won the election?")]
    [InlineData("How do I reset my laptop password?")]
    [InlineData("Explain mortgage interest rates.")]
    [InlineData("vegetables vegetables vegetables")]
    [InlineData("How should I do that?")]
    [InlineData("")]
    public void Unsupported_or_insufficient_keywords_return_no_passage(
        string question)
    {
        Assert.Null(Create().Find(question, Array.Empty<ChefTurn>()));
    }

    [Fact]
    public void Equal_best_scores_reject_an_ambiguous_question()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "ask-chef-lexical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Neither query word appears in any fixed lesson title.
            File.WriteAllText(Path.Combine(directory, "roasted-vegetables.md"),
                "Saffron cinnamon.");
            File.WriteAllText(Path.Combine(directory, "pasta-water.md"),
                "Saffron cinnamon.");
            File.WriteAllText(Path.Combine(directory, "vinaigrette.md"),
                "Unrelated material.");
            var library = new LessonLibrary(directory);
            Assert.Null(library.Find("Explain saffron cinnamon.",
                Array.Empty<ChefTurn>()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
