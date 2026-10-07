namespace TXTextControl.AISidebar.UI;

using Button = System.Windows.Forms.Button;

/// <summary>Shared colors and native Windows button factories for the assistant UI.</summary>
internal static class UiTheme
{
    public static readonly Color Ink = Color.FromArgb(31, 42, 62);
    public static readonly Color Muted = Color.FromArgb(102, 116, 137);
    public static readonly Color Accent = Color.FromArgb(65, 81, 218);
    public static readonly Color Surface = Color.FromArgb(246, 248, 252);

    /// <summary>Uses system button colors so enabled, disabled and focused states retain native contrast.</summary>
    public static Button Button(string text) => new()
    {
        Text = text, AutoSize = false, Height = 34, Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Standard,
        BackColor = SystemColors.Control, ForeColor = SystemColors.ControlText,
        Margin = new Padding(3), UseVisualStyleBackColor = true
    };

    /// <summary>Creates an automatically sized label with a consistent body or heading font.</summary>
    public static Label Label(string text, bool heading = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = heading ? Ink : Muted,
        Font = new Font("Segoe UI", heading ? 10 : 9, heading ? FontStyle.Bold : FontStyle.Regular),
        Margin = Padding.Empty
    };
}
