using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeMote;

class Motion
{
    public string Name;
    public bool Diff;
    public override string ToString() { return (Diff ? "动作 · " : "表情 · ") + Name; }
}

class Viewer : Window
{
    const int W = 520, H = 580;
    readonly D3DImage surface = new D3DImage();
    readonly ComboBox models = new ComboBox(), motions = new ComboBox();
    readonly TextBlock status = new TextBlock();
    readonly DispatcherTimer timer = new DispatcherTimer();
    readonly Stopwatch watch = new Stopwatch();
    Emote engine;
    EmotePlayer player;
    bool loading, closed;
    float scale = .22f;
    readonly bool test;
    int testStage;
    DateTime nextTest;
    readonly List<string> report = new List<string>();

    public Viewer(bool testing)
    {
        test = testing;
        Title = "Amadeus · 红莉栖 E-mote 测试";
        Width = 552; Height = 785; ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(239, 241, 246));
        FontFamily = new FontFamily("Microsoft YaHei UI");
        var layout = new DockPanel { Margin = new Thickness(12), Background=Background };
        Content = layout;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "AMADEUS  /  E-MOTE", FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        models.Margin = new Thickness(0, 0, 0, 8); models.Height = 30;
        foreach (string f in new[] { "牧瀬紅莉栖ab_白衣.psb.m", "牧瀬紅莉栖ab.psb.m", "牧瀬紅莉栖c_白衣.psb.m", "牧瀬紅莉栖c.psb.m" }) models.Items.Add(f);
        top.Children.Add(models);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        motions.Height = 30; motions.Margin = new Thickness(0, 8, 0, 6); bottom.Children.Add(motions);
        var buttons = new UniformGridProxy(); bottom.Children.Add(buttons);
        AddButton(buttons, "◀ 上一个", delegate { Step(-1); });
        AddButton(buttons, "下一个 ▶", delegate { Step(1); });
        AddButton(buttons, "重播", delegate { ApplyMotion(); });
        AddButton(buttons, "重置视图", delegate { Fit(); });
        status.FontSize = 11; status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 6, 0, 0); bottom.Children.Add(status);
        var image = new Image { Source = surface, Stretch = Stretch.Uniform };
        var canvas = new Border { Background = new SolidColorBrush(Color.FromRgb(221, 227, 235)), Child = image, ClipToBounds = true };
        layout.Children.Add(canvas);
        image.MouseWheel += delegate(object s, System.Windows.Input.MouseWheelEventArgs e) { if (player != null) { scale = Math.Max(.025f, Math.Min(1f, scale * (e.Delta > 0 ? 1.1f : .91f))); player.SetScale(scale, 0, 0); } };
        Point last = new Point();
        image.MouseLeftButtonDown += delegate(object s, System.Windows.Input.MouseButtonEventArgs e) { last = e.GetPosition(image); image.CaptureMouse(); };
        image.MouseLeftButtonUp += delegate { image.ReleaseMouseCapture(); };
        image.MouseMove += delegate(object s, System.Windows.Input.MouseEventArgs e) { if (image.IsMouseCaptured && player != null) { var p = e.GetPosition(image); player.OffsetCoord((int)(p.X-last.X), (int)(p.Y-last.Y)); last=p; } };
        models.SelectionChanged += delegate { try { if (engine != null && !loading) LoadModel(); } catch(Exception ex) { Fail(ex); } };
        motions.SelectionChanged += delegate { try { if (!loading) ApplyMotion(); } catch(Exception ex) { Fail(ex); } };
        Loaded += delegate { try { Initialize(); } catch(Exception ex) { Fail(ex); } };
        Closed += delegate { closed=true; timer.Stop(); surface.Lock(); surface.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); surface.Unlock(); if(engine != null) engine.Dispose(); };
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += delegate { try { Render(); } catch(Exception ex) { timer.Stop(); Fail(ex); } };
    }
    void AddButton(Panel panel, string label, Action action)
    {
        var b = new Button { Content=label, Height=31, Margin=new Thickness(2,0,2,0) };
        b.Click += delegate { try { action(); } catch(Exception ex) { Fail(ex); } }; panel.Children.Add(b);
    }
    void Initialize()
    {
        Log("Initializing native renderer");
        engine = new Emote(new WindowInteropHelper(this).Handle, W, H, true);
        engine.EmoteInit();
        engine.Device.SetMipMapEnabled(true);
        Log("Renderer ready");
        surface.Lock(); surface.SetBackBuffer(D3DResourceType.IDirect3DSurface9, engine.D3DSurface); surface.Unlock();
        models.SelectedIndex=0;
        watch.Start(); timer.Start(); nextTest=DateTime.UtcNow.AddSeconds(2);
    }
    void LoadModel()
    {
        loading=true;
        try
        {
            if(player != null) { engine.DeletePlayer("kurisu"); player=null; }
            string file = "cache\\kurisu" + models.SelectedIndex + ".psb";
            // Compatibility copies use uncompressed textures supported by DX9.
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            if(!File.Exists(file)) throw new FileNotFoundException("缺少兼容模型，请运行 prepare-models.ps1",file);
            Log("Loading " + file);
            player=engine.CreatePlayer("kurisu", file);
            if(player == null) throw new InvalidOperationException("驱动未能加载此模型。");
            player.SetSmoothing(true); player.Show();
            motions.Items.Clear();
            for(uint i=0; i<player.CountMainTimelines(); i++) AddMotion(player.GetMainTimelineLabelAt(i), false);
            for(uint i=0; i<player.CountDiffTimelines(); i++) AddMotion(player.GetDiffTimelineLabelAt(i), true);
            Log("Loaded, motions=" + motions.Items.Count + ", variables=" + player.CountVariables());
            if(motions.Items.Count == 0) throw new InvalidOperationException("模型未暴露可播放时间线，可能与驱动不兼容。");
            Fit(); motions.SelectedIndex=0;
        }
        finally { loading=false; }
        ApplyMotion();
    }
    void AddMotion(string name, bool diff)
    {
        // Only standing-picture variants: exclude walk/exit timelines that move offscreen.
        if(String.IsNullOrWhiteSpace(name) || !(name.StartsWith("表情_") || name.StartsWith("差分_") || name.StartsWith("腕差分"))) return;
        motions.Items.Add(new Motion { Name=name, Diff=diff });
    }
    void Fit()
    {
        if(player == null) return;
        // Model coordinates are in original artwork pixels; frame the upper body.
        scale=.24f; player.SetScale(scale,0,0); player.SetCoord(0, -140,0,0);
    }
    void ApplyMotion()
    {
        if(player == null || motions.SelectedItem == null) return;
        Motion m=(Motion)motions.SelectedItem;
        player.StopTimeline("");
        player.PlayTimeline("初期化", TimelinePlayFlags.NONE); player.Skip();
        if(m.Diff) { var neutral=motions.Items.Cast<Motion>().FirstOrDefault(x=>!x.Diff); if(neutral != null) { player.PlayTimeline(neutral.Name, TimelinePlayFlags.NONE); player.Skip(); } }
        player.PlayTimeline(m.Name, m.Diff ? TimelinePlayFlags.TIMELINE_PLAY_DIFFERENCE : TimelinePlayFlags.NONE);
        status.Text=String.Format("{0}/{1}  ·  {2}\n滚轮缩放 · 拖动立绘调整位置",motions.SelectedIndex+1,motions.Items.Count,m.Name);
        Log("Playing " + m.Name);
    }
    void Step(int direction) { if(motions.Items.Count>0) motions.SelectedIndex=(motions.SelectedIndex+direction+motions.Items.Count)%motions.Items.Count; }
    void Render()
    {
        if(closed || player == null || !surface.IsFrontBufferAvailable) return;
        float dt=(float)Math.Min(100,watch.Elapsed.TotalMilliseconds); watch.Restart();
        uint device=engine.D3DTestCooperativeLevel();
        if(device != 0) return;
        engine.Update(dt);
        surface.Lock();
        try { engine.D3DBeginScene(); engine.Draw(); engine.D3DEndScene(); surface.AddDirtyRect(new Int32Rect(0,0,W,H)); }
        finally { surface.Unlock(); }
        if(test && DateTime.UtcNow >= nextTest) TestStep();
    }
    void TestStep()
    {
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output"); Directory.CreateDirectory(folder);
        Capture(Path.Combine(folder,"stage-"+testStage+".png"));
        report.Add("Stage "+testStage+": "+models.SelectedItem+" / "+motions.SelectedItem);
        File.WriteAllLines(Path.Combine(folder,"report.txt"),report);
        testStage++;
        if(testStage==1) Step(1);
        else if(testStage==2) { var index=motions.Items.Cast<Motion>().ToList().FindIndex(m=>m.Name=="腕差分A"); if(index>=0) motions.SelectedIndex=index; else Step(1); }
        else if(testStage==3) models.SelectedIndex=1;
        else if(testStage==4) models.SelectedIndex=2;
        else if(testStage==5) models.SelectedIndex=3;
        else if(testStage==6) { models.SelectedIndex=0; Step(-1); Step(1); }
        else { Log("SMOKE TEST COMPLETED"); Close(); return; }
        nextTest=DateTime.UtcNow.AddSeconds(2);
    }
    void Capture(string path)
    {
        // Capture the application's own render surface, never the user's desktop.
        byte[] pixels=engine.RenderToBuffer(W,H);
        Log("Capture bytes="+pixels.Length);
        int visible=0; for(int i=3;i<pixels.Length;i+=4) if(pixels[i]>32 && pixels[i-1]+pixels[i-2]+pixels[i-3]>60) visible++;
        if(visible<10000) throw new InvalidOperationException("渲染检查失败：立绘为空或面积异常。");
        var bitmap=BitmapSource.Create(W,H,96,96,PixelFormats.Bgra32,null,pixels,W*4);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var f=File.Create(path)) png.Save(f);
        var ui=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);
        ui.Render((Visual)Content);
        var uiPng=new PngBitmapEncoder(); uiPng.Frames.Add(BitmapFrame.Create(ui));
        using(var f=File.Create(Path.ChangeExtension(path,"window.png"))) uiPng.Save(f);
    }
    void Fail(Exception ex)
    {
        Log(ex.ToString()); status.Text="加载失败："+ex.Message+"（详见 app.log）";
        if(test) { Environment.ExitCode=1; Close(); }
    }
    static void Log(string text) { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app.log"),DateTime.Now.ToString("HH:mm:ss ")+text+Environment.NewLine); }
    [STAThread] static int Main(string[] args)
    {
        try { var app=new Application(); app.Run(new Viewer(args.Contains("--test"))); return Environment.ExitCode; }
        catch(Exception ex) { Log(ex.ToString()); if(!args.Contains("--test")) MessageBox.Show(ex.Message,"E-mote 启动失败"); return 1; }
    }
}
class UniformGridProxy : System.Windows.Controls.Primitives.UniformGrid { public UniformGridProxy() { Columns=4; } }
