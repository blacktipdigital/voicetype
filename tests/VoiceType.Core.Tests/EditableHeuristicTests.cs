using VoiceType.Core.Targeting;

namespace VoiceType.Core.Tests;

public class EditableHeuristicTests
{
    [Fact]
    public void WritableValuePattern_IsEditable() =>
        Assert.True(EditableHeuristic.IsEditable(hasValuePattern: true, valueIsReadOnly: false, isEditControl: false));

    [Fact]
    public void ReadOnlyValuePattern_IsNotEditable_EvenForEditControls() =>
        Assert.False(EditableHeuristic.IsEditable(hasValuePattern: true, valueIsReadOnly: true, isEditControl: true));

    [Fact]
    public void PlainEditControlWithoutValuePattern_IsEditable() =>
        Assert.True(EditableHeuristic.IsEditable(hasValuePattern: false, valueIsReadOnly: false, isEditControl: true));

    [Fact]
    public void DocumentWithoutValuePattern_IsNotEditable()
    {
        // Read-only documents (PDF viewers, previews) expose TextPattern but
        // no ValuePattern; TextPattern is not proof of writability.
        Assert.False(EditableHeuristic.IsEditable(hasValuePattern: false, valueIsReadOnly: false, isEditControl: false));
    }
}
