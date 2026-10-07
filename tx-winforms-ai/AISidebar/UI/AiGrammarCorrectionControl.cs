using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

using BorderStyle = System.Windows.Forms.BorderStyle;
using Button = System.Windows.Forms.Button;

/// <summary>Compact correction preview with explicit Apply and Ignore commands.</summary>
internal sealed class AiGrammarCorrectionControl : UserControl
{
    private readonly Button _apply = UiTheme.Button("Apply");
    private readonly Button _ignore = UiTheme.Button("Ignore");
    internal event Action? ApplyRequested;
    internal event Action? IgnoreRequested;

    /// <summary>Builds a correction preview with explicit Apply and Ignore buttons.</summary>
    internal AiGrammarCorrectionControl(AiGrammarIssue issue, float scale)
    {
        Size = new Size((int)(340 * scale), (int)(215 * scale));
        Font = new Font("Segoe UI", 10); BackColor = Color.White; ForeColor = UiTheme.Ink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding((int)(10 * scale)), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28 * scale));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36 * scale));
        layout.Controls.Add(new Label { Text = "Grammar suggestion", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 0);
        layout.Controls.Add(new RichTextBox { Text = issue.Replacement.Length == 0 ? $"Remove “{issue.Original}”" : $"{issue.Original} → {issue.Replacement}",
            ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None, BackColor = Color.White,
            ForeColor = UiTheme.Ink, Dock = DockStyle.Fill, ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = false,
            AccessibleName = "Suggested correction" }, 0, 1);
        layout.Controls.Add(new RichTextBox { Text = issue.Explanation, ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = UiTheme.Muted, Dock = DockStyle.Fill, ScrollBars = RichTextBoxScrollBars.Vertical,
            TabStop = false, AccessibleName = "Explanation" }, 0, 2);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.Controls.Add(_apply, 0, 0); buttons.Controls.Add(_ignore, 1, 0);
        layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
        _apply.Click += (_, _) => ApplyRequested?.Invoke();
        _ignore.Click += (_, _) => IgnoreRequested?.Invoke();
    }
}
