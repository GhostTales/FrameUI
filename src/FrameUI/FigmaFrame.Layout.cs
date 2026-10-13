using System.Collections.Generic;
using UnityEngine;

namespace FrameUI
{
    public partial class FigmaFrame
    {
        Vector2 _content;

        /// <summary>Size this frame needs to show its content (padding and gaps included). This is what Hug uses.</summary>
        public Vector2 ContentSize() => MeasureTree();

        // Bottom-up, once per pass, so a frame is never measured more than once however deep the tree is.
        Vector2 MeasureTree()
        {
            for (int i = 0; i < Children.Count; i++) Children[i].MeasureTree();
            return _content = Measure();
        }

        /// <summary>Content size of this frame. Children are already measured when this is called.</summary>
        protected virtual Vector2 Measure()
        {
            switch (Layout)
            {
                case FigmaLayout.None: return Bounds.size;
                case FigmaLayout.Grid: return MeasureGrid();
                default: return MeasureStack();
            }
        }

        // Size along one axis as seen by the parent: Fixed uses Bounds, Hug and Fill use the content.
        float Intrinsic(bool horizontal)
        {
            if ((horizontal ? WidthSize : HeightSize) == FigmaSize.Fixed)
                return horizontal ? Bounds.width : Bounds.height;
            return horizontal ? _content.x : _content.y;
        }

        float InnerWidth(Rect rect) => Mathf.Max(0f, rect.width - Padding.x - Padding.z);
        float InnerHeight(Rect rect) => Mathf.Max(0f, rect.height - Padding.y - Padding.w);

        // Gives this frame its rectangle and places the children inside it.
        void PlaceAt(Rect rect)
        {
            LayoutRect = rect;
            switch (Layout)
            {
                case FigmaLayout.None:
                    for (int i = 0; i < Children.Count; i++)
                    {
                        var c = Children[i];
                        c.PlaceAt(new Rect(rect.x + c.Bounds.x, rect.y + c.Bounds.y, c.Bounds.width, c.Bounds.height));
                    }
                    break;
                case FigmaLayout.Grid:
                    var cells = LayoutGrid(rect);
                    for (int i = 0; i < Children.Count; i++) Children[i].PlaceAt(cells[i]);
                    break;
                default:
                    var rects = LayoutStack(rect);
                    for (int i = 0; i < Children.Count; i++) Children[i].PlaceAt(rects[i]);
                    break;
            }
        }

        // ---- Horizontal / Vertical ----

        Vector2 MeasureStack()
        {
            bool horizontal = Layout == FigmaLayout.Horizontal;
            float main = Gap * Mathf.Max(0, Children.Count - 1);
            float cross = 0f;
            for (int i = 0; i < Children.Count; i++)
            {
                main += Children[i].Intrinsic(horizontal);
                cross = Mathf.Max(cross, Children[i].Intrinsic(!horizontal));
            }
            return horizontal
                ? new Vector2(main + Padding.x + Padding.z, cross + Padding.y + Padding.w)
                : new Vector2(cross + Padding.x + Padding.z, main + Padding.y + Padding.w);
        }

        Rect[] LayoutStack(Rect rect)
        {
            bool horizontal = Layout == FigmaLayout.Horizontal;
            float mainSize = horizontal ? InnerWidth(rect) : InnerHeight(rect);
            float crossSize = horizontal ? InnerHeight(rect) : InnerWidth(rect);

            int n = Children.Count;
            var main = new float[n];
            float used = 0f, weights = 0f;
            for (int i = 0; i < n; i++)
            {
                var c = Children[i];
                if ((horizontal ? c.WidthSize : c.HeightSize) == FigmaSize.Fill) weights += Mathf.Max(0f, c.FillWeight);
                else used += main[i] = c.Intrinsic(horizontal);
            }

            float free = Mathf.Max(0f, mainSize - used - Gap * Mathf.Max(0, n - 1));
            if (weights > 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    var c = Children[i];
                    if ((horizontal ? c.WidthSize : c.HeightSize) == FigmaSize.Fill)
                        main[i] = free * Mathf.Max(0f, c.FillWeight) / weights;
                }
                free = 0f;
            }

            float gap = Gap, pos = 0f;
            switch (MainAlign)
            {
                case FigmaAlign.Center: pos = free * 0.5f; break;
                case FigmaAlign.End: pos = free; break;
                case FigmaAlign.SpaceBetween: if (n > 1) gap += free / (n - 1); break;
            }

            var rects = new Rect[n];
            float x = rect.x + Padding.x, y = rect.y + Padding.y;
            for (int i = 0; i < n; i++)
            {
                var c = Children[i];
                bool crossFill = (horizontal ? c.HeightSize : c.WidthSize) == FigmaSize.Fill;
                float cross = crossFill ? crossSize : c.Intrinsic(!horizontal);
                float crossPos = 0f;
                if (CrossAlign == FigmaAlign.Center) crossPos = (crossSize - cross) * 0.5f;
                else if (CrossAlign == FigmaAlign.End) crossPos = crossSize - cross;

                rects[i] = horizontal
                    ? new Rect(x + pos, y + crossPos, main[i], cross)
                    : new Rect(x + crossPos, y + pos, cross, main[i]);
                pos += main[i] + gap;
            }
            return rects;
        }

        // ---- Grid ----

        int ColumnCount => ColumnTracks != null && ColumnTracks.Length > 0 ? ColumnTracks.Length : Mathf.Max(1, Columns);

