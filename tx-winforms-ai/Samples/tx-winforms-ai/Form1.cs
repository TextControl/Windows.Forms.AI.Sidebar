using TXTextControl;
using TXTextControl.Windows.Forms.Ribbon;
using System.ComponentModel;

namespace tx_winforms_ai;

/// <summary>Sample host for the native editor and reusable AI component.</summary>
public partial class Form1 : RibbonForm
{
    /// <summary>Creates the sample editor and connects one toolbox-compatible AI component.</summary>
    public Form1()
    {
        InitializeComponent();
        aiAssistant1.SettingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TX AI Writer", "connection.json");
    }

    // Document loading is runtime-only; the designer displays the native editor's empty page.
    private void Form1_Shown(object? sender, EventArgs e)
    {
        BeginInvoke(InitializeDocument);
    }

    private void InitializeDocument()
    {
        _editor.Load(Path.Combine(AppContext.BaseDirectory, "test.rtf"), StreamType.RichTextFormat);
    }


}
