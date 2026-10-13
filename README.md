# FrameUI

A Figma-style UI library for Hell Clock mods. Everything is a frame: there are no buttons, progress bars or
widgets, only `FigmaFrame` (plus `FigmaText` and `FigmaImage`, which are frames that also draw text or an image).
A button is a frame with a `Clicked` handler and hover/pressed colours; a progress bar is a few nested frames.

It targets the game's stripped IMGUI (`UnityEngine.IMGUIModule`), so it draws with `GUI.DrawTexture` and handles
mouse input from raw events. It is compiled against the game's own assemblies, so it cannot use a call that is
missing at runtime.

## Building

```
dotnet build src/FrameUI.sln
dotnet test src/FrameUI.Tests
```

The game assemblies are read from `~/.local/share/Steam/steamapps/common/Hell Clock/Hell Clock_Data/Managed`.
Use `-p:GameManagedDir=<path>` to point somewhere else. A mod references `FrameUI.csproj` (or `FrameUI.dll`) and ships
`FrameUI.dll` next to its own dll.

## Using it

```csharp
using FrameUI;

var root = new FigmaFrame(new Rect(0f, 0f, window.Rect.width, window.Rect.height))
{
    Fill = Background,
    CornerRadius = 10f,
    Layout = FigmaLayout.Vertical,
};

var button = root.Add(new FigmaFrame(default)
{
    Layout = FigmaLayout.Horizontal,
    MainAlign = FigmaAlign.Center,
    CrossAlign = FigmaAlign.Center,
    Fill = Background,
    Border = Primary,
    BorderWidth = 1f,
    CornerRadius = 3f,
    Clicked = Reset,
    HoverTint = Color.white,
    PressedTint = Pressed,
}.Size(FigmaSize.Hug, FigmaSize.Hug));
button.Add(new FigmaText(default, "Reset", 14, Primary, TextAnchor.MiddleCenter).Size(FigmaSize.Hug, FigmaSize.Hug));

root.Render(new Rect(0f, 0f, window.Rect.width, window.Rect.height)); // on every GUI event
```

- **Layout**: `FigmaLayout.None | Horizontal | Vertical | Grid` with `Padding`, `Gap`, `MainAlign`, `CrossAlign`.
  Children size themselves with `.Size(FigmaSize.Fixed | Hug | Fill, ...)`; `Fill` children share space by `FillWeight`.
  Grids use `ColumnTracks` / `RowTracks` (`FigmaTrack.Px`, `Fr`, `Hug`), `ColumnSpan`, `RowSpan` and `CellAlignX/Y`.
- **Look**: `Fill`, `Border` (per side via `BorderLeft` ...), `BorderWidths`, `CornerRadii`, `Tint`.
- **Interaction**: `Clicked`, `Hovered`, and `Hover*` / `Pressed*` overrides for tint, fill and border. `Tint` is
  inherited by all descendants.
- **Windows**: `FigmaWindow` wraps `GUI.Window` and can be resizable and follow its content size (`FitTo`). It also
  blocks click-through: while the pointer is over a rendered window, the game's `EventSystem` reports the pointer as over
  UI and raycasts return nothing, so clicks don't reach the game behind it (Harmony patches, installed automatically;
  call `Destroy()` when a window is no longer used).
- **Icons**: `Gui.Icon("Name.png")` loads `Textures.Name.png` embedded in the calling assembly.
  Use `<EmbeddedResource Include="Textures/*.png" LogicalName="Textures.%(Filename)%(Extension)" />`.
- **Settings**: `Gui.LinearTextFix` (text colour correction for the game's linear colour space), `Gui.Warn` (log hook),
  `FigmaFrame.Wireframe` (outline every frame).

Build the tree and call `Render` on every IMGUI event (Layout, Repaint and mouse events) with a tree of the same shape.
