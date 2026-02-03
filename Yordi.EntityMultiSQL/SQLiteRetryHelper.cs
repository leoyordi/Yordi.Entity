using System.Data;
using System.Data.SQLite;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Helper para executar operações SQLite com retry automático em caso de "database is locked"
    /// </summary>
    public static class SQLiteRetryHelper
    {
        /// <summary>
        /// Número máximo de tentativas padrão
        /// </summary>
        public static int DefaultMaxRetries { get; set; } = 3;

        /// <summary>
        /// Delay base em milissegundos entre tentativas
        /// </summary>
        public static int DefaultRetryDelayMs { get; set; } = 500;

        /// <summary>
        /// Executa uma operação async com retry em caso de database locked
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            int? maxRetries = null,
            int? retryDelayMs = null,
            Action<int, Exception>? onRetry = null)
        {
            int retries = maxRetries ?? DefaultMaxRetries;
            int delay = retryDelayMs ?? DefaultRetryDelayMs;

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (SQLiteException ex) when (IsDatabaseLocked(ex) && attempt < retries)
                {
                    onRetry?.Invoke(attempt + 1, ex);
                    await Task.Delay(delay * (attempt + 1)); // Backoff exponencial
                }
            }

            // Última tentativa sem catch
            return await operation();
        }

        /// <summary>
        /// Executa uma operação async sem retorno com retry em caso de database locked
        /// </summary>
        public static async Task ExecuteWithRetryAsync(
            Func<Task> operation,
            int? maxRetries = null,
            int? retryDelayMs = null,
            Action<int, Exception>? onRetry = null)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true;
            }, maxRetries, retryDelayMs, onRetry);
        }

        /// <summary>
        /// Verifica se a exceção é do tipo "database is locked"
        /// </summary>
        public static bool IsDatabaseLocked(SQLiteException ex)
        {
            return ex.ResultCode == SQLiteErrorCode.Busy ||
                   ex.ResultCode == SQLiteErrorCode.Locked ||
                   ex.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifica se qualquer exceção é relacionada a database locked
        /// </summary>
        public static bool IsDatabaseLocked(Exception ex)
        {
            if (ex is SQLiteException sqliteEx)
                return IsDatabaseLocked(sqliteEx);

            return ex.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Tenta liberar locks do banco SQLite
        /// </summary>
        /// <param name="connectionString">String de conexão do SQLite</param>
        /// <returns>True se conseguiu liberar, False caso contrário</returns>
        public static async Task<bool> TentarLiberarLocksAsync(string connectionString)
        {
            try
            {
                // Força garbage collection para liberar conexões não disposadas
                SQLiteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Abre conexão temporária para executar comandos de manutenção
                using var conexao = new SQLiteConnection(connectionString);
                await conexao.OpenAsync();

                using var cmd = conexao.CreateCommand();

                // Interrompe operações pendentes
                cmd.CommandText = "PRAGMA busy_timeout = 0;";
                await cmd.ExecuteNonQueryAsync();

                // Força checkpoint do WAL
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await cmd.ExecuteNonQueryAsync();

                // Libera memória do cache
                cmd.CommandText = "PRAGMA shrink_memory;";
                await cmd.ExecuteNonQueryAsync();

                // Reseta o timeout
                cmd.CommandText = "PRAGMA busy_timeout = 30000;";
                await cmd.ExecuteNonQueryAsync();

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Limpa todos os pools de conexão SQLite
        /// </summary>
        public static void LimparPoolsConexao()
        {
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// <summary>
        /// Verifica o status de lock do banco de dados
        /// </summary>
        public static async Task<SQLiteLockStatus> VerificarStatusLockAsync(string connectionString)
        {
            var status = new SQLiteLockStatus();

            try
            {
                using var conexao = new SQLiteConnection(connectionString);
                await conexao.OpenAsync();

                using var cmd = conexao.CreateCommand();

                // Verifica modo do journal
                cmd.CommandText = "PRAGMA journal_mode;";
                status.JournalMode = (await cmd.ExecuteScalarAsync())?.ToString() ?? "unknown";

                // Verifica status do WAL
                cmd.CommandText = "PRAGMA wal_checkpoint;";
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    status.WalBlocked = reader.GetInt32(0) == 1;
                    status.WalPages = reader.GetInt32(1);
                    status.WalCheckpointed = reader.GetInt32(2);
                }

                // Verifica se consegue escrever
                try
                {
                    cmd.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
                    await cmd.ExecuteNonQueryAsync();
                    status.PodeEscrever = true;
                }
                catch
                {
                    status.PodeEscrever = false;
                }

                status.Conectado = true;
            }
            catch (Exception ex)
            {
                status.Conectado = false;
                status.Erro = ex.Message;
            }

            return status;
        }
    }

    /// <summary>
    /// Status do lock do banco SQLite
    /// </summary>
    public class SQLiteLockStatus
    {
        public bool Conectado { get; set; }
        public bool PodeEscrever { get; set; }
        public bool WalBlocked { get; set; }
        public int WalPages { get; set; }
        public int WalCheckpointed { get; set; }
        public string JournalMode { get; set; } = string.Empty;
        public string? Erro { get; set; }

        public override string ToString()
        {
            if (!Conectado)
                return $"Desconectado: {Erro}";

            return $"Conectado | Escrita: {(PodeEscrever ? "OK" : "BLOQUEADA")} | " +
                   $"Journal: {JournalMode} | WAL Blocked: {WalBlocked} | " +
                   $"WAL Pages: {WalPages}/{WalCheckpointed}";
        }
    }
}