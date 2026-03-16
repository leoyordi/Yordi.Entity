using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Gerencia o ciclo de vida de conexões SQLite, incluindo WAL mode,
    /// serialização de escritas e shutdown gracioso.
    /// <para>
    /// Não herda de <see cref="DbConnection"/> — <see cref="SQLiteConnection"/> é sealed.
    /// Em vez disso, atua como factory e gerenciador:
    /// <list type="bullet">
    ///   <item><see cref="CriarConexao"/> — cria <see cref="SQLiteConnection"/> com BusyTimeout</item>
    ///   <item><see cref="ConfigurarConexaoAsync"/> — habilita WAL mode (idempotente)</item>
    ///   <item><see cref="AguardarLockEscritaAsync"/>/<see cref="LiberarLockEscrita"/> — semáforo de escrita</item>
    ///   <item><see cref="EncerrarAsync"/> — checkpoint WAL + fechamento para shutdown</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Modelo de conexão:</b> cada operação cria sua própria <see cref="SQLiteConnection"/>
    /// via <see cref="CriarConexao"/>. O pool nativo do SQLite reaproveita a conexão nativa —
    /// instanciar o objeto .NET é barato. Isso elimina <see cref="ObjectDisposedException"/>
    /// causado por múltiplas threads compartilhando a mesma instância de conexão.
    /// </para>
    /// </summary>
    internal sealed class SQLiteConnectionManager
    {
        private readonly string _connectionString;
        private readonly bool _usarWALMode;

        /// <summary>
        /// Flag thread-safe para evitar PRAGMAs redundantes após o primeiro sucesso.
        /// WAL mode persiste no arquivo .db — basta definir uma vez.
        /// </summary>
        private volatile bool _walHabilitado;

        /// <summary>
        /// Semáforo estático para serializar operações de escrita no SQLite.
        /// Estático porque todas as instâncias de repositório compartilham o mesmo arquivo .db.
        /// </summary>
        private static readonly SemaphoreSlim _writeLock = new(1, 1);

        private SQLiteConnectionManager(string connectionString, bool usarWALMode)
        {
            _connectionString = connectionString;
            _usarWALMode = usarWALMode;
        }

        /// <summary>
        /// String de conexão montada, com BusyTimeout garantido.
        /// </summary>
        internal string ConnectionString => _connectionString;

        #region Factory

        /// <summary>
        /// Cria o manager a partir do <see cref="DBConfig"/>.
        /// Cria o arquivo .db se não existir e garante BusyTimeout na connection string.
        /// </summary>
        /// <exception cref="ArgumentNullException">Se Local, Database ou ConnectionString forem nulos/vazios.</exception>
        internal static SQLiteConnectionManager Criar(DBConfig dbConfig)
        {
            CriarArquivoSeNecessario(dbConfig.Local, dbConfig.Database);

            string? conn = dbConfig.StringDeConexaoMontada();
            if (string.IsNullOrEmpty(conn))
                throw new ArgumentNullException("ConnectionString",
                    "Sem dados de conexão de banco de dados");

            if (!conn.Contains("BusyTimeout", StringComparison.OrdinalIgnoreCase))
                conn = conn.TrimEnd(';') + ";BusyTimeout=30000";

            return new SQLiteConnectionManager(conn, true.Equals(dbConfig.UsarSQLiteWALMode));
        }

        /// <summary>
        /// Cria uma nova <see cref="SQLiteConnection"/> pronta para ser aberta.
        /// O pool nativo do SQLite reaproveita a conexão nativa — criar o objeto .NET é barato.
        /// Cada chamada retorna uma instância independente, segura para uso com <c>using</c>.
        /// </summary>
        internal SQLiteConnection CriarConexao() => new(_connectionString);

        /// <summary>
        /// Cria o arquivo SQLite se não existir.
        /// </summary>
        internal static void CriarArquivoSeNecessario(string? local, string? database)
        {
            if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(database))
                throw new ArgumentNullException("Local ou Database",
                    "Sem dados de conexão de banco de dados");

            string file = FileTools.Combina(local, database);
            if (!FileTools.ArquivoExiste(file))
                SQLiteConnection.CreateFile(file);
        }

        #endregion

        #region WAL Mode

        /// <summary>
        /// Habilita WAL mode na conexão aberta, se configurado e ainda não habilitado.
        /// <para>
        /// WAL mode persiste no arquivo .db — basta definir uma vez por vida útil do banco.
        /// Chamadas subsequentes são no-op graças à flag <see cref="_walHabilitado"/> (volatile).
        /// </para>
        /// </summary>
        internal async Task ConfigurarConexaoAsync(DbConnection conexao)
        {
            if (!_usarWALMode || _walHabilitado || conexao.State != ConnectionState.Open)
                return;

            try
            {
                using var cmd = conexao.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                await cmd.ExecuteNonQueryAsync();
                _walHabilitado = true;
            }
            catch
            {
                // WAL mode é desejável mas não obrigatório — não impede operação
            }
        }

        #endregion

        #region Write Lock

        /// <summary>
        /// Adquire lock exclusivo para operações de escrita no SQLite.
        /// Todas as instâncias de repositório compartilham este semáforo estático.
        /// </summary>
        internal static async Task<bool> AguardarLockEscritaAsync(
            CancellationToken cancellationToken = default, int timeout = 30000)
        {
            try
            {
                return await _writeLock.WaitAsync(timeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Libera o lock de escrita. Seguro para chamar mesmo se o lock não foi adquirido.
        /// </summary>
        internal static void LiberarLockEscrita()
        {
            try
            {
                if (_writeLock.CurrentCount == 0)
                    _writeLock.Release();
            }
            catch (SemaphoreFullException) { /* já estava liberado */ }
        }

        private static void ReleaseLockSafe()
        {
            try { _writeLock.Release(); }
            catch (SemaphoreFullException) { }
        }

        #endregion

        #region Shutdown

        /// <summary>
        /// Encerra a conexão SQLite de forma graciosa:
        /// <list type="number">
        ///   <item>Aguarda escritas pendentes (semáforo)</item>
        ///   <item>Faz checkpoint WAL (na conexão fornecida ou em uma temporária)</item>
        ///   <item>Fecha e descarta a conexão, limpa pools</item>
        /// </list>
        /// </summary>
        /// <param name="conexaoAtual">
        /// Conexão atualmente em uso (pode ser null).
        /// Se null, cria conexão temporária para o checkpoint — necessário porque
        /// <see cref="BDConexao"/> não mantém conexão armazenada para SQLite.
        /// </param>
        internal void Encerrar(DbConnection? conexaoAtual)
        {
            bool lockObtido = false;
            try
            {
                lockObtido = _writeLock.Wait(TimeSpan.FromSeconds(10));
                if (conexaoAtual != null && conexaoAtual.State == ConnectionState.Open)
                {
                    ExecutarCheckpoint(conexaoAtual);
                }
                else
                {
                    using var temp = CriarConexao();
                    temp.Open();
                    ExecutarCheckpoint(temp);
                }
            }
            catch (ObjectDisposedException) { }
            catch { }
            finally
            {
                if (lockObtido)
                    ReleaseLockSafe();
            }

            FecharConexao(conexaoAtual);
        }

        /// <summary>
        /// Versão assíncrona de <see cref="Encerrar"/>.
        /// </summary>
        internal async Task EncerrarAsync(DbConnection? conexaoAtual)
        {
            bool lockObtido = false;
            try
            {
                lockObtido = await _writeLock.WaitAsync(TimeSpan.FromSeconds(10));
                if (conexaoAtual != null && conexaoAtual.State == ConnectionState.Open)
                {
                    await ExecutarCheckpointAsync(conexaoAtual);
                }
                else
                {
                    using var temp = CriarConexao();
                    await temp.OpenAsync();
                    await ExecutarCheckpointAsync(temp);
                }
            }
            catch (ObjectDisposedException) { }
            catch { }
            finally
            {
                if (lockObtido)
                    ReleaseLockSafe();
            }

            FecharConexao(conexaoAtual);
        }

        private static void ExecutarCheckpoint(DbConnection? conexao)
        {
            if (conexao == null || conexao.State != ConnectionState.Open) return;
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA shrink_memory;";
            cmd.ExecuteNonQuery();
        }

        private static async Task ExecutarCheckpointAsync(DbConnection? conexao)
        {
            if (conexao == null || conexao.State != ConnectionState.Open) return;
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "PRAGMA shrink_memory;";
            await cmd.ExecuteNonQueryAsync();
        }

        private static void FecharConexao(DbConnection? conexao)
        {
            if (conexao != null)
            {
                try
                {
                    if (conexao.State != ConnectionState.Closed)
                        conexao.Close();
                    conexao.Dispose();
                }
                catch (ObjectDisposedException) { }
            }
            SQLiteConnection.ClearAllPools();
        }

        #endregion

        #region Utilitários estáticos

        /// <summary>
        /// Obtém informações do arquivo SQLite (PRAGMA schema_version).
        /// Usa conexão temporária independente.
        /// </summary>
        internal static string? ObterInformacoesArquivo(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return null;
            try
            {
                using var conexao = new SQLiteConnection(connectionString);
                conexao.Open();
                using var comando = new SQLiteCommand("PRAGMA schema_version", conexao);
                return comando.ExecuteScalar()?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Libera todos os locks do SQLite para shutdown definitivo.
        /// Cria conexão temporária, faz checkpoint, muda journal_mode para DELETE
        /// (removendo arquivos -wal/-shm) e limpa pools.
        /// <para>Seguro para chamar após <see cref="Encerrar"/>.</para>
        /// </summary>
        internal static async Task<bool> LiberarLocksAsync(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return false;

            try
            {
                SQLiteConnection.ClearAllPools();

                using var conexao = new SQLiteConnection(connectionString);
                await conexao.OpenAsync();
                using var cmd = conexao.CreateCommand();

                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "PRAGMA journal_mode=DELETE;";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "PRAGMA shrink_memory;";
                await cmd.ExecuteNonQueryAsync();

                await conexao.CloseAsync();
                SQLiteConnection.ClearAllPools();

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}