using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Forms = System.Windows.Forms;

namespace StickyModern {
 class TaskItem {
  public Grid Row; public TextBox Editor; public TextBlock DoneText; public CheckBox Check;
 }
 class Note : IDisposable {
  public Window Window;
  public List<TaskItem> Items = new List<TaskItem>();
  public TextBox Input;
  StackPanel tasks; TextBlock status, placeholder; Button pin;
  DispatcherTimer timer = new DispatcherTimer(); Forms.NotifyIcon tray;
  readonly string path; readonly bool test;
  bool loading = true, dirty; string theme = "cream";
  bool hotkeyEnabled = true, hotkeyRegistered; HwndSource hotkeySource;
  const int HotkeyId = 0x4745;
  MenuItem hotkeyOption;
  WeatherCity city; bool disposed, weatherBusy; DateTime nextWeather = DateTime.MinValue; DispatcherTimer clock = new DispatcherTimer(); System.Drawing.Icon gooseIcon;
  public Note(string dataPath, bool testing, Func<WeatherCity, System.Threading.Tasks.Task<string>> fetchWeather = null) {
   path = dataPath; test = testing;
   weatherFetch = fetchWeather ?? WeatherService.Fetch;
   using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Modern.xaml")) Window = (Window)System.Windows.Markup.XamlReader.Load(stream);
   var rounded = new FontFamily(); rounded.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-024F", Target = "Arial Rounded MT Bold" }); rounded.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-FFFF", Target = "幼圆" }); Window.FontFamily = rounded;
   var textStyle = new Style(typeof(TextBox), (Style)Window.Resources[typeof(TextBox)]); textStyle.Setters.Add(new Setter(Control.FontFamilyProperty, rounded)); Window.Resources[typeof(TextBox)] = textStyle;
   WindowChrome.SetWindowChrome(Window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(12) });
   tasks = Find<StackPanel>("Tasks"); Input = Find<TextBox>("Input"); status = Find<TextBlock>("Status"); placeholder = Find<TextBlock>("Placeholder"); pin = Find<Button>("Pin");
   var logo = new BitmapImage(); using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("goose.png")) { logo.BeginInit(); logo.CacheOption = BitmapCacheOption.OnLoad; logo.StreamSource = stream; logo.DecodePixelWidth = 256; logo.EndInit(); logo.Freeze(); } Window.Icon = logo; Find<ImageBrush>("GooseBrush").ImageSource = logo;
   using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("goose.ico")) { using(var icon = new System.Drawing.Icon(stream)) gooseIcon = (System.Drawing.Icon)icon.Clone(); }
   Find<Button>("Weather").Click += delegate { ChooseCity(); };
   Find<Grid>("DragBar").MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if(e.OriginalSource is Grid || e.OriginalSource is TextBlock || e.OriginalSource is System.Windows.Shapes.Ellipse) Window.DragMove(); };
   pin.Click += delegate { Window.Topmost = !Window.Topmost; UpdatePin(); Changed(); };
   Find<Button>("HideNote").Click += delegate { Window.Hide(); };
   Find<Button>("CloseNote").Click += delegate { Window.Close(); };
   Find<Button>("Add").Click += delegate { AddInput(); };
   Input.TextChanged += delegate { placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Changed(); };
   Input.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if(e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift && !composing) { AddInput(); e.Handled = true; } };
   TextCompositionManager.AddPreviewTextInputStartHandler(Input, delegate { composing = true; });
   TextCompositionManager.AddPreviewTextInputHandler(Input, delegate { composing = false; });
   BuildMenu();
   timer.Interval = TimeSpan.FromMilliseconds(400); timer.Tick += delegate { timer.Stop(); Save(); };
   Window.Topmost = true; Load(); loading = false; UpdateStatus(); UpdatePin();
   UpdateDay(); clock.Interval = TimeSpan.FromMinutes(1); clock.Tick += delegate { UpdateDay(); if(!test && DateTime.Now >= nextWeather) RefreshWeather(); }; clock.Start();
   // Start async UI work only after WPF installs its dispatcher synchronization context.
   Window.Loaded += delegate { if(!test || fetchWeather != null) RefreshWeather(); };
   Window.SizeChanged += delegate { Changed(); }; Window.LocationChanged += delegate { Changed(); };
   Window.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if(!Save()) e.Cancel = MessageBox.Show(Window, "未能保存任务，仍要退出吗？", "鹅鹅便利贴", MessageBoxButton.YesNo) != MessageBoxResult.Yes; };
   Window.Closed += delegate { Dispose(); };
   Window.SourceInitialized += delegate { try { int value = 2; DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle, 33, ref value, 4); } catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {} if(!test) { hotkeySource = HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle); hotkeySource.AddHook(HotkeyHook); ApplyHotkey(); } };
   if(!test) {
    tray = new Forms.NotifyIcon { Icon = gooseIcon, Text = "鹅鹅便利贴", Visible = true };
    var menu = new Forms.ContextMenuStrip(); menu.Items.Add("显示便利贴", null, delegate { Window.Show(); Window.Activate(); }); menu.Items.Add("退出", null, delegate { Window.Close(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Window.Show(); Window.Activate(); };
   }
  }
  bool composing;
  public void ShowNote() { Window.Show(); if(Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal; Window.Activate(); }
  public void ToggleVisibility() { if(Window.IsVisible && Window.WindowState != WindowState.Minimized) Window.Hide(); else ShowNote(); }
  [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
  [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
  IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) { if(msg == 0x0312 && wParam.ToInt32() == HotkeyId) { ToggleVisibility(); handled = true; } return IntPtr.Zero; }
  void ApplyHotkey() {
   if(hotkeySource == null) return;
   if(hotkeyRegistered) { UnregisterHotKey(hotkeySource.Handle, HotkeyId); hotkeyRegistered = false; }
   if(hotkeyEnabled) hotkeyRegistered = RegisterHotKey(hotkeySource.Handle, HotkeyId, 0x4003, 0x4E);
   hotkeyOption.IsChecked = hotkeyEnabled;
   hotkeyOption.Header = hotkeyEnabled && !hotkeyRegistered ? "显示/隐藏快捷键（被占用，点击关闭）" : "显示/隐藏快捷键（Ctrl+Alt+N）";
   if(hotkeyEnabled && !hotkeyRegistered) status.Text = "Ctrl+Alt+N 被占用，可使用托盘显示";
  }
  readonly Func<WeatherCity, System.Threading.Tasks.Task<string>> weatherFetch;
  void UpdateDay() { Find<TextBlock>("Headline").Text = DayInfo.Headline(DateTime.Today); Find<TextBlock>("Date").Text = DateTime.Today.ToString("M月d日 · dddd", CultureInfo.GetCultureInfo("zh-CN")) + "  " + DayInfo.Lunar(DateTime.Today); }
  async void RefreshWeather() {
   if(city == null || weatherBusy || disposed) return; weatherBusy = true; var requested = city; nextWeather = DateTime.Now.AddMinutes(30);
   Find<TextBlock>("WeatherText").Text = city.Name.Split('·')[0].Trim() + " · 更新天气…";
   try { string report = await weatherFetch(requested); if(!disposed && city == requested) { Find<TextBlock>("WeatherText").Text = city.Name.Split('·')[0].Trim() + " · " + report; Find<Button>("Weather").ToolTip = "Open-Meteo · " + DateTime.Now.ToString("HH:mm") + " 更新 · 点击更换城市"; } }
   catch(Exception error) { if(!disposed && city == requested) { Find<TextBlock>("WeatherText").Text = city.Name.Split('·')[0].Trim() + " · 天气暂不可用"; Find<Button>("Weather").ToolTip = WeatherService.ErrorMessage(error) + "。可在更多菜单重试。天气来源：Open-Meteo"; nextWeather = DateTime.Now.AddMinutes(5); } }
   finally { weatherBusy = false; if(!disposed && city != requested) RefreshWeather(); }
  }
  void ChooseCity() {
   var dialog = new Window { Title = "天气城市 · 鹅鹅便利贴", Owner = Window, Width = 360, Height = 340, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#FFFCF4"), FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13, Topmost = Window.Topmost };
   var panel = new StackPanel { Margin = new Thickness(18) }; dialog.Content = panel;
   panel.Children.Add(new TextBlock { Text = "输入城市名称，搜索后选择地区", Margin = new Thickness(0,0,0,12) });
   var query = new TextBox { Text = city == null ? "" : city.Name.Split('·')[0].Trim(), Height = 30 }; panel.Children.Add(query);
   var search = new Button { Content = "搜索城市", Height = 30, Margin = new Thickness(0,8,0,8) }; panel.Children.Add(search);
   var results = new ListBox { Height = 100 }; panel.Children.Add(results);
   var hint = new TextBlock { Text = "常用城市可离线选择 · 天气来自 Open-Meteo", FontSize = 10, Margin = new Thickness(0,8,0,8), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(hint);
   var use = new Button { Content = "使用此城市", Height = 30, IsEnabled = false }; panel.Children.Add(use);
   results.SelectionChanged += delegate { use.IsEnabled = results.SelectedItem != null; };
   search.Click += async delegate { if(string.IsNullOrWhiteSpace(query.Text)) return; search.IsEnabled = false; results.ItemsSource = null; hint.Text = "正在搜索…"; try { var found = await WeatherService.Search(query.Text); results.ItemsSource = found; hint.Text = found.Count == 0 ? "未找到，可尝试城市拼音或英文名" : "请选择正确地区 · 天气来自 Open-Meteo"; if(found.Count == 1) results.SelectedIndex = 0; } catch(Exception error) { hint.Text = WeatherService.ErrorMessage(error); } finally { search.IsEnabled = true; } };
   query.KeyDown += delegate(object s, KeyEventArgs e) { if(e.Key == Key.Enter && search.IsEnabled) search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
   use.Click += delegate { city = (WeatherCity)results.SelectedItem; Changed(); dialog.Close(); RefreshWeather(); }; dialog.ShowDialog();
  }
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int size);
  T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
  static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
  void UpdatePin() { pin.Foreground = Brush(Window.Topmost ? "#73865E" : "#999B92"); pin.ToolTip = Window.Topmost ? "已置顶 · 点击取消" : "置顶显示"; }
  void BuildMenu() {
   var menu = new ContextMenu();
   foreach(string name in new[] {"奶油纸", "雾白纸", "鼠尾草"}) { var item = new MenuItem { Header = name }; string selected = name == "奶油纸" ? "cream" : name == "雾白纸" ? "white" : "sage"; item.Click += delegate { ApplyTheme(selected); Changed(); }; menu.Items.Add(item); }
   menu.Items.Add(new Separator()); var clear = new MenuItem { Header = "清理已完成" }; clear.Click += delegate { foreach(var item in Items.Where(x => x.Check.IsChecked == true).ToArray()) Remove(item); }; menu.Items.Add(clear);
   var location = new MenuItem { Header = "设置天气城市" }; location.Click += delegate { ChooseCity(); }; menu.Items.Add(location); var refresh = new MenuItem { Header = "刷新天气" }; refresh.Click += delegate { RefreshWeather(); }; menu.Items.Add(refresh);
   menu.Items.Add(new Separator());
   var startup = new MenuItem { Header = "开机启动", IsCheckable = true }; menu.Items.Add(startup);
   startup.Click += delegate { try { StartupSetting.SetEnabled(startup.IsChecked); } catch(Exception) { startup.IsChecked = !startup.IsChecked; MessageBox.Show(Window, "无法更改开机启动，请检查系统权限。", "鹅鹅便利贴"); } };
   menu.Opened += delegate { try { startup.IsChecked = StartupSetting.IsEnabled(); } catch { startup.IsEnabled = false; } hotkeyOption.IsChecked = hotkeyEnabled; };
   hotkeyOption = new MenuItem { Header = "显示/隐藏快捷键（Ctrl+Alt+N）", IsCheckable = true, IsChecked = hotkeyEnabled }; menu.Items.Add(hotkeyOption);
   hotkeyOption.Click += delegate { hotkeyEnabled = hotkeyOption.IsChecked; Changed(); ApplyHotkey(); };
   Find<Button>("More").Click += delegate { menu.PlacementTarget = Find<Button>("More"); menu.IsOpen = true; };
  }
  void ApplyTheme(string value) { theme = value; var paper = Brush(value == "white" ? "#FAFAF8" : value == "sage" ? "#EFF3E9" : "#FFFCF4"); Window.Background = paper; Find<Border>("Paper").Background = paper; Find<Border>("Composer").Background = Brush(value == "white" ? "#EFEFEC" : value == "sage" ? "#E2E9DB" : "#F2F0E5"); }
  public void AddInput() { if(string.IsNullOrWhiteSpace(Input.Text)) return; AddTask(Input.Text.Trim(), false); Input.Clear(); Changed(); Input.Focus(); Window.Dispatcher.BeginInvoke(new Action(delegate { Find<ScrollViewer>("TaskScroll").ScrollToEnd(); }), DispatcherPriority.Background); }
  public TaskItem AddTask(string text, bool done) {
   var item = new TaskItem();
   var row = new Grid { Margin = new Thickness(3,0,0,0), MinHeight = 47 };
   row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
   var check = new CheckBox { IsChecked = done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0,11,0,0) };
   System.Windows.Automation.AutomationProperties.SetName(check, "标记完成");
   var editor = new TextBox { Text = text, Margin = new Thickness(1,10,3,10), VerticalAlignment = VerticalAlignment.Top };
   System.Windows.Automation.AutomationProperties.SetName(editor, "任务内容");
   var finished = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextDecorations = TextDecorations.Strikethrough, FontSize = 16, Foreground = Brush("#9CAFBF"), Margin = new Thickness(1,12,3,12) };
   Grid.SetColumn(editor,1); Grid.SetColumn(finished,1); row.Children.Add(check); row.Children.Add(editor); row.Children.Add(finished);
   item.Row = row; item.Check = check; item.Editor = editor; item.DoneText = finished;
   Action style = delegate { editor.Visibility = check.IsChecked == true ? Visibility.Collapsed : Visibility.Visible; finished.Visibility = check.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; };
   check.Checked += delegate { style(); Changed(); }; check.Unchecked += delegate { style(); Changed(); };
   editor.TextChanged += delegate { finished.Text = editor.Text; Changed(); }; style();
   var menu = new ContextMenu(); var edit = new MenuItem { Header = "编辑任务" }; edit.Click += delegate { check.IsChecked = false; editor.Focus(); }; menu.Items.Add(edit); var delete = new MenuItem { Header = "删除任务" }; delete.Click += delegate { Remove(item); }; menu.Items.Add(delete); row.ContextMenu = menu;
   var up = new MenuItem { Header = "上移", InputGestureText = "Alt+↑" }; up.Click += delegate { MoveTask(item, -1); }; menu.Items.Insert(0, up);
   var down = new MenuItem { Header = "下移", InputGestureText = "Alt+↓" }; down.Click += delegate { MoveTask(item, 1); }; menu.Items.Insert(1, down); menu.Items.Insert(2, new Separator());
   menu.Opened += delegate { up.IsEnabled = Items.IndexOf(item) > 0; down.IsEnabled = Items.IndexOf(item) < Items.Count - 1; };
   row.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { Key key = e.Key == Key.System ? e.SystemKey : e.Key; if(Keyboard.Modifiers == ModifierKeys.Alt && (key == Key.Up || key == Key.Down)) { MoveTask(item, key == Key.Up ? -1 : 1); e.Handled = true; } };
   // Keep the editor's native cut/copy/paste menu while exposing task actions there too.
   var editorMenu = new ContextMenu();
   editorMenu.Items.Add(new MenuItem { Header = "剪切", Command = ApplicationCommands.Cut, CommandTarget = editor });
   editorMenu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy, CommandTarget = editor });
   editorMenu.Items.Add(new MenuItem { Header = "粘贴", Command = ApplicationCommands.Paste, CommandTarget = editor });
   editorMenu.Items.Add(new Separator());
   var editorUp = new MenuItem { Header = "上移", InputGestureText = "Alt+↑" }; editorUp.Click += delegate { MoveTask(item, -1); }; editorMenu.Items.Add(editorUp);
   var editorDown = new MenuItem { Header = "下移", InputGestureText = "Alt+↓" }; editorDown.Click += delegate { MoveTask(item, 1); }; editorMenu.Items.Add(editorDown);
   var editorDelete = new MenuItem { Header = "删除任务" }; editorDelete.Click += delegate { Remove(item); }; editorMenu.Items.Add(editorDelete);
   editorMenu.Opened += delegate { editorUp.IsEnabled = Items.IndexOf(item) > 0; editorDown.IsEnabled = Items.IndexOf(item) < Items.Count - 1; }; editor.ContextMenu = editorMenu;
   Items.Add(item); tasks.Children.Add(row); return item;
  }
  public void MoveTask(TaskItem item, int offset) { int from = Items.IndexOf(item), to = from + offset; if(from < 0 || to < 0 || to >= Items.Count) return; bool focusEditor = item.Editor.IsKeyboardFocusWithin; int caret = item.Editor.CaretIndex; Items.RemoveAt(from); Items.Insert(to, item); tasks.Children.Remove(item.Row); tasks.Children.Insert(to, item.Row); if(focusEditor) { item.Editor.Focus(); item.Editor.CaretIndex = caret; } else item.Check.Focus(); item.Row.BringIntoView(); Changed(); }
  void Remove(TaskItem item) { Items.Remove(item); tasks.Children.Remove(item.Row); Changed(); }
  void UpdateStatus() { int done = Items.Count(x => x.Check.IsChecked == true); status.Text = Items.Count == 0 ? "留一点空间，给今天的想法" : done + " / " + Items.Count + " 已完成"; }
  void Changed() { if(loading) return; dirty = true; UpdateStatus(); timer.Stop(); timer.Start(); }
  public bool Save() {
   if(loading || !dirty) return true;
   try {
    var root = new XElement("note", new XAttribute("x", double.IsNaN(Window.Left) ? 100 : Window.Left), new XAttribute("y", double.IsNaN(Window.Top) ? 100 : Window.Top), new XAttribute("width", Window.Width), new XAttribute("height", Window.Height), new XAttribute("pinned", Window.Topmost), new XAttribute("draft", Input.Text), new XAttribute("theme", theme), new XAttribute("layoutVersion", 3));
    root.SetAttributeValue("hotkeyEnabled", hotkeyEnabled);
    foreach(var item in Items) root.Add(new XElement("task", new XAttribute("done", item.Check.IsChecked == true), item.Editor.Text));
    if(city != null) root.Add(new XElement("weather", new XAttribute("name", city.Name), new XAttribute("latitude", city.Latitude), new XAttribute("longitude", city.Longitude)));
    Directory.CreateDirectory(Path.GetDirectoryName(path)); new XDocument(root).Save(path + ".tmp"); if(File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak"); else File.Move(path + ".tmp", path); dirty = false; UpdateStatus(); return true;
   } catch { status.Text = "保存失败，请检查磁盘权限"; return false; }
  }
  void Load() {
   if(!File.Exists(path)) { Window.WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
   XDocument doc;
   try { doc = XDocument.Load(path); } catch { try { doc = XDocument.Load(path + ".bak"); } catch { throw new InvalidDataException("无法读取任务文件及备份。原文件已保留：" + path); } }
   var root = doc.Root;
   hotkeyEnabled = (bool?)root.Attribute("hotkeyEnabled") ?? true;
   var weather = root.Element("weather"); if(weather != null) city = new WeatherCity { Name = (string)weather.Attribute("name"), Latitude = (double)weather.Attribute("latitude"), Longitude = (double)weather.Attribute("longitude") };
   if((int?)root.Attribute("layoutVersion") == 3) { Window.Width = Math.Max(300, Math.Min(1200, (double?)root.Attribute("width") ?? 390)); Window.Height = Math.Max(350, Math.Min(1200, (double?)root.Attribute("height") ?? 550)); }
   double left = (double?)root.Attribute("x") ?? 100, top = (double?)root.Attribute("y") ?? 100;
   Window.Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Window.Width)); Window.Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Window.Height));
   Window.Topmost = (bool?)root.Attribute("pinned") ?? true; Input.Text = (string)root.Attribute("draft") ?? ""; ApplyTheme((string)root.Attribute("theme") ?? "cream");
   foreach(var task in root.Elements("task")) AddTask(task.Value, (bool?)task.Attribute("done") ?? false);
  }
  public void Dispose() { disposed = true; timer.Stop(); clock.Stop(); if(hotkeySource != null) { if(hotkeyRegistered) UnregisterHotKey(hotkeySource.Handle, HotkeyId); hotkeySource.RemoveHook(HotkeyHook); hotkeySource = null; hotkeyRegistered = false; } if(tray != null) { tray.Dispose(); tray = null; } if(gooseIcon != null) { gooseIcon.Dispose(); gooseIcon = null; } }
 }
 static class StartupSetting {
  const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
  const string ValueName = "GooseStickyNotes";
  public static string Command { get { return "\"" + Assembly.GetExecutingAssembly().Location + "\""; } }
  public static bool IsEnabled() { using(var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && string.Equals(key.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase); }
  public static void SetEnabled(bool enabled) { using(var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) { if(enabled) key.SetValue(ValueName, Command); else key.DeleteValue(ValueName, false); } }
 }
 static class Program {
  [STAThread] static void Main(string[] args) {
   try {
    if(args.Length > 0 && args[0] == "--verify-weather") {
     string report; try { var local = WeatherService.Search("北京").GetAwaiter().GetResult(); var remote = WeatherService.Search("Beijing").GetAwaiter().GetResult(); if(local.Count == 0 || remote.Count == 0) throw new Exception("City search returned no results"); report = "PASS: standalone EXE; offline Beijing; online Beijing; weather: " + WeatherService.Fetch(local[0]).GetAwaiter().GetResult(); } catch(Exception error) { report = "FAIL: " + error; }
     File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "weather-result.txt"), report); return;
    }
    if(args.Length > 0 && args[0] == "--verify") { Verify.Run(); return; }
    bool created;
    using(var showRequest = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Local\\StickyTasks.Show"))
    using(var mutex = new System.Threading.Mutex(true, "Local\\StickyTasks.Desktop", out created)) {
     if(!created) { showRequest.Set(); return; }
     var app = new Application(); var note = new Note(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyTasks", "tasks.xml"), false);
     var activation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
     activation.Tick += delegate {
      if(!showRequest.WaitOne(0)) return;
      note.Window.Show();
      if(note.Window.WindowState == WindowState.Minimized) note.Window.WindowState = WindowState.Normal;
      note.Window.Activate();
     };
     activation.Start();
     try { app.Run(note.Window); } finally { activation.Stop(); }
    }
   } catch(Exception e) { MessageBox.Show(e.Message, "便利贴启动失败"); }
  }
 }
}
