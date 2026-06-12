using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.TestRecorder.Assertions;
using Avalonia.TestRecorder.CodeGen;
using Avalonia.TestRecorder.Selectors;
using Avalonia.VisualTree;
using Avalonia.Automation;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;

namespace Avalonia.TestRecorder;

/// <summary>
/// Implementation of recorder session.
/// </summary>
public sealed class RecorderSession : IRecorderSession
{
    private readonly Window _window;
    private readonly RecorderOptions _options;
    private readonly SelectorResolver _selectorResolver;
    private readonly TestCodeGenerator _codeGenerator;
    private readonly List<RecordedStep> _steps = [];
    private readonly List<IAssertValueExtractor> _extractors;
    private readonly ILogger? _logger;
    private readonly StepValidator _stepValidator; // Added validator
    private RecorderState _state = RecorderState.Off;
    private readonly System.Timers.Timer? _textInputTimer;
    private Control? _lastTextControl;
    private string _accumulatedText = string.Empty;
    private Point _lastPointerPosition;
    private Control? _lastHoveredControl;
    
    // Callback for showing save dialog
    private Func<Task<string?>>? _showSaveDialogCallback;
    
    // Callbacks for overlay interaction
    private Action? _onClearCallback;
    private Action? _onMinimizeRestoreCallback;

    /// <inheritdoc/>
    public RecorderState State => _state;

    /// <summary>
    /// Gets the current step count.
    /// </summary>
    public int GetStepCount() => _steps.Count;
    /// <summary>
    /// Initialisiert eine neue Instanz der <see cref="RecorderSession"/>-Klasse für ein bestimmtes Anwendungsfenster.
    /// </summary>
    /// <remarks>
    /// Diese Methode bereitet die Aufzeichnungssitzung vor, indem sie:
    /// <list type="bullet">
    /// <item><description>Logger, Selektor-Resolver und Schritt-Validatoren einrichtet.</description></item>
    /// <item><description>Den <see cref="TestCodeGenerator"/> mit dem ermittelten Anwendungsnamen instanziiert.</description></item>
    /// <item><description>Eingebaute sowie benutzerdefinierte Wert-Extraktoren (<see cref="IAssertValueExtractor"/>) registriert.</description></item>
    /// <item><description>Einen Timer (500ms Debounce) für die Zusammenfassung aufeinanderfolgender Texteingaben konfiguriert.</description></item>
    /// <item><description>Die globalen Event-Handler an das Zielfenster bindet.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="window">Das zu überwachende Avalonia-Fenster, auf dem die Benutzerinteraktionen aufgezeichnet werden.</param>
    /// <param name="options">Die Konfigurationsoptionen für den Recorder, einschließlich Logging und Codegenerierung.</param>
    public RecorderSession(Window window, RecorderOptions options)
    {
        _window = window;
        _options = options;
        _logger = options.Logger;
        _selectorResolver = new SelectorResolver(options.Selector, _logger, window); // Pass window for validation
        _stepValidator = new StepValidator(window, _logger); // Initialize validator
        
        var appName = Assembly.GetEntryAssembly()?.GetName().Name ?? "App";
        _codeGenerator = new TestCodeGenerator(options.Codegen, appName);

        // Initialize extractors
        _extractors = [.. BuiltInExtractors.GetDefault(), .. options.AssertExtractors];

        // Text input coalescing timer
        _textInputTimer = new System.Timers.Timer(500); // 500ms debounce
        _textInputTimer.Elapsed += (s, e) => FlushTextInput();
        _textInputTimer.AutoReset = false;

        AttachEventHandlers();
        _logger?.LogInformation("RecorderSession initialized for window: {Window}", window.Title);
    }
    /// <summary>
    /// Startet die Aufzeichnung der Sitzung, sofern diese sich im inaktiven Zustand befindet.
    /// </summary>
    /// <remarks>
    /// Wenn der aktuelle Zustand <see cref="RecorderState.Off"/> ist, wird der Status auf <see cref="RecorderState.Recording"/> gesetzt und der Start im Logger vermerkt. Bereits laufende oder pausierte Aufzeichnungen werden ignoriert.
    /// </remarks>
    public void Start()
    {
        if (_state == RecorderState.Off)
        {
            _state = RecorderState.Recording;
            _logger?.LogInformation("Recording started");
        }
    }
    /// <summary>
    /// Stoppt die aktuelle Aufzeichnungssitzung und versetzt sie in den inaktiven Zustand.
    /// </summary>
    /// <remarks>
    /// Wenn die Aufzeichnung aktiv oder pausiert ist, werden ausstehende Texteingaben über <see cref="FlushTextInput"/> verarbeitet, der Status auf <see cref="RecorderState.Off"/> zurückgesetzt und das Ende im Logger vermerkt.
    /// </remarks>
    public void Stop()
    {
        if (_state != RecorderState.Off)
        {
            FlushTextInput();
            _state = RecorderState.Off;
            _logger?.LogInformation("Recording stopped");
        }
    }

