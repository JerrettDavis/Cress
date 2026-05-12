using System.Drawing.Drawing2D;

namespace Cress.Companion.Windows;

internal enum CompanionButtonStyle
{
    Primary,
    Secondary,
    Danger,
    Ghost
}

internal static class CompanionUiTheme
{
    public static Color WindowBackground => Color.FromArgb(7, 12, 20);
    public static Color Surface => Color.FromArgb(15, 23, 42);
    public static Color SurfaceRaised => Color.FromArgb(17, 29, 53);
    public static Color SurfaceSelected => Color.FromArgb(21, 45, 74);
    public static Color Border => Color.FromArgb(38, 56, 88);
    public static Color TextPrimary => Color.FromArgb(240, 249, 255);
    public static Color TextSecondary => Color.FromArgb(148, 163, 184);
    public static Color Accent => Color.FromArgb(13, 148, 136);
    public static Color AccentMuted => Color.FromArgb(19, 78, 74);
    public static Color Success => Color.FromArgb(16, 185, 129);
    public static Color Warning => Color.FromArgb(245, 158, 11);
    public static Color Danger => Color.FromArgb(239, 68, 68);
    public static Color Ghost => Color.FromArgb(30, 41, 59);

    public static void StyleCard(Control control)
    {
        control.BackColor = Surface;
        control.ForeColor = TextPrimary;
        control.Paint += PaintCardBorder;
    }

    public static void StyleButton(Button button, CompanionButtonStyle style, bool compact = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseDownBackColor = ResolvePressedBackColor(style);
        button.FlatAppearance.MouseOverBackColor = ResolveHoverBackColor(style);
        button.Font = new Font("Segoe UI", compact ? 9F : 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Height = compact ? 32 : 38;
        button.Width = compact ? button.Width : Math.Max(button.Width, 110);
        button.Padding = new Padding(10, 0, 10, 0);

        switch (style)
        {
            case CompanionButtonStyle.Primary:
                button.BackColor = Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Accent;
                break;
            case CompanionButtonStyle.Danger:
                button.BackColor = Color.FromArgb(65, 24, 32);
                button.ForeColor = Color.FromArgb(254, 226, 226);
                button.FlatAppearance.BorderColor = Color.FromArgb(127, 29, 29);
                break;
            case CompanionButtonStyle.Ghost:
                button.BackColor = Color.Transparent;
                button.ForeColor = TextSecondary;
                button.FlatAppearance.BorderColor = Border;
                break;
            default:
                button.BackColor = Ghost;
                button.ForeColor = TextPrimary;
                button.FlatAppearance.BorderColor = Border;
                break;
        }
    }

    public static void StyleListBox(ListBox listBox, string accessibleName)
    {
        listBox.AccessibleName = accessibleName;
        listBox.BackColor = SurfaceRaised;
        listBox.ForeColor = TextPrimary;
        listBox.BorderStyle = BorderStyle.None;
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
        listBox.ItemHeight = 60;
        listBox.IntegralHeight = false;
        listBox.Font = new Font("Segoe UI", 10, FontStyle.Regular);
    }

    public static void DrawListItem(DrawItemEventArgs e, CompanionListItemPresentation item)
    {
        if (e.Index < 0)
        {
            return;
        }

        e.DrawBackground();

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using var backgroundBrush = new SolidBrush(selected ? SurfaceSelected : SurfaceRaised);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        var contentBounds = Rectangle.Inflate(e.Bounds, -12, -8);
        var badgeWidth = Math.Min(96, Math.Max(58, TextRenderer.MeasureText(item.Badge, new Font("Segoe UI", 8.5F, FontStyle.Bold)).Width + 20));
        var badgeBounds = new Rectangle(contentBounds.Right - badgeWidth, contentBounds.Top + 2, badgeWidth, 24);
        var titleBounds = new Rectangle(contentBounds.Left, contentBounds.Top, Math.Max(20, contentBounds.Width - badgeWidth - 12), 22);
        var subtitleBounds = new Rectangle(contentBounds.Left, contentBounds.Top + 28, contentBounds.Width, 22);

        using var titleFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        TextRenderer.DrawText(
            e.Graphics,
            item.Title,
            titleFont,
            titleBounds,
            TextPrimary,
            TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(
            e.Graphics,
            item.Subtitle,
            e.Font,
            subtitleBounds,
            TextSecondary,
            TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        DrawPill(e.Graphics, badgeBounds, item.Badge, item.Tone, selected);
        using var dividerPen = new Pen(Border);
        e.Graphics.DrawLine(dividerPen, contentBounds.Left, e.Bounds.Bottom - 1, contentBounds.Right, e.Bounds.Bottom - 1);
        e.DrawFocusRectangle();
    }

    public static void ApplyTone(Label label, CompanionVisualTone tone)
    {
        label.BackColor = ResolveToneBackColor(tone);
        label.ForeColor = ResolveToneTextColor(tone);
        label.Paint -= PaintPillBorder;
        label.Paint += PaintPillBorder;
    }

    public static void ApplyRoundedRegion(Form form, int radius)
    {
        using var path = CreateRoundedPath(new Rectangle(Point.Empty, form.Size), radius);
        form.Region?.Dispose();
        form.Region = new Region(path);
    }

    private static void PaintCardBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(control.ClientRectangle, -1, -1);
        using var path = CreateRoundedPath(bounds, 18);
        using var borderPen = new Pen(Border);
        e.Graphics.DrawPath(borderPen, path);
    }

    private static void PaintPillBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Label label)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(label.ClientRectangle, -1, -1);
        using var path = CreateRoundedPath(bounds, 12);
        using var borderPen = new Pen(Color.FromArgb(110, ResolveToneBorderColor(label.BackColor)));
        e.Graphics.DrawPath(borderPen, path);
    }

