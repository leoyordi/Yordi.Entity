using System.Data.Common;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public interface IBDConexao : IDisposable, IAsyncDisposable
    {
        Version? ServerVersion { get; }
        TipoDB TipoDB { get; }
        DBConfig DBConfig { get; }
        bool AllowCurrentTimeStamp { get; }
        bool Conectado { get; }
        IEnumerable<Type>? Tabelas { get; }
        Task<bool> IsServerConnectedAsync();
        Task<DbConnection> ObterConexaoAsync(int? timesToReconnect = null);
        
        // Métodos para gerenciamento de locks
        void ResetarConexao();

        /// <summary>
        /// Executa checkpoint manual do SQLite (WAL -> arquivo principal) sem mudar o journal mode.
        /// Útil para cenários de ciclo de vida como Windows Service em <c>OnPause</c>, quando se deseja
        /// reduzir WAL e manter operação em <c>OnContinue</c>.
        /// </summary>
        Task<bool> CheckpointSQLiteAsync();

        /// <summary>
        /// Liberação forte de locks/arquivos auxiliares do SQLite.
        /// Recomendado para encerramento definitivo (ex.: <c>OnStop</c>/<c>OnShutdown</c>),
        /// normalmente seguido por <see cref="IAsyncDisposable.DisposeAsync"/>.
        /// </summary>
        Task<bool> LiberarLocksSQLiteAsync();

        /// <summary>
        /// Adquire lock exclusivo para operações de escrita no SQLite.
        /// Para outros bancos, retorna imediatamente.
        /// </summary>
        Task<bool> AguardarLockEscritaAsync(CancellationToken cancellationToken = default, int timeout = 30000);

        /// <summary>
        /// Libera o lock de escrita adquirido por <see cref="AguardarLockEscritaAsync"/>.
        /// </summary>
        void LiberarLockEscrita();
    }
}
