using System;
using UnityEngine;

namespace Silksong.ScreenshotMarker;

public record MarkerData {
    public string Name;
    public DateTime Time;
    public string FileName;
    public Vector2 Position;
}
