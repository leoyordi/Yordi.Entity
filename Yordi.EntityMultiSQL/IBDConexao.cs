using System.Data.Common;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public interface IBDConexao
    {
        Version? ServerVersion { get; }
        TipoDB TipoDB { get; }
        DBConfig DBConfig { get; }
        bool AllowCurrentTimeStamp { get; }
        bool Conectado { get; }
        IEnumerable<Type>? Tabelas { get; }
        Task<bool> IsServerConnectedAsync();
        Task<DbConnection> ObterConexaoAsync(int? timesToReconnect = null);
    }
}
