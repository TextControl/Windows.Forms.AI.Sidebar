using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TXTextControl.AISidebar.UI;

using Image = System.Drawing.Image;

/// <summary>Draws resolution-independent sidebar artwork into owned bitmaps.</summary>
internal static class SidebarIcons
{
    /// <summary>Draws the sidebar settings icon at the requested pixel size.</summary>
    public static Image Settings(int size) => Draw(size, graphics =>
    {
        var points = new PointF[32];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = (i * 11.25 - 22.5) * Math.PI / 180;
            float radius = i % 4 is 1 or 2 ? 14 : 11;
            points[i] = new PointF(16 + radius * (float)Math.Cos(angle), 16 + radius * (float)Math.Sin(angle));
        }
        using var gear = new GraphicsPath(FillMode.Alternate);
        gear.AddPolygon(points);
        gear.AddEllipse(11, 11, 10, 10);
        using var brush = new SolidBrush(UiTheme.Ink);
        graphics.FillPath(brush, gear);
    });

    /// <summary>Renders artwork in a fixed logical coordinate system into a scaled transparent bitmap.</summary>
    private static Image Draw(int size, Action<Graphics> draw)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.ScaleTransform(size / 32f, size / 32f);
        draw(graphics);
        return bitmap;
    }
}
