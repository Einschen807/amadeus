using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion {
internal sealed class ChromeIcon : VisualElement {
 readonly bool gear;
 public ChromeIcon(bool gear) {
  this.gear=gear; style.width=28;style.height=28;style.flexShrink=0;
  pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;
 }
 void Draw(MeshGenerationContext context) {
  var p=context.painter2D;p.strokeColor=new Color(.85f,.85f,.85f);p.lineWidth=1.8f;
  if(gear){
   p.BeginPath();
   for(int i=0;i<64;i++){
    float a=i*Mathf.PI/32f;float r=(i%8>=2&&i%8<=5)?12:9.3f;
    var v=new Vector2(14+Mathf.Cos(a)*r,14+Mathf.Sin(a)*r);
    if(i==0)p.MoveTo(v);else p.LineTo(v);
   }
   p.ClosePath();p.Stroke();p.BeginPath();p.Arc(new Vector2(14,14),4.2f,0,360);p.Stroke();
  } else {
   p.BeginPath();p.MoveTo(new Vector2(23,5));p.LineTo(new Vector2(23,16));p.LineTo(new Vector2(5,16));
   p.MoveTo(new Vector2(11,10));p.LineTo(new Vector2(5,16));p.LineTo(new Vector2(11,22));p.Stroke();
  }
 }
}
}
