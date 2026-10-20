using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion {
internal sealed class TextInputIcon : VisualElement {
 bool active;
 public bool Active { set { active = value; MarkDirtyRepaint(); } }
 public TextInputIcon() {
  style.width = 26; style.height = 28; style.flexShrink = 0;
  pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
 }
 void Draw(MeshGenerationContext context) {
  var p = context.painter2D;
  p.strokeColor = active ? new Color(.91f,.48f,.47f) : new Color(.85f,.85f,.85f);
  p.lineWidth = 2;
  p.BeginPath(); p.MoveTo(new Vector2(5,6)); p.LineTo(new Vector2(21,6));
  p.MoveTo(new Vector2(13,6)); p.LineTo(new Vector2(13,24)); p.Stroke();
  if (!active) {
   p.BeginPath(); p.MoveTo(new Vector2(3,3)); p.LineTo(new Vector2(23,25)); p.Stroke();
  }
 }
}
}
