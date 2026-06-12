using Avalonia.Controls;
using Avalonia.Threading;
using System.Reflection;
using System.Text;

namespace Avalonia.TestRecorder.CodeGen;

/// <summary>
/// Generates C# test code from recorded steps.
/// </summary>
/// <remarks>
/// Initialisiert eine neue Instanz der <see cref="TestCodeGenerator"/>-Klasse mit den angegebenen Codegenerierungsoptionen und dem Anwendungsnamen.
/// </remarks>
/// <param name="options">Die Konfigurationsoptionen für den Codegenerierungsprozess.</param>
/// <param name="appName">Der Name der Anwendung, für die der Testcode generiert wird.</param>
public sealed class TestCodeGenerator(CodegenOptions options, string appName)
{
    private readonly CodegenOptions _options = options;
    private readonly string _appName = appName;

    /// <summary>
    /// Generiert den vollständigen Quellcode für eine Testklasse basierend auf den aufgezeichneten Schritten, dem Szenarionamen und der aktuellen Fensterinstanz.
    /// </summary>
    /// <remarks>
    /// Die Methode lädt ein Template und ersetzt Platzhalter wie Namespace, Klassenname, Testmethode und die Testschritte. 
    /// Der Zugriff auf den <see cref="Avalonia.StyledElement.DataContext"/> des Fensters erfolgt thread-sicher über den UI-Thread, um Cross-Thread-Ausnahmen zu vermeiden.
    /// </remarks>
    /// <param name="steps">Eine Liste der aufgezeichneten Interaktionsschritte, die im Test ausgeführt werden sollen.</param>
    /// <param name="scenarioName">Der Name des Szenarios, der für die Benennung der Testklasse und -methode verwendet wird.</param>
    /// <param name="window">Das aktuelle Anwendungsfenster, aus dem Typen- und DataContext-Informationen extrahiert werden.</param>
    /// <returns>Der fertig generierte C#-Testcode als Zeichenkette (String).</returns>
    public string Generate(IEnumerable<RecordedStep> steps, string scenarioName, Window window)
    {
        var template = LoadTemplate();
        var className = $"Recorded_{scenarioName}_Tests";
        var methodName = _options.IncludeTimestamp
            ? $"Scenario_{scenarioName}_{DateTime.Now:yyyyMMdd_HHmmss}"
            : $"Scenario_{scenarioName}";
        var namespaceName = _options.Namespace ?? $"{_appName}.Tests";

        // Get window and DataContext information
        var windowTypeName = window.GetType().FullName ?? window.GetType().Name;
        
        // Access DataContext on the UI thread to avoid cross-thread exceptions
        object? dataContext = null;
        if (Dispatcher.UIThread.CheckAccess())
        {
            dataContext = window.DataContext;
        }
        else
        {
            dataContext = Dispatcher.UIThread.Invoke(() => window.DataContext);
        }
        
        var dataContextTypeName = dataContext?.GetType().FullName ?? dataContext?.GetType().Name ?? "object";
        
        // Handle generic types properly
        windowTypeName = FormatTypeName(windowTypeName);
        dataContextTypeName = FormatTypeName(dataContextTypeName);

        var windowInitCode = GenerateWindowInitializationCode(windowTypeName, dataContextTypeName);

        var stepsCode = GenerateStepsCode(steps);

        return template
            .Replace("{Namespace}", namespaceName)
            .Replace("{ClassName}", className)
            .Replace("{TestMethod}", methodName)
            .Replace("{WindowInit}", windowInitCode)
            .Replace("{Steps}", stepsCode);
    }

    private static string GenerateWindowInitializationCode(string windowTypeName, string dataContextTypeName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"        var window = new {windowTypeName}");
        sb.AppendLine("        {");
        
        // Check if DataContext type is available and not null
        if (!string.IsNullOrEmpty(dataContextTypeName) && dataContextTypeName != "object")
        {
            sb.AppendLine($"            DataContext = new {dataContextTypeName}(),");
        }
        
