namespace VisualData
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            DataGridViewCellStyle dataGridViewCellStyle1 = new DataGridViewCellStyle();
            splitMain = new SplitContainer();
            splitLeft = new SplitContainer();
            grpSQL = new GroupBox();
            txtSQL = new RichTextBox();
            grpBanco = new GroupBox();
            lblCaminhoLabel = new Label();
            txtBancoDados = new TextBox();
            btnBrowse = new Button();
            btnConectar = new Button();
            lblStatus = new Label();
            panelBotoes = new Panel();
            btnExecutar = new Button();
            btnLimparGrid = new Button();
            grpResultados = new GroupBox();
            gridResultados = new DataGridView();
            grpLog = new GroupBox();
            lstLog = new ListBox();
            btnLimparLog = new Button();
            btnTeste = new Button();
            ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
            splitMain.Panel1.SuspendLayout();
            splitMain.Panel2.SuspendLayout();
            splitMain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitLeft).BeginInit();
            splitLeft.Panel1.SuspendLayout();
            splitLeft.Panel2.SuspendLayout();
            splitLeft.SuspendLayout();
            grpSQL.SuspendLayout();
            grpBanco.SuspendLayout();
            panelBotoes.SuspendLayout();
            grpResultados.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)gridResultados).BeginInit();
            grpLog.SuspendLayout();
            SuspendLayout();
            // 
            // splitMain
            // 
            splitMain.Dock = DockStyle.Fill;
            splitMain.Location = new Point(0, 0);
            splitMain.Name = "splitMain";
            splitMain.Orientation = Orientation.Horizontal;
            // 
            // splitMain.Panel1
            // 
            splitMain.Panel1.Controls.Add(splitLeft);
            // 
            // splitMain.Panel2
            // 
            splitMain.Panel2.Controls.Add(grpLog);
            splitMain.Size = new Size(1200, 700);
            splitMain.SplitterDistance = 497;
            splitMain.TabIndex = 0;
            // 
            // splitLeft
            // 
            splitLeft.Dock = DockStyle.Fill;
            splitLeft.Location = new Point(0, 0);
            splitLeft.Name = "splitLeft";
            // 
            // splitLeft.Panel1
            // 
            splitLeft.Panel1.Controls.Add(grpSQL);
            // 
            // splitLeft.Panel2
            // 
            splitLeft.Panel2.Controls.Add(grpResultados);
            splitLeft.Size = new Size(1200, 497);
            splitLeft.SplitterDistance = 966;
            splitLeft.TabIndex = 0;
            // 
            // grpSQL
            // 
            grpSQL.Controls.Add(txtSQL);
            grpSQL.Controls.Add(grpBanco);
            grpSQL.Controls.Add(panelBotoes);
            grpSQL.Dock = DockStyle.Fill;
            grpSQL.Location = new Point(0, 0);
            grpSQL.Name = "grpSQL";
            grpSQL.Padding = new Padding(8, 4, 8, 4);
            grpSQL.Size = new Size(966, 497);
            grpSQL.TabIndex = 1;
            grpSQL.TabStop = false;
            grpSQL.Text = "Comando SQL";
            // 
            // txtSQL
            // 
            txtSQL.AcceptsTab = true;
            txtSQL.Dock = DockStyle.Fill;
            txtSQL.Font = new Font("Consolas", 10F);
            txtSQL.Location = new Point(8, 102);
            txtSQL.Name = "txtSQL";
            txtSQL.ScrollBars = RichTextBoxScrollBars.Vertical;
            txtSQL.Size = new Size(950, 355);
            txtSQL.TabIndex = 3;
            txtSQL.Text = "";
            // 
            // grpBanco
            // 
            grpBanco.Controls.Add(lblCaminhoLabel);
            grpBanco.Controls.Add(txtBancoDados);
            grpBanco.Controls.Add(btnBrowse);
            grpBanco.Controls.Add(btnConectar);
            grpBanco.Controls.Add(lblStatus);
            grpBanco.Dock = DockStyle.Top;
            grpBanco.Location = new Point(8, 20);
            grpBanco.Name = "grpBanco";
            grpBanco.Padding = new Padding(8, 4, 8, 4);
            grpBanco.Size = new Size(950, 82);
            grpBanco.TabIndex = 2;
            grpBanco.TabStop = false;
            grpBanco.Text = "Banco de Dados SQLite";
            // 
            // lblCaminhoLabel
            // 
            lblCaminhoLabel.AutoSize = true;
            lblCaminhoLabel.Location = new Point(13, 25);
            lblCaminhoLabel.Name = "lblCaminhoLabel";
            lblCaminhoLabel.Size = new Size(59, 15);
            lblCaminhoLabel.TabIndex = 0;
            lblCaminhoLabel.Text = "Caminho:";
            // 
            // txtBancoDados
            // 
            txtBancoDados.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtBancoDados.Location = new Point(70, 22);
            txtBancoDados.Name = "txtBancoDados";
            txtBancoDados.Size = new Size(832, 23);
            txtBancoDados.TabIndex = 1;
            // 
            // btnBrowse
            // 
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Location = new Point(909, 20);
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Size = new Size(30, 25);
            btnBrowse.TabIndex = 2;
            btnBrowse.Text = "…";
            btnBrowse.Click += btnBrowse_Click;
            // 
            // btnConectar
            // 
            btnConectar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnConectar.Location = new Point(812, 51);
            btnConectar.Name = "btnConectar";
            btnConectar.Size = new Size(90, 25);
            btnConectar.TabIndex = 3;
            btnConectar.Text = "Conectar";
            btnConectar.Click += btnConectar_Click;
            // 
            // lblStatus
            // 
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.AutoSize = true;
            lblStatus.ForeColor = Color.Gray;
            lblStatus.Location = new Point(724, 56);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(82, 15);
            lblStatus.TabIndex = 4;
            lblStatus.Text = "Desconectado";
            // 
            // panelBotoes
            // 
            panelBotoes.Controls.Add(btnTeste);
            panelBotoes.Controls.Add(btnExecutar);
            panelBotoes.Controls.Add(btnLimparGrid);
            panelBotoes.Dock = DockStyle.Bottom;
            panelBotoes.Location = new Point(8, 457);
            panelBotoes.Name = "panelBotoes";
            panelBotoes.Size = new Size(950, 36);
            panelBotoes.TabIndex = 1;
            // 
            // btnExecutar
            // 
            btnExecutar.Location = new Point(0, 5);
            btnExecutar.Name = "btnExecutar";
            btnExecutar.Size = new Size(150, 28);
            btnExecutar.TabIndex = 0;
            btnExecutar.Text = "▶  Executar (F5)";
            btnExecutar.Click += btnExecutar_Click;
            // 
            // btnLimparGrid
            // 
            btnLimparGrid.Location = new Point(158, 5);
            btnLimparGrid.Name = "btnLimparGrid";
            btnLimparGrid.Size = new Size(110, 28);
            btnLimparGrid.TabIndex = 1;
            btnLimparGrid.Text = "Limpar Grade";
            btnLimparGrid.Click += btnLimparGrid_Click;
            // 
            // grpResultados
            // 
            grpResultados.Controls.Add(gridResultados);
            grpResultados.Dock = DockStyle.Fill;
            grpResultados.Location = new Point(0, 0);
            grpResultados.Name = "grpResultados";
            grpResultados.Padding = new Padding(4);
            grpResultados.Size = new Size(230, 497);
            grpResultados.TabIndex = 0;
            grpResultados.TabStop = false;
            grpResultados.Text = "Resultados";
            // 
            // gridResultados
            // 
            gridResultados.AllowUserToAddRows = false;
            gridResultados.AllowUserToDeleteRows = false;
            dataGridViewCellStyle1.BackColor = Color.AliceBlue;
            gridResultados.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            gridResultados.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            gridResultados.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            gridResultados.Dock = DockStyle.Fill;
            gridResultados.Location = new Point(4, 20);
            gridResultados.Name = "gridResultados";
            gridResultados.ReadOnly = true;
            gridResultados.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridResultados.Size = new Size(222, 473);
            gridResultados.TabIndex = 0;
            // 
            // grpLog
            // 
            grpLog.Controls.Add(lstLog);
            grpLog.Controls.Add(btnLimparLog);
            grpLog.Dock = DockStyle.Fill;
            grpLog.Location = new Point(0, 0);
            grpLog.Name = "grpLog";
            grpLog.Padding = new Padding(4);
            grpLog.Size = new Size(1200, 199);
            grpLog.TabIndex = 0;
            grpLog.TabStop = false;
            grpLog.Text = "Log";
            // 
            // lstLog
            // 
            lstLog.Dock = DockStyle.Fill;
            lstLog.Font = new Font("Consolas", 9F);
            lstLog.HorizontalScrollbar = true;
            lstLog.IntegralHeight = false;
            lstLog.Location = new Point(4, 20);
            lstLog.Name = "lstLog";
            lstLog.Size = new Size(1192, 147);
            lstLog.TabIndex = 0;
            // 
            // btnLimparLog
            // 
            btnLimparLog.Dock = DockStyle.Bottom;
            btnLimparLog.Location = new Point(4, 167);
            btnLimparLog.Name = "btnLimparLog";
            btnLimparLog.Size = new Size(1192, 28);
            btnLimparLog.TabIndex = 1;
            btnLimparLog.Text = "Limpar Log";
            btnLimparLog.Click += btnLimparLog_Click;
            // 
            // btnTeste
            // 
            btnTeste.Location = new Point(274, 5);
            btnTeste.Name = "btnTeste";
            btnTeste.Size = new Size(110, 28);
            btnTeste.TabIndex = 2;
            btnTeste.Text = "Teste";
            btnTeste.Click += btnTeste_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1200, 700);
            Controls.Add(splitMain);
            MinimumSize = new Size(900, 550);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "VisualData — Teste SQLite / Yordi.EntityMultiSQL";
            splitMain.Panel1.ResumeLayout(false);
            splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
            splitMain.ResumeLayout(false);
            splitLeft.Panel1.ResumeLayout(false);
            splitLeft.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitLeft).EndInit();
            splitLeft.ResumeLayout(false);
            grpSQL.ResumeLayout(false);
            grpBanco.ResumeLayout(false);
            grpBanco.PerformLayout();
            panelBotoes.ResumeLayout(false);
            grpResultados.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)gridResultados).EndInit();
            grpLog.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer splitMain;
        private SplitContainer splitLeft;
        private GroupBox grpSQL;
        private Panel panelBotoes;
        private Button btnExecutar;
        private Button btnLimparGrid;
        private GroupBox grpResultados;
        private DataGridView gridResultados;
        private GroupBox grpLog;
        private ListBox lstLog;
        private Button btnLimparLog;
        private RichTextBox txtSQL;
        private GroupBox grpBanco;
        private Label lblCaminhoLabel;
        private TextBox txtBancoDados;
        private Button btnBrowse;
        private Button btnConectar;
        private Label lblStatus;
        private Button btnTeste;
    }
}

