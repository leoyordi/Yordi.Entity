using Microsoft.Extensions.Logging;
using System.Data;
using Yordi.Tools;
using Yordi.EntityMultiSQL;

namespace VisualData
{
    public partial class Form1 : Form
    {
        private IBDConexao? _conexao;
        private readonly ILogger _logger;

        public Form1()
        {
            InitializeComponent();
            _logger = LoggerYordi.LoggerInstance();
            txtBancoDados.Text = AppDomain.CurrentDomain.BaseDirectory;
        }

        private void TxtSQL_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                btnExecutar.PerformClick();
            }
        }

        // ── Banco de Dados ───────────────────────────────────────────────────

        private void btnConectar_Click(object sender, EventArgs e)
        {
            try
            {
                _conexao?.Dispose();
                _conexao = null;

                var caminho = txtBancoDados.Text.Trim();
                if (string.IsNullOrWhiteSpace(caminho))
                {
                    AdicionarLog("Informe o caminho completo do banco de dados SQLite.");
                    return;
                }

                var pasta = Path.GetDirectoryName(caminho) ?? ".";
                var arquivo = Path.GetFileName(caminho);

                var config = new DBConfig
                {
                    TipoDB = TipoDB.SQLite,
                    Local = pasta,
                    Database = arquivo,
                    TryReconnect = 3,
                    SecondsWaitToTry = 1,
                    UsarSQLiteWALMode = true
                };

                _conexao = new BDConexao(config);

                if (_conexao is EventBaseClass eb)
                {
                    eb.MessageEvent += (msg, origem, line, path) => AdicionarLog($"[MSG] {msg}");
                    eb.ErroEvent += (msg, origem, line, path) => AdicionarLog($"[ERR] {msg}");
                    eb.ExceptionEvent += (ex, origem, line, path) => AdicionarLog($"[EXC] {ex.Message}");
                }

                lblStatus.Text = "Conectado";
                lblStatus.ForeColor = Color.Green;
                AdicionarLog($"Conectado: {caminho}");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Erro";
                lblStatus.ForeColor = Color.Red;
                AdicionarLog($"[FALHA] {ex.Message}");
                _logger.LogError(ex, "Erro ao conectar");
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Selecionar banco de dados SQLite",
                Filter = "SQLite (*.db;*.sqlite;*.db3)|*.db;*.sqlite;*.db3|Todos os arquivos (*.*)|*.*",
                CheckFileExists = false
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                txtBancoDados.Text = dlg.FileName;
        }

        // ── Execução SQL ─────────────────────────────────────────────────────

        private async void btnExecutar_Click(object sender, EventArgs e)
        {
            var sql = txtSQL.Text.Trim();
            if (string.IsNullOrWhiteSpace(sql))
            {
                AdicionarLog("Digite um comando SQL.");
                return;
            }

            if (_conexao == null)
            {
                AdicionarLog("Não há conexão ativa. Conecte-se a um banco primeiro.");
                return;
            }

            try
            {
                btnExecutar.Enabled = false;
                AdicionarLog($"[SQL] {sql}");

                var dt = await ExecutarConsultaAsync(sql);

                gridResultados.DataSource = null;
                gridResultados.DataSource = dt;
                gridResultados.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);

                AdicionarLog($"Retornadas {dt.Rows.Count} linha(s).");
            }
            catch (Exception ex)
            {
                AdicionarLog($"[ERRO] {ex.Message}");
                _logger.LogError(ex, "Erro ao executar SQL");
            }
            finally
            {
                btnExecutar.Enabled = true;
            }
        }

        private async Task<DataTable> ExecutarConsultaAsync(string sql)
        {
            var dt = new DataTable();
            var conn = await _conexao!.ObterConexaoAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            dt.Load(reader);
            return dt;
        }

        // ── Log ──────────────────────────────────────────────────────────────

        private void AdicionarLog(string mensagem)
        {
            if (lstLog.InvokeRequired)
            {
                lstLog.Invoke(() => AdicionarLog(mensagem));
                return;
            }

            var linha = $"[{DateTime.Now:HH:mm:ss}] {mensagem}";
            lstLog.Items.Add(linha);
            lstLog.TopIndex = lstLog.Items.Count - 1;

            _logger.LogInformation(mensagem);
        }

        private void btnLimparLog_Click(object sender, EventArgs e)
        {
            lstLog.Items.Clear();
        }

        private void btnLimparGrid_Click(object sender, EventArgs e)
        {
            gridResultados.DataSource = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _conexao?.Dispose();
            base.OnFormClosing(e);
        }

        private void btnTeste_Click(object sender, EventArgs e)
        {
            if (_conexao == null)
            {
                AdicionarLog("Não há conexão ativa. Conecte-se a um banco primeiro.");
                return;
            }
            gridResultados.DataSource = null;

            var repo = new RepoEntidade(_conexao);
            var lista = repo.Lista();
            gridResultados.DataSource = lista.Result;
        }
    }
}
