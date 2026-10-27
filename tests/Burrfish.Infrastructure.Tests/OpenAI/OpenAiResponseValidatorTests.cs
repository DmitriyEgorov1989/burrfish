using Burrfish.Application.OpenAI;
using Burrfish.Infrastructure.OpenAI;

namespace Burrfish.Infrastructure.Tests.OpenAI;

public sealed class OpenAiResponseValidatorTests
{
    [Theory]
    [InlineData("price", AiDialogDecisionKind.Price)]
    [InlineData("services", AiDialogDecisionKind.Services)]
    public void Template_intent_requires_null_text(
        string kind,
        AiDialogDecisionKind expectedKind)
    {
        var validator = new OpenAiResponseValidator();

        var result = validator.Validate($$"""
            { "kind": "{{kind}}", "text": null }
            """);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedKind, result.Decision!.Kind);
        Assert.Null(result.Decision.Text);
    }

    [Theory]
    [InlineData("ask_clarifying_question", AiDialogDecisionKind.AskClarifyingQuestion)]
    [InlineData("propose_handoff", AiDialogDecisionKind.ProposeHandoff)]
    public void Generated_text_intent_requires_non_empty_text(
        string kind,
        AiDialogDecisionKind expectedKind)
    {
        var validator = new OpenAiResponseValidator();

        var result = validator.Validate($$"""
            { "kind": "{{kind}}", "text": "Опишите задачу подробнее." }
            """);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedKind, result.Decision!.Kind);
        Assert.Equal("Опишите задачу подробнее.", result.Decision.Text);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("{ \"kind\": \"unknown\", \"text\": null }")]
    [InlineData("{ \"kind\": \"price\", \"text\": \"Выдуманная цена\" }")]
    [InlineData("{ \"kind\": \"ask_clarifying_question\", \"text\": null }")]
    [InlineData("{ \"kind\": \"ask_clarifying_question\", \"text\": \"  \" }")]
    [InlineData("{ \"kind\": \"price\", \"text\": null, \"extra\": true }")]
    [InlineData("{ \"kind\": \"price\", \"kind\": \"services\", \"text\": null }")]
    public void Invalid_decision_returns_expected_failure(string json)
    {
        var validator = new OpenAiResponseValidator();

        var result = validator.Validate(json);

        Assert.False(result.Succeeded);
        Assert.Null(result.Decision);
        Assert.Equal("InvalidDecision", result.Error!.Code);
        Assert.False(result.Error.IsTransient);
    }

    [Fact]
    public void Generated_text_over_limit_returns_failure()
    {
        var validator = new OpenAiResponseValidator();
        var text = new string('a', 1_001);

        var result = validator.Validate($$"""
            { "kind": "propose_handoff", "text": "{{text}}" }
            """);

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidDecision", result.Error!.Code);
    }
}
