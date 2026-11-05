using Aquarium;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
namespace AquariumRenderTests;
public partial class App:Application {
 Window? window;NativeWorkspaceSurface? surface;HostedAppSession? session;
 public App()=>InitializeComponent();
 protected override void OnLaunched(LaunchActivatedEventArgs args) {
  var input=Environment.GetCommandLineArgs();if(input.Length!=3)throw new ArgumentException("Pass Color.exe from tests/Color and a report path. Requires an interactive desktop.");
  window=new Window{Title="Aquarium generated render fixture"};window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100,780));
  var root=new Grid{Background=new SolidColorBrush(Microsoft.UI.Colors.MidnightBlue),RequestedTheme=ElementTheme.Dark};
  root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(80)});root.RowDefinitions.Add(new RowDefinition());
  root.Children.Add(new TextBlock{Text="Generated WinForms rendering regression",FontSize=24,Margin=new Thickness(20)});
  var area=new Grid{Background=new SolidColorBrush(Microsoft.UI.Colors.DarkSlateGray),Margin=new Thickness(88,0,16,16)};Grid.SetRow(area,1);root.Children.Add(area);
  window.Content=root;var owner=WinRT.Interop.WindowNative.GetWindowHandle(window);surface=new NativeWorkspaceSurface(owner);
  root.Loaded+=async(_,_)=>{try {
   session=new HostedAppSession(input[1],surface.Handle);
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));await session.AttachAsync(timeout.Token);
   if(!HostedAppSession.Native.IsWindow(surface.Handle))throw new Exception("Clipping surface destroyed before configuration: "+surface.Handle.ToString("X"));
   try{surface.SetLegacyRendering(session.UsesManagedVisibility);}catch(Exception ex){throw new Exception("Surface="+surface.Handle.ToString("X")+" valid="+HostedAppSession.Native.IsWindow(surface.Handle)+" parent="+HostedAppSession.Native.GetParent(surface.Handle).ToString("X"),ex);}
   void Place(){var p=area.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());var scale=root.XamlRoot.RasterizationScale;surface.Place((int)(p.X*scale),(int)(p.Y*scale),(int)(area.ActualWidth*scale),(int)(area.ActualHeight*scale));session.Place(0,0,(int)(area.ActualWidth*scale),(int)(area.ActualHeight*scale));}
   Place();for(int attempt=0;attempt<150 && GetForegroundWindow()!=owner;attempt++)await Task.Delay(100);await Task.Delay(700);AssertPainted(owner);
   int pid=session.ProcessId;var child=session.Window;session.Hide();surface.Hide();await Task.Delay(200);if(HostedAppSession.Native.IsWindowVisible(child))throw new Exception("Home left fixture visible");
   area.Margin=new Thickness(208,0,16,16);root.UpdateLayout();Place();for(int attempt=0;attempt<150 && GetForegroundWindow()!=owner;attempt++)await Task.Delay(100);await Task.Delay(700);AssertPainted(owner);
   if(session.ProcessId!=pid||session.Window!=child)throw new Exception("Sidebar/Home/Resume replaced the window");
   File.WriteAllText(input[2],"PASS: lime/magenta generated controls visibly painted above the actual WinUI compositor; sidebar resize, Home hide and same-window Resume passed.");
   if(!await session.RequestCloseAsync(TimeSpan.FromSeconds(3)))throw new Exception("Fixture refused close");
  }catch(Exception ex){Environment.ExitCode=1;File.WriteAllText(input[2],ex.ToString());}finally{if(session?.IsAlive==true)await session.RequestCloseAsync(TimeSpan.FromSeconds(1));session?.Dispose();surface?.Dispose();window.Close();}};
  window.Activate();
 }
 void AssertPainted(nint owner) {
  if(GetForegroundWindow()!=owner)throw new Exception("Interactive test requires its own unobscured foreground window; no pixel result asserted.");
  var origin=new Point();ClientToScreen(surface!.Handle,ref origin);var dc=GetDC(0);int lime=0,magenta=0;
  try {for(int y=20;y<400;y+=50)for(int x=20;x<600;x+=50){uint pixel=GetPixel(dc,origin.X+x,origin.Y+y);if(pixel==0x00FF00)lime++;if(pixel==0xFF00FF)magenta++;}}
  finally{ReleaseDC(0,dc);}
  if(lime<3||magenta<10)throw new Exception($"Native controls are obscured by WinUI: lime={lime}, magenta={magenta}.");
 }
 [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
 [DllImport("user32.dll")]static extern nint GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool ClientToScreen(nint h,ref Point point);
 [DllImport("user32.dll")]static extern nint GetDC(nint h);
 [DllImport("user32.dll")]static extern int ReleaseDC(nint h,nint dc);
 [DllImport("gdi32.dll")]static extern uint GetPixel(nint dc,int x,int y);
}
