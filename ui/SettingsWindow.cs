#nullable enable
using AspectSwitch;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.Graphics;
using Windows.UI;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace AspectSettings;
internal sealed class SettingsWindow : Window
{
    private readonly EditorSession session;
    private readonly Grid root = new() { RequestedTheme = ElementTheme.Dark };
    private readonly NavigationView navigation = new();
    private readonly StackPanel content = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock heading = new() { FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 24) };
    private readonly InfoBar notice = new() { IsOpen = false, IsClosable = true, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button save = new() { Content = "변경 사항 저장", MinWidth = 132 };
    private readonly Button cancel = new() { Content = "닫기", MinWidth = 88 };
    private readonly Button back = new() { Content = new FontIcon { Glyph = "\uE72B", FontSize = 16 }, Width = 40, Height = 32, IsEnabled = false };
    private readonly List<MonitorCandidate> candidates = new();
    private readonly Stack<string> history = new();
    private string currentPage = "";
    private bool busy, navigating;
    internal SettingsWindow(EditorSession session)
    {
        this.session = session; Title = session.Preview ? "Aspect Switch — 미리보기" : "Aspect Switch";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        ExtendsContentIntoTitleBar = true;
        AppWindow.Resize(new SizeInt32(1160, 820));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 880;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + Math.Max(0, (area.Width - 1160) / 2), area.Y + Math.Max(0, (area.Height - 820) / 2)));
        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent; titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.White; titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 153, 153, 153);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = new Grid(); title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        back.Style = (Style)Application.Current.Resources["SubtleButtonStyle"]; back.Margin = new Thickness(4, 0, 0, 0); back.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(back, "뒤로"); ToolTipService.SetToolTip(back, "뒤로"); back.Click += (_, _) => GoBack(); title.Children.Add(back);
        var titleDrag = new Grid { Background = new SolidColorBrush(Colors.Transparent), Margin = new Thickness(8, 0, 140, 0) };
        titleDrag.Children.Add(new TextBlock { Text = session.Preview ? "설정 · 미리보기" : "설정", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(titleDrag, 1); title.Children.Add(titleDrag); root.Children.Add(title); SetTitleBar(titleDrag);
        root.SizeChanged += (_, _) => titleDrag.Margin = new Thickness(8, 0, titleBar.RightInset / (root.XamlRoot?.RasterizationScale ?? 1), 0);
        navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left; navigation.IsPaneOpen = true; navigation.OpenPaneLength = 280;
        navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed; navigation.IsPaneToggleButtonVisible = false;
        navigation.IsSettingsVisible = false; navigation.AlwaysShowHeader = false; navigation.IsTitleBarAutoPaddingEnabled = false;
        navigation.PaneHeader = BuildProfile(); navigation.PaneFooter = new TextBlock { Text = "Aspect Switch  1.0.0", FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush"), Margin = new Thickness(20, 16, 20, 20) };
        var search = new AutoSuggestBox { PlaceholderText = "설정 찾기", QueryIcon = new SymbolIcon(Symbol.Find) };
        search.TextChanged += (_, e) => { if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput) search.ItemsSource = new[] { "기기와 화면", "모니터 호환성", "일반" }.Where(t => t.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); };
        search.QuerySubmitted += (_, e) => { string q = e.ChosenSuggestion as string ?? e.QueryText; if (q.Contains("모니터") || q.Contains("호환")) Navigate("compatibility"); else if (q.Contains("일반") || q.Contains("시작")) Navigate("general"); else if (q.Length > 0) Navigate("devices"); };
        navigation.AutoSuggestBox = search;
        AddNav("기기와 화면", "devices", "\uE7F4"); AddNav("모니터 호환성", "compatibility", "\uE950"); AddNav("일반", "general", "\uE713");
        navigation.SelectionChanged += (_, e) => { if (!navigating && e.SelectedItem is NavigationViewItem item) Navigate((string)item.Tag); };
        var body = new Grid { MaxWidth = 1100, Padding = new Thickness(32, 14, 36, 20), Background = new SolidColorBrush(Colors.Transparent) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(heading);
        var scroller = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Enabled, Padding = new Thickness(0, 0, 20, 20) };
        Grid.SetRow(scroller, 1); body.Children.Add(scroller);
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 20, 0) }; bottom.Children.Add(notice);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        save.Style = (Style)Application.Current.Resources["AccentButtonStyle"]; save.Click += (_, _) => Save(); cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel); buttons.Children.Add(save); bottom.Children.Add(buttons); Grid.SetRow(bottom, 2); body.Children.Add(bottom);
        navigation.Content = body; Grid.SetRow(navigation, 1); root.Children.Add(navigation); Content = root;
        AppWindow.Closing += (_, e) => { if (busy) e.Cancel = true; };
        Navigate(session.Draft.MonitorName.Length == 0 ? "compatibility" : session.InitialPage, false);
        root.Loaded += async (_, _) => {
            double scale = root.XamlRoot.RasterizationScale;
            int width = Math.Min((int)(1160 * scale), area.Width - 32), height = Math.Min((int)(820 * scale), area.Height - 32);
            AppWindow.Resize(new SizeInt32(width, height));
            AppWindow.Move(new PointInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2));
            if (AppWindow.Presenter is OverlappedPresenter sized) { sized.PreferredMinimumWidth = (int)(880 * scale); sized.PreferredMinimumHeight = (int)(600 * scale); }
            if (session.RenderPath != null) { await Task.Delay(600); await Render(session.RenderPath); }
            if (session.SmokePath != null) { await Task.Delay(400); await Smoke(); }
        };
    }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static TextBlock Text(string text, double size = 14, bool secondary = false) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush") };
    private static FontIcon Glyph(string glyph, double size = 20) => new() { Glyph = glyph, FontSize = size, FontFamily = new FontFamily("Segoe Fluent Icons"), VerticalAlignment = VerticalAlignment.Center };
    private UIElement BuildProfile()
    {
        var grid = new Grid { Margin = new Thickness(16, 8, 12, 22) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(AppImage(56));
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Spacing = 4 };
        labels.Children.Add(new TextBlock { Text = "Aspect Switch", FontWeight = FontWeights.SemiBold, FontSize = 16 }); labels.Children.Add(Text("화면 비율 자동 전환", 12, true)); Grid.SetColumn(labels, 1); grid.Children.Add(labels); return grid;
    }
    private void AddNav(string title, string tag, string glyph) => navigation.MenuItems.Add(new NavigationViewItem { Content = title, Tag = tag, Icon = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons") } });
    private void GoBack() { if (history.Count > 0) Navigate(history.Pop(), false); }
    private void Navigate(string page, bool record = true)
    {
        if (busy) return;
        if (record && currentPage.Length > 0 && currentPage != page) history.Push(currentPage);
        currentPage = page; back.IsEnabled = history.Count > 0; navigating = true;
        navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().First(n => (string)n.Tag == page); navigating = false;
        content.Children.Clear(); notice.IsOpen = false;
        if (page == "compatibility") BuildCompatibility(); else if (page == "general") BuildGeneral(); else BuildDevices();
    }
    private Border Card(UIElement child, double padding = 20) => new() { Background = Brush("CardBackgroundFillColorDefaultBrush"), BorderBrush = Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(padding), Child = child, HorizontalAlignment = HorizontalAlignment.Stretch };
    private Grid Row(string icon, string title, string detail, FrameworkElement? action = null)
    {
        var grid = new Grid { MinHeight = 34, ColumnSpacing = 20 }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Glyph(icon)); var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center }; labels.Children.Add(Text(title)); if (detail.Length > 0) labels.Children.Add(Text(detail, 12, true)); Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        if (action != null) { Grid.SetColumn(action, 2); if (action is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(action); } return grid;
    }
    private void Section(string title, FrameworkElement? action = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 24, 0, 10) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Text(title); text.FontWeight = FontWeights.SemiBold; text.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(text);
        if (action != null) { Grid.SetColumn(action, 1); grid.Children.Add(action); } content.Children.Add(grid);
    }
    private ComboBox Choices(Choice[] choices, int selected, string label, Action<int> changed)
    {
        var box = new ComboBox { Header = label, ItemsSource = choices, DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 130 };
        // Choice uses fields for the shared serializer, so display items explicitly.
        box.ItemsSource = null; box.DisplayMemberPath = "";
        foreach (var choice in choices) box.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == selected);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem item) changed((int)item.Tag); }; AutomationProperties.SetName(box, label); return box;
    }
    private ToggleSwitch Toggle(string label, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = "켬", OffContent = "끔", MinWidth = 80 }; AutomationProperties.SetName(toggle, label); toggle.Toggled += (_, _) => changed(toggle.IsOn); return toggle;
    }
    private void BuildDevices()
    {
        heading.Text = "기기와 화면";
        var monitor = new Grid { ColumnSpacing = 20, Margin = new Thickness(0, 0, 0, 14) }; monitor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); monitor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var screen = new Border { Width = 86, Height = 56, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), BorderBrush = Brush("TextFillColorSecondaryBrush"), Background = Brush("ControlFillColorSecondaryBrush"), Child = Glyph("\uE7F4", 28) }; monitor.Children.Add(screen);
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center }; labels.Children.Add(new TextBlock { Text = session.Draft.MonitorName.Length == 0 ? "모니터를 선택하세요" : session.Draft.MonitorName, FontSize = 20, FontWeight = FontWeights.SemiBold }); labels.Children.Add(Text("기기 " + session.Draft.Devices.Count + "개 · 입력별 화면 비율", 12, true)); Grid.SetColumn(labels, 1); monitor.Children.Add(labels); content.Children.Add(monitor);
        content.Children.Add(Card(Row("\uE7F4", "자동 화면 비율 적용", "입력이 바뀌면 기기에 지정한 화면 비율을 적용합니다.", Toggle("자동 화면 비율 적용", session.Draft.AutoEnabled, value => session.Draft.AutoEnabled = value))));
        var add = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Glyph("\uE710", 12), Text("기기 추가") } } }; add.Click += (_, _) => { try { session.AddDevice(); Navigate("devices", false); } catch (Exception e) { Error(e.Message); } }; Section("연결된 기기", add);
        foreach (var rule in session.Draft.Devices) content.Children.Add(DeviceCard(rule));
        if (session.Draft.Devices.Count == 0) content.Children.Add(Card(Row("\uE710", "첫 번째 기기를 추가하세요", "PC, Switch, PlayStation, Xbox 등의 입력과 비율을 지정합니다.")));
    }
    private Border DeviceCard(DeviceRule rule)
    {
        var stack = new StackPanel { Spacing = 16 };
        var head = new Grid { ColumnSpacing = 16 }; head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Glyph(rule.Name.Contains("PC", StringComparison.OrdinalIgnoreCase) ? "\uE7F4" : "\uE7FC"));
        var name = new DeviceNameEditor(rule.Name, value => rule.Name = value); Grid.SetColumn(name, 1); head.Children.Add(name);
        var toggle = Toggle(rule.Name + " 사용", rule.Enabled, value => rule.Enabled = value); Grid.SetColumn(toggle, 2); head.Children.Add(toggle);
        var remove = new Button { Content = Glyph("\uE74D", 16), Style = (Style)Application.Current.Resources["SubtleButtonStyle"] }; AutomationProperties.SetName(remove, rule.Name + " 삭제"); ToolTipService.SetToolTip(remove, "기기 삭제"); remove.Click += (_, _) => { session.Draft.Devices.Remove(rule); Navigate("devices", false); }; Grid.SetColumn(remove, 3); head.Children.Add(remove); stack.Children.Add(head);
        var fields = new Grid { ColumnSpacing = 16, Margin = new Thickness(44, 0, 0, 0) }; fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        fields.Children.Add(Choices(session.Draft.AvailableInputs, rule.Input, "입력 포트", value => rule.Input = value)); var aspect = Choices(session.Draft.AspectModes, rule.Aspect, "화면 비율", value => rule.Aspect = value); Grid.SetColumn(aspect, 1); fields.Children.Add(aspect); stack.Children.Add(fields); return Card(stack);
    }
    private void BuildGeneral()
    {
        heading.Text = "일반";
        content.Children.Add(Card(Row("\uE713", "자동 화면 비율 적용", "등록한 기기의 화면 비율을 자동으로 맞춥니다.", Toggle("자동 적용", session.Draft.AutoEnabled, value => session.Draft.AutoEnabled = value))));
        content.Children.Add(Card(Row("\uE7E8", "Windows 로그인 시 시작", "로그인할 때 트레이에서 실행합니다.", Toggle("로그인 시 시작", session.StartAtLogin, value => session.StartAtLogin = value))));
        var number = new NumberBox { Value = session.Draft.PollMilliseconds / 1000.0, Minimum = 1, Maximum = 10, SmallChange = .5, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 120 }; AutomationProperties.SetName(number, "입력 확인 간격 (초)"); number.ValueChanged += (_, _) => { if (!double.IsNaN(number.Value)) session.Draft.PollMilliseconds = (int)(number.Value * 1000); };
        content.Children.Add(Card(Row("\uE823", "입력 확인 간격", "1~10초 · 기본 2초", number)));
        Section("데이터 관리");
        var reset = new Button { Content = "앱 초기화…" }; reset.Click += async (_, _) => await ResetApp();
        content.Children.Add(Card(Row("\uE777", "모든 데이터 삭제 및 초기화", "모니터·기기·화면 비율, 설정 백업과 로그를 삭제하고 자동 시작을 해제합니다.", reset)));
        Section("앱 정보"); content.Children.Add(Card(Row("\uE946", "Aspect Switch", "버전 1.0.0 · WinUI 3\n모니터 제조사와 무관한 독립 유틸리티입니다.")));
    }
    private void BuildCompatibility()
    {
        heading.Text = "모니터 호환성";
        var select = new ComboBox { Header = "대상 모니터", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "모니터를 검색하세요" };
        for (int i = 0; i < candidates.Count; i++) select.Items.Add(new ComboBoxItem { Content = candidates[i].Name + " · 장치 " + (i + 1), Tag = candidates[i] });
        if (select.Items.Count > 0) select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => {
            var candidate = (MonitorCandidate)item.Tag;
            return session.Draft.MonitorId.Length > 0 ? String.Equals(candidate.Id, session.Draft.MonitorId, StringComparison.OrdinalIgnoreCase) : String.Equals(candidate.Name, session.Draft.MonitorName, StringComparison.OrdinalIgnoreCase);
        }) ?? select.Items[0];
        var scan = new Button { Content = "모니터 검색", IsEnabled = !session.Preview }; var use = new Button { Content = "선택한 모니터 사용", IsEnabled = !session.Preview && candidates.Count > 0 };
        scan.Click += async (_, _) => await Hardware(async () => {
            var found = EditorSession.SearchResults(await session.ReadHardware("list"));
            candidates.Clear(); candidates.AddRange(found);
            return found.Length == 0 ? "검색된 모니터가 없습니다. PC와 영상 케이블 연결 및 DDC/CI 설정을 확인하세요." : "모니터 " + found.Length + "대를 찾았습니다. 기존 등록 정보는 유지됩니다.";
        });
        use.Click += async (_, _) => { if (select.SelectedItem is ComboBoxItem item) await Hardware(async () => { var r = await session.ReadHardware("inspect", (MonitorCandidate)item.Tag); session.Configure(One(r)); return "모니터를 선택했습니다. 기기와 화면에서 입력을 설정하세요."; }); };
        var chooser = new StackPanel { Spacing = 16 }; chooser.Children.Add(select); chooser.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { scan, use } }); chooser.Children.Add(Text("다른 모니터를 선택하면 기기 목록을 새로 설정합니다.", 12, true)); content.Children.Add(Card(chooser));
        var forget = new Button { Content = "모니터 등록 해제…", IsEnabled = session.Draft.MonitorName.Length > 0 };
        forget.Click += async (_, _) => await ForgetMonitor();
        Section("화면 비율 프로필", forget); content.Children.Add(Card(Row("\uE7F4", session.Draft.MonitorName.Length == 0 ? "모니터를 선택하세요" : session.Draft.MonitorName, Catalog.IsKnown(session.Draft.MonitorName) ? "검증된 모델 프리셋 · 입력 " + session.Draft.AvailableInputs.Length + "개 / 비율 " + session.Draft.AspectModes.Length + "개" : "사용자 등록 프로필 · 실제 화면 확인 필요")));
        var learnStack = new StackPanel { Spacing = 16 }; learnStack.Children.Add(Row("\uE8A7", "OSD에서 선택한 비율 등록", "모니터 OSD에서 원하는 비율을 선택한 뒤 이름을 붙여 등록하세요."));
        var modeName = new TextBox { Header = "화면 비율 이름", Text = "원본 화면 비율", MaxLength = 80, HorizontalAlignment = HorizontalAlignment.Stretch }; learnStack.Children.Add(modeName);
        var capture = new Button { Content = "현재 비율 등록", IsEnabled = !session.Preview && session.Draft.MonitorName.Length > 0 };
        capture.Click += async (_, _) => await Hardware(async () => { var r = await session.ReadHardware("inspect", new MonitorCandidate { Name = session.Draft.MonitorName, Id = session.Draft.MonitorId }); session.Learn(One(r), modeName.Text); return "화면 비율을 등록했습니다. 기기와 화면에서 선택하세요."; }); learnStack.Children.Add(capture); content.Children.Add(Card(learnStack));
        var note = Text("설정 창이 열린 동안 자동 적용은 잠시 멈춥니다. G-Menu 지원 여부만으로 모든 모델의 호환성을 보장하지는 않습니다.", 12, true); note.Margin = new Thickness(0, 16, 0, 0); content.Children.Add(note);
    }
    private static Image AppImage(double size) => new() { Width = size, Height = size, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"))) };
    private async Task<bool> Confirm(string title, string message, string action)
    {
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = title, Content = message,
            PrimaryButtonText = action, CloseButtonText = "취소", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task ForgetMonitor()
    {
        if (!await Confirm("모니터 등록을 해제할까요?", "이 모니터의 기기와 화면 비율 프로필을 함께 제거합니다. 변경 사항 저장을 눌러야 적용됩니다.", "등록 해제")) return;
        session.RemoveMonitor(); candidates.Clear(); Navigate("compatibility", false); Success("등록을 해제했습니다. 변경 사항을 저장하면 적용됩니다.");
    }
    private async Task ResetApp()
    {
        if (!await Confirm("모든 데이터를 삭제할까요?", "등록한 모니터와 기기, 화면 비율 프로필, 일반 설정, 설정 백업, 로그·진단 파일을 삭제하고 Windows 자동 시작을 해제합니다. 즉시 적용되며 되돌릴 수 없습니다. 프로그램 파일은 유지됩니다.", "모두 삭제 및 초기화")) return;
        try {
            session.ResetAllData();
            if (session.Preview) { candidates.Clear(); history.Clear(); Navigate("compatibility", false); Success("미리보기만 초기화했습니다. 실제 데이터는 그대로입니다."); }
            else Close();
        } catch (Exception e) { Error(e.Message); }
    }
    private static MonitorCandidate One(Reading reading) => reading.Monitors?.Length == 1 ? reading.Monitors[0] : throw new Exception(reading.Error ?? "모니터 확인 실패");
    private async Task Hardware(Func<Task<string>> work)
    {
        busy = true; navigation.IsEnabled = false; navigation.IsPaneVisible = true; save.IsEnabled = false; cancel.IsEnabled = false;
        notice.Severity = InfoBarSeverity.Informational; notice.Message = "모니터와 통신 중…"; notice.IsOpen = true;
        string? message = null; Exception? failure = null;
        try { message = await work(); } catch (Exception e) { failure = e; }
        finally { busy = false; navigation.IsEnabled = true; save.IsEnabled = true; cancel.IsEnabled = true; }
        Navigate("compatibility", false); if (failure != null) Error(failure.Message); else Success(message!);
    }
    private void Error(string text) { notice.Severity = InfoBarSeverity.Error; notice.Message = text; notice.IsOpen = true; }
    private void Success(string text) { notice.Severity = InfoBarSeverity.Success; notice.Message = text; notice.IsOpen = true; }
    private void Save() { try { session.Save(); if (session.Preview) Success("미리보기 설정을 확인했습니다. 실제 설정과 모니터는 변경하지 않았습니다."); else Close(); } catch (Exception e) { Error(e.Message); } }
    private async Task Render(string path)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
        var pixels = await bitmap.GetPixelsAsync(); File.WriteAllBytes(Path.GetFullPath(path), Array.Empty<byte>()); var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        using var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels); var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes); await encoder.FlushAsync();
    }
    private async Task Smoke()
    {
        var checks = new List<string>();
        try
        {
            if (!session.Preview) throw new Exception("Smoke tests require --preview.");
            foreach (var page in new[] { "devices", "compatibility", "general" }) { Navigate(page, false); await Task.Delay(80); if (content.Children.Count == 0 || content.ActualWidth < 300) throw new Exception("Page layout failed: " + page); checks.Add("page:" + page); }
            string editedName = "PC";
            var nameEditor = new DeviceNameEditor(editedName, value => editedName = value);
            content.Children.Add(nameEditor); await Task.Delay(80);
            nameEditor.Selector.SelectedItem = "Nintendo Switch 2"; await Task.Delay(40);
            if (editedName != "Nintendo Switch 2" || nameEditor.CustomInput.Visibility != Visibility.Collapsed) throw new Exception("Preset name selection failed.");
            nameEditor.Selector.SelectedItem = DeviceNameEditor.CustomOption;
            nameEditor.CustomInput.Text = "거실 콘솔"; await Task.Delay(40);
            if (editedName != "거실 콘솔" || nameEditor.CustomInput.Visibility != Visibility.Visible) throw new Exception("Custom name entry failed: name=" + editedName + "; visibility=" + nameEditor.CustomInput.Visibility + "; selection=" + nameEditor.Selector.SelectedItem + "; text=" + nameEditor.CustomInput.Text);
            nameEditor.Selector.SelectedItem = "PC"; await Task.Delay(40); nameEditor.Selector.SelectedItem = DeviceNameEditor.CustomOption; await Task.Delay(40);
            if (editedName != "거실 콘솔") throw new Exception("Custom name draft was lost.");
            checks.Add("name-preset-custom-roundtrip");
            bool changedOnLoad = false;
            var legacyName = new DeviceNameEditor("내 게임기", _ => changedOnLoad = true);
            if (changedOnLoad || (string)legacyName.Selector.SelectedItem != DeviceNameEditor.CustomOption || legacyName.CustomInput.Text != "내 게임기") throw new Exception("Existing custom name was changed.");
            checks.Add("existing-custom-name-preserved");
            session.AddDevice(); session.AddDevice(); session.Draft.Validate(); checks.Add("add-four-devices");
            session.Draft.Devices[2].Input = session.Draft.Devices[0].Input; bool rejected = false; try { session.Save(); } catch { rejected = true; } if (!rejected) throw new Exception("Duplicate input accepted."); session.Draft.Devices.RemoveRange(2, 2); checks.Add("duplicate-validation");
            var clone = Settings.FromJson(session.Draft.ToJson()); if (clone.Devices.Count != 2) throw new Exception("Shared serializer roundtrip failed."); checks.Add("shared-settings-roundtrip");
            bool blocked = false; try { await session.ReadHardware("input"); } catch { blocked = true; } if (!blocked) throw new Exception("Preview allowed hardware."); checks.Add("preview-blocks-hardware");
            session.AddDevice(); session.AddDevice();
            var found = EditorSession.SearchResults(new Reading { Ok = true, Monitors = new[] { new MonitorCandidate { Name = "AOC AGP346UCSD", Id = "fixture" } } });
            if (found.Length != 1 || session.Draft.Devices.Count != 4) throw new Exception("Search modified full device list."); checks.Add("search-with-all-inputs-registered");
            if (EditorSession.SearchResults(new Reading { Ok = true, Monitors = Array.Empty<MonitorCandidate>() }).Length != 0) throw new Exception("Empty search failed."); checks.Add("empty-search");
            bool searchRejected = false; try { EditorSession.SearchResults(new Reading { Error = "fixture error" }); } catch (Exception e) { searchRejected = e.Message == "fixture error"; }
            if (!searchRejected) throw new Exception("Search hid failure."); checks.Add("search-failure-detail");
            session.StartAtLogin = true; session.Draft.PollMilliseconds = 3500; session.RemoveMonitor();
            if (session.Draft.MonitorName.Length != 0 || session.Draft.Devices.Count != 0 || session.Draft.AspectModes.Length != 0 || session.Draft.AvailableInputs.Length != 0 || session.Draft.PollMilliseconds != 3500 || !session.StartAtLogin) throw new Exception("Unregister did not isolate monitor settings.");
            Navigate("compatibility", false); checks.Add("unregister-monitor");
            session.ResetAllData(); session.Draft.Validate(); Navigate("general", false);
            if (session.StartAtLogin || session.Draft.PollMilliseconds != 2000) throw new Exception("Reset defaults failed."); checks.Add("reset-preview-defaults");
            string ipc = Path.Combine(Path.GetTempPath(), "AspectUiContract-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(ipc);
            try {
                string request = Path.Combine(ipc, "request.json"), response = Path.Combine(ipc, "response.json");
                File.WriteAllText(request, JsonSerializer.Serialize(new UiRequest { Settings = Settings.KnownMonitor(), SessionId = "test", StartAtLogin = true }, EditorSession.Json));
                var isolated = new EditorSession(new[] { "--request", request, "--response", response });
                isolated.ResetAllData();
                var result = JsonSerializer.Deserialize<UiResponse>(File.ReadAllText(response), EditorSession.Json)!;
                if (!result.ResetAllData || result.StartAtLogin || result.SessionId != "test" || result.Settings.MonitorName.Length != 0) throw new Exception("Reset IPC contract failed.");
                checks.Add("reset-ipc-response");
            } finally { Directory.Delete(ipc, true); }
            File.WriteAllText(session.SmokePath!, JsonSerializer.Serialize(new { Passed = true, Checks = checks }, EditorSession.Json));
        }
        catch (Exception e) { File.WriteAllText(session.SmokePath!, JsonSerializer.Serialize(new { Passed = false, Error = e.ToString(), Checks = checks }, EditorSession.Json)); }
        Close();
    }
}