        sb.AppendLine("        };");
        sb.AppendLine("        window.Show();");
        return sb.ToString();
    }

    private static string FormatTypeName(string typeName)
    {
        // Handle generic types by escaping them properly for C#
        return typeName.Replace("+", ".");
    }

    private static string GenerateStepsCode(IEnumerable<RecordedStep> steps)
    {
        var sb = new StringBuilder();
        foreach (var step in steps)
        {
            sb.AppendLine(GenerateStepLine(step));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Generates the C# code line for a single recorded step.
    /// Used by both test file generation and UI previews to keep logic in one place.
    /// </summary>
    public static string GenerateStepCode(RecordedStep step)
    {
        // For previews we don't need leading indentation
        return GenerateStepLine(step).TrimStart();
    }

    private static string GenerateStepLine(RecordedStep step)
    {
        var warning = step.Warning != null ? $" // {step.Warning}" : "";
        var intend = "        ";
        return step.Type switch
        {
            StepType.Click         => $"{intend}ui.Click(\"{step.Selector}\");{warning}",
            StepType.RightClick    => $"{intend}ui.RightClick(\"{step.Selector}\");{warning}",
            StepType.DoubleClick   => $"{intend}ui.DoubleClick(\"{step.Selector}\");{warning}",
            StepType.TypeText      => $"{intend}ui.TypeText(\"{step.Selector}\", \"{EscapeString(step.Parameter ?? "")}\");{warning}",
            StepType.KeyPress      => $"{intend}ui.KeyPress(\"{step.Parameter}\");{warning}",
            StepType.Scroll        => $"{intend}ui.Scroll(\"{step.Selector}\", {step.Parameter});{warning}",
            StepType.Hover         => $"{intend}ui.Hover(\"{step.Selector}\");{warning}",
            StepType.AssertText    => $"{intend}ui.AssertText(\"{step.Selector}\", \"{EscapeString(step.Parameter ?? "")}\");{warning}",
            StepType.AssertChecked => $"{intend}ui.AssertChecked(\"{step.Selector}\", {step.Parameter});{warning}",
            StepType.AssertVisible => $"{intend}ui.AssertVisible(\"{step.Selector}\");{warning}",
            StepType.AssertEnabled => $"{intend}ui.AssertEnabled(\"{step.Selector}\");{warning}",
            StepType.SelectItem    => $"{intend}ui.SelectItem(\"{step.Selector}\", \"{EscapeString(step.Parameter ?? "")}\");{warning}",
            _                      => $"{intend}// Unknown step type: {step.Type} Selector: {step.Selector} Parameter: {step.Parameter}"
        };
    }
    private static string EscapeString(string str)
    {
        return str.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private string LoadTemplate()
    {
        var templateName = _options.TestFramework == TestFramework.XUnit
            ? "xUnit.TestClass.template"
            : "NUnit.TestClass.template";

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Avalonia.TestRecorder.CodeGen.Templates.{templateName}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // Return default template if embedded resource not found
            return _options.TestFramework == TestFramework.XUnit
                ? GetDefaultXUnitTemplate()
                : GetDefaultNUnitTemplate();
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string GetDefaultXUnitTemplate()
    {
        return @"using Avalonia.Headless.XUnit;
using Avalonia.HeadlessTestKit;
using Xunit;

namespace {Namespace};

public partial class {ClassName}
{
    [AvaloniaFact]
    public void {TestMethod}()
    {
        // Initialize window with DataContext
{WindowInit}
        
        var ui = new Ui(window);
        
{Steps}
    }
}
";
    }

    private static string GetDefaultNUnitTemplate()
    {
        return @"using Avalonia.Headless.NUnit;
using Avalonia.HeadlessTestKit;
using NUnit.Framework;

namespace {Namespace};

public partial class {ClassName}
{
    [AvaloniaTest]
    public void {TestMethod}()
    {
        // Initialize window with DataContext
{WindowInit}
        
        var ui = new Ui(window);
        
{Steps}
    }
}
";
    }
}