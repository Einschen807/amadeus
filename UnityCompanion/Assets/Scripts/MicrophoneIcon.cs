using UnityEngine;
using UnityEngine.UIElements;
namespace KurisuCompanion {
internal sealed class MicrophoneIcon : VisualElement {
 bool active;
 public bool Active{set{active=value;MarkDirtyRepaint();}}
 public MicrophoneIcon(){style.width=26;style.height=28;style.flexShrink=0;pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
 void Draw(MeshGenerationContext context){
  var p=context.painter2D;p.strokeColor=active?new Color(.91f,.48f,.47f):new Color(.85f,.85f,.85f);p.lineWidth=2;
  p.BeginPath();p.MoveTo(new Vector2(9,7));p.BezierCurveTo(new Vector2(9,1),new Vector2(17,1),new Vector2(17,7));p.LineTo(new Vector2(17,14));p.BezierCurveTo(new Vector2(17,20),new Vector2(9,20),new Vector2(9,14));p.ClosePath();p.Stroke();
  p.BeginPath();p.MoveTo(new Vector2(5,12));p.BezierCurveTo(new Vector2(5,25),new Vector2(21,25),new Vector2(21,12));p.Stroke();
  p.BeginPath();p.MoveTo(new Vector2(13,22));p.LineTo(new Vector2(13,27));p.MoveTo(new Vector2(8,27));p.LineTo(new Vector2(18,27));p.Stroke();
  if(!active){p.BeginPath();p.MoveTo(new Vector2(3,3));p.LineTo(new Vector2(23,25));p.Stroke();}
 }
}
}
