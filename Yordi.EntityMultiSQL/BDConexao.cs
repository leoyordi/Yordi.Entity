using MySql.Data.MySqlClient;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Reflection;
using System.Text.Json.Serialization;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public class BDConexao : EventBaseClass, IBDConexao
    {
        private readonly DBConfig _dbConfig;

        /// <summary>
        /// Conexão reutilizável — usada APENAS para bancos não-SQLite (MySQL, etc.).
        /// <para>Para SQLite, cada chamada a <see cref="ObterConexaoAsync"/> cria uma nova
        /// <see cref="SQLiteConnection"/> — o pool nativo reaproveita a conexão nativa,
        /// e criar o objeto .NET é barato.</para>
        /// </summary>
        private DbConnection? _conexao;
        private bool _nova;

        /// <summary>
        /// Manager estático compartilhado: configuração WAL, semáforo de escrita e shutdown.
        /// Estático porque todas as instâncias que acessam o mesmo arquivo .db
        /// devem compartilhar o mesmo semáforo e estado WAL.
        /// </summary>
        private static SQLiteConnectionManager? _sqliteManager;

        public TipoDB TipoDB { get { return _dbConfig.TipoDB; } }

        public Version? ServerVersion { get; private set; }

        private bool conectado;
        public bool Conectado => conectado;

        [JsonIgnore]
        public IEnumerable<Type>? Tabelas { get; private set; }

        public DBConfig DBConfig => _dbConfig;
        public bool AllowCurrentTimeStamp
        {
            get
            {
                if (_dbConfig.TipoDB == TipoDB.MySQL)
                    return ServerVersion >= Version.Parse("5.7");
                else if (_dbConfig.TipoDB == TipoDB.SQLite)
                    return false;
                return true;
            }
        }

        public bool Verbose => _dbConfig.Verbose ?? false;

        public BDConexao(DBConfig dbConfig)
        {
            _dbConfig = dbConfig;
            _nova = true;
            ListaTabelas();
        }

        private void ListaTabelas()
        {
            try
            {
                IEnumerable<Type> types = from assembly in AppDomain.CurrentDomain.GetAssemblies()
                                          from type in assembly.GetTypes()
                                          where type.IsDefined(typeof(POCOtoDBAttribute))
                                          select type;

                if (types.Any())
                    Tabelas = types.ToList();
            }
            catch (Exception ex) { Error(ex); }
        }

        #region Lock de escrita — delega para SQLiteConnectionManager

        /// <summary>
        /// Adquire lock exclusivo para operações de escrita no SQLite.
        /// Para MySQL e outros bancos, retorna imediatamente com true.
        /// </summary>
        public async Task<bool> AguardarLockEscritaAsync(CancellationToken cancellationToken = default, int timeout = 30000)
        {
            if (_dbConfig.TipoDB != TipoDB.SQLite)
                return true;

            return await SQLiteConnectionManager.AguardarLockEscritaAsync(cancellationToken, timeout);
        }

        /// <summary>
        /// Libera o lock de escrita do SQLite.
        /// Seguro para chamar mesmo se o lock não foi adquirido.
        /// </summary>
        public void LiberarLockEscrita()
        {
            if (_dbConfig.TipoDB != TipoDB.SQLite)
                return;

            SQLiteConnectionManager.LiberarLockEscrita();
        }

        #endregion

        #region Conexão

        public async Task<DbConnection> ObterConexaoAsync(int? timesToReconnect = null)
        {
            DbConnection conexao = ConnectionObject();
            int times = timesToReconnect ?? _dbConfig.TryReconnect;
            if (times == 0)
                times = 3;
            int time = _dbConfig.SecondsWaitToTry;
            if (time == 0)
                time = 1;

            for (int i = 0; i <= times; i++)
            {
                try
                {
                    if (conexao.State == ConnectionState.Open)
                    {
                        if (ServerVersion == null)
                            await DefinirVersaoServidorAsync(conexao);
                        conectado = true;
                        break;
                    }

                    await conexao.OpenAsync();

                    // WAL mode gerenciado pelo manager (idempotente — no-op após primeiro sucesso)
                    if (_sqliteManager != null && conexao.State == ConnectionState.Open)
                        await _sqliteManager.ConfigurarConexaoAsync(conexao);

                    if (ServerVersion == null)
                        await DefinirVersaoServidorAsync(conexao);
                    conectado = true;
                    break;
                }
                catch (Exception) when (i < times)
                {
                    conectado = false;
                    await Task.Delay(time * 1000);
                }
                catch (Exception e)
                {
                    conectado = false;
                    Error(e);
                }
            }
            if (conexao.State != ConnectionState.Open)
            {
                conectado = false;
                Error("Sem conexão");
            }
            return conexao;
        }

        private async Task DefinirVersaoServidorAsync(DbConnection conexao)
        {
            if (_dbConfig.TipoDB == TipoDB.MySQL && conexao.ServerVersion == null)
            {
                using var comando = conexao.CreateCommand();
                comando.CommandText = "SELECT VERSION()";
                var versao = (await comando.ExecuteScalarAsync())?.ToString();
                if (!string.IsNullOrEmpty(versao))
                    ServerVersion = Conversores.ToVersion(versao.ToString());
            }
            else
            {
                ServerVersion = Version.Parse(conexao.ServerVersion);
            }
        }

        public async Task<bool> IsServerConnectedAsync()
        {
            try
            {
                using DbConnection conexaoSql = await ObterConexaoAsync(0);
            }
            catch { }
            return conectado;
        }

        /// <summary>
        /// Retorna uma conexão pronta para uso.
        /// <para><b>SQLite:</b> sempre cria nova <see cref="SQLiteConnection"/>.
        /// O pool nativo do SQLite reaproveita a conexão nativa — instanciar o objeto .NET é barato.
        /// Cada operação/thread recebe sua própria conexão, eliminando <see cref="ObjectDisposedException"/>
        /// causado por <c>using</c> que descartava a conexão compartilhada.</para>
        /// <para><b>MySQL e outros:</b> reutiliza <see cref="_conexao"/> de instância se estiver aberta;
        /// caso contrário, descarta e cria nova.</para>
        /// </summary>
        private DbConnection ConnectionObject()
        {
            // ── SQLite: sempre nova conexão ──────────────────────────────────
            // WAL mode permite múltiplos readers concorrentes.
            // Cada chamada recebe sua própria conexão → seguro com using/Dispose.
            if (_dbConfig.TipoDB == TipoDB.SQLite)
            {
                try
                {
                    _sqliteManager ??= SQLiteConnectionManager.Criar(_dbConfig);
                    return _sqliteManager.CriarConexao();
                }
                catch (Exception e)
                {
                    Error(e);
                    throw;
                }
            }

            // ── Outros bancos: reutiliza conexão de instância ────────────────
            if (_conexao != null && !_nova)
            {
                try
                {
                    var state = _conexao.State;
                    if (state == ConnectionState.Open || state == ConnectionState.Connecting)
                        return _conexao;

                    _conexao.Dispose();
                    _conexao = null;
                }
                catch (ObjectDisposedException)
                {
                    _conexao = null;
                }
            }

            switch (_dbConfig.TipoDB)
            {
                case TipoDB.MySQL:
                    _conexao = new MySqlConnection(_dbConfig.StringDeConexaoMontada());
                    break;
                default:
                    throw new NotSupportedException(
                        $"TipoDB '{_dbConfig.TipoDB}' não possui implementação de conexão.");
            }
            _nova = false;
            return _conexao;
        }

        #endregion

        #region SQLite — delegação para SQLiteConnectionManager

        public string? ObterInformacoesArquivoSQLite()
        {
            try
            {
                return SQLiteConnectionManager.ObterInformacoesArquivo(
                    _dbConfig.StringDeConexaoMontada());
            }
            catch (Exception ex)
            {
                Error(ex);
                return null;
            }
        }

        public void ResetarConexao()
        {
            try
            {
                if (_conexao != null)
                {
                    try
                    {
                        if (_conexao.State != ConnectionState.Closed)
                            _conexao.Close();
                        _conexao.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                    _conexao = null;
                }
                _nova = true;
                conectado = false;

                // Para SQLite, limpar pools libera conexões nativas em cache
                if (_dbConfig.TipoDB == TipoDB.SQLite)
                    SQLiteConnection.ClearAllPools();

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        public async Task<bool> LiberarLocksSQLiteAsync()
        {
            if (_dbConfig.TipoDB != TipoDB.SQLite)
                return true;

            try
            {
                ResetarConexao();
                return await SQLiteConnectionManager.LiberarLocksAsync(
                    _sqliteManager?.ConnectionString ?? _dbConfig.StringDeConexaoMontada());
            }
            catch (Exception ex)
            {
                Error(ex);
                return false;
            }
        }

        #endregion

        #region IDisposable / IAsyncDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_dbConfig.TipoDB == TipoDB.SQLite && _sqliteManager != null)
            {
                // SQLite não armazena _conexao — Encerrar cria conexão temporária para checkpoint
                _sqliteManager.Encerrar(null);
                _sqliteManager = null;
            }
            else if (_conexao != null)
            {
                try
                {
                    if (_conexao.State != ConnectionState.Closed)
                        _conexao.Close();
                    _conexao.Dispose();
                }
                catch (ObjectDisposedException) { }
                _conexao = null;
            }

            _nova = true;
            conectado = false;

            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (_dbConfig.TipoDB == TipoDB.SQLite && _sqliteManager != null)
            {
                await _sqliteManager.EncerrarAsync(null);
                _sqliteManager = null;
            }
            else if (_conexao != null)
            {
                try
                {
                    if (_conexao.State != ConnectionState.Closed)
                        await _conexao.CloseAsync();
                    await _conexao.DisposeAsync();
                }
                catch (ObjectDisposedException) { }
                _conexao = null;
            }

            _nova = true;
            conectado = false;

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
