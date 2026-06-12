using Avalonia.TestRecorder.Assertions;
using Microsoft.Extensions.Logging;

namespace Avalonia.TestRecorder;

/// <summary>
/// Configuration options for the test recorder.
/// </summary>
public sealed class RecorderOptions
{
    /// <summary>
    /// Gets or sets the output directory for generated test files.
    /// If null, uses default location based on build configuration.
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// Gets or sets the scenario name used in test file naming.
    /// Default is "Scenario".
    /// </summary>
    public string ScenarioName { get; init; } = "Scenario";

    /// <summary>
    /// Gets or sets the hotkey configuration.
    /// </summary>
    public RecorderHotkeys Hotkeys { get; init; } = RecorderHotkeys.Default;

    /// <summary>
    /// Gets or sets the selector resolution options.
    /// </summary>
    public SelectorOptions Selector { get; init; } = new();

    /// <summary>
    /// Gets or sets the code generation options.
    /// </summary>
    public CodegenOptions Codegen { get; init; } = new();

    /// <summary>
    /// Gets the list of assertion value extractors.
    /// </summary>
    public IList<IAssertValueExtractor> AssertExtractors { get; } = [];

    /// <summary>
    /// Gets or sets whether to show the overlay panel.
    /// Default is true.
    /// </summary>
    public bool ShowOverlay { get; init; } = true;

    /// <summary>
    /// Gets or sets the overlay theme.
    /// Null means auto-detect from application theme.
    /// </summary>
    public OverlayTheme? OverlayTheme { get; init; }

    /// <summary>
    /// Gets or sets the logger for diagnostic output.
    /// </summary>
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Hotkey configuration for recorder control.
/// </summary>
public sealed class RecorderHotkeys
{
    /// <summary>
    /// Gets the default hotkey configuration.
    /// </summary>
    public static RecorderHotkeys Default { get; } = new();

    /// <summary>
    /// Gets or sets the hotkey for starting/stopping recording (Ctrl+Shift+R).
    /// </summary>
    public string? StartStop { get; init; } = "Ctrl+Shift+R";

    /// <summary>
    /// Gets or sets the hotkey for pausing/resuming recording (Ctrl+Shift+P).
    /// </summary>
    public string? PauseResume { get; init; } = "Ctrl+Shift+P";

    /// <summary>
    /// Gets or sets the hotkey for saving test to file (Ctrl+Shift+S).
    /// </summary>
    public string? Save { get; init; } = "Ctrl+Shift+S";

    /// <summary>
    /// Gets or sets the hotkey for capturing assertion (Ctrl+Shift+A).
    /// </summary>
    public string? CaptureAssert { get; init; } = "Ctrl+Shift+A";
}

/// <summary>
/// Options for element selector resolution.
/// </summary>
public sealed class SelectorOptions
{
    /// <summary>
    /// Gets or sets whether to prefer Name property before tree path fallback.
    /// </summary>
    public bool PreferName { get; init; } = false;

    /// <summary>
    /// Gets or sets whether to allow tree path fallback when AutomationId/Name not found.
    /// </summary>
    public bool AllowTreePath { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate warning comments for fallback selectors.
    /// </summary>
    public bool WarnOnFallback { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to log coordinate information (for diagnostics).
    /// </summary>
    public bool CoordinateLogging { get; init; } = false;
}

/// <summary>
/// Options for test code generation.
/// </summary>
public sealed class CodegenOptions
{
    /// <summary>
    /// Gets or sets the target test framework.
    /// </summary>
    public TestFramework TestFramework { get; init; } = TestFramework.XUnit;

    /// <summary>
    /// Gets or sets the namespace for generated test code.
    /// If null, auto-detected from application name.
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Gets or sets whether to include timestamp in method name.
    /// </summary>
    public bool IncludeTimestamp { get; init; } = true;
}

/// <summary>
/// Supported test frameworks.
/// </summary>
public enum TestFramework
{
    /// <summary>
    /// Xunit is one selectale Test Framework.
    /// </summary>
    XUnit,
    /// <summary>
    /// XNUunit is one selectale Test Framework.
    /// </summary>
    NUnit
}

/// <summary>
/// Overlay panel theme options.
/// </summary>
public enum OverlayTheme
{
    /// <summary>
    /// Light theme with bright background.
    /// </summary>
    Light,
    
    /// <summary>
    /// Dark theme with dark background.
    /// </summary>
    Dark
}