        // Row-major auto placement; returns per child (col, row, colSpan, rowSpan).
        Vector4[] PlaceCells(out int rowCount)
        {
            int cols = ColumnCount, n = Children.Count;
            var cells = new Vector4[n];
            var occupied = new List<bool[]>();
            int cursor = 0;
            rowCount = RowTracks != null ? RowTracks.Length : 0;
            for (int i = 0; i < n; i++)
            {
                int cs = Mathf.Clamp(Children[i].ColumnSpan, 1, cols);
                int rs = Mathf.Max(1, Children[i].RowSpan);
                while (cursor % cols + cs > cols || !IsFree(occupied, cursor % cols, cursor / cols, cs, rs)) cursor++;
                int c0 = cursor % cols, r0 = cursor / cols;
                for (int r = r0; r < r0 + rs; r++)
                {
                    while (occupied.Count <= r) occupied.Add(new bool[cols]);
                    for (int c = c0; c < c0 + cs; c++) occupied[r][c] = true;
                }
                cells[i] = new Vector4(c0, r0, cs, rs);
                rowCount = Mathf.Max(rowCount, r0 + rs);
                cursor += cs;
            }
            return cells;
        }

        static bool IsFree(List<bool[]> occupied, int col, int row, int cs, int rs)
        {
            for (int r = row; r < row + rs && r < occupied.Count; r++)
                for (int c = col; c < col + cs; c++)
                    if (occupied[r][c]) return false;
            return true;
        }

        // Fixed = px, Hug = widest/tallest single-span child, Fr = share of the leftover space.
        // available < 0 means "measuring": fr tracks collapse to their content.
        float[] ResolveTracks(bool horizontal, int count, Vector4[] cells, float available)
        {
            var defs = horizontal ? ColumnTracks : RowTracks;
            float gap = horizontal ? ColumnGap : RowGap;
            var content = new float[count];
            for (int i = 0; i < cells.Length; i++)
            {
                int start = (int)(horizontal ? cells[i].x : cells[i].y);
                float span = horizontal ? cells[i].z : cells[i].w;
                if (span == 1f) content[start] = Mathf.Max(content[start], Children[i].Intrinsic(horizontal));
            }

            var sizes = new float[count];
            float fixedTotal = 0f, frTotal = 0f;
            for (int t = 0; t < count; t++)
            {
                var def = defs != null && t < defs.Length ? defs[t] : FigmaTrack.Fr(1f);
                if (def.Kind == FigmaTrackKind.Fr) frTotal += Mathf.Max(0f, def.Value);
                else fixedTotal += sizes[t] = def.Kind == FigmaTrackKind.Fixed ? def.Value : content[t];
            }

            float free = available < 0f ? 0f : Mathf.Max(0f, available - gap * Mathf.Max(0, count - 1) - fixedTotal);
            for (int t = 0; t < count; t++)
            {
                var def = defs != null && t < defs.Length ? defs[t] : FigmaTrack.Fr(1f);
                if (def.Kind != FigmaTrackKind.Fr) continue;
                sizes[t] = available < 0f ? content[t] : (frTotal > 0f ? free * Mathf.Max(0f, def.Value) / frTotal : 0f);
            }
            return sizes;
        }

        Vector2 MeasureGrid()
        {
            var cells = PlaceCells(out int rows);
            int cols = ColumnCount;
            float w = ColumnGap * (cols - 1) + Padding.x + Padding.z;
            float h = RowGap * Mathf.Max(0, rows - 1) + Padding.y + Padding.w;
            foreach (float v in ResolveTracks(true, cols, cells, -1f)) w += v;
            foreach (float v in ResolveTracks(false, rows, cells, -1f)) h += v;
            return new Vector2(w, h);
        }

        Rect[] LayoutGrid(Rect rect)
        {
            var cells = PlaceCells(out int rows);
            int cols = ColumnCount;
            var colW = ResolveTracks(true, cols, cells, InnerWidth(rect));
            var rowH = ResolveTracks(false, rows, cells, InnerHeight(rect));

            var colX = new float[cols];
            for (int c = 1; c < cols; c++) colX[c] = colX[c - 1] + colW[c - 1] + ColumnGap;
            var rowY = new float[rows];
            for (int r = 1; r < rows; r++) rowY[r] = rowY[r - 1] + rowH[r - 1] + RowGap;

            var rects = new Rect[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                int c0 = (int)cells[i].x, r0 = (int)cells[i].y, cs = (int)cells[i].z, rs = (int)cells[i].w;
                float cellW = colX[c0 + cs - 1] + colW[c0 + cs - 1] - colX[c0];
                float cellH = rowY[r0 + rs - 1] + rowH[r0 + rs - 1] - rowY[r0];

                var child = Children[i];
                float w = child.WidthSize == FigmaSize.Fill ? cellW : child.Intrinsic(true);
                float h = child.HeightSize == FigmaSize.Fill ? cellH : child.Intrinsic(false);
                rects[i] = new Rect(
                    rect.x + Padding.x + colX[c0] + Offset(child.CellAlignX, cellW - w),
                    rect.y + Padding.y + rowY[r0] + Offset(child.CellAlignY, cellH - h),
                    w,
                    h);
            }
            return rects;
        }

        static float Offset(FigmaAlign align, float free)
        {
            return align == FigmaAlign.Center ? free * 0.5f : align == FigmaAlign.End ? free : 0f;
        }
    }
}
