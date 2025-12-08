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
        private static DbConnection? _conexao;
        private static bool _nova;

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
            //Batteries.Init();
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
                    var task = conexao.OpenAsync();
                    Task.WaitAll(task);
                    if (task.IsFaulted)
                        if (i < times)
                            await Task.Delay(time * 1000);
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

        private DbConnection ConnectionObject()
        {
            if (_conexao != null && !_nova && _dbConfig.TipoDB != TipoDB.SQLite)
                return _conexao;

            switch (_dbConfig.TipoDB)
            {
                case TipoDB.MySQL:
                    _conexao = new MySqlConnection(_dbConfig.StringDeConexaoMontada());
                    break;
                case TipoDB.SQLite:
                default:
                    CreateSQLiteDB();
                    string? conn = _dbConfig.StringDeConexaoMontada();
                    if (!string.IsNullOrEmpty(conn))
                    {
                        try
                        {
                            _conexao = new SQLiteConnection(conn);
                        }
                        catch (Exception e) 
                        { 
                            Error(e); 
                            throw; 
                        }
                    }
                    else
                        throw new ArgumentNullException("ConnectionString", "Sem dados de conexão de banco de dados");

                    break;
            }
            // _conexao.ConnectionString = _dbConfig.ConnectionString;
            _nova = false;
            return _conexao;
        }
        private void CreateSQLiteDB()
        {
            if (string.IsNullOrEmpty(_dbConfig.Local) || string.IsNullOrEmpty(_dbConfig.Database))
                throw new ArgumentNullException("Local ou Database", "Sem dados de conexão de banco de dados");
            string file = FileTools.Combina(_dbConfig.Local, _dbConfig.Database);
            if (!FileTools.ArquivoExiste(file))
            {
                if (Verbose)
                    Message($"Criando arquivo SQLite: {file}");
                SQLiteConnection.CreateFile(file);
            }
        }

        public string? ObterInformacoesArquivoSQLite()
        {
            string? informacoesArquivo = string.Empty;
            try
            {
                using (var conexao = new SQLiteConnection(_dbConfig.StringDeConexaoMontada()))
                {
                    conexao.Open();
                    using (var comando = new SQLiteCommand("PRAGMA schema_version", conexao))
                    {
                        informacoesArquivo = comando?.ExecuteScalar()?.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Error(ex);
            }
            return informacoesArquivo;
        }
    }
}
