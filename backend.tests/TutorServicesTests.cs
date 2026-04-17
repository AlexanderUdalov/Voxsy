using System;
using Xunit;

public class TutorServicesTests
{
    [Fact]
    public void LearnerMemoryService_MergesSessionAggregates()
    {
        var memory = new LearnerMemoryService();
        var now = DateTimeOffset.UtcNow;

        memory.MergeSessionPool(
        [
            new ErrorAggregate
            {
                ErrorKey = "verb_tense_present_perfect",
                Category = "grammar",
                Hint = "Use present perfect for recent experiences.",
                Example = "I have visited London.",
                Count = 2,
                Severity = 3,
                LastSeenAt = now
            }
        ]);

        var focus = memory.GetFocusAreas();
        Assert.Single(focus);
        Assert.Equal("verb_tense_present_perfect", focus[0].ErrorKey);
        Assert.Equal(2, focus[0].CountTotal);
    }

    [Fact]
    public void PromptBuilder_FeedbackPrompt_ContainsSessionAggregates()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(
            ResponseType.Feedback,
            [],
            [
                new ErrorAggregate
                {
                    ErrorKey = "article_usage",
                    Category = "grammar",
                    Hint = "Use an article before singular countable nouns.",
                    Example = "I bought a book.",
                    Count = 3,
                    Severity = 2,
                    LastSeenAt = DateTimeOffset.UtcNow
                }
            ]);

        Assert.Contains("final feedback report", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("article", prompt, StringComparison.OrdinalIgnoreCase);
    }
}

