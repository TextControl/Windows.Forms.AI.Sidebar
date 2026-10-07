#nullable enable

namespace tx_winforms_ai
{
    partial class Form1
    {
        // The sample owns the document UI. The component owns only the AI integration.
        private System.ComponentModel.IContainer? components;

        /// <summary>Disposes the AI component before its associated editor controls.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>Creates the native editor layout and connects the reusable AI component.</summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            _editor = new TXTextControl.TextControl();
            _ribbon = new TXTextControl.Windows.Forms.Ribbon.Ribbon();
            _homeTab = new TXTextControl.Windows.Forms.Ribbon.RibbonFormattingTab();
            _insertTab = new TXTextControl.Windows.Forms.Ribbon.RibbonInsertTab();
            _pageLayoutTab = new TXTextControl.Windows.Forms.Ribbon.RibbonPageLayoutTab();
            _viewTab = new TXTextControl.Windows.Forms.Ribbon.RibbonViewTab();
            _horizontalRuler = new TXTextControl.RulerBar();
            _statusBar = new TXTextControl.StatusBar();
            _verticalRuler = new TXTextControl.RulerBar();
            _canvas = new Panel();
            aiAssistant1 = new TXTextControl.AiAssistant(components);
            _ribbon.SuspendLayout();
            _canvas.SuspendLayout();
            (aiAssistant1).BeginInit();
            SuspendLayout();
            //
            // _editor
            //
            _editor.Dock = DockStyle.Fill;
            _editor.Font = new Font("Arial", 10F);
            _editor.FormattingPrinter = "Standard";
            _editor.HideSelection = false;
            _editor.Location = new Point(38, 38);
            _editor.Margin = new Padding(4);
            _editor.Name = "_editor";
            _editor.Ribbon = _ribbon;
            _editor.RulerBar = _horizontalRuler;
            _editor.Size = new Size(1693, 1423);
            _editor.StatusBar = _statusBar;
            _editor.TabIndex = 0;
            _editor.UserNames = null;
            _editor.VerticalRulerBar = _verticalRuler;
            //
            // _ribbon
            //
            _ribbon.Controls.Add(_homeTab);
            _ribbon.Controls.Add(_insertTab);
            _ribbon.Controls.Add(_pageLayoutTab);
            _ribbon.Controls.Add(_viewTab);
            _ribbon.Dock = DockStyle.Top;
            _ribbon.Font = new Font("Segoe UI", 9F);
            _ribbon.HasApplicationMenu = false;
            _ribbon.HotTrack = true;
            _ribbon.Location = new Point(0, 45);
            _ribbon.Name = "_ribbon";
            _ribbon.SelectedIndex = 0;
            _ribbon.Size = new Size(1731, 174);
            _ribbon.TabIndex = 3;
            //
            // _homeTab
            //
            _homeTab.Font = new Font("Segoe UI", 9F);
            _homeTab.Location = new Point(4, 34);
            _homeTab.Name = "_homeTab";
            _homeTab.Size = new Size(1723, 136);
            _homeTab.TabIndex = 1;
            //
            // _insertTab
            //
            _insertTab.Font = new Font("Segoe UI", 9F);
            _insertTab.Location = new Point(4, 34);
            _insertTab.Name = "_insertTab";
            _insertTab.Size = new Size(1723, 136);
            _insertTab.TabIndex = 2;
            //
            // _pageLayoutTab
            //
            _pageLayoutTab.Font = new Font("Segoe UI", 9F);
            _pageLayoutTab.Location = new Point(4, 34);
            _pageLayoutTab.Name = "_pageLayoutTab";
            _pageLayoutTab.Size = new Size(1723, 136);
            _pageLayoutTab.TabIndex = 3;
            //
            // _viewTab
            //
            _viewTab.Font = new Font("Segoe UI", 9F);
            _viewTab.Location = new Point(4, 34);
            _viewTab.Name = "_viewTab";
            _viewTab.Size = new Size(1723, 136);
            _viewTab.TabIndex = 4;
            //
            // _horizontalRuler
            //
            _horizontalRuler.Dock = DockStyle.Top;
            _horizontalRuler.Location = new Point(0, 0);
            _horizontalRuler.Margin = new Padding(4);
            _horizontalRuler.Name = "_horizontalRuler";
            _horizontalRuler.Size = new Size(1731, 38);
            _horizontalRuler.TabIndex = 2;
            //
            // _statusBar
            //
            _statusBar.BackColor = SystemColors.Control;
            _statusBar.ColumnText = "Column ";
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Font = new Font("Segoe UI", 9F);
            _statusBar.LineText = "Row ";
            _statusBar.Location = new Point(0, 1680);
            _statusBar.Margin = new Padding(4);
            _statusBar.Name = "_statusBar";
            _statusBar.PageCounterText = " of ";
            _statusBar.PageText = "Page ";
            _statusBar.SectionCounterText = " of ";
            _statusBar.SectionText = "Section ";
            _statusBar.Size = new Size(1731, 35);
            _statusBar.TabIndex = 2;
            //
            // _verticalRuler
            //
            _verticalRuler.Alignment = TXTextControl.RulerBarAlignment.Left;
            _verticalRuler.Dock = DockStyle.Left;
            _verticalRuler.Location = new Point(0, 38);
            _verticalRuler.Margin = new Padding(4);
            _verticalRuler.Name = "_verticalRuler";
            _verticalRuler.Size = new Size(38, 1423);
            _verticalRuler.TabIndex = 1;
            //
            // _canvas
            //
            _canvas.Controls.Add(_editor);
            _canvas.Controls.Add(_verticalRuler);
            _canvas.Controls.Add(_horizontalRuler);
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(0, 219);
            _canvas.Margin = new Padding(4);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(1731, 1461);
            _canvas.TabIndex = 0;
            //
            // aiAssistant1
            //
            aiAssistant1.SidebarHost = this;
            aiAssistant1.TextControl = _editor;
            //
            // Form1
            //
            AutoScaleDimensions = new SizeF(144F, 144F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1731, 1715);
            Controls.Add(_canvas);
            Controls.Add(_statusBar);
            Controls.Add(_ribbon);
            Font = new Font("Segoe UI", 9F);
            Margin = new Padding(4);
            MinimumSize = new Size(1608, 1092);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "TX AI Writer";
            Shown += Form1_Shown;
            _ribbon.ResumeLayout(false);
            _canvas.ResumeLayout(false);
            (aiAssistant1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        // Designer fields expose the complete document application in the sample project.
        private TXTextControl.TextControl _editor = null!;
        private TXTextControl.Windows.Forms.Ribbon.Ribbon _ribbon = null!;
        private TXTextControl.Windows.Forms.Ribbon.RibbonFormattingTab _homeTab = null!;
        private TXTextControl.Windows.Forms.Ribbon.RibbonInsertTab _insertTab = null!;
        private TXTextControl.Windows.Forms.Ribbon.RibbonPageLayoutTab _pageLayoutTab = null!;
        private TXTextControl.Windows.Forms.Ribbon.RibbonViewTab _viewTab = null!;
        private TXTextControl.RulerBar _horizontalRuler = null!;
        private TXTextControl.RulerBar _verticalRuler = null!;
        private TXTextControl.StatusBar _statusBar = null!;
        private Panel _canvas = null!;
        private TXTextControl.AiAssistant aiAssistant1 = null!;
    }
}
