using System.Runtime.InteropServices;
[assembly: System.Reflection.AssemblyMetadata("AquariumWorkspace", "1")]
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();using var fixture=new ColorFixture();Application.Run(fixture);
sealed class ColorFixture:Form {
 static readonly uint visibility=RegisterWindowMessage("Aquarium.Workspace.Visibility.v1");
 public ColorFixture(){Text="Generated color fixture";BackColor=System.Drawing.Color.Magenta;Size=new System.Drawing.Size(900,600);
  Controls.Add(new Label{Text="GENERATED WINDOW: VISIBLE",Dock=DockStyle.Top,Height=100,BackColor=System.Drawing.Color.Lime,ForeColor=System.Drawing.Color.Black,Font=new System.Drawing.Font("Segoe UI",24)});
  var button=new Button{Text="Fixture button",Location=new System.Drawing.Point(70,140),Size=new System.Drawing.Size(300,80)};button.Click+=(_,_)=>button.Text="Fixture clicked";Controls.Add(button);
  Shown+=(_,_)=>{SetProp(Handle,"Aquarium.Workspace.ManagedVisibility.v1",1);SetProp(Handle,"Aquarium.Workspace.v1",1);};
 }
 protected override void WndProc(ref Message m){if((uint)m.Msg==visibility){if(m.WParam==0)Hide();else Show();return;}base.WndProc(ref m);}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool SetProp(nint window,string name,nint value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
}
