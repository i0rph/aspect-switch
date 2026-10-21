#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace AspectSettings;

internal sealed class DeviceNameEditor : StackPanel
{
    internal const string CustomOption = "직접 입력";
    private static readonly string[] Presets = {
        "PC", "노트북", "Nintendo Switch", "Nintendo Switch 2",
        "PlayStation 5", "PlayStation 4", "Xbox Series X", "Xbox Series S",
        "Xbox One", "Steam Deck"
    };
    internal ComboBox Selector { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 120 };
    internal TextBox CustomInput { get; } = new() { PlaceholderText = "기기 이름 입력", MaxLength = 48, HorizontalAlignment = HorizontalAlignment.Stretch };

    internal DeviceNameEditor(string name, Action<string> changed)
    {
        Spacing = 8; VerticalAlignment = VerticalAlignment.Center;
        bool known = Presets.Contains(name);
        foreach (string preset in Presets) Selector.Items.Add(preset);
        Selector.Items.Add(CustomOption);
        Selector.SelectedItem = known ? name : CustomOption;
        // Preserve existing user names exactly; selecting a preset does not erase the custom draft.
        CustomInput.Text = known ? "" : name;
        CustomInput.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(Selector, "기기 이름 선택");
        AutomationProperties.SetName(CustomInput, "기기 이름 직접 입력");
        Children.Add(Selector); Children.Add(CustomInput);
        Selector.SelectionChanged += (_, _) => {
            bool custom = (string?)Selector.SelectedItem == CustomOption;
            CustomInput.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            changed(custom ? CustomInput.Text : (string)Selector.SelectedItem);
            if (custom && IsLoaded) CustomInput.Focus(FocusState.Programmatic);
        };
        CustomInput.TextChanging += (_, _) => {
            if ((string?)Selector.SelectedItem == CustomOption) changed(CustomInput.Text);
        };
    }
}
