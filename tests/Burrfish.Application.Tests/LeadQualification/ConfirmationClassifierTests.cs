using Burrfish.Application.LeadQualification;

namespace Burrfish.Application.Tests.LeadQualification;

public sealed class ConfirmationClassifierTests
{
    [Theory]
    [InlineData(" Да! ", ConfirmationKind.Confirmed)]
    [InlineData("согласна", ConfirmationKind.Confirmed)]
    [InlineData("да, передайте", ConfirmationKind.Confirmed)]
    [InlineData("НЕТ.", ConfirmationKind.Declined)]
    [InlineData("не передавайте", ConfirmationKind.Declined)]
    [InlineData("нет, спасибо", ConfirmationKind.Declined)]
    [InlineData("да, но пока не передавайте", ConfirmationKind.Ambiguous)]
    [InlineData("давайте позже", ConfirmationKind.Ambiguous)]
    [InlineData("наверное", ConfirmationKind.Ambiguous)]
    [InlineData("да нет", ConfirmationKind.Ambiguous)]
    [InlineData("", ConfirmationKind.Ambiguous)]
    [InlineData(null, ConfirmationKind.Ambiguous)]
    public void Only_explicit_complete_answers_are_classified(string? text, ConfirmationKind expected)
    {
        Assert.Equal(expected, ConfirmationClassifier.Classify(text));
    }
}
