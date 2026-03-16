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
