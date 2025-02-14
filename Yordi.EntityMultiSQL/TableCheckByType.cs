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

                    DbDataReader reader = await cmm.ExecuteReaderAsync();

                    if (reader == null)
                        return false;

                    if (reader.HasRows)
                    {
                        while (reader.Read())
                            resultado++;
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
            Message($"Verificando tabela {type.Name}: ");
            bool tabelaExiste = await TabelaExiste(type);
            if (tabelaExiste && !excluirSeExisitir)
            {
                Message($"Tabela {type.Name} existe");
                return await AlteraTabela(type);
                //return true;
            }
            else if (!tabelaExiste)
                Message($"Tabela {type.Name} NÃO existe");

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
                        Message($"Excluindo tabela {type.Name}");
                        await cmm.ExecuteNonQueryAsync();
                    }
                    sql = CreateTable(type);
                    cmm.CommandText = sql;
                    /*
                     * For UPDATE, INSERT, and DELETE statements, the return value is the number of rows affected 
                     * by the command. 
                     * For all other types of statements, the return value is -1.
                     * Mas MySQL retorna 0 :(
                     */
                    resultado = await cmm.ExecuteNonQueryAsync();

                    tabelaExiste = await TabelaExiste(type);
                    if (tabelaExiste)
                    {
                        Message($"Tabela {type.Name} criada");
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
                    Message($"Nenhuma alteração feita em {type.Name}");
                    return true;
                }
                else
                {
                    Message($"Campos adicionais encontrados: {string.Join(",", CamposAdicionados.ToArray())}");
                }
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    DbCommand cmm = conexaoSql.CreateCommand();
                    cmm.Connection = conexaoSql;
                    if (TipoDB == TipoDB.MySQL)
                        sql.Append("START TRANSACTION;");
                    else
                        sql.Append("BEGIN TRANSACTION;");
                    sql.Append(add);
                    sql.Append("COMMIT;");
                    cmm.CommandText = sql.ToString();

                    //For UPDATE, INSERT, and DELETE statements, the return value is the number of rows affected 
                    // by the command. 
                    // For all other types of statements, the return value is -1.
                    // Mas MySQL retorna 0 :(
                    _ = await cmm.ExecuteNonQueryAsync();
                    Message($"Tabela {type.Name} alterada");

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
                    if (TipoDB == TipoDB.MySQL)
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
    }
}
