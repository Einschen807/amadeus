using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    // A restrained square grid, faint binary readout, straight light trails, and drifting phosphor dust.
    internal sealed class FlowingCircuitBackground : VisualElement
    {
        private const int TrailSteps = 9;
        private const int NoiseCount = 72;
        private static readonly float[] HorizontalLanes = { .10f, .29f, .48f, .68f, .78f, .87f };
        private static readonly float[] VerticalLanes = { .13f, .28f, .37f, .62f, .87f };
        private static readonly string[] BinaryRows = {
            "01001010", "10110100", "00101101", "11001001", "01010110",
            "10100101", "01101001", "10010110", "00110101", "11010010"
        };
        private readonly Label[] binary = new Label[BinaryRows.Length];

        private float nextPaint;
        public float CellSize { get; private set; }
        public int TrailCount => HorizontalLanes.Length + VerticalLanes.Length;
        public int HorizontalTrailCount => HorizontalLanes.Length;
        public int VerticalTrailCount => VerticalLanes.Length;

        public FlowingCircuitBackground()
        {
            name = "flowing-circuit-background";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
            generateVisualContent += Paint;
            for (int i = 0; i < binary.Length; i++)
            {
                var label = new Label(BinaryRows[i]) { pickingMode = PickingMode.Ignore };
                label.style.position = Position.Absolute;
                label.style.fontSize = 12;
                label.style.color = new Color(.38f, .72f, .55f, i % 3 == 0 ? .19f : .13f);
                label.style.whiteSpace = WhiteSpace.NoWrap;
                binary[i] = label;
                Add(label);
            }
            RegisterCallback<GeometryChangedEvent>(_ => PositionBinary());
        }

        public void Tick()
        {
            if (Time.unscaledTime < nextPaint) return;
            nextPaint = Time.unscaledTime + 1f / 24f;
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext context)
        {
            float width = contentRect.width, height = contentRect.height;
            if (width < 64 || height < 64) return;
            CellSize = Mathf.Clamp(Mathf.Min(width, height) / 7.5f, 60f, 90f);
            int columns = Mathf.Max(2, Mathf.FloorToInt(width / CellSize));
            int rows = Mathf.Max(2, Mathf.FloorToInt(height / CellSize));
            var painter = context.painter2D;

            // The grid is visible but quieter than the moving trails.
            for (int column = 1; column * CellSize < width; column++)
                DrawLine(painter, new Vector2(column * CellSize, 0),
                    new Vector2(column * CellSize, height), .7f, .095f);
            for (int row = 1; row * CellSize < height; row++)
                DrawLine(painter, new Vector2(0, row * CellSize),
                    new Vector2(width, row * CellSize), .7f, .095f);

            for (int i = 0; i < HorizontalLanes.Length; i++)
            {
                int row = Mathf.Clamp(Mathf.RoundToInt(HorizontalLanes[i] * rows), 1, rows - 1);
                DrawHorizontal(painter, row * CellSize, width, CellSize, i);
            }
            for (int i = 0; i < VerticalLanes.Length; i++)
            {
                int column = Mathf.Clamp(Mathf.RoundToInt(VerticalLanes[i] * columns), 1, columns - 1);
                DrawVertical(painter, column * CellSize, height, CellSize, i);
            }
            DrawFloatingNoise(painter, width, height);
        }

        private static void DrawFloatingNoise(Painter2D painter, float width, float height)
        {
            float time = Time.unscaledTime;
            for (int i = 0; i < NoiseCount; i++)
            {
                float x0 = Hash01(i * 4 + 1) * width;
                float y0 = Hash01(i * 4 + 2) * height;
                float speed = 6f + Hash01(i * 4 + 3) * 10f;
                bool falling = i % 4 == 0;
                float x = falling
                    ? x0 + Mathf.Sin(time * .32f + i) * 4f
                    : Mathf.Repeat(x0 - time * speed + 8f, width + 16f) - 8f;
                float y = falling
                    ? Mathf.Repeat(y0 + time * speed + 8f, height + 16f) - 8f
                    : y0 + Mathf.Sin(time * .38f + i * 1.7f) * 4f;
                float pulse = .65f + .35f * Mathf.Sin(time * (1.1f + Hash01(i + 301)) + i * 2.3f);
                float radius = .7f + Hash01(i + 601) * .8f;
                float brightness = i % 9 == 0 ? 1.35f : 1f;
                FillCircle(painter, new Vector2(x, y), radius * 3.5f, .025f * pulse * brightness);
                FillCircle(painter, new Vector2(x, y), radius, .15f * pulse * brightness);
            }
        }

        private static float Hash01(int seed)
        {
            uint value = unchecked((uint)seed * 747796405u + 2891336453u);
            value ^= value >> 16;
            value = unchecked(value * 2246822519u);
            value ^= value >> 13;
            return (value & 0x00ffffffu) / 16777215f;
        }

        private static void FillCircle(Painter2D painter, Vector2 center, float radius, float alpha)
        {
            painter.fillColor = new Color(.47f, 1f, .68f, alpha);
            painter.BeginPath();
            painter.Arc(center, radius, 0, 360);
            painter.ClosePath();
            painter.Fill();
        }

        private void PositionBinary()
        {
            float width = contentRect.width, height = contentRect.height;
            if (width < 64 || height < 64) return;
            float cell = Mathf.Clamp(Mathf.Min(width, height) / 7.5f, 60f, 90f);
            int columns = Mathf.Max(2, Mathf.FloorToInt(width / cell));
            int rows = Mathf.Max(2, Mathf.FloorToInt(height / cell));
            for (int i = 0; i < binary.Length; i++)
            {
                // Repeatable fragments in separate cells, mostly at the open edges of the portrait.
                int column = (i * 5 + 1) % columns;
                int row = (i * 3 + i / 3 + 1) % rows;
                binary[i].style.left = column * cell + 9f;
                binary[i].style.top = row * cell + 15f;
            }
        }

        private static void DrawHorizontal(Painter2D painter, float y, float width, float cell, int index)
        {
            float tail = cell * .62f;
            float head = width + tail - Mathf.Repeat(Time.unscaledTime * (28f + index * 3f) + index * width * .29f, width + 2f * tail);
            for (int step = 0; step < TrailSteps; step++)
            {
                float from = head + tail * (1f - step / (float)TrailSteps);
                float to = head + tail * (1f - (step + 1f) / TrailSteps);
                Stroke(painter, new Vector2(from, y), new Vector2(to, y), step);
            }
        }

        private static void DrawVertical(Painter2D painter, float x, float height, float cell, int index)
        {
            float tail = cell * .62f;
            float head = -tail + Mathf.Repeat(Time.unscaledTime * (25f + index * 3.4f) + index * height * .33f, height + 2f * tail);
            for (int step = 0; step < TrailSteps; step++)
            {
                float from = head - tail + tail * step / TrailSteps;
                float to = head - tail + tail * (step + 1f) / TrailSteps;
                Stroke(painter, new Vector2(x, from), new Vector2(x, to), step);
            }
        }

        private static void Stroke(Painter2D painter, Vector2 start, Vector2 end, int step)
        {
            float strength = (step + 1f) / TrailSteps;
            DrawLine(painter, start, end, 9f, .036f * strength);
            DrawLine(painter, start, end, 2f, .18f * strength);
        }

        private static void DrawLine(Painter2D painter, Vector2 start, Vector2 end, float width, float alpha)
        {
            painter.lineWidth = width;
            painter.strokeColor = new Color(.40f, .94f, .63f, alpha);
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.Stroke();
        }
    }
}
