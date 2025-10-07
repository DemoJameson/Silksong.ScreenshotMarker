using System.Collections.Generic;

namespace Silksong.ScreenshotMarker.Extensions;

public static class ListExtensions {
    public static T? GetOrDefault<T>(this List<T> list, int index) {
        if (list.Count > index) {
            return list[index];
        } else {
            return default;
        }
    }

}
