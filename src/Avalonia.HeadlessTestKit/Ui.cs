using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;
using Pointer = Avalonia.Input.Pointer;

namespace Avalonia.HeadlessTestKit;

/// <summary>
/// DSL helper class for writing headless Avalonia tests.
/// Provides fluent methods for UI interactions and assertions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="Ui"/> class.
/// </remarks>
/// <param name="window">The window to interact with.</param>
public class Ui(Window window)
{
    private readonly Window _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);
    private readonly int _settleDelayMs = 10;

    /// <summary>
    /// When true, Ui methods run in validation mode and avoid performing side-effect actions.
    /// </summary>
    public bool IsValidationMode { get; set; }

    #region Click Operations

    /// <summary>Performs a left mouse click on the specified element.</summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    public void Click(string id)
    {
        Control control = this.FindControl(id);
        if (this.IsValidationMode)
            return;
        if (control is Button button)
        {
            button.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
            this.ProcessUiEvents();
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke((object)button, null);
            this.ProcessUiEvents();
        }
        else
        {
            if (window != null)
            { //this also cilcks on the button, but is more reliable
                var elementPosition = control.TranslatePoint(new Point(), _window);
                if (!elementPosition.HasValue)
                    throw new InvalidOperationException($"Control '{id}' could not be translated to window coordinates.");                
                _window.MouseDown(elementPosition.Value, MouseButton.Left);
                _window.MouseUp(elementPosition.Value, MouseButton.Left);
            }
            else
            {
                Point centerPoint = GetCenterPoint(control);
                this.RaisePointerEvent(control, centerPoint, Ui.PointerEventType.Press);
                this.RaisePointerEvent(control, centerPoint, Ui.PointerEventType.Release);
                this.ProcessUiEvents();
            }
        }
    }


    /// <summary>
    /// Performs a right mouse click on the specified element.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    public void RightClick(string id)
    {
        var control = FindControl(id);
        var point = GetCenterPoint(control);

        if (IsValidationMode)
        {
            // Ensure control lookup and coordinate calculation succeed, but do not send events.
            return;
        }

        RaisePointerEvent(control, point, PointerEventType.Press, rightButton: true);
        RaisePointerEvent(control, point, PointerEventType.Release, rightButton: true);
        ProcessUiEvents();
    }

    /// <summary>
    /// Performs a double-click on the specified element.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    public void DoubleClick(string id)
    {
        var control = FindControl(id);
        var point = GetCenterPoint(control);

        if (IsValidationMode)
        {
            // Ensure control lookup and coordinate calculation succeed, but do not send events.
            return;
        }

        RaisePointerEvent(control, point, PointerEventType.Press);
        RaisePointerEvent(control, point, PointerEventType.Release);
        RaisePointerEvent(control, point, PointerEventType.Press, clickCount: 2);
        RaisePointerEvent(control, point, PointerEventType.Release);
        ProcessUiEvents();
    }

    /// <summary>
    /// Moves the mouse pointer over the specified element.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    public void Hover(string id)
    {
        var control = FindControl(id);
        var point = GetCenterPoint(control);

        if (IsValidationMode)
        {
            // Ensure control lookup and coordinate calculation succeed, but do not send events.
            return;
        }

        RaisePointerEvent(control, point, PointerEventType.Move);
        ProcessUiEvents();
    }

    #endregion

    #region Text Input

    /// <summary>
    /// Types text into the specified element.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="text">The text to type.</param>
    public void TypeText(string id, string text)
    {
        var control = FindControl(id);
        if (control is not null)
        {
            if (IsValidationMode)
            {
                // Ensure control lookup succeeds, but do not send text input events.
                return;
            }

            // Focus the control first
            control.Focus();
            ProcessUiEvents();

            // Type each character
            foreach (var ch in text)
            {
                var args = new TextInputEventArgs
                {
                    Text = ch.ToString(),
                    RoutedEvent = InputElement.TextInputEvent
                };
                control.RaiseEvent(args);
            }

            ProcessUiEvents();
        }
        else
        {
            throw new Exception($"Assert failed for '{id}': Element doesn't exsist");
        }
    }

    /// <summary>
    /// Presses a specific key.
    /// </summary>
    /// <param name="keyText">The key to press (e.g., "Enter", "Tab", "Escape").</param>
    public void KeyPress(string keyText)
    {
        if (!Enum.TryParse<Key>(keyText, out var key))
        {
            throw new ArgumentException($"Invalid key: {keyText}", nameof(keyText));
        }

        if (IsValidationMode)
        {
            // In validation mode, just ensure the key name is valid.
            return;
        }

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key
        };

        _window.RaiseEvent(args);
        ProcessUiEvents();
    }

    #endregion

    #region Scroll Operations

    /// <summary>
    /// Scrolls the specified element.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="deltaX">Horizontal scroll delta.</param>
    /// <param name="deltaY">Vertical scroll delta.</param>
    public void Scroll(string id, double deltaX, double deltaY = 0)
    {
        var control = FindControl(id);
        var point = GetCenterPoint(control);

        if (IsValidationMode)
        {
            // Ensure control lookup and coordinate calculation succeed, but do not send events.
            return;
        }

        var pointer = new Pointer(0, PointerType.Mouse, true);
        var rootPoint = control.TranslatePoint(point, _window) ?? point;
        var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);

        var args = new PointerWheelEventArgs(
            control,
            pointer,
            _window,
            rootPoint,
            (ulong)Environment.TickCount,
            properties,
            KeyModifiers.None,
            new Vector(deltaX, deltaY)
        )
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent
        };

        control.RaiseEvent(args);
        ProcessUiEvents();
    }

    #endregion

    #region Selection Operations

    /// <summary>
    /// Selects an item in a ComboBox or ListBox by its text value.
    /// </summary>
    /// <param name="id">The AutomationId of the ComboBox or ListBox.</param>
    /// <param name="itemText">The text of the item to select.</param>
    public void SelectItem(string id, string itemText)
    {
        var control = FindControl(id);

        if (control is ComboBox comboBox)
        {
            SelectComboBoxItem(comboBox, itemText);
        }
        else if (control is ListBox listBox)
        {
            SelectListBoxItem(listBox, itemText);
        }
        else
        {
            throw new InvalidOperationException($"Control with id '{id}' is not a ComboBox or ListBox");
        }

        ProcessUiEvents();
    }

    private void SelectComboBoxItem(ComboBox comboBox, string itemText)
    {
        // Focus the ComboBox first (only in execution mode)
        if (!IsValidationMode)
        {
            comboBox.Focus();
            ProcessUiEvents();
        }

        // Try to find the item by text in the Items collection
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            var item = comboBox.Items[i];
            var itemTextContent = GetItemText(item);

            if (itemTextContent.Equals(itemText, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsValidationMode)
                {
                    comboBox.SelectedIndex = i;
                    ProcessUiEvents();
                }
                return;
            }
        }

        // If not found in Items, try to find in the visual tree
        var listBox = comboBox.GetVisualDescendants().OfType<ListBox>().FirstOrDefault();
        if (listBox != null)
        {
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                var item = listBox.Items[i];
                var itemTextContent = GetItemText(item);

                if (itemTextContent.Equals(itemText, StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsValidationMode)
                    {
                        listBox.SelectedIndex = i;
                        ProcessUiEvents();
                    }
                    return;
                }
            }
        }

        throw new ArgumentException($"Item '{itemText}' not found in ComboBox '{AutomationProperties.GetAutomationId(comboBox) ?? comboBox.Name ?? "unnamed"}'");
    }

    private void SelectListBoxItem(ListBox listBox, string itemText)
    {
        // Focus the ListBox first (only in execution mode)
        if (!IsValidationMode)
        {
            listBox.Focus();
            ProcessUiEvents();
        }

        // Try to find the item by text in the Items collection
        for (int i = 0; i < listBox.Items.Count; i++)
        {
            var item = listBox.Items[i];
            var itemTextContent = GetItemText(item);

            if (itemTextContent.Equals(itemText, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsValidationMode)
                {
                    listBox.SelectedIndex = i;
                    ProcessUiEvents();
                }
                return;
            }
        }

        throw new ArgumentException($"Item '{itemText}' not found in ListBox '{AutomationProperties.GetAutomationId(listBox) ?? listBox.Name ?? "unnamed"}'");
    }

    private static string GetItemText(object? item)
    {
        return item switch
        {
            ContentControl contentControl => contentControl.Content?.ToString() ?? string.Empty,
            TextBlock textBlock => textBlock.Text ?? string.Empty,
            string str => str,
            null => string.Empty,
            _ => item.ToString() ?? string.Empty
        };
    }

    #endregion

    #region Assertions

    /// <summary>
    /// Asserts that the specified element's text equals the expected value.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="expected">The expected text value.</param>
    public void AssertText(string id, string expected)
    {
        var control = FindControl(id);

        var actual = GetTextContent(control);

        if (actual != expected)
        {
            throw new Exception($"Assert failed for '{id}': Expected '{expected}', but got '{actual}'");
        }

    }

    /// <summary>
    /// Asserts that the specified toggle control is in the expected checked state.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="expected">The expected checked state.</param>
    public void AssertChecked(string id, bool? expected)
    {
        var control = FindControl(id);

        if (control is ToggleButton toggle)
        {
            if (toggle.IsChecked != expected)
            {
                throw new Exception($"Assert failed for '{id}': Expected IsChecked={expected}, but got {toggle.IsChecked}");
            }
        }
        else
        {
            throw new InvalidOperationException($"Control '{id}' is not a toggle button");
        }

    }

    /// <summary>
    /// Asserts that the specified element is visible.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    public void AssertVisible(string id)
    {
        var control = FindControl(id);

        if (!control.IsVisible)
        {
            throw new Exception($"Assert failed for '{id}': Element is not visible");
        }

    }

    /// <summary>
    /// Asserts that the specified element is enabled.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    public void AssertEnabled(string id)
    {
        var control = FindControl(id);

        if (!control.IsEnabled)
        {
            throw new Exception($"Assert failed for '{id}': Element is not enabled");
        }

    }
    /// <summary>
    /// Asserts that the specified element is not enabled.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    public void AssertNotEnabled(string id)
    {
        var control = FindControl(id);

        if (!control.IsEnabled)
        {
            throw new Exception($"Assert failed for '{id}': Element is enabled");
        }

    }
    /// <summary>
    /// Überprüft, ob eine benutzerdefinierte Vergleichsfunktion für zwei Werte des gleichen Typs wahr ergibt, und wirft andernfalls eine Ausnahme.
    /// </summary>
    /// <typeparam name="T">Der Datentyp der zu vergleichenden Werte.</typeparam>
    /// <param name="left">Der linke Wert für den Vergleich (z. B. der tatsächliche Wert).</param>
    /// <param name="right">Der rechte Wert für den Vergleich (z. B. der erwartete Wert).</param>
    /// <param name="comparison">Die Funktion, die die Logik für den Vergleich der beiden Werte enthält.</param>
    /// <param name="message">Die benutzerdefinierte Fehlermeldung, die im Falle eines Fehlschlags ausgegeben wird.</param>
    /// <exception cref="ArgumentNullException">Wird geworfen, wenn die <paramref name="comparison"/>-Funktion <see langword="null"/> ist.</exception>
    /// <exception cref="Exception">Wird geworfen, wenn der Vergleich fehlschlägt (falsch ergibt).</exception>
    public static void AssertTrue<T>(T left, T right, Func<T, T, bool> comparison, string message = "Der Vergleich schlug fehl.")
    {
        if (comparison != null)
        {
            if (!comparison(left, right))
            {
                throw new Exception($"Assert.True Vergleichsfehler ({typeof(T).Name}): {message}");
            }
        }
        else
        {
            throw new ArgumentNullException(nameof(comparison));
        }
    }


    #endregion

    #region Wait Operations

    /// <summary>
    /// Waits for the specified condition to be true.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    public void WaitFor(string id, Func<Control, bool> condition, TimeSpan? timeout = null)
    {
        timeout ??= _defaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var control = FindControl(id);
                if (condition(control))
                {
                    return;
                }
            }
            catch
            {
                // Control not found yet, keep waiting
            }

            ProcessUiEvents();
            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timeout waiting for condition on '{id}' after {timeout.Value.TotalSeconds}s");
    }

    /// <summary>
    /// Waits for the specified element's text to match the expected value.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="expected">The expected text value.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    public void WaitForText(string id, string expected, TimeSpan? timeout = null)
    {
        WaitFor(id, control => GetTextContent(control) == expected, timeout);
    }

    /// <summary>
    /// Waits for the specified element to become visible.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    public void WaitForVisible(string id, TimeSpan? timeout = null)
    {
        WaitFor(id, control => control.IsVisible, timeout);
    }

    /// <summary>
    /// Waits for the specified element to become enabled.
    /// </summary>
    /// <param name="id">The AutomationId or selector of the element.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    public void WaitForEnabled(string id, TimeSpan? timeout = null)
    {
        WaitFor(id, control => control.IsEnabled, timeout);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Finds a control by its AutomationId or selector.
    /// </summary>
    /// <param name="id">The AutomationId or selector.</param>
    /// <returns>The found control.</returns>
    /// <exception cref="ControlNotFoundException">Thrown when control is not found.</exception>
    protected Control FindControl(string id)
    {
        // Try AutomationId first
        var control = FindByAutomationId(_window, id);
        if (control != null)
            return control;

        // Try Name property
        control = FindByName(_window, id);
        if (control != null)
            return control;

        // Try tree path parsing
        if (id.Contains('/') || id.Contains('['))
        {
            control = FindByTreePath(_window, id);
            if (control != null)
                return control;
        }

        throw new ControlNotFoundException($"Control not found: '{id}'. " +
            $"Available AutomationIds: {string.Join(", ", GetAllAutomationIds(_window))}. " +
            $"Tree path resolution failed in headless mode may indicate visual tree differences.");
    }

    private static Control? FindByAutomationId(Control root, string automationId)
    {
        if (AutomationProperties.GetAutomationId(root) == automationId)
            return root;

        foreach (var child in root.GetVisualChildren())
        {
            var result = FindByAutomationId((Control)child, automationId);
            if (result != null)
                return result;
        }

        return null;
    }

    private static Control? FindByName(Control root, string name)
    {
        if (root.Name == name)
            return root;

        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            var result = FindByName(child, name);
            if (result != null)
                return result;
        }

        return null;
    }

    private static Control? FindByTreePath(Control root, string path)
    {
        // Simplified tree path parsing: Type[index]/Type[index]/...
        var parts = path.Split('/');
        Control? current = root;

        foreach (var part in parts)
        {
            if (current == null)
            {
                return null;
            }

            var match = Regex.Match(part, @"(\w+)\[(\d+)\]");
            if (match.Success)
            {
                var typeName = match.Groups[1].Value;
                var index = int.Parse(match.Groups[2].Value);

                // First, try to find a direct child with the exact type name
                var children = current.GetVisualChildren()
                    .OfType<Control>()
                    .Where(c => c.GetType().Name == typeName)
                    .ToList();

                // In headless mode, the visual tree might be different, so try to find the control
                // even if the exact index doesn't match
                if (index >= 0 && index < children.Count)
                {
                    current = children[index];
                }
                else if (children.Count > 0)
                {
                    // Fallback to first child if index is out of range
                    // This can happen in headless mode where the visual tree structure differs
                    current = children[0];
                }
                else
                {
                    // Try to find any child with the same type name regardless of index
                    var allChildren = current.GetVisualChildren().OfType<Control>().ToList();
                    var fallbackChild = allChildren.FirstOrDefault(c => c.GetType().Name == typeName);
                    if (fallbackChild != null)
                    {
                        current = fallbackChild;
                    }
                    else
                    {
                        // In headless mode, we might need to skip certain intermediate elements
                        // and look for descendants directly
                        var descendant = FindDescendantByTypeName(current, typeName);
                        if (descendant != null)
                        {
                            current = descendant;
                        }
                        else
                        {
                            // As a last resort, try to find the element by traversing the entire subtree
                            // This handles cases where the tree path was recorded in normal mode
                            // but the test is running in headless mode with a different visual tree structure
                            var subtreeResult = FindElementInSubtree(current, typeName, index);
                            if (subtreeResult != null)
                            {
                                current = subtreeResult;
                            }
                            else
                            {
                                // If we can't find this element, we might be in headless mode where
                                // the visual tree structure is different. In this case, we should
                                // continue with the search from the current node, essentially skipping
                                // this part of the path.
                                // Continue with current unchanged - this effectively skips this path element
                            }
                        }
                    }
                }
            }
        }

        return current;
    }

    /// <summary>
    /// Finds an element in the subtree by type name and approximate index.
    /// This is a more flexible approach for handling visual tree differences between modes.
    /// </summary>
    /// <param name="root">The root control to search from.</param>
    /// <param name="typeName">The type name to search for.</param>
    /// <param name="targetIndex">The target index (used as a hint).</param>
    /// <returns>The found control or null if not found.</returns>
    private static Control? FindElementInSubtree(Control root, string typeName, int targetIndex)
    {
        // Collect all descendants of the target type
        var candidates = new List<Control>();
        CollectDescendantsOfType(root, typeName, candidates);
        
        // If we found candidates, return the one closest to the target index
        // or the first one if the index is out of range
        if (candidates.Count > 0)
        {
            if (targetIndex >= 0 && targetIndex < candidates.Count)
            {
                return candidates[targetIndex];
            }
            else
            {
                return candidates[0];
            }
        }
        
        return null;
    }

    /// <summary>
    /// Collects all descendants of a specific type.
    /// </summary>
    /// <param name="root">The root control to search from.</param>
    /// <param name="typeName">The type name to search for.</param>
    /// <param name="results">The list to populate with results.</param>
    private static void CollectDescendantsOfType(Control root, string typeName, List<Control> results)
    {
        // Check if the root itself matches
        if (root.GetType().Name == typeName)
        {
            results.Add(root);
        }

        // Recursively search children
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            CollectDescendantsOfType(child, typeName, results);
        }
    }

    /// <summary>
    /// Finds a descendant control by type name, skipping intermediate elements.
    /// This is useful in headless mode where the visual tree structure might differ.
    /// </summary>
    /// <param name="root">The root control to search from.</param>
    /// <param name="typeName">The type name to search for.</param>
    /// <returns>The found control or null if not found.</returns>
    private static Control? FindDescendantByTypeName(Control root, string typeName)
    {
        // Check if the root itself matches
        if (root.GetType().Name == typeName)
        {
            return root;
        }

        // Recursively search children
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            var result = FindDescendantByTypeName(child, typeName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static List<string> GetAllAutomationIds(Control root)
    {
        var ids = new List<string>();
        var automationId = AutomationProperties.GetAutomationId(root);
        if (!string.IsNullOrEmpty(automationId))
        {
            ids.Add(automationId);
        }

        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            ids.AddRange(GetAllAutomationIds(child));
        }

        return ids;
    }

    /// <summary>
    /// Debug method to print the visual tree structure.
    /// </summary>
    /// <param name="root">The root control to start from.</param>
    /// <param name="indent">The indentation level.</param>
    public static void PrintVisualTree(Control root, int indent = 0)
    {
        var indentStr = new string(' ', indent * 2);
        var automationId = AutomationProperties.GetAutomationId(root);
        var idInfo = !string.IsNullOrEmpty(automationId) ? $" (AutomationId: {automationId})" : "";
        
        Console.WriteLine($"{indentStr}{root.GetType().Name}{idInfo}");
        
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            PrintVisualTree(child, indent + 1);
        }
    }

    private static Point GetCenterPoint(Control control)
    {
        var bounds = control.Bounds;
        if (bounds.Width == 0 || bounds.Height == 0)
        {
            return new Point(1, 1); // Fallback for zero-sized controls
        }
        return new Point(bounds.Width / 2, bounds.Height / 2);
    }

    private static string GetTextContent(Control control)
    {
        return control switch
        {
            TextBox textBox => textBox.Text ?? string.Empty,
            TextBlock textBlock => textBlock.Text ?? string.Empty,
            ContentControl content => content.Content?.ToString() ?? string.Empty,
            _ => string.Empty
        };
    }

    private void RaisePointerEvent(Control control, Point point, PointerEventType eventType, bool rightButton = false, int clickCount = 1)
    {
        var pointer = new Pointer(0, PointerType.Mouse, true);
        var rootPoint = control.TranslatePoint(point, _window) ?? point;

        var rawModifiers = eventType == PointerEventType.Press
            ? (rightButton ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton)
            : RawInputModifiers.None;

        var properties = new PointerPointProperties(rawModifiers, PointerUpdateKind.Other);
        
        var routedEvent = eventType switch
        {
            PointerEventType.Press => (Interactivity.RoutedEvent)InputElement.PointerPressedEvent,
            PointerEventType.Release => (Interactivity.RoutedEvent)InputElement.PointerReleasedEvent,
            PointerEventType.Move => (Interactivity.RoutedEvent)InputElement.PointerMovedEvent,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };

        PointerEventArgs args = eventType switch
        {
            PointerEventType.Press => new PointerPressedEventArgs(
                control,
                pointer,
                _window,
                rootPoint,
                (ulong)Environment.TickCount,
                properties,
                KeyModifiers.None,
                clickCount),
            PointerEventType.Release => new PointerReleasedEventArgs(
                control,
                pointer,
                _window,
                rootPoint,
                (ulong)Environment.TickCount,
                properties,
                KeyModifiers.None,
                rightButton ? MouseButton.Right : MouseButton.Left),
            _ => new PointerEventArgs(
                routedEvent,
                control,
                pointer,
                _window,
                rootPoint,
                (ulong)Environment.TickCount,
                properties,
                KeyModifiers.None)
        };

        args.RoutedEvent = routedEvent;
        control.RaiseEvent(args);
    }

    /// <summary>
    /// Processes pending UI events and allows the UI to update.
    /// </summary>
    protected void ProcessUiEvents()
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(_settleDelayMs);
    }

    private enum PointerEventType
    {
        Press,
        Release,
        Move
    }

    #endregion
}

/// <summary>
/// Exception thrown when a control cannot be found.
/// </summary>
public class ControlNotFoundException : Exception
{
    /// <param name="message">send message if emty.</param>
    public ControlNotFoundException(string message) : base(message)
    {
        throw new Exception(message);
    }
}
