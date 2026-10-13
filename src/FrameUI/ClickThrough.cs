using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FrameUI
{
    /// <summary>
    /// IMGUI is invisible to the game's EventSystem, so clicks on a FigmaWindow would also hit the game behind it.
    /// This makes the EventSystem report "pointer over UI" (and hides uGUI elements from raycasts) while the pointer
    /// is over a window that was rendered recently. Installed automatically when the first FigmaWindow is created.
    /// </summary>
    internal static class ClickThrough
    {
        static readonly List<FigmaWindow> Windows = new List<FigmaWindow>();
        static bool _installed;

        internal static void Register(FigmaWindow window)
        {
            Windows.Add(window);
            if (_installed) return;
            _installed = true;
            new Harmony("FrameUI.ClickThrough").PatchAll(typeof(ClickThrough).Assembly);
        }

        internal static void Unregister(FigmaWindow window) => Windows.Remove(window);

        // OnGUI runs after Update, so a window counts as visible if it was rendered this frame or the one before.
        // A window that stops rendering (hidden) therefore stops blocking within a frame.
        internal static bool IsPointerOverWindow
        {
            get
            {
                var mouse = Input.mousePosition;
                var position = new Vector2(mouse.x, Screen.height - mouse.y);
                for (int i = 0; i < Windows.Count; i++)
                    if (Windows[i].LastRenderFrame >= Time.frameCount - 1 && Windows[i].Contains(position)) return true;
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.IsPointerOverGameObject), new System.Type[0])]
    internal static class PointerOverPatch
    {
        static void Postfix(ref bool __result)
        {
            if (!__result && ClickThrough.IsPointerOverWindow) __result = true;
        }
    }

    [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.IsPointerOverGameObject), new[] { typeof(int) })]
    internal static class PointerOverIdPatch
    {
        static void Postfix(ref bool __result)
        {
            if (!__result && ClickThrough.IsPointerOverWindow) __result = true;
        }
    }

    // Input modules find the clicked uGUI element through RaycastAll; emptying it hides the UI behind the window.
    [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.RaycastAll))]
    internal static class RaycastPatch
    {
        static void Postfix(List<RaycastResult> raycastResults)
        {
            if (ClickThrough.IsPointerOverWindow) raycastResults.Clear();
        }
    }
}
