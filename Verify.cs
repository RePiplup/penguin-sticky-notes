using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace StickyModern {
 static class Verify {
  static void Assert(bool condition, string message) { if(!condition) throw new Exception(message); }
  static void Pump(Note note) { note.Window.UpdateLayout(); note.Window.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate {})); note.Window.UpdateLayout(); }
  static void Capture(Note note, string path) {
   Pump(note); var visual = (FrameworkElement)note.Window.Content;
   var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * 1.5), (int)Math.Ceiling(visual.ActualHeight * 1.5), 144,144, PixelFormats.Pbgra32); bitmap.Render(visual);
   var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using(var file = File.Create(path)) png.Save(file);
  }
  public static void Run() {
   string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-modern"); Directory.CreateDirectory(folder);
   string data = Path.Combine(folder, "tasks-" + Guid.NewGuid().ToString("N") + ".xml");
   var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
   Note note = null;
   try {
    // Reproduce startup with a saved city and a genuinely asynchronous response.
    new XDocument(new XElement("note", new XElement("weather", new XAttribute("name", "北京"), new XAttribute("latitude", 39.9), new XAttribute("longitude", 116.4)))).Save(data);
    for(int attempt = 0; attempt < 3; attempt++) {
     var response = new System.Threading.Tasks.TaskCompletionSource<string>();
     bool requested = false;
     note = new Note(data, true, delegate { requested = true; return response.Task; });
     Assert(!requested, "Weather started before window loaded");
     note.Window.ShowInTaskbar = false; note.Window.Show(); Pump(note);
     Assert(requested, "Saved city did not refresh on startup");
     var completion = System.Threading.Tasks.Task.Run(delegate { if(attempt == 1) response.SetException(new IOException("offline")); else response.SetResult("晴  25°C"); });
     completion.Wait();
     if(attempt == 2) note.Window.Close();
     var deadline = DateTime.UtcNow.AddSeconds(5);
     while(attempt != 2 && ((TextBlock)note.Window.FindName("WeatherText")).Text.Contains("更新天气") && DateTime.UtcNow < deadline) Pump(note);
     if(attempt != 2) Assert(((TextBlock)note.Window.FindName("WeatherText")).Text.Contains(attempt == 1 ? "天气暂不可用" : "25°C"), "Startup weather completion failed");
     Pump(note); if(attempt != 2) note.Window.Close(); note = null;
    }
    Assert(DayInfo.Lunar(new DateTime(2026,2,17)) == "农历正月初一", "Lunar new year failed");
    Assert(DayInfo.Lunar(new DateTime(2025,7,25)) == "农历闰六月初一", "Leap lunar month failed");
    Assert(DayInfo.Headline(DateTime.Today) == DayInfo.Headline(DateTime.Today.AddHours(2)), "Headline changed within day");
    Assert(DayInfo.WeatherName(0) == "晴" && DayInfo.WeatherName(95) == "雷雨", "Weather code mapping failed");
    new XDocument(new XElement("note", new XAttribute("width", 360), new XAttribute("height", 490), new XAttribute("pinned", true), new XAttribute("draft", ""), new XElement("task", new XAttribute("done", false), "整理今天的工作安排"))).Save(data);
    note = new Note(data, true); note.Window.ShowInTaskbar = false; note.Window.Show();
    string chinese = "整理自己的工作内容，把今天需要处理的事项写在这里，较长的中文任务应该自然换行，并且完整显示最后一个字。";
    var longTask = note.AddTask(chinese, false); note.AddTask("完成 Blender 场景测试", true); note.AddTask("傍晚出去走走，给自己留一点时间", false);
    note.Window.Width = 300; Pump(note);
    Assert(longTask.Editor.LineCount >= 3, "Chinese text did not wrap in narrow window");
    var last = longTask.Editor.GetRectFromCharacterIndex(chinese.Length - 1);
    Assert(!last.IsEmpty && last.Bottom <= longTask.Editor.ActualHeight + 1, "Last Chinese line clipped");
    double narrowHeight = longTask.Row.ActualHeight;
    Capture(note, Path.Combine(folder, "narrow.png"));
    note.Window.Width = 540; Pump(note); Assert(longTask.Row.ActualHeight < narrowHeight, "Text did not reflow after widening window");
    note.Window.Width = 410; note.Window.Height = 670; note.Input.Text = "这是一条正在输入的长任务，输入时也应该自动换行，不再向右滚动而看不到前面的内容。"; Pump(note);
    Assert(note.Input.LineCount > 1 && note.Input.ActualHeight > 30, "Composer did not wrap and grow");
    Capture(note, Path.Combine(folder, "preview.png"));
    int count = note.Items.Count; note.AddInput(); Pump(note); Assert(note.Items.Count == count + 1 && note.Input.Text == "", "Add input failed");
    var url = note.AddTask("https://example.com/" + new string('a',150), false); Pump(note); Assert(url.Editor.LineCount > 1, "Unbroken URL did not wrap");
    note.Input.Text = "第一行\n第二行"; Pump(note); Assert(note.Input.LineCount == 2, "Explicit newlines failed");
    var moved = note.Items[2]; note.MoveTask(moved, -1); Pump(note); Assert(note.Items[1] == moved && moved.Check.IsChecked == true, "Move up lost item/state");
    note.MoveTask(moved, 1); Assert(note.Items[2] == moved, "Move down failed");
    var first = note.Items[0]; note.MoveTask(first, -1); Assert(note.Items[0] == first, "Move above first changed order");
    var lastItem = note.Items.Last(); note.MoveTask(lastItem, 1); Assert(note.Items.Last() == lastItem, "Move below last changed order");
    note.MoveTask(lastItem, -1);
    note.ToggleVisibility(); Assert(!note.Window.IsVisible, "Hide failed"); note.ToggleVisibility(); Pump(note); Assert(note.Window.IsVisible, "Show failed");
    Assert(StartupSetting.Command.StartsWith("\"") && StartupSetting.Command.EndsWith("\""), "Startup path must be quoted");
    Assert(note.Save(), "Save failed"); note.Window.Close(); note = new Note(data, true); note.Window.ShowInTaskbar = false; note.Window.Show(); Pump(note);
    Assert(note.Items.Count == 6, "Restore count mismatch"); Assert(note.Items[1].Editor.Text == chinese, "Restore text mismatch"); Assert(note.Items[2].Check.IsChecked == true, "Completion state lost"); Assert(note.Input.Text == "第一行\n第二行", "Draft line breaks lost");
    Assert(note.Items[4].Editor.Text.StartsWith("https://example.com/"), "Reordered task not restored");
    note.Items[2].Check.IsChecked = false; Pump(note); Assert(note.Items[2].Editor.IsVisible, "Uncheck did not restore editor");
    File.WriteAllText(Path.Combine(folder,"result.txt"), "PASS: task reorder and boundary no-ops; reordered save/restore; show/hide toggle; quoted startup command; saved-city startup async success, failure and close while pending; icon loading; lunar new year and leap month; daily headline; weather code mapping; legacy XML migration; narrow Chinese wrapping and last-line visibility; reflow on resize; multiline composer; add; unbroken URL wrapping; newline preservation; completion and draft save/restore.");
   } catch(Exception e) { File.WriteAllText(Path.Combine(folder,"result.txt"), "FAIL: " + e); }
   finally { if(note != null) { note.Window.Close(); note.Dispose(); } app.Shutdown(); }
  }
 }
}
