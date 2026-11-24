using SemanticCache.Api.Features.Chat;

namespace SemanticCache.Tests;

public sealed class ChatRequestTests
{
    private static readonly ChatRequest Valid = new("How many rows can I export?");

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Null(Valid.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_question_is_required(string message)
    {
        Assert.NotNull((Valid with { Message = message }).Validate());
    }

    [Fact]
    public void Long_questions_unknown_modes_tenants_and_sessions_are_rejected()
    {
        Assert.NotNull((Valid with { Message = new string('x', 2001) }).Validate());
        Assert.NotNull((Valid with { Mode = "other" }).Validate());
        Assert.NotNull((Valid with { TenantId = "other" }).Validate());
        Assert.NotNull((Valid with { SessionId = "not valid" }).Validate());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.3, true)]
    [InlineData(-0.01, false)]
    [InlineData(0.31, false)]
    [InlineData(double.NaN, false)]
    public void Threshold_stays_between_zero_and_point_three(double threshold, bool valid)
    {
        Assert.Equal(valid, (Valid with { Threshold = threshold }).Validate() is null);
    }
}