    /// <summary>
    /// Clears all recorded steps.
    /// </summary>
    public void ClearSteps()
    {
        FlushTextInput();
        _steps.Clear();
        _logger?.LogInformation("Steps cleared");
    }

    /// <summary>
    /// Saves the recorded test steps to a file.
    /// </summary>
    /// <returns></returns>
    public string SaveTestToFile()
    {
        var outputDir = _options.OutputDirectory 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RecordedTests");
        Directory.CreateDirectory(outputDir);

        var fileName = GetSuggestedFileName();
        var filePath = Path.Combine(outputDir, fileName);

        return SaveTestToFile(filePath);
    }

    /// <summary>
    /// Saves the test code to the specified file path.
    /// </summary>
    public string SaveTestToFile(string filePath)
    {
        FlushTextInput();
        var code = ExportTestCode();
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, code);
        _logger?.LogInformation("Test saved to: {FilePath}", filePath);

        return filePath;
    }

    /// <summary>
    /// Gets the suggested file name for the test.
    /// </summary>
    public string GetSuggestedFileName()
    {
        var appName = Assembly.GetEntryAssembly()?.GetName().Name ?? "App";
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{appName}.{_options.ScenarioName}.{timestamp}.g.cs";
    }
    /// <summary>
    /// Verarbeitet verbleibende Eingaben und exportiert die aufgezeichneten Schritte als fertigen C#-Testcode.
    /// </summary>
    /// <remarks>
    /// Die Methode stellt durch den Aufruf von <see cref="FlushTextInput"/> sicher, dass alle gepufferten Texteingaben vor dem Export verarbeitet werden. Anschließend generiert der interne Codegenerator die vollständige Testklasse.
    /// </remarks>
    /// <returns>Der fertig generierte C#-Testcode als Zeichenkette (String).</returns>
    public string ExportTestCode()
    {
        FlushTextInput();
        return _codeGenerator.Generate(_steps, _options.ScenarioName, _window);
    }
    /// <summary>
    /// Gibt alle von der <see cref="RecorderSession"/> verwendeten Ressourcen frei und meldet die Ereignishandler ab.
    /// </summary>
    /// <remarks>
    /// Diese Methode entfernt die Event-Handler vom überwachten Fenster, gibt den Timer für das Texteingabe-Debouncing frei und protokolliert den Abschluss der Sitzung im Logger.
    /// </remarks>
    public void Dispose()
    {
        DetachEventHandlers();
        _textInputTimer?.Dispose();
        _logger?.LogInformation("RecorderSession disposed");
    }

    private void AttachEventHandlers()
    {
        _window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        // Use Bubble strategy to ensure keyboard shortcuts work even when controls like TextBox have focus
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        
        // Subscribe to window loaded event to monitor for ComboBox and ListBox controls
        _window.Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        _logger?.LogDebug("Window loaded event fired");
        if (_window.Content is Control content)
        {
            _logger?.LogDebug("Window content found, subscribing to selection events");
            SubscribeToSelectionEvents(content);
        }
        else
        {
            _logger?.LogDebug("No window content found");
        }
    }

    private void DetachEventHandlers()
    {
        _window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _window.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _window.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _window.Loaded -= OnWindowLoaded;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_state != RecorderState.Recording)
            return;

        FlushTextInput(); // Flush any pending text before recording click

        var control = e.Source as Control;
        if (control == null)
            return;

        // Update pointer position and hovered control
        _lastPointerPosition = e.GetPosition(_window);
        _lastHoveredControl = control;

        var (selector, quality, warning) = _selectorResolver.Resolve(control);
        
        var stepType = e.GetCurrentPoint(_window).Properties.IsRightButtonPressed
            ? StepType.RightClick
            : StepType.Click;

        var step = new RecordedStep
        {
            Type = stepType,
            Selector = selector,
            Quality = quality,
            Warning = warning
        };

        // Validate the step and try fallbacks if needed
        ValidateAndAddStep(step, control);
        
        _logger?.LogDebug("Recorded {StepType}: {Selector}", stepType, selector);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_state != RecorderState.Recording)
            return;

        // Update last known pointer position and control under pointer
        _lastPointerPosition = e.GetPosition(_window);
        
        // Store the control under pointer for assertion capture
        if (e.Source is Control control)
        {
            _lastHoveredControl = control;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_state != RecorderState.Recording || string.IsNullOrEmpty(e.Text))
            return;

        var control = e.Source as Control;
        if (control == null)
            return;

        // Accumulate text input for same control
        if (_lastTextControl != control)
        {
            FlushTextInput();
            _lastTextControl = control;
        }

        _accumulatedText += e.Text;
        _textInputTimer?.Stop();
        _textInputTimer?.Start(); // Reset timer
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle global hotkeys regardless of recording state
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.R: // Start/Stop
                    if (_state == RecorderState.Off)
                        Start();
                    else if (_state == RecorderState.Recording)
                        Stop();
                    e.Handled = true;
                    return;

                case Key.S: // Save
                    // Instead of directly saving, show the save dialog
                    _ = Task.Run(async () =>
                    {
                        var path = await SaveTestToFileWithDialog();
                        if (path != null)
                        {
                            Debug.WriteLine($"Test saved to: {path}");
                        }
                    });
                    e.Handled = true;
                    return;

                case Key.A: // Capture Assert (auto-detect)
                    CaptureAssert();
                    e.Handled = true;
                    return;

                case Key.T: // Assert Text
                    CaptureSpecificAssertion(StepType.AssertText);
                    e.Handled = true;
                    return;

                case Key.V: // Assert Visible
                    CaptureSpecificAssertion(StepType.AssertVisible);
                    e.Handled = true;
                    return;

                case Key.E: // Assert Enabled
                    CaptureSpecificAssertion(StepType.AssertEnabled);
                    e.Handled = true;
                    return;

                case Key.K: // Assert Checked
                    CaptureSpecificAssertion(StepType.AssertChecked);
                    e.Handled = true;
                    return;

                case Key.C: // Clear Steps
                    ClearSteps();
                    _onClearCallback?.Invoke();
                    e.Handled = true;
                    return;

                case Key.M: // Minimize/Restore Overlay
                    _onMinimizeRestoreCallback?.Invoke();
                    e.Handled = true;
                    return;
            }
        }

        // Only handle other keys when recording
        if (_state != RecorderState.Recording)
            return;

        // Record special keys (Enter, Tab, etc.)
        if (e.Key == Key.Enter || e.Key == Key.Tab || e.Key == Key.Escape)
        {
            FlushTextInput();
            
            var step = new RecordedStep
            {
                Type = StepType.KeyPress,
                Selector = "",
                Parameter = e.Key.ToString()
            };
            _steps.Add(step);
            _logger?.LogDebug("Recorded KeyPress: {Key}", e.Key);
        }
    }

    private void FlushTextInput()
    {
        if (_lastTextControl != null && !string.IsNullOrEmpty(_accumulatedText))
        {
            var (selector, quality, warning) = _selectorResolver.Resolve(_lastTextControl);
            
            var step = new RecordedStep
            {
                Type = StepType.TypeText,
                Selector = selector,
                Parameter = _accumulatedText,
                Quality = quality,
                Warning = warning
            };

            // Validate the step and try fallbacks if needed
            ValidateAndAddStep(step, _lastTextControl);
            
            _logger?.LogDebug("Recorded TypeText: {Selector} = {Text}", selector, _accumulatedText);

            _accumulatedText = string.Empty;
            _lastTextControl = null;
        }
    }

    private void CaptureAssert()
    {
        // Try to get control under mouse or focused control
        var control = GetTargetControl();
        if (control == null)
        {
            _logger?.LogWarning("No control found for assertion capture");
            return;
        }

        var (selector, quality, warning) = _selectorResolver.Resolve(control);
        var uiValidator = new ValidationUi(_window);
        var foundControl = uiValidator.FindControlPublic(selector);

        // Try extractors
        foreach (var extractor in _extractors)
        {
            if (extractor.TryExtract(foundControl, out var step) && step != null)
            {
                // Create new step with updated selector info
                var updatedStep = new RecordedStep
                {
                    Type = step.Type,
                    Selector = selector,
                    Parameter = step.Parameter,
                    Quality = quality,
                    Warning = warning
                };
                
                // Validate the step and try fallbacks if needed
                ValidateAndAddStep(updatedStep, control);
                
                _logger?.LogInformation("Captured assertion: {Type} on {Selector}", updatedStep.Type, selector);
                return;
            }
        }

        _logger?.LogWarning("No assertion extractor matched control type: {Type}", control.GetType().Name);
    }

    /// <summary>
    /// Captures a specific type of assertion on the hovered control.
    /// </summary>
    /// <param name="assertionType">The type of assertion to capture.</param>
    private void CaptureSpecificAssertion(StepType assertionType)
    {
        // Try to get control under mouse or focused control
        var control = GetTargetControl();
        if (control == null)
        {
            _logger?.LogWarning("No control found for specific assertion capture");
            return;
        }

        var (selector, quality, warning) = _selectorResolver.Resolve(control);
        
        string? parameter = null;
        
        switch (assertionType)
        {
            case StepType.AssertText:
                // Extract text from the control
                parameter = ExtractTextFromControl(control);
                break;
            case StepType.AssertChecked:
                // Extract checked state
                parameter = ExtractCheckedState(control)?.ToString().ToLowerInvariant() ?? "false";
                break;
            case StepType.AssertVisible:
            case StepType.AssertEnabled:
                // No parameter needed
                break;
            default:
                _logger?.LogWarning("Unsupported assertion type: {Type}", assertionType);
                return;
        }

        var step = new RecordedStep
        {
            Type = assertionType,
            Selector = selector,
            Parameter = parameter,
            Quality = quality,
            Warning = warning
        };

        ValidateAndAddStep(step, control);
        _logger?.LogInformation("Captured specific assertion: {Type} on {Selector}", assertionType, selector);
    }

    /// <summary>
    /// Extracts text from a control.
    /// </summary>
    private static  string? ExtractTextFromControl(Control control)
    {
        return control switch
        {
            TextBlock tb => tb.Text,
            TextBox tx => tx.Text,
            Button btn => btn.Content?.ToString(),
            ContentControl cc => cc.Content?.ToString(),
            _ => null
        };
    }

    /// <summary>
    /// Extracts the checked state from a control.
    /// </summary>
    private static  bool? ExtractCheckedState(Control control)
    {
        return control switch
        {
            CheckBox cb => cb.IsChecked,
            RadioButton rb => rb.IsChecked,
            ToggleButton tb => tb.IsChecked,
            _ => null
        };
    }

    private Control? GetTargetControl()
    {
        // Priority 1: Control under mouse pointer (most recent hover)
        if (_lastHoveredControl != null)
        {
            _logger?.LogDebug("Using hovered control for assertion: {Type}", _lastHoveredControl.GetType().Name);
            return _lastHoveredControl;
        }

        // Priority 2: Try to find control at last pointer position using hit testing
        var controlAtPointer = FindControlAtPosition(_lastPointerPosition);
        if (controlAtPointer != null)
        {
            _logger?.LogDebug("Found control at pointer position: {Type}", controlAtPointer.GetType().Name);
            return controlAtPointer;
        }

        // Priority 3: Focused control as fallback
        var focused = TopLevel.GetTopLevel(_window)?.FocusManager?.GetFocusedElement() as Control;
        if (focused != null)
        {
            _logger?.LogDebug("Using focused control for assertion: {Type}", focused.GetType().Name);
            return focused;
        }

        _logger?.LogWarning("No target control found for assertion capture");
        return null;
    }

    private Control? FindControlAtPosition(Point position)
    {
        try
        {
            // Perform hit testing to find control at the given position
            var hitTestResult = _window.InputHitTest(position);
            
            if (hitTestResult is Control control)
            {
                return control;
            }
            
            // If hit test returns a visual that's not a control, walk up to find the parent control
            if (hitTestResult is Visual visual)
            {
                var parent = visual.GetVisualParent();
                while (parent != null)
                {
                    if (parent is Control parentControl)
                        return parentControl;
                    parent = parent.GetVisualParent();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error finding control at position {Position}", position);
        }
        
        return null;
    }

    /// <summary>
    /// Sets a callback function for showing the save file dialog.
    /// This is used to enable the keyboard shortcut to show the save dialog.
    /// </summary>
    /// <param name="callback">Function that shows the save dialog and returns the selected file path, or null if cancelled.</param>
    public void SetSaveDialogCallback(Func<Task<string?>> callback)
    {
        _showSaveDialogCallback = callback;
    }

    /// <summary>
    /// Sets a callback for when Clear is triggered via keyboard shortcut.
    /// </summary>
    public void SetClearCallback(Action callback)
    {
        _onClearCallback = callback;
    }

    /// <summary>
    /// Sets a callback for when Minimize/Restore is triggered via keyboard shortcut.
    /// </summary>
    public void SetMinimizeRestoreCallback(Action callback)
    {
        _onMinimizeRestoreCallback = callback;
    }
    /// <summary>
    /// Generiert eine Code-Vorschau für einen einzelnen, spezifischen Aufzeichnungsschritt.
    /// </summary>
    /// <remarks>
    /// Diese Methode delegiert den Aufruf an den internen Codegenerator, um die Repräsentation eines einzelnen Interaktionsschritts als ausführbaren C#-Code zu erzeugen, ohne das gesamte Test-Template zu rendern.
    /// </remarks>
    /// <param name="step">Der aufgezeichnete Schritt, für den die Code-Vorschau generiert werden soll.</param>
    /// <returns>Die C#-Code-Vorschau des Einzelschritts als Zeichenkette (String).</returns>
    public string GenerateStepCodePreview(RecordedStep step)
    {
        return TestCodeGenerator.GenerateStepCode(step);
    }

    /// <summary>
    /// Saves the test code to a file selected by the user via dialog.
    /// </summary>
    /// <returns>The path to the saved file, or null if cancelled.</returns>
    public async Task<string?> SaveTestToFileWithDialog()
    {
        if (_showSaveDialogCallback != null)
        {
            var filePath = await _showSaveDialogCallback();
            if (!string.IsNullOrEmpty(filePath))
            {
                return SaveTestToFile(filePath);
            }
        }
        else
        {
            // Fallback to default behavior if no callback is set
            return SaveTestToFile();
        }
        
        return null;
    }
    
    /// <summary>
    /// Validates a step and adds it to the recorded steps.
    /// If validation fails, attempts fallback strategies.
    /// </summary>
    /// <param name="step">The step to validate and add.</param>
    /// <param name="control">The control associated with the step.</param>
    private void ValidateAndAddStep(RecordedStep step, Control? control)
    {
        if (control == null)
        {
            // If we don't have a control reference, just add the step
            _steps.Add(step);
            return;
        }
        
        // Validate the step with control matching
        // Ensure validation happens on the UI thread to avoid "Call from invalid thread" errors
        ValidationResult validationResult;
        if (Dispatcher.UIThread.CheckAccess())
        {
            // Already on UI thread
            validationResult = _stepValidator.ValidateStep(step, control);
        }
        else
        {
            // Switch to UI thread for validation
            validationResult = Dispatcher.UIThread.InvokeAsync(() => _stepValidator.ValidateStep(step, control)).Result;
        }
        
        if (validationResult.IsSuccess)
        {
            // Step is valid, annotate the warning field with validation status and add it
            step = new RecordedStep
            {
                Type = step.Type,
                Selector = step.Selector,
                Parameter = step.Parameter,
                Quality = step.Quality,
                Warning = step.Warning == null
                    ? "VALIDATION OK"
                    : $"{step.Warning}; VALIDATION OK"
            };
            _steps.Add(step);
        }
        else
        {
            // All validation attempts failed, add the step but mark it as problematic
            step = new RecordedStep
            {
                Type = step.Type,
                Selector = step.Selector,
                Parameter = step.Parameter,
                Quality = step.Quality,
                Warning = step.Warning == null 
                    ? $"VALIDATION FAILED: {validationResult.ErrorMessage}" 
                    : $"{step.Warning}; VALIDATION FAILED: {validationResult.ErrorMessage}"
            };
            _steps.Add(step);
                
            _logger?.LogWarning("Step validation failed completely: {Message}", validationResult.ErrorMessage);
        }
    }

    private void SubscribeToSelectionEvents(Control rootControl)
    {
        _logger?.LogDebug("Searching for ComboBox and ListBox controls to subscribe to selection events");
        
        // Find all ComboBox and ListBox controls in the visual tree
        int comboBoxCount = 0;
        int listBoxCount = 0;
        
        foreach (var control in rootControl.GetVisualDescendants().OfType<Control>())
        {
            if (control is ComboBox comboBox)
            {
                comboBoxCount++;
                comboBox.SelectionChanged += OnComboBoxSelectionChanged;
                _logger?.LogDebug("Subscribed to ComboBox SelectionChanged event: {ControlName}", 
                    AutomationProperties.GetAutomationId(comboBox) ?? comboBox.Name ?? "unnamed");
            }
            else if (control is ListBox listBox)
            {
                listBoxCount++;
                listBox.SelectionChanged += OnListBoxSelectionChanged;
                _logger?.LogDebug("Subscribed to ListBox SelectionChanged event: {ControlName}", 
                    AutomationProperties.GetAutomationId(listBox) ?? listBox.Name ?? "unnamed");
            }
        }
        
        _logger?.LogDebug("Found and subscribed to {ComboBoxCount} ComboBox and {ListBoxCount} ListBox controls", 
            comboBoxCount, listBoxCount);
    }

    private void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderState.Recording || sender is not ComboBox comboBox)
            return;

        _logger?.LogDebug("ComboBox selection changed detected");

        // Get the AutomationId of the ComboBox
        var comboBoxId = AutomationProperties.GetAutomationId(comboBox);
        if (string.IsNullOrEmpty(comboBoxId))
        {
            // Try to find a parent with AutomationId
            var parentWithId = FindControlWithAutomationId(comboBox);
            if (parentWithId != null)
            {
                comboBoxId = AutomationProperties.GetAutomationId(parentWithId);
            }
        }

        if (string.IsNullOrEmpty(comboBoxId))
        {
            _logger?.LogDebug("Skipping ComboBox selection recording - no AutomationId found");
            return; // Skip if we can't identify the ComboBox
        }

        // Get the selected value
        var selectedValue = GetSelectedItemText(comboBox.SelectedItem);

        if (!string.IsNullOrEmpty(selectedValue))
        {
            var step = new RecordedStep
            {
                Type = StepType.SelectItem,
                Selector = comboBoxId,
                Parameter = selectedValue,
                Quality = SelectorQuality.High
            };

            ValidateAndAddStep(step, comboBox);
            _logger?.LogDebug("Recorded ComboBox selection: {ComboBoxId} = {SelectedValue}", comboBoxId, selectedValue);
        }
        else
        {
            _logger?.LogDebug("Skipping ComboBox selection recording - no selected value found");
        }
    }

    private void OnListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderState.Recording || sender is not ListBox listBox)
            return;

        _logger?.LogDebug("ListBox selection changed detected");

        // Get the AutomationId of the ListBox
        var listBoxId = AutomationProperties.GetAutomationId(listBox);
        if (string.IsNullOrEmpty(listBoxId))
        {
            // Try to find a parent with AutomationId
            var parentWithId = FindControlWithAutomationId(listBox);
            if (parentWithId != null)
            {
                listBoxId = AutomationProperties.GetAutomationId(parentWithId);
            }
        }

        if (string.IsNullOrEmpty(listBoxId))
        {
            _logger?.LogDebug("Skipping ListBox selection recording - no AutomationId found");
            return; // Skip if we can't identify the ListBox
        }

        // Get the selected value
        var selectedValue = GetSelectedItemText(listBox.SelectedItem);

        if (!string.IsNullOrEmpty(selectedValue))
        {
            var step = new RecordedStep
            {
                Type = StepType.SelectItem,
                Selector = listBoxId,
                Parameter = selectedValue,
                Quality = SelectorQuality.High
            };

            ValidateAndAddStep(step, listBox);
            _logger?.LogDebug("Recorded ListBox selection: {ListBoxId} = {SelectedValue}", listBoxId, selectedValue);
        }
        else
        {
            _logger?.LogDebug("Skipping ListBox selection recording - no selected value found");
        }
    }

    private static string GetSelectedItemText(object? selectedItem)
    {
        if (selectedItem == null)
            return string.Empty;

        // Try to get the text representation of the selected item
        return selectedItem switch
        {
            ContentControl contentControl => contentControl.Content?.ToString() ?? string.Empty,
            TextBlock textBlock => textBlock.Text ?? string.Empty,
            string str => str,
            _ => selectedItem.ToString() ?? string.Empty
        };
    }

    private static Control? FindControlWithAutomationId(Control startControl)
    {
        var current = startControl as Visual;

        while (current != null)
        {
            if (current is Control ctrl)
            {
                var automationId = AutomationProperties.GetAutomationId(ctrl);
                if (!string.IsNullOrEmpty(automationId))
                {
                    return ctrl;
                }
            }

            current = current.GetVisualParent();
            if (current is Window)
                break;
        }

        return null;
    }
}
