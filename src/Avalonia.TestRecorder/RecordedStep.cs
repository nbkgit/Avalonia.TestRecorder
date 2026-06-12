namespace Avalonia.TestRecorder;

/// <summary>
/// Represents a single recorded interaction step.
/// </summary>
public sealed class RecordedStep
{
    public StepType Type { get; init; }
    public string Selector { get; init; } = string.Empty;
    public string? Parameter { get; init; }
    public SelectorQuality Quality { get; init; } = SelectorQuality.High;
    public string? Warning { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

/// <summary>
/// Type of recorded step.
/// </summary>
public enum StepType
{
    Click,
    RightClick,
    DoubleClick,
    TypeText,
    KeyPress,
    Scroll,
    Hover,
    AssertText,
    AssertChecked,
    AssertVisible,
    AssertEnabled,
    SelectItem,
    AssertNotEnabled,
    AssertTrue
}

/// <summary>
/// Quality/stability of selector.
/// </summary>
public enum SelectorQuality
{
    High,    // AutomationId
    Medium,  // Name
    Low      // Tree path
}
