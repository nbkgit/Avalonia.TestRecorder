using Avalonia.Headless.XUnit;
using Avalonia.HeadlessTestKit;
using Xunit;

namespace SampleApp.Tests;

public partial class Recorded_LoginFlow_Tests
{
    [AvaloniaFact]
    public void Scenario_LoginFlow_20260603_173129()
    {
        // Initialize window with DataContext
        var window = new SampleApp.Views.MainWindow
        {
            DataContext = new SampleApp.ViewModels.MainWindowViewModel(),
        };
        window.Show();

        
        var ui = new Ui(window);
        
        ui.Click("basic_usernameField"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.TypeText("basic_usernameField", "a"); // VALIDATION OK
        ui.TypeText("basic_usernameField", "b"); // VALIDATION OK
        ui.TypeText("basic_usernameField", "c"); // VALIDATION OK
        ui.Click("basic_passwordField"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.TypeText("basic_passwordField", "a"); // VALIDATION OK
        ui.TypeText("basic_passwordField", "b"); // VALIDATION OK
        ui.TypeText("basic_passwordField", "c"); // VALIDATION OK
        ui.Click("basic_countryCombo"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("ContentPresenter_NoId"); // ERROR: No stable selector available; VALIDATION FAILED: Control not found: 'ContentPresenter_NoId'. Available AutomationIds: basic_usernameField, basic_passwordField, basic_countryCombo, basic_subscribeCheck, basic_genderMale, basic_genderFemale, basic_submitButton, basic_statusLabel. Tree path resolution failed in headless mode may indicate visual tree differences.
        ui.SelectItem("basic_countryCombo", "Germany"); // VALIDATION OK
        ui.Click("basic_genderMale"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.AssertNotEnabled("basic_submitButton"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("Panel[0]/VisualLayerManager[0]/ContentPresenter[0]/TabControl[0]/Border[0]/DockPanel[0]/ItemsPresenter[0]/WrapPanel[0]/TabItem[1]/Border[0]/Panel[0]/ContentPresenter[0]/AccessText[0]"); // VALIDATION OK
        ui.Click("item_list_1"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("item_list_2"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("item_list_3"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("advanced_treeView"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("advanced_treeView"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("advanced_treeView"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
        ui.Click("advanced_treeView"); // VALIDATION FAILED: Control mismatch: Found control doesn't match the original control. Multiple controls may have the same AutomationId.
    }
}