    private static void DrawPill(Graphics graphics, Rectangle bounds, string text, CompanionVisualTone tone, bool selected)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = selected ? Blend(ResolveToneBackColor(tone), SurfaceSelected, 0.22F) : ResolveToneBackColor(tone);
        using var path = CreateRoundedPath(bounds, 12);
        using var backgroundBrush = new SolidBrush(fill);
        using var borderPen = new Pen(ResolveToneBorderColor(fill));
        graphics.FillPath(backgroundBrush, path);
        graphics.DrawPath(borderPen, path);
        TextRenderer.DrawText(
            graphics,
            text,
            new Font("Segoe UI", 8.5F, FontStyle.Bold),
            bounds,
            ResolveToneTextColor(tone),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ResolveHoverBackColor(CompanionButtonStyle style)
        => style switch
        {
            CompanionButtonStyle.Primary => Color.FromArgb(15, 118, 110),
            CompanionButtonStyle.Danger => Color.FromArgb(95, 28, 38),
            CompanionButtonStyle.Ghost => Color.FromArgb(24, 39, 63),
            _ => Color.FromArgb(41, 54, 77)
        };

    private static Color ResolvePressedBackColor(CompanionButtonStyle style)
        => style switch
        {
            CompanionButtonStyle.Primary => Color.FromArgb(17, 94, 89),
            CompanionButtonStyle.Danger => Color.FromArgb(127, 29, 29),
            CompanionButtonStyle.Ghost => Color.FromArgb(19, 32, 51),
            _ => Color.FromArgb(30, 41, 59)
        };

    private static Color ResolveToneBackColor(CompanionVisualTone tone)
        => tone switch
        {
            CompanionVisualTone.Accent => Color.FromArgb(19, 78, 74),
            CompanionVisualTone.Success => Color.FromArgb(6, 78, 59),
            CompanionVisualTone.Warning => Color.FromArgb(120, 53, 15),
            CompanionVisualTone.Danger => Color.FromArgb(127, 29, 29),
            _ => Color.FromArgb(30, 41, 59)
        };

    private static Color ResolveToneTextColor(CompanionVisualTone tone)
        => tone switch
        {
            CompanionVisualTone.Accent => Color.FromArgb(153, 246, 228),
            CompanionVisualTone.Success => Color.FromArgb(209, 250, 229),
            CompanionVisualTone.Warning => Color.FromArgb(254, 243, 199),
            CompanionVisualTone.Danger => Color.FromArgb(254, 226, 226),
            _ => Color.FromArgb(226, 232, 240)
        };

    private static Color ResolveToneTextColor(Color background)
        => background.GetBrightness() < 0.4F ? TextPrimary : WindowBackground;

    private static Color ResolveToneBorderColor(Color background)
        => Blend(background, Color.White, 0.18F);

    private static Color Blend(Color foreground, Color background, float amount)
    {
        var clamped = Math.Max(0F, Math.Min(1F, amount));
        var red = (int)Math.Round((foreground.R * (1 - clamped)) + (background.R * clamped));
        var green = (int)Math.Round((foreground.G * (1 - clamped)) + (background.G * clamped));
        var blue = (int)Math.Round((foreground.B * (1 - clamped)) + (background.B * clamped));
        return Color.FromArgb(red, green, blue);
    }
}
