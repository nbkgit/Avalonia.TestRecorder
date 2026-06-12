using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.HeadlessTestKit;
using Avalonia.TestRecorder.Selectors;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using Avalonia.Automation;
using Avalonia.Data;

namespace Avalonia.TestRecorder;

/// <summary>
/// Validates that recorded steps can successfully find and interact with target elements.
/// </summary>
public class StepValidator(Window window, ILogger? logger = null)
{
    private readonly Window _window = window;
    private readonly ILogger? _logger = logger;
    private bool  bo;

    /// <summary>
    /// Validates that a recorded step can successfully execute and that the found control matches the original.
    /// </summary>
    /// <param name="step">The step to validate.</param>
    /// <param name="originalControl">The original control that was recorded.</param>
    /// <returns>Validation result with success status and any error messages.</returns>
    public ValidationResult ValidateStep(RecordedStep step, Control? originalControl = null)
    {
        try
        {
            // Create a temporary UI helper to test the step
            var ui = new ValidationUi(_window)
            {
                IsValidationMode = true
            };
            
            // Try to find the control first to check if it matches the original
            if (originalControl != null)
            {
                var foundControl = ui.FindControlPublic(step.Selector);
                if (!AreControlsEquivalent(originalControl, foundControl))
                {
                    return new ValidationResult(false, $"Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.");
                }
            }
            
            // Try to execute the step based on its type
            switch (step.Type)
            {
                case StepType.Click:
                    ui.Click(step.Selector);
                    break;
                case StepType.RightClick:
                    ui.RightClick(step.Selector);
                    break;
                case StepType.DoubleClick:
                    ui.DoubleClick(step.Selector);
                    break;
                case StepType.TypeText:
                    ui.TypeText(step.Selector, step.Parameter ?? string.Empty);
                    break;
                case StepType.Hover:
                    ui.Hover(step.Selector);
                    break;
                case StepType.AssertText:
                    ui.AssertText(step.Selector, step.Parameter ?? string.Empty); 
                    break;
                case StepType.AssertChecked:
                    ui.AssertChecked(step.Selector, bool.Parse(step.Parameter ?? "false"));
                    break;
                case StepType.AssertVisible:
                    ui.AssertVisible(step.Selector);
                    break;
                case StepType.AssertEnabled:
                    ui.AssertEnabled(step.Selector);
                    break;
                case StepType.AssertNotEnabled:
                    ui.AssertNotEnabled(step.Selector);
                    break;
                case StepType.SelectItem:
                    ui.SelectItem(step.Selector, step.Parameter ?? string.Empty);
                    break;
                case StepType.KeyPress:
                    ui.KeyPress(step.Parameter ?? string.Empty);
                    break;
                case StepType.Scroll:
                    var (deltaX, deltaY) = ParseScrollParameter(step.Parameter);
                    ui.Scroll(step.Selector, deltaX, deltaY);
                    break;
                case StepType.AssertTrue:
                    Ui.AssertTrue<string>(step.Parameter ?? string.Empty, step.Parameter ?? string.Empty,
                        (left, right) => left == right,
                        "Der Vergleich schlug fehl.");
                    break;
                default:
                    return new ValidationResult(false, $"Unknown step type: {step.Type}");
            }
            
            return new ValidationResult(true, null);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Step validation failed for selector: {Selector}", step.Selector);
            return new ValidationResult(false, ex.Message);
        }
    }

    private static (double deltaX, double deltaY) ParseScrollParameter(string? parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return (0, 0);
        }

        var parts = parameter.Split(',');
        double deltaX = 0;
        double deltaY = 0;

        if (parts.Length > 0)
        {
            double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out deltaX);
        }

        if (parts.Length > 1)
        {
            double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out deltaY);
        }

        return (deltaX, deltaY);
    }
    
    /// <summary>
    /// Compares two controls to see if they are equivalent.
    /// </summary>
    /// <param name="control1">First control to compare.</param>
    /// <param name="control2">Second control to compare.</param>
    /// <returns>True if controls are equivalent, false otherwise.</returns>
    private static bool AreControlsEquivalent(Control? control1, Control? control2)
    {
        if (control1 == null && control2 == null)
            return true;
            
        if (control1 == null || control2 == null)
            return false;
            
        // Check if they are the exact same object
        if (ReferenceEquals(control1, control2))
            return true;
            
        // Check if they have the same type and position in the visual tree
        if (control1.GetType() != control2.GetType())
            return false;
            
        // Compare their positions in the visual tree by getting their path from root
        var path1 = GetControlPath(control1);
        var path2 = GetControlPath(control2);
        
        return path1.Equals(path2, StringComparison.Ordinal);
    }
    
    /// <summary>
    /// Gets a unique path for a control in the visual tree.
    /// </summary>
    /// <param name="control">The control to get the path for.</param>
    /// <returns>A string representing the control's path in the visual tree.</returns>
    private static string GetControlPath(Control control)
    {
        var pathParts = new List<string>();
        var current = control as Visual;
        
        while (current != null)
        {
            if (current is Control currentControl)
            {
                // Add type name and index among siblings of the same type
                var parent = current.GetVisualParent();
                if (parent != null)
                {
                    var siblings = parent.GetVisualChildren()
                        .OfType<Control>()
                        .Where(c => c.GetType() == currentControl.GetType())
                        .ToList();
                        
                    var index = siblings.IndexOf(currentControl);
                    pathParts.Add($"{currentControl.GetType().Name}[{index}]");
                }
                else
                {
                    pathParts.Add(currentControl.GetType().Name);
                }
            }
            
            current = current.GetVisualParent();
        }
        
        pathParts.Reverse();
        return string.Join("/", pathParts);
    }
}

/// <summary>
/// Result of step validation.
/// </summary>
/// <remarks>
/// Initialisiert eine neue Instanz der <see cref="ValidationResult"/>-Klasse mit dem Erfolgsstatus und einer optionalen Fehlermeldung.
/// </remarks>
/// <param name="isSuccess"><see langword="true"/>, wenn die Validierung erfolgreich war; andernfalls <see langword="false"/>.</param>
/// <param name="errorMessage">Die Beschreibung des Fehlers, oder <see langword="null"/>, wenn kein Fehler vorliegt.</param>
public class ValidationResult(bool isSuccess, string? errorMessage)
{
    /// <summary>
    /// Ruft einen Wert ab, der angibt, ob die Operation oder Überprüfung erfolgreich war.
    /// </summary>
    /// <value>
    /// <see langword="true"/>, wenn die Operation erfolgreich abgeschlossen wurde; andernfalls <see langword="false"/>.
    /// </value>
    public bool IsSuccess { get; } = isSuccess;
    /// <summary>
    /// Ruft die Fehlermeldung ab, wenn die Operation oder Überprüfung fehlgeschlagen ist.
    /// </summary>
    /// <value>
    /// Eine Zeichenkette (String), die den Fehler beschreibt, oder <see langword="null"/>, wenn die Operation erfolgreich war.
    /// </value>
    public string? ErrorMessage { get; } = errorMessage;
}

/// <summary>
/// Specialized UI helper for validation that exposes protected APIs from Ui.
/// </summary>
internal class ValidationUi(Window window) : Ui(window)
{

    /// <summary>
    /// Public method to expose the FindControl method for validation purposes.
    /// </summary>
    public Control FindControlPublic(string id)
    {
        return FindControl(id);
    }
}
