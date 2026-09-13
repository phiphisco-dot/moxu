using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;

[DataContract] public class PlanItem {
 [DataMember] public string Id = Guid.NewGuid().ToString();
 [DataMember] public string Title = "";
 [DataMember] public string Time = "";
 [DataMember] public string Note = "";
 [DataMember] public bool Important;
 [DataMember] public bool Done;
}
[DataContract] public class PlanDay {
 [DataMember] public string Date = "";
 [DataMember] public string Journal = "";
 [DataMember] public List<PlanItem> Items = new List<PlanItem>();
}
[DataContract] public class PlanData {
 [DataMember] public int Version = 1;
 [DataMember] public List<PlanDay> Days = new List<PlanDay>();
}
public class Storage {
 public string FilePath;
 public PlanData Data = new PlanData();
 public bool Recovered;
 public Storage(string folder) { Directory.CreateDirectory(folder); FilePath = Path.Combine(folder,"plans.json"); }
 static PlanData Read(string path) {
  using(var f=File.OpenRead(path)) {
   var d=(PlanData)new DataContractJsonSerializer(typeof(PlanData)).ReadObject(f);
   if(d==null || d.Version!=1 || d.Days==null) throw new InvalidDataException("不支持的数据格式");
   var seen=new HashSet<string>();
   foreach(var day in d.Days) {
    DateTime date;
    if(day==null || !DateTime.TryParseExact(day.Date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out date) || !seen.Add(day.Date) || day.Items==null || day.Items.Any(t=>t==null || string.IsNullOrEmpty(t.Id))) throw new InvalidDataException("计划内容不完整");
    day.Journal=day.Journal??"";
   }
   return d;
  }
 }
 public void Load() {
  if(!File.Exists(FilePath)) return;
  try { Data=Read(FilePath); }
  catch(Exception ex) {
   if(!File.Exists(FilePath+".bak")) throw new IOException("无法读取计划，原文件已保留。请勿删除 Data 文件夹。",ex);
   Data=Read(FilePath+".bak");
   File.Copy(FilePath,FilePath+".damaged-"+DateTime.Now.ToString("yyyyMMddHHmmssfff"));
   Recovered=true;
  }
 }
 public PlanDay Day(DateTime date) {
  string key=date.ToString("yyyy-MM-dd"); var day=Data.Days.FirstOrDefault(d=>d.Date==key);
  if(day==null) { day=new PlanDay{Date=key}; Data.Days.Add(day); } return day;
 }
 public void Save() {
  string temp=FilePath+".tmp";
  using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {
   new DataContractJsonSerializer(typeof(PlanData)).WriteObject(f,Data); f.Flush(true);
  }
  if(File.Exists(FilePath)) File.Replace(temp,FilePath,FilePath+".bak"); else File.Move(temp,FilePath);
 }
}
public class Planner {
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 public Window W;
 public Storage Store;
 DateTime date=DateTime.Today, month=DateTime.Today;
 bool loading,dirty;
 DispatcherTimer timer;
 DispatcherTimer activationTimer;
 RichTextBox journal;
 TextBox quick;
 PlanItem deleted;
 DateTime deletedDate;
 Border editingBorder;
 Action commitEditor;
 readonly string[] weekdays={"星期日","星期一","星期二","星期三","星期四","星期五","星期六"};
 public T Get<T>(string name) where T:class { return W.FindName(name) as T; }
 public static Brush B(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
 public TextBlock Text(string text,double size,string color) { return new TextBlock{Text=text,FontSize=size,Foreground=B(color),TextWrapping=TextWrapping.Wrap}; }
 public Button Button(string text,Action action) { var b=new Button{Content=text}; b.Click+=(s,e)=>action(); return b; }
 public Planner(Storage storage) {
  Store=storage;
  using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("App.xaml")) W=(Window)XamlReader.Load(stream);
  W.SourceInitialized+=(s,e)=>{try{int dark=1;DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(W).Handle,20,ref dark,4);}catch{}};
  try { using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico")) if(s!=null) W.Icon=BitmapFrame.Create(s); } catch {}
  var fonts=Fonts.SystemFontFamilies.FirstOrDefault(f=>f.Source.IndexOf("方正仿宋",StringComparison.OrdinalIgnoreCase)>=0 || f.Source.IndexOf("FZFangSong",StringComparison.OrdinalIgnoreCase)>=0);
  if(fonts!=null) W.FontFamily=fonts;
  quick=Get<TextBox>("QuickAdd"); journal=Get<RichTextBox>("FreeJournal");
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(450)};
  timer.Tick+=(s,e)=>{timer.Stop(); Save(false);};
  activationTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};
  activationTimer.Tick+=(s,e)=>{
   string request=Path.Combine(Path.GetDirectoryName(Store.FilePath),"activate.request");
   if(!File.Exists(request))return;
   try {File.Delete(request);}catch {return;}
   W.Show();if(W.WindowState==WindowState.Minimized)W.WindowState=WindowState.Normal;
   W.Activate();W.Topmost=true;W.Topmost=false;W.Focus();
  };
  W.Loaded+=(s,e)=>{activationTimer.Start();W.Activate();};
  W.Closed+=(s,e)=>activationTimer.Stop();
  quick.TextChanged+=(s,e)=>Get<TextBlock>("QuickPlaceholder").Visibility=quick.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
  quick.KeyDown+=(s,e)=>{if(e.Key==Key.Enter && !string.IsNullOrWhiteSpace(quick.Text)){Add();e.Handled=true;}};
  Get<Button>("AddButton").Click+=(s,e)=>Add();
  Get<Button>("TodayButton").Click+=(s,e)=>Select(DateTime.Today);
  Get<Button>("PrevMonth").Click+=(s,e)=>{month=month.AddMonths(-1); Calendar();};
  Get<Button>("NextMonth").Click+=(s,e)=>{month=month.AddMonths(1); Calendar();};
  Get<Button>("PrevWeek").Click+=(s,e)=>Select(date.AddDays(-7));
  Get<Button>("NextWeek").Click+=(s,e)=>Select(date.AddDays(7));
  Get<Button>("UndoButton").Click+=(s,e)=>UndoDelete();
  Get<Button>("JournalText").Click+=(s,e)=>journal.Focus();
  Get<Button>("JournalUndo").Click+=(s,e)=>{if(journal.CanUndo)journal.Undo();journal.Focus();};
  journal.TextChanged+=(s,e)=>{
   if(loading)return;
   Store.Day(date).Journal=JournalText(); Changed();
   Get<Button>("JournalUndo").IsEnabled=journal.CanUndo;
  };
  journal.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((s,e)=>{
   var grid=Get<Grid>("JournalPaper"); var brush=grid.Background as DrawingBrush;
   if(brush!=null) { var copy=brush.Clone();copy.Viewport=new Rect(0,-(e.VerticalOffset%42),1,42);grid.Background=copy; }
  }));
  DataObject.AddPastingHandler(journal,(s,e)=>{
   if(e.DataObject.GetDataPresent(DataFormats.UnicodeText)) e.DataObject=new DataObject(DataFormats.UnicodeText,e.DataObject.GetData(DataFormats.UnicodeText));
   else e.CancelCommand();
  });
  W.PreviewKeyDown+=(s,e)=>{
   if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.N){quick.Focus();e.Handled=true;}
   if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.S){Commit();Save(true);e.Handled=true;}
  };
  W.Closing+=(s,e)=>{
   Commit();timer.Stop();
   if(dirty && !Save(true)) e.Cancel=true;
  };
  Select(date);
  if(Store.Recovered) MessageBox.Show(W,"已从上次备份恢复计划。原文件已保留在 Data 文件夹。","墨序",MessageBoxButton.OK,MessageBoxImage.Information);
 }
 string JournalText() {
  var value=new TextRange(journal.Document.ContentStart,journal.Document.ContentEnd).Text;
  if(value.EndsWith("\r\n")) value=value.Substring(0,value.Length-2);
  return value;
 }
 public void SetJournal(string value) {
  loading=true;
  journal.Document=new FlowDocument(new Paragraph(new Run(value)){Margin=new Thickness(0),LineHeight=42,LineStackingStrategy=LineStackingStrategy.BlockLineHeight}){PagePadding=new Thickness(0),FontFamily=W.FontFamily,FontSize=21,Foreground=B("#F2E6D5")};
  var paragraphStyle=new Style(typeof(Paragraph));paragraphStyle.Setters.Add(new Setter(Paragraph.MarginProperty,new Thickness(0)));paragraphStyle.Setters.Add(new Setter(Paragraph.LineHeightProperty,42.0));paragraphStyle.Setters.Add(new Setter(Paragraph.LineStackingStrategyProperty,LineStackingStrategy.BlockLineHeight));journal.Document.Resources.Add(typeof(Paragraph),paragraphStyle);
  journal.IsUndoEnabled=false;journal.IsUndoEnabled=true;
  Get<Button>("JournalUndo").IsEnabled=false;
  loading=false;
 }
 public void Changed() {dirty=true;Get<TextBlock>("Status").Text="正在保存…";timer.Stop();timer.Start();}
 public bool Save(bool showError) {
  try { if(dirty)Store.Save();dirty=false;Get<TextBlock>("Status").Text="已保存到本机";return true; }
  catch(Exception ex) {
   Get<TextBlock>("Status").Text="保存失败，请保留窗口并按 Ctrl + S 重试";
   if(showError)MessageBox.Show(W,"暂时无法保存，请检查磁盘空间和文件权限。窗口将保留，以免丢失内容。\n\n"+ex.Message,"墨序",MessageBoxButton.OK,MessageBoxImage.Warning);
   return false;
  }
 }
 void Commit() { if(commitEditor!=null) {var action=commitEditor;commitEditor=null;action();} }
 public void Select(DateTime selected) {
  Commit(); if(dirty&&!Save(true))return;
  date=selected.Date;month=new DateTime(date.Year,date.Month,1);
  Get<TextBlock>("DateHeading").Text=date.Day.ToString();
  Get<TextBlock>("WeekdayHeading").Text=weekdays[(int)date.DayOfWeek];
  Get<TextBlock>("MonthHeading").Text=date.ToString("MMMM",System.Globalization.CultureInfo.InvariantCulture).ToUpper()+"   /   "+date.Month+"月手记";
  Get<TextBlock>("DayMarker").Text=date==DateTime.Today?"TODAY  ·  今天":date>DateTime.Today?"PLAN  ·  期待":"MEMORY  ·  回看";
  Get<TextBlock>("DateSubtitle").Text=date.ToString("yyyy / MM / dd")+"    为热爱的事，留一点时间。";
  Get<TextBlock>("TasksHeading").Text=date==DateTime.Today?"今日 · 安排":date.ToString("M月d日")+" · 安排";
  Get<TextBlock>("CompletionHeading").Text=date==DateTime.Today?"今日进度":"当日进度";
  quick.Clear();SetJournal(Store.Day(date).Journal);Calendar();Week();Tasks();
 }
 void Calendar() {
  Get<TextBlock>("MonthLabel").Text=month.ToString("yyyy 年 M 月");
  var grid=Get<UniformGrid>("CalendarGrid");grid.Children.Clear();
  DateTime first=new DateTime(month.Year,month.Month,1);int offset=((int)first.DayOfWeek+6)%7;
  for(int i=0;i<42;i++) {
   DateTime d=first.AddDays(i-offset);
   var b=Button(d.Day.ToString(),()=>Select(d));b.Padding=new Thickness(0);b.Height=34;b.Margin=new Thickness(1);b.FontSize=15;
   b.Foreground=B(d.Month==month.Month?"#D2BDA1":"#676056");
   if(Store.Data.Days.Any(day=>day.Date==d.ToString("yyyy-MM-dd")&&(day.Items.Count>0||!string.IsNullOrWhiteSpace(day.Journal)))) b.Foreground=B("#F3C578");
   if(d==DateTime.Today)b.FontWeight=FontWeights.Bold;
   if(d==date){b.Background=B("#F0B54F");b.Foreground=B("#281A0E");}
   grid.Children.Add(b);
  }
 }
 void Week() {
  var grid=Get<UniformGrid>("WeekGrid");grid.Children.Clear();DateTime start=date.AddDays(-(((int)date.DayOfWeek+6)%7));
  for(int i=0;i<7;i++) {
   DateTime d=start.AddDays(i);var b=Button("",()=>Select(d));b.Padding=new Thickness(0,13,0,13);b.Margin=new Thickness(4,0,4,0);
   var s=new StackPanel();bool selected=d==date;string color=selected?"#32220D":"#B19D86";
   var label=Text(d==DateTime.Today?"今天":"周"+"一二三四五六日"[i],16,color);label.HorizontalAlignment=HorizontalAlignment.Center;
   var number=Text(d.Day.ToString(),27,selected?"#281A0E":"#E6D7C2");number.HorizontalAlignment=HorizontalAlignment.Center;number.Margin=new Thickness(0,8,0,0);
   if(selected)b.Background=new LinearGradientBrush(Color.FromRgb(255,209,124),Color.FromRgb(217,154,60),65);
   s.Children.Add(label);s.Children.Add(number);b.Content=s;grid.Children.Add(b);
  }
 }
 public void Add() {
  Commit();string title=quick.Text.Trim();if(title.Length==0)return;
  var item=new PlanItem{Title=title};Store.Day(date).Items.Add(item);quick.Clear();Changed();Tasks();Calendar();quick.Focus();
 }
 public void Tasks() {
  var grid=Get<StackPanel>("TaskList");grid.Children.Clear();editingBorder=null;
  var items=Store.Day(date).Items;int done=items.Count(t=>t.Done);
  Get<TextBlock>("TaskCount").Text=items.Count+" 项安排";Get<TextBlock>("ProgressLabel").Text=done+" / "+items.Count+" 已完成";
  Get<ProgressBar>("Progress").Value=items.Count==0?0:100.0*done/items.Count;
  if(items.Count==0){var empty=Text("写下一件想做的事，\n从这里开始新的一天。",20,"#A99C88");empty.Margin=new Thickness(18,30,12,0);empty.LineHeight=38;grid.Children.Add(empty);}
  foreach(var item in items.OrderBy(t=>t.Done).ToList()) {
   var border=new Border{CornerRadius=new CornerRadius(8),Padding=new Thickness(18,22,18,22),Margin=new Thickness(0,0,0,10),Background=B(item.Important?"#292117":"#1B1917"),BorderThickness=new Thickness(item.Important?2:1,1,1,1),BorderBrush=B(item.Important?"#D49A42":"#373028")};
   var panel=new Grid();panel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});panel.ColumnDefinitions.Add(new ColumnDefinition());
   var check=Button(item.Done?"✓":"",()=>{Commit();item.Done=!item.Done;Changed();Tasks();});check.Width=20;check.Height=20;check.Padding=new Thickness(0);check.FontSize=14;check.Background=B(item.Done?"#3C3121":"#29251E");check.Foreground=B("#F0B54F");check.HorizontalAlignment=HorizontalAlignment.Left;check.VerticalAlignment=VerticalAlignment.Top;check.Margin=new Thickness(0,3,0,0);check.ToolTip=item.Done?"标为未完成":"标为完成";
   var wrap=new Border{Child=check,BorderBrush=B("#9F8055"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Width=22,Height=22,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left};check.Margin=new Thickness(0);
   panel.Children.Add(wrap);
   var body=new StackPanel{Cursor=Cursors.Hand};Grid.SetColumn(body,1);
   var title=Text(item.Title,20,item.Done?"#9C9283":"#F0E5D5");if(item.Done)title.TextDecorations=TextDecorations.Strikethrough;body.Children.Add(title);
   string meta=string.Join("    ",new[]{item.Time,item.Important?"重点安排":"",item.Note}.Where(t=>!string.IsNullOrWhiteSpace(t)).ToArray());
   if(meta.Length>0){var note=Text(meta,15,item.Important?"#F0B54F":"#B4A28A");note.Margin=new Thickness(0,10,0,0);body.Children.Add(note);}
   body.ToolTip="点击编辑安排、时间和备注";
   body.MouseLeftButtonUp+=(s,e)=>{Commit();Edit(item,border);e.Handled=true;};
   panel.Children.Add(body);border.Child=panel;grid.Children.Add(border);
  }
 }
 public void Edit(PlanItem item,Border border) {
  if(editingBorder!=null)Tasks();
  // Find the freshly rebuilt card if a different editor was previously open.
  int idx=Store.Day(date).Items.OrderBy(t=>t.Done).ToList().IndexOf(item);
  border=(Border)Get<StackPanel>("TaskList").Children[idx];editingBorder=border;
  var s=new StackPanel();var title=new TextBox{Text=item.Title,MaxLength=300,FontSize=20,TextWrapping=TextWrapping.Wrap,MinHeight=48};s.Children.Add(title);
  var fields=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,10)};
  var time=new TextBox{Text=item.Time,Width=92,MaxLength=5,ToolTip="时间可留空，例如 09:30"};fields.Children.Add(time);
  var important=new CheckBox{Content="重点安排",IsChecked=item.Important,Margin=new Thickness(15,0,0,0),VerticalAlignment=VerticalAlignment.Center};fields.Children.Add(important);s.Children.Add(fields);
  var note=new TextBox{Text=item.Note,TextWrapping=TextWrapping.Wrap,MaxLength=2000,MinHeight=45,ToolTip="补充备注（可留空）"};s.Children.Add(note);
  Action apply=()=>{
   string newTitle=title.Text.Trim();
   // Empty edits retain the existing title so a saved task cannot become invisible.
   if(newTitle.Length>0)item.Title=newTitle;
   item.Time=time.Text.Trim();item.Note=note.Text;item.Important=important.IsChecked==true;Changed();
  };
  title.TextChanged+=(a,b)=>apply();time.TextChanged+=(a,b)=>apply();note.TextChanged+=(a,b)=>apply();important.Checked+=(a,b)=>apply();important.Unchecked+=(a,b)=>apply();
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};
  var ok=Button("完成编辑",()=>{apply();commitEditor=null;Tasks();Save(false);});ok.Foreground=B("#F0B54F");buttons.Children.Add(ok);
  var del=Button("删除",()=>{commitEditor=null;Delete(item);});del.Foreground=B("#AD9283");buttons.Children.Add(del);s.Children.Add(buttons);
  commitEditor=apply;border.Child=s;title.Focus();title.SelectAll();
 }
 public void Delete(PlanItem item) {deleted=item;deletedDate=date;Store.Day(date).Items.Remove(item);Changed();Tasks();Calendar();Get<Button>("UndoButton").Visibility=Visibility.Visible;}
 public void UndoDelete() {if(deleted==null)return;Store.Day(deletedDate).Items.Add(deleted);deleted=null;Changed();Get<Button>("UndoButton").Visibility=Visibility.Collapsed;Tasks();Calendar();}
 public void Fit() {
  Rect area=SystemParameters.WorkArea;
  W.Width=Math.Min(1360,area.Width-30);W.Height=Math.Min(900,area.Height-35);
  W.MinWidth=Math.Min(1060,area.Width-30);W.MinHeight=Math.Min(700,area.Height-35);
  if(W.Height<800) {
   var host=Get<Grid>("MainRoot");
   var transform=new ScaleTransform(0.87,0.87);host.LayoutTransform=transform;
  }
 }
}
public class Program {
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd,int command);
 static void ActivateExisting(string root) {
  File.WriteAllText(Path.Combine(root,"activate.request"),DateTime.UtcNow.ToString("O"));
  foreach(var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)) {
   try {if(process.Id!=Process.GetCurrentProcess().Id && process.MainWindowHandle!=IntPtr.Zero){ShowWindowAsync(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);}}catch{}
  }
 }
 [STAThread] public static int Main(string[] args) {
  bool test=args.Contains("--test"),preview=args.Contains("--preview");
  if(test) {try {RunTests(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1]+".failure.txt",e.ToString());return 1;}}
  FileStream gate=null;
  try {
   var app=new Application();
   string root=preview?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data");
   Directory.CreateDirectory(root);
   try {gate=new FileStream(Path.Combine(root,"session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);} catch(IOException){ActivateExisting(root);return 0;}
   var store=new Storage(root);store.Load();
   if(preview && store.Data.Days.Count==0){var day=store.Day(DateTime.Today);day.Items.Add(new PlanItem{Title="整理下周的工作安排",Time="10:00",Important=true});day.Items.Add(new PlanItem{Title="读书半小时",Time="14:00",Note="留一段不被打扰的时间"});day.Items.Add(new PlanItem{Title="散步，给自己一点放松",Time="18:30"});day.Journal="把时间留给真正喜欢的事。\r\n\r\n今天的想法，慢慢写在这里。\r\n认真生活，也记得休息。";}
   var planner=new Planner(store);
   if(preview){Render(planner.W,args[2]);return 0;}
   planner.Fit();app.Run(planner.W);return 0;
  } catch(Exception e){MessageBox.Show("墨序暂时无法打开。你的原有数据不会被清空。\n\n"+e.Message,"墨序",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
  finally {if(gate!=null)gate.Dispose();}
 }
 static void Render(Window w,string path) {
  w.Width=1360;w.Height=900;w.Measure(new Size(1360,900));w.Arrange(new Rect(0,0,1360,900));w.UpdateLayout();
  var root=(FrameworkElement)w.Content;root.Measure(new Size(1360,900));root.Arrange(new Rect(0,0,1360,900));root.UpdateLayout();
  var bitmap=new RenderTargetBitmap(2040,1350,144,144,PixelFormats.Pbgra32);bitmap.Render(root);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(path))png.Save(f);w.Close();
 }
 static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
 static void RunTests(string folder) {
  Directory.CreateDirectory(folder);var a=new Storage(folder);a.Load();var date=new DateTime(2026,12,31);
  var item=new PlanItem{Title="中文计划 <>&\" 与 emoji 🌙",Time="09:30",Note="第一行\n第二行",Important=true};
  a.Day(date).Items.Add(item);a.Day(date).Journal="随记第一行\r\n\r\n第二段。\r\n";a.Day(date.AddDays(1)).Journal="新年";a.Save();
  var b=new Storage(folder);b.Load();Assert(b.Day(date).Journal==a.Day(date).Journal,"日记换行未保留");Assert(b.Day(date).Items[0].Title==item.Title,"中文任务未保留");Assert(b.Day(date.AddDays(1)).Journal=="新年","跨年日期混淆");
  b.Day(date).Items[0].Done=true;b.Save();var c=new Storage(folder);c.Load();Assert(c.Day(date).Items[0].Done,"完成状态未保存");Assert(File.Exists(c.FilePath+".bak"),"未生成备份");
  File.WriteAllText(c.FilePath,"damaged");var recovery=new Storage(folder);recovery.Load();Assert(recovery.Recovered,"未恢复备份");Assert(recovery.Day(date).Items[0].Title==item.Title,"备份恢复失败");recovery.Save();
  var app=new Application();var uiStore=new Storage(Path.Combine(folder,"ui"));var ui=new Planner(uiStore);
  ui.Get<TextBox>("QuickAdd").Text="界面添加测试";ui.Add();Assert(uiStore.Day(DateTime.Today).Items.Count==1,"添加失败");
  var journal=ui.Get<RichTextBox>("FreeJournal");journal.AppendText("当天随记\r\n下一行");ui.Save(true);ui.Select(DateTime.Today.AddDays(1));journal.AppendText("明天随记");ui.Save(true);ui.Select(DateTime.Today);
  Assert(new TextRange(journal.Document.ContentStart,journal.Document.ContentEnd).Text.Contains("当天随记"),"切换日期丢失随记");
  var task=uiStore.Day(DateTime.Today).Items[0];
  var card=(Border)ui.Get<StackPanel>("TaskList").Children[0];ui.Edit(task,card);var editor=(StackPanel)card.Child;((TextBox)editor.Children[0]).Text="修改后的安排";ui.Select(DateTime.Today.AddDays(1));ui.Select(DateTime.Today);Assert(task.Title=="修改后的安排","编辑后跨日丢失");
  card=(Border)ui.Get<StackPanel>("TaskList").Children[0];var taskGrid=(Grid)card.Child;var toggle=(Button)((Border)taskGrid.Children[0]).Child;toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(task.Done,"完成勾选失败");
  ui.Delete(task);Assert(uiStore.Day(DateTime.Today).Items.Count==0,"删除失败");ui.UndoDelete();Assert(uiStore.Day(DateTime.Today).Items.Count==1,"撤销删除失败");ui.Save(true);
  var reopen=new Storage(Path.Combine(folder,"ui"));reopen.Load();Assert(reopen.Day(DateTime.Today).Journal.Contains("下一行"),"重开日记丢失");Assert(reopen.Day(DateTime.Today.AddDays(1)).Journal.Contains("明天随记"),"重开跨日内容丢失");
  ui.W.Close();File.WriteAllText(Path.Combine(folder,"passed.txt"),"PASS: Chinese/emoji, multiline journal, year boundary, completion, atomic backup, corruption recovery, UI add, date switching, delete/undo, reopen persistence.");
 }
}
