using System;

namespace KurisuCompanion {
[Serializable] internal sealed class PortraitPose {
 // User-confirmed framing captured on 2026-09-27. Offsets are relative to the viewport.
 public static PortraitPose ConfirmedDefault => new PortraitPose {
  zoom=1.2336781024932861f, offsetX=-0.0291708055883646f, offsetY=-0.04789682850241661f
 };
 public float zoom=1, offsetX, offsetY;
}
}
