namespace VoiceType.Core.Targeting;

/// <summary>
/// Element-level writability heuristic. TextPattern is deliberately not
/// accepted as proof: read-only documents expose it too. A ValuePattern's
/// IsReadOnly flag is authoritative when present; otherwise only a plain
/// Edit control counts.
/// </summary>
public static class EditableHeuristic
{
    public static bool IsEditable(bool hasValuePattern, bool valueIsReadOnly, bool isEditControl) =>
        hasValuePattern ? !valueIsReadOnly : isEditControl;
}
