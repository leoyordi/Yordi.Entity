using System.Data;
using System.Data.Common;
using System.Text;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public class TableCheckByType : TableCheckBase, ITableCheck
    {
        public TipoDB TipoDB { get; set; }
        public List<string> CamposAdicionados { get; set; } = new List<string>();
        private readonly IBDConexao _bd;


        private List<Type> pocos = new List<Type>();

        public IEnumerable<Type> Tables { get => pocos; }

        public TableCheckByType(IBDConexao bd) : base(bd)
        {
            TipoDB = TipoDB.SQLite;
            _bd = bd;
        }
        public TableCheckByType(TipoDB tipoDB, IBDConexao bd) : base(bd)
        {
            TipoDB = tipoDB;
            _bd = bd;
        }
        public TableCheckByType(IBDConexao bd, bool debug) : base(bd, debug)
        {
            TipoDB = bd.TipoDB;
            _bd = bd;
        }

        public async Task<bool> TryConnectToDB()
        {
            int tentativas = 0;
            while (tentativas < 3)
            {
                try
                {
                    DbConnection conexaoSql = await _bd.ObterConexaoAsync();
                    await conexaoSql.CloseAsync();
                    conexaoSql.Dispose();
                    return true;
                }
                catch (Exception)
                {
                    tentativas++;
                    await Task.Delay(500);
                }
            }
            return false;
        }

        #region Tabelas

        private async Task<bool> TabelaExiste(Type type)
        {
            string nomeTabela = type.Name;
            int resultado = 0;
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    StringBuilder sql = new StringBuilder("SELECT ");
                    DbCommand cmm = conexaoSql.CreateCommand();
                    int cont = 0;
                    if (_bd.TipoDB == TipoDB.MySQL)
                    {
                        List<Chave> chaves = new List<Chave>();
                        chaves.Add(new Chave()
                        {
                            Campo = "TABLE_NAME",
                            Operador = Operador.IGUAL,
                            Parametro = "tabela",
                            Tipo = Tipo.STRING,
                            Valor = nomeTabela
                        });
                        chaves.Add(new Chave()
                        {
                            Operador = Operador.IGUAL,
                            Parametro = "base",
                            Tipo = Tipo.STRING,
                            Valor = conexaoSql.Database,
                            Campo = "table_schema"
                        });
                        sql.Append("COLUMN_NAME AS ColumnName FROM ");
                        sql.Append("INFORMATION_SCHEMA.COLUMNS");
                        sql.Append(" WHERE ");

                        foreach (var c in chaves)
                        {
                            if (cont > 0)
                                sql.Append(" AND ");
                            sql.Append(_bdTools.WhereExpression(c));
                            cont++;

                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, c)); // WithValue($"@{c.Nome}", c.Valor);
                        }
                    }
                    else if (_bd.TipoDB == TipoDB.SQLite)
                    {
                        sql.Append("name AS ColumnName FROM pragma_table_info ('");
                        sql.Append(nomeTabela);
                        sql.Append("')");
                    }
                    cmm.CommandText = sql.ToString();

                    using (DbDataReader reader = await cmm.ExecuteReaderAsync())
                    {
                        if (reader == null)
                            return false;

                        if (reader.HasRows)
                        {
                            while (reader.Read())
                                resultado++;
                        }
                        if (reader != null)
                        {
                            if (reader.IsClosed == false)
                                reader.Close();
                            reader.Dispose();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
            return resultado > 0;
        }
        private string CreateTable(Type type)
        {
            string _tabela = type.Name;
            object? obj = Activator.CreateInstance(type);
            if (obj == null)
                return string.Empty;
            bool temAutoIncremento = false;
            StringBuilder sql = new StringBuilder($"CREATE TABLE {_bd.DBConfig.OpenName}{_tabela}{_bd.DBConfig.CloseName} ");
            sql.Append(" (");
            List<Chave> PK = new List<Chave>();
            List<ColumnTable> colunas = BDTools.Campos(obj);

            int cont = 0;
            for (int i = 0; i < colunas.Count; i++)
            {
                if (colunas[i] == null || colunas[i].BDIgnorar)
                    continue;
                if (cont > 0)
                    sql.Append(", ");

                if (!String.IsNullOrEmpty(colunas[i].Campo))
                {
                    sql.Append($"{_bd.DBConfig.OpenName}{colunas[i].Campo}{_bd.DBConfig.CloseName} ");

                    sql.Append(FieldsSQLDefinition(colunas[i], _bd.AllowCurrentTimeStamp));
                    if (colunas[i].IsKey || colunas[i].IsAutoIncrement || !colunas[i].PermiteNulo)
                    {
                        if (colunas[i].IsAutoIncrement && _bd.TipoDB == TipoDB.SQLite)
                            sql.Append("PRIMARY KEY ");
                        else
                            sql.Append("NOT NULL ");
                        if (colunas[i].IsKey)
                        {
                            PK.Add(new Chave
                            {
                                Campo = colunas[i].Campo,
                                Tipo = colunas[i].Tipo
                            });
                        }
                        if (colunas[i].IsAutoIncrement)
                            temAutoIncremento = true;
                    }
                    else
                        sql.Append("NULL ");
                    cont++;
                }
            }
            if (PK.Count > 0)
            {
                if (_bd.TipoDB == TipoDB.MySQL && temAutoIncremento)
                    sql.Append($", UNIQUE KEY {_bd.DBConfig.OpenName}UK_{_tabela}{_bd.DBConfig.CloseName} (");
                else if (_bd.TipoDB == TipoDB.MySQL)
                    sql.Append($", PRIMARY KEY {_bd.DBConfig.OpenName}PK_{_tabela}{_bd.DBConfig.CloseName} (");
                else if (_bd.TipoDB == TipoDB.SQLite)
                    sql.Append($", UNIQUE (");
                int i = 0;
                foreach (var C in PK)
                {
                    if (i > 0)
                        sql.Append(", ");
                    sql.Append(C.Campo);
                    i++;
                }
                sql.Append(") ");
            }
            sql.Append(");");
            return sql.ToString();
        }

        private string MSSQLTriggerOnUpdate(Type type, ColumnTable coluna)
        {
            _tableName = type.Name;

            StringBuilder s = new StringBuilder();
            s.Append($"CREATE TRIGGER {_tableName}_{coluna.Campo} ON {_tableName} AFTER UPDATE AS BEGIN ");
            s.Append("SET NOCOUNT ON; ");
            s.Append($"UPDATE {_tableName} SET [{coluna.Campo}] = GETDATE() WHERE Auto IN(SELECT Auto FROM Inserted) END");
            return s.ToString();
        }
        private string MySQLTriggerOnUpdate(Type type, IEnumerable<ColumnTable> columns)
        {
            _tableName = type.Name;
            StringBuilder s = new StringBuilder();
            s.Append($"CREATE TRIGGER {_tableName}_{DateTime.Now.ToString("yyyyMMddHHmm")}_BeforeUpdate ");
            s.Append($"BEFORE UPDATE ON {_tableName} FOR EACH ROW BEGIN ");
            foreach (var item in columns)
            {
                s.Append($"SET NEW.{_bd.DBConfig.OpenName}{item.Campo}{_bd.DBConfig.CloseName} = NOW();");
            }
            s.Append(" END");
            return s.ToString();
        }
        private string MySQLTriggerOnInsert(Type type, IEnumerable<ColumnTable> columns)
        {
            _tableName = type.Name;
            StringBuilder s = new StringBuilder();
            s.Append($"CREATE TRIGGER {_tableName}_{DateTime.Now.ToString("yyyyMMddHHmm")}_BeforeInsert ");
            s.Append($"BEFORE INSERT ON {_tableName} FOR EACH ROW BEGIN ");
            foreach (var item in columns)
            {
                s.Append($"SET ");
                //s.Append($"{openName}{ _tableName}{closeName}.");
                s.Append($"NEW.{_bd.DBConfig.OpenName}{item.Campo}{_bd.DBConfig.CloseName} = NOW();");
            }
            s.Append(" END");
            return s.ToString();
        }


        public virtual async Task<bool> CriaTabela(Type type, bool excluirSeExisitir = false)
        {
            pocos.Add(type);
            if (Debug)
                Message($"Verificando tabela {type.Name}: ");
            bool tabelaExiste = await TabelaExiste(type);
            if (tabelaExiste && !excluirSeExisitir)
            {
                if (Debug)
                    Message($"Tabela {type.Name} existe");
                return await AlteraTabela(type);
            }
            else if (!tabelaExiste)
                if (Debug) Message($"Tabela {type.Name} NÃO existe");

            string sql;
            int resultado = 0;

            try
            {
                var colunas = BDTools.Campos(type);
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    DbCommand cmm = conexaoSql.CreateCommand();
                    cmm.Connection = conexaoSql;
                    if (tabelaExiste)
                    {
                        sql = $"DROP TABLE {type.Name}";
                        cmm.CommandText = sql;
                        if (Debug) Message($"Excluindo tabela {type.Name}");
                        await cmm.ExecuteNonQueryAsync();
                    }
                    sql = CreateTable(type);
                    cmm.CommandText = sql;
                    resultado = await cmm.ExecuteNonQueryAsync();

                    tabelaExiste = await TabelaExiste(type);
                    if (tabelaExiste)
                    {
                        if (Debug) Message($"Tabela {type.Name} criada");
                        resultado = -1;
                        if (_bd.DBConfig.TipoDB == TipoDB.MySQL)
                        {
                            if (!_bd.AllowCurrentTimeStamp)
                            {
                                var triggerOnInsert = colunas.Where(m => m.AutoInsertDate);
                                sql = MySQLTriggerOnInsert(type, triggerOnInsert);
                                cmm.CommandText = sql;
                                var sa = await cmm.ExecuteNonQueryAsync();

                                var triggerOnUpdate = colunas.Where(m => m.AutoUpdateDate);
                                sql = MySQLTriggerOnUpdate(type, triggerOnUpdate);
                                cmm.CommandText = sql;
                                sa = await cmm.ExecuteNonQueryAsync();
                            }
                        }

                        await GerenciarIndices(type, cmm);
                    }
                }
            }
            catch (Exception e)
            {
                e.Data.Add("Tabela", type.Name);
                Error(e);
            }
            return resultado != 0;

        }
        public async Task<bool> CriaTabela<T>(bool excluirSeExisitir = false) where T : class
        {
            Type type = typeof(T);
            return await CriaTabela(type, excluirSeExisitir);
        }

        #endregion


        #region Alter Table

        public virtual async Task<bool> AlteraTabela(Type type)
        {
            StringBuilder sql = new StringBuilder();
            try
            {
                string? add = await AddNewColumns(type);
                if (string.IsNullOrEmpty(add))
                {
                    if (Debug) Message($"Nenhuma alteração feita em {type.Name}");
                }
                else if (Debug)
                {
                    Message($"Campos adicionais encontrados: {string.Join(",", CamposAdicionados.ToArray())}");
                }

                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    DbCommand cmm = conexaoSql.CreateCommand();
                    cmm.Connection = conexaoSql;

                    if (!string.IsNullOrEmpty(add))
                    {
                        if (TipoDB == TipoDB.MySQL)
                            sql.Append("START TRANSACTION;");
                        else
                            sql.Append("BEGIN TRANSACTION;");
                        sql.Append(add);
                        sql.Append("COMMIT;");
                        cmm.CommandText = sql.ToString();
                        _ = await cmm.ExecuteNonQueryAsync();
                        Message($"Tabela {type.Name} alterada");
                    }

                    await GerenciarIndices(type, cmm);
                }

            }
            catch (Exception e)
            {
                e.Data.Add("SQL", sql.ToString());
                Error(e);
                return false;
            }
            return true;

        }

        private async Task<IEnumerable<string>?> ListaColunas(Type type)
        {
            _tableName = type.Name;
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    DbCommand cmm = conexaoSql.CreateCommand();
                    int cont = 0;
                    StringBuilder sql = new StringBuilder("SELECT ");
                    if (_bd.TipoDB == TipoDB.MySQL)
                    {
                        List<Chave> chaves = new List<Chave>();
                        chaves.Add(new Chave()
                        {
                            Campo = "TABLE_NAME",
                            Operador = Operador.IGUAL,
                            Parametro = "tabela",
                            Tipo = Tipo.STRING,
                            Valor = _tableName
                        });
                        chaves.Add(new Chave()
                        {
                            Operador = Operador.IGUAL,
                            Parametro = "base",
                            Tipo = Tipo.STRING,
                            Valor = conexaoSql.Database,
                            Campo = "table_schema"
                        });
                        sql.Append("COLUMN_NAME AS ColumnName FROM ");
                        sql.Append("INFORMATION_SCHEMA.COLUMNS");
                        sql.Append(" WHERE ");

                        foreach (var c in chaves)
                        {
                            if (cont > 0)
                                sql.Append(" AND ");
                            sql.Append(_bdTools.WhereExpression(c));
                            cont++;

                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, c)); // WithValue($"@{c.Nome}", c.Valor);
                        }
                    }
                    else if (_bd.TipoDB == TipoDB.SQLite)
                    {
                        sql.Append("name AS ColumnName FROM pragma_table_info ('");
                        sql.Append(_tableName);
                        sql.Append("')");
                    }
                    else
                    {
                        return null;
                    }
                    cmm.CommandText = sql.ToString();

                    return ListaColunas(await cmm.ExecuteReaderAsync());
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
            return null;
        }

        private async Task<string?> AddNewColumns(Type type)
        {
            _tableName = type.Name;
            CamposAdicionados.Clear();
            List<ColumnTable> colunas = BDTools.Campos(type);
            var atual = await ListaColunas(type);
            StringBuilder s = new StringBuilder();
            int i = 0;
            foreach (var coluna in colunas)
            {
                // ADD Column não permite muita coisa
                if (coluna.AutoInsertDate
                    || coluna.AutoUpdateDate
                    || coluna.BDIgnorar
                    || coluna.IsAutoIncrement
                    || coluna.IsKey)
                    continue;
                if (!coluna.PermiteNulo && (coluna.Tipo != Tipo.STRING) && coluna.ValorPadrao == null)
                    continue;

                if (coluna.Campo != null && (atual == null || !atual.Any(m => m == coluna.Campo)))
                {
                    s.Append("ALTER TABLE ");
                    s.Append(_bd.DBConfig.OpenName);
                    s.Append(_tableName);
                    s.Append(_bd.DBConfig.CloseName);
                    s.Append(" ADD COLUMN ");
                    s.Append(_bd.DBConfig.OpenName);
                    s.Append(coluna.Campo);
                    s.Append(_bd.DBConfig.CloseName);
                    s.Append(' ');
                    s.Append(FieldsSQLDefinition(coluna, false)); //AllowCurrentTimeStamp é indiferente aqui
                    s.Append(';');
                    i++;
                    CamposAdicionados.Add(coluna.Campo);
                }
            }
            if (i > 0)
                return s.ToString();
            return null;
        }


        #endregion

        #region Indexes

        private async Task GerenciarIndices(Type type, DbCommand cmm)
        {
            if (!typeof(IPOCOIndexes).IsAssignableFrom(type))
                return;

            try
            {
                object? obj = Activator.CreateInstance(type);
                if (obj == null || obj is not IPOCOIndexes pocoIndexes)
                    return;

                var indicesDesejados = pocoIndexes.GetIndexes();
                if (indicesDesejados == null || !indicesDesejados.Any())
                    return;

                if (Debug) Message($"Verificando índices para tabela {type.Name}");

                var indicesExistentes = await ListarIndicesExistentes(type, cmm);
                var indicesInfo = ConstruirIndicesInfo(type, indicesDesejados);

                var indicesParaCriar = ObterIndicesParaCriar(indicesInfo, indicesExistentes);
                var indicesParaRemover = ObterIndicesParaRemover(type, indicesInfo, indicesExistentes);

                foreach (var indexName in indicesParaRemover)
                {
                    string dropIndexSql = GerarScriptRemocaoIndice(type, indexName);
                    if (!string.IsNullOrEmpty(dropIndexSql))
                    {
                        cmm.CommandText = dropIndexSql;
                        await cmm.ExecuteNonQueryAsync();
                        if (Debug) Message($"Índice {indexName} removido da tabela {type.Name}");
                    }
                }

                foreach (var indexInfo in indicesParaCriar)
                {
                    string createIndexSql = GerarScriptCriacaoIndice(type, indexInfo);
                    if (!string.IsNullOrEmpty(createIndexSql))
                    {
                        cmm.CommandText = createIndexSql;
                        await cmm.ExecuteNonQueryAsync();
                        if (Debug) Message($"Índice {indexInfo.IndexName} criado na tabela {type.Name}");
                    }
                }
            }
            catch (Exception e)
            {
                e.Data.Add("Tabela", type.Name);
                e.Data.Add("Operacao", "GerenciarIndices");
                Error(e);
            }
        }

        private Dictionary<string, IPOCOIndexes.IndexInfo> ConstruirIndicesInfo(Type type, IEnumerable<IPOCOIndexes.IndexInfo> indexes)
        {
            var indicesAgrupados = new Dictionary<string, IPOCOIndexes.IndexInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var index in indexes)
            {
                if (string.IsNullOrEmpty(index.IndexName) || (index.Columns == null || !index.Columns.Any()))
                    continue;
                
                if (!indicesAgrupados.ContainsKey(index.IndexName))
                    indicesAgrupados[index.IndexName] = index;
            }            
            return indicesAgrupados;
        }

        private async Task<Dictionary<string, IPOCOIndexes.IndexInfo>> ListarIndicesExistentes(Type type, DbCommand cmm)
        {
            string tableName = type.Name;
            var indices = new Dictionary<string, IPOCOIndexes.IndexInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                StringBuilder sql = new StringBuilder();

                if (_bd.TipoDB == TipoDB.MySQL)
                {
                    sql.Append("SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE ");
                    sql.Append("FROM INFORMATION_SCHEMA.STATISTICS ");
                    sql.Append("WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table ");
                    sql.Append("AND INDEX_NAME != 'PRIMARY' ");
                    sql.Append("ORDER BY INDEX_NAME, SEQ_IN_INDEX");

                    cmm.Parameters.Clear();
                    var schemaParam = cmm.CreateParameter();
                    schemaParam.ParameterName = "@schema";
                    schemaParam.Value = cmm.Connection?.Database ?? string.Empty;
                    cmm.Parameters.Add(schemaParam);

                    var tableParam = cmm.CreateParameter();
                    tableParam.ParameterName = "@table";
                    tableParam.Value = tableName;
                    cmm.Parameters.Add(tableParam);
                }
                else if (_bd.TipoDB == TipoDB.SQLite)
                {
                    sql.Append($"SELECT name AS INDEX_NAME FROM pragma_index_list('{tableName}') WHERE origin = 'c'");
                }
                else
                {
                    return indices;
                }

                cmm.CommandText = sql.ToString();

                using (DbDataReader reader = await cmm.ExecuteReaderAsync())
                {
                    if (_bd.TipoDB == TipoDB.MySQL)
                    {
                        while (reader.Read())
                        {
                            string indexName = reader["INDEX_NAME"].ToString() ?? string.Empty;
                            string columnName = reader["COLUMN_NAME"].ToString() ?? string.Empty;
                            bool isUnique = reader["NON_UNIQUE"].ToString() == "0";

                            if (!indices.ContainsKey(indexName))
                            {
                                indices[indexName] = new IPOCOIndexes.IndexInfo
                                {
                                    IndexName = indexName,
                                    IsUnique = isUnique,
                                    Columns = new List<string>(),
                                    Chaves = Enumerable.Empty<Chave>()
                                };
                            }
                            indices[indexName].Columns.Add(columnName);
                        }
                    }
                    else if (_bd.TipoDB == TipoDB.SQLite)
                    {
                        while (reader.Read())
                        {
                            string indexName = reader["INDEX_NAME"].ToString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(indexName))
                            {
                                indices[indexName] = new IPOCOIndexes.IndexInfo
                                {
                                    IndexName = indexName,
                                    Columns = new List<string>(),
                                    Chaves = Enumerable.Empty<Chave>()
                                };
                            }
                        }
                        reader.Close();

                        foreach (var indexName in indices.Keys.ToList())
                        {
                            cmm.CommandText = $"SELECT name AS COLUMN_NAME FROM pragma_index_info('{indexName}')";
                            using (DbDataReader colReader = await cmm.ExecuteReaderAsync())
                            {
                                while (colReader.Read())
                                {
                                    string columnName = colReader["COLUMN_NAME"].ToString() ?? string.Empty;
                                    if (!string.IsNullOrEmpty(columnName))
                                        indices[indexName].Columns.Add(columnName);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                e.Data.Add("Tabela", tableName);
                e.Data.Add("Operacao", "ListarIndicesExistentes");
                Error(e);
            }

            return indices;
        }

        private List<IPOCOIndexes.IndexInfo> ObterIndicesParaCriar(Dictionary<string, IPOCOIndexes.IndexInfo> indicesDesejados, Dictionary<string, IPOCOIndexes.IndexInfo> indicesExistentes)
        {
            var indicesParaCriar = new List<IPOCOIndexes.IndexInfo>();

            foreach (var indexInfo in indicesDesejados.Values)
            {
                bool deveCriar = false;

                if (!indicesExistentes.ContainsKey(indexInfo.IndexName))
                {
                    deveCriar = true;
                }
                else
                {
                    var existente = indicesExistentes[indexInfo.IndexName];
                    bool colunasDiferentes = !indexInfo.Columns.SequenceEqual(existente.Columns, StringComparer.OrdinalIgnoreCase);

                    if (colunasDiferentes)
                    {
                        deveCriar = true;
                    }
                }

                if (deveCriar)
                {
                    indicesParaCriar.Add(indexInfo);
                }
            }

            return indicesParaCriar;
        }

        private List<string> ObterIndicesParaRemover(Type type, Dictionary<string, IPOCOIndexes.IndexInfo> indicesDesejados, Dictionary<string, IPOCOIndexes.IndexInfo> indicesExistentes)
        {
            var indicesParaRemover = new List<string>();

            foreach (var indexName in indicesExistentes.Keys)
            {
                if (indexName.StartsWith($"IX_{type.Name}_", StringComparison.OrdinalIgnoreCase))
                {
                    if (!indicesDesejados.ContainsKey(indexName))
                    {
                        indicesParaRemover.Add(indexName);
                    }
                    else
                    {
                        var existente = indicesExistentes[indexName];
                        var desejado = indicesDesejados[indexName];

                        bool colunasDiferentes = !desejado.Columns.SequenceEqual(existente.Columns, StringComparer.OrdinalIgnoreCase);

                        if (colunasDiferentes)
                        {
                            indicesParaRemover.Add(indexName);
                        }
                    }
                }
            }

            return indicesParaRemover;
        }

        private string GerarScriptCriacaoIndice(Type type, IPOCOIndexes.IndexInfo indexInfo)
        {
            if (indexInfo.Columns == null || !indexInfo.Columns.Any())
                return string.Empty;

            string tableName = type.Name;
            StringBuilder sql = new StringBuilder();

            if (_bd.TipoDB == TipoDB.MySQL)
            {
                sql.Append("CREATE ");
                if (indexInfo.IsUnique)
                    sql.Append("UNIQUE ");
                sql.Append("INDEX ");
                sql.Append($"{_bd.DBConfig.OpenName}{indexInfo.IndexName}{_bd.DBConfig.CloseName} ");
                sql.Append($"ON {_bd.DBConfig.OpenName}{tableName}{_bd.DBConfig.CloseName} (");
                sql.Append(string.Join(", ", indexInfo.Columns.Select(c => $"{_bd.DBConfig.OpenName}{c}{_bd.DBConfig.CloseName}")));
                sql.Append(")");

                if (indexInfo.Chaves != null && indexInfo.Chaves.Any())
                {
                    sql.Append(" WHERE ");
                    int cont = 0;
                    foreach (var chave in indexInfo.Chaves)
                    {
                        if (cont > 0)
                            sql.Append(" AND ");
                        sql.Append(_bdTools.WhereExpression(chave));
                        cont++;
                    }
                }

                sql.Append(";");
            }
            else if (_bd.TipoDB == TipoDB.SQLite)
            {
                sql.Append("CREATE ");
                if (indexInfo.IsUnique)
                    sql.Append("UNIQUE ");
                sql.Append("INDEX IF NOT EXISTS ");
                sql.Append($"{_bd.DBConfig.OpenName}{indexInfo.IndexName}{_bd.DBConfig.CloseName} ");
                sql.Append($"ON {_bd.DBConfig.OpenName}{tableName}{_bd.DBConfig.CloseName} (");
                sql.Append(string.Join(", ", indexInfo.Columns.Select(c => $"{_bd.DBConfig.OpenName}{c}{_bd.DBConfig.CloseName}")));
                sql.Append(")");

                if (indexInfo.Chaves != null && indexInfo.Chaves.Any())
                {
                    sql.Append(" WHERE ");
                    int cont = 0;
                    foreach (var chave in indexInfo.Chaves)
                    {
                        if (cont > 0)
                            sql.Append(" AND ");
                        sql.Append(_bdTools.WhereExpression(chave));
                        cont++;
                    }
                }

                sql.Append(";");
            }

            return sql.ToString();
        }

        private string GerarScriptRemocaoIndice(Type type, string indexName)
        {
            if (string.IsNullOrEmpty(indexName))
                return string.Empty;

            StringBuilder sql = new StringBuilder();

            if (_bd.TipoDB == TipoDB.MySQL)
            {
                sql.Append($"DROP INDEX {_bd.DBConfig.OpenName}{indexName}{_bd.DBConfig.CloseName} ");
                sql.Append($"ON {_bd.DBConfig.OpenName}{type.Name}{_bd.DBConfig.CloseName};");
            }
            else if (_bd.TipoDB == TipoDB.SQLite)
            {
                sql.Append($"DROP INDEX IF EXISTS {_bd.DBConfig.OpenName}{indexName}{_bd.DBConfig.CloseName};");
            }

            return sql.ToString();
        }

        #endregion
    }
}
