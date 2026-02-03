using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public abstract class RepositorioAsyncAbstract<T> : RepositorioBaseAbstract<T> where T : class
    {

        protected readonly IBDConexao _bd;
        protected readonly BDTools _bdTools;
        protected RepositorioAsyncAbstract(IBDConexao bd) : base(bd)
        {
            _bd = bd;
            _bdTools = new BDTools(bd);
        }

        /// <summary>
        /// Inclui o objeto informado, com base nos atributos AutoIncrement ou KEY
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public virtual async Task<T?> Incluir(T obj)
        {
            T? result = await Insert(obj);
            if (result == null)
                Message("Objeto não foi incluído!");
            return result;
        }

        /// <summary>
        /// Atualiza o objeto informado, com base nos atributos AutoIncrement ou KEY
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public virtual async Task<T?> Atualizar(T obj)
        {
            T? result = null;
            result = await Update(obj);
            return result;
        }

        /// <summary>
        /// Atualiza ou inclui o objeto, dependendo se ele já existe ou não. <br/>
        /// Usa a mesma transação de banco para SELECT, INSERT e UPDATE. Caso não tenha sucesso, usa Rollback.<br/>
        /// Para incluir, o objeto deve ter o valor da propriedade com atributo AutoIncrement = 0.<br/>
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public virtual async Task<T?> AtualizarOuIncluir(T obj)
        {
            bool ok = false;
            List<ColumnTable> colunas = BDTools.Campos(obj);
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (var transaction = conexaoSql.BeginTransaction())
                    {
                        DataTable dt = await SelectTransaction(conexaoSql, colunas);
                        if (dt == null || dt.Rows == null || dt.Rows.Count == 0) // insert
                        {
                            (bool, List<ColumnTable>) insert = await InsertTransaction(conexaoSql, colunas);
                            ok = insert.Item1;
                            colunas = insert.Item2;
                        }
                        else if (dt.Rows.Count == 1) // update
                            ok = await UpdateTransaction(conexaoSql, colunas);
                        else // erro
                        {
                            var e = new Exception("Mais de um item encontrado para atualizar");
                            StringBuilder sb = new StringBuilder();
                            foreach (var c in colunas)
                            {
                                sb.Append(c.Parametro ?? c.Campo);
                                sb.Append('=');
                                sb.Append(c.Valor?.ToString() ?? "NULL");
                                sb.Append('|');
                            }
                            e.Data.Add("Param", sb.ToString());
                            Error(e);
                        }
                        if (ok)
                        {
                            await transaction.CommitAsync();
                            dt = await SelectTransaction(conexaoSql, colunas);
                            if (dt?.Rows != null && dt.Rows.Count == 1)
                                return AtualizaObjetoOriginal(obj, dt.Rows[0]);
                        }
                        else
                            await transaction.RollbackAsync();
                    }
                }
            }
            catch (Exception e) when (SQLiteRetryHelper.IsDatabaseLocked(e))
            {
                // Tenta liberar locks e repetir a operação
                if (await TentarLiberarERepetirAsync())
                    return await AtualizarOuIncluir(obj); // Recursão controlada
                RegistraErro(e, string.Empty, colunas);
            }
            catch (Exception e)
            {
                RegistraErro(e, string.Empty, colunas);
            }
            return null;
        }

        protected async Task<DataTable> SelectTransaction(DbConnection conexaoSql, IEnumerable<ColumnTable> colunas)
        {
            DataTable dt = new DataTable();
            var where = BDTools.WhereKeyOrID(colunas);
            using (DbCommand selectCommand = conexaoSql.CreateCommand())
            {
                string sql = SelectWithWhereParameters(where);
                selectCommand.CommandText = sql;
                foreach (var c in where)
                {
                    selectCommand.Parameters.Add(BDTools.CriaParameter(selectCommand, c));
                }
                using (DbDataReader reader = await selectCommand.ExecuteReaderAsync())
                    dt.Load(reader);
            }
            return dt;
        }

        protected async Task<DataTable> SelectTransaction(DbConnection conexaoSql, string sql, IEnumerable<Chave> where)
        {
            DataTable dt = new DataTable();
            if (!sql.StartsWith("SELECT", StringComparison.InvariantCultureIgnoreCase))
                return dt;
            using (DbCommand selectCommand = conexaoSql.CreateCommand())
            {
                selectCommand.CommandText = sql;
                if (where != null && where.Any())
                {
                    foreach (var c in where)
                        selectCommand.Parameters.Add(BDTools.CriaParameter(selectCommand, c));
                }
                using (DbDataReader reader = await selectCommand.ExecuteReaderAsync())
                    dt.Load(reader);
            }
            return dt;
        }

        protected async Task<(bool, List<ColumnTable>)> InsertTransaction(DbConnection conexaoSql, List<ColumnTable> colunas)
        {
            ColumnTable? coluna = null;
            if (!AllowCurrentTimeStamp)
            {
                coluna = colunas.Find(m => m.AutoInsertDate);
                if (coluna != null)
                    coluna.Valor = DateTime.UtcNow;
            }
            if (!string.IsNullOrEmpty(ControleAlteracao.Usuario))
            {
                coluna = colunas.Find(m => m.Campo == nameof(Basico.UsuarioInclusao));
                if (coluna != null)
                    coluna.Valor = ControleAlteracao.Usuario;
            }
            coluna = colunas.Find(m => m.Campo == nameof(Basico.Origem));
            if (coluna != null)
                coluna.Valor = ControleAlteracao.Origem ?? Environment.MachineName;

            string sql = InsertWithParameters(colunas);
            coluna = null;
            int resultado = 0;

            using (DbCommand insertCmm = conexaoSql.CreateCommand())
            {
                insertCmm.CommandText = sql;
                ColumnTable? d = null;
                foreach (var c in colunas)
                {
                    if (!BDTools.CampoEditavel(c, _bd.AllowCurrentTimeStamp)) continue;
                    if (c.IsAutoIncrement)
                    {
                        coluna = c;
                        continue;
                    }

                    d = BDTools.AtualizaValor(c);
                    insertCmm.Parameters.Add(BDTools.CriaParameter(insertCmm, d));
                }

                // Usa retry helper para SQLite
                if (_bd.TipoDB == TipoDB.SQLite)
                {
                    resultado = await ExecuteWithSQLiteRetryAsync(async () =>
                    {
                        if (coluna != null)
                        {
                            var undefined = await insertCmm.ExecuteScalarAsync();
                            if (int.TryParse(undefined?.ToString(), out int res))
                            {
                                coluna.Valor = res;
                                return res;
                            }
                            return 0;
                        }
                        else
                            return await insertCmm.ExecuteNonQueryAsync();
                    }, sql, colunas);
                }
                else
                {
                    try
                    {
                        if (coluna != null)
                        {
                            var undefined = await insertCmm.ExecuteScalarAsync();
                            if (int.TryParse(undefined?.ToString(), out resultado))
                                coluna.Valor = resultado;
                        }
                        else
                            resultado = await insertCmm.ExecuteNonQueryAsync();
                    }
                    catch (Exception e)
                    {
                        RegistraErro(e, sql, colunas);
                    }
                }
            }

            if (Verbose)
            {
                if (coluna?.Valor == null)
                    Message($"Insert {resultado} registros na tabela {_tableName}");
                else
                    Message($"Insert registro na tabela {_tableName} com AutoIncrement = {coluna.Valor}");
            }
            return (resultado > 0, colunas);
        }

        protected async Task<bool> UpdateTransaction(DbConnection conexaoSql, List<ColumnTable> colunas)
        {
            string sql;
            var coluna = colunas.Find(m => m.AutoUpdateDate);
            if (coluna != null && !AllowCurrentTimeStamp)
                coluna.Valor = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(ControleAlteracao.Usuario))
            {
                coluna = colunas.Find(m => m.Campo == nameof(Basico.UsuarioAlteracao));
                if (coluna != null)
                    coluna.Valor = ControleAlteracao.Usuario;
            }
            coluna = colunas.Find(m => m.Campo == nameof(Basico.Origem));
            if (coluna != null)
                coluna.Valor = ControleAlteracao.Origem ?? Environment.MachineName;

            if (colunas.Any(m => m.IsAutoIncrement && m.Tipo != Tipo.GUID && m.Valor is int valor && valor > 0))
                sql = UpdateForAutoParameters(colunas);
            else if (colunas.Any(m => m.IsKey))
                sql = UpdateWithKeyParameters(colunas);
            else
            {
                Error("Impossível atualizar, não há colunas de referência para a cláusula WHERE");
                return false;
            }

            int resultado = 0;
            using (DbCommand updateCmm = conexaoSql.CreateCommand())
            {
                updateCmm.CommandText = sql;
                foreach (var c in colunas)
                {
                    if (c.OnlyInsert) continue;
                    if (!BDTools.CampoEditavel(c, _bd.AllowCurrentTimeStamp)) continue;
                    updateCmm.Parameters.Add(BDTools.CriaParameter(updateCmm, c));
                }

                // Usa retry helper para SQLite
                if (_bd.TipoDB == TipoDB.SQLite)
                {
                    resultado = await ExecuteWithSQLiteRetryAsync(
                        async () => await updateCmm.ExecuteNonQueryAsync(),
                        sql, colunas);
                }
                else
                {
                    try
                    {
                        resultado = await updateCmm.ExecuteNonQueryAsync();
                    }
                    catch (Exception e)
                    {
                        RegistraErro(e, sql, colunas);
                    }
                }

                if (Verbose)
                    Message($"Update {resultado} registros na tabela {_tableName}");
            }

            Rows(resultado);
            return resultado > 0;
        }

        /// <summary>
        /// Exclui o objeto informado com base no atributo AutoIncrement ou KEY
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public virtual async Task<bool> Excluir(T obj)
        {
            int result = 0;
            T? item = await Item(BDTools.WhereKeyOrID(obj));

            if (item != null)
            {
                result = await Delete(obj);
                Message($"{_tableName} excluídos: {result}");
                return result > 0;
            }
            else
            {
                Message($"Nenhum {_tableName} encontrado. Nada foi excluído!");
                return false;
            }
        }

        /// <summary>
        /// Exclui os objetos informados com base na lista de parâmetros em <paramref name="where"/>
        /// </summary>
        /// <param name="where"></param>
        /// <returns></returns>
        protected virtual async Task<int> Excluir(IEnumerable<Chave> where)
        {
            return await Delete(where);
        }

        /// <summary>
        /// Exclui os objetos informados com base nos atributos Autoincrement ou KEY
        /// </summary>
        /// <param name="lista"></param>
        /// <returns></returns>
        public virtual async Task<int> Excluir(IEnumerable<T> lista)
        {
            Rows(lista.Count());
            if (lista.Count() > 1000)
            {
                return await Delete(lista, 200);
            }
            var r = await Delete(lista, true);

            return r;
        }


        /// <summary>
        /// Traz a lista inteira de elementos da tabela do objeto T
        /// </summary>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Lista()
        {
            CheckTableName();
            StringBuilder s = new StringBuilder("SELECT * FROM ");
            s.Append(_bd.DBConfig.OpenName);
            s.Append(_tableName);
            s.Append(_bd.DBConfig.CloseName);
            if (typeof(IAuto).IsAssignableFrom(typeof(T)))
                s.Append(" ORDER BY Auto");
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = s.ToString();
                        using (var consulta = await cmm.ExecuteReaderAsync())
                            return FromDataReader(consulta);
                    }
                }
            }
            catch (Exception e)
            {
                e.Data.Add("Objeto", typeof(T).Name);
                Error(e);
            }
            return null;

        }

        /// <summary>
        /// Traz a lista inteira de elementos da tabela do objeto T e aplica a função <paramref name="where"/>
        /// </summary>
        /// <param name="where"></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Lista(Func<T, bool> where)
        {
            var lista = await Lista();
            if (lista != null && lista.Any())
                lista = lista.Where(where);
            return lista;
        }

        /// <summary>
        /// Retorna uma lista de objetos de acordo com as informações passadas
        /// </summary>
        /// <param name="keys">Lista de chaves para montar a cláusula WHERE</param>
        /// <param name="ou">Informar na clausula WHERE se usa OR ou AND (padrão é AND)</param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Lista(IEnumerable<Chave> keys, bool ou = false)
        {
            StringBuilder s = new StringBuilder(SelectWithWhereParameters(keys, ou));
            List<T>? lista = null;
            if (typeof(IAuto).IsAssignableFrom(typeof(T)))
                s.Append(" ORDER BY Auto");
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        foreach (var c in keys)
                        {
                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                        }
                        cmm.Connection = conexaoSql;
                        cmm.CommandText = s.ToString();
                        using (DbDataReader d = await cmm.ExecuteReaderAsync())
                            lista = FromDataReader(d);
                    }
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
            return lista;
        }

        /// <summary>
        /// Procura lista de objetos em que DataInclusao esteja dentro do intervalo informado
        /// </summary>
        /// <param name="inicial"></param>
        /// <param name="final"></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Lista(DateTime inicial, DateTime final)
        {
            var chaves = Datas(inicial, final);
            if (chaves != null)
                return await Lista(chaves);
            return null;
        }

        /// <summary>
        /// Usa a cláusula WHERE com IN caso o array seja menor que 100.
        /// Caso contrário, usa JOIN com tabela criada para isso (Temp_In_Use), onde os ids serão inseridos nela.
        /// <b>Por enquanto só funciona com classes que implementaram a interface IAuto</b>
        /// </summary>
        /// <param name="ids"></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Lista(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                Message("Sem IDs informados.");
                return null;
            }
            if (!typeof(IAuto).IsAssignableFrom(typeof(T)))
            {
                Message("Não implementa IAuto.");
                return null;
            }

            CheckTableName();
            StringBuilder s = new StringBuilder();
            var max = ids.Length;
            if (ids.Length > 100)
            {
                if (_bd.TipoDB == TipoDB.SQLite)
                    await ExecuteSQL("DELETE FROM Temp_In_Use");
                else
                    await ExecuteSQL("TRUNCATE TABLE Temp_In_Use;");
                string insert = "INSERT INTO Temp_In_Use VALUES ";
                int values = 0;
                s.Append(insert);
                int i = 0;
                for (; i < max; i++)
                {
                    if (values > 100)
                    {
                        s.Append(';');
                        await ExecuteSQL(s.ToString());
                        s.Clear();
                        s.Append(insert);
                        values = 0;
                    }
                    if (values > 0)
                        s.Append(',');
                    s.Append('(');
                    s.Append(ids[i]);
                    s.Append(')');
                    values++;
                }
                await ExecuteSQL(s.ToString());
                s.Clear();
                s.Append($"SELECT T.* FROM {_bd.DBConfig.OpenName}{_tableName}{_bd.DBConfig.CloseName} AS T ");
                s.Append(" INNER JOIN Temp_In_Use U WHERE T.Auto = U.ID");
            }
            else
            {
                s.Append($"SELECT T.* FROM {_bd.DBConfig.OpenName}{_tableName}{_bd.DBConfig.CloseName} AS T ");
                s.Append($" WHERE T.Auto IN ({string.Join(",", ids)})");
            }
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    if (conexaoSql == null || !IsConnected().Result)
                    {
                        Message("Sem conexão com banco ou arquivo de dados");
                        return default;
                    }
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = s.ToString();
                        using (DbDataReader reader = await cmm.ExecuteReaderAsync())
                            return FromDataReader(reader);
                    }
                }
            }
            catch (Exception e)
            {
                e.Data.Add("sql", s.ToString());
                Error(e);
            }
            return null;
        }


        /// <summary>
        /// Usa a cláusula WHERE com IN caso o array seja menor que 100.<br/>
        /// Caso contrário, usa JOIN com tabela criada para isso (Temp_In_Use), onde os ids serão inseridos nela.<br/>
        /// <b>O campo de busca será o informado no parâmetro <paramref name="campoUnico"/></b>
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="campoUnico">Campo a ser usado do lado esquerdo da operação.<br/>
        /// <code><paramref name="campoUnico"/> IN (ids1, ids2, ...)</code>
        /// ou
        /// <code> INNER JOIN Temp_In_Use U WHERE T.<paramref name="campoUnico"/> = U.ID</code></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Lista(int[] ids, string campoUnico)
        {
            if (ids == null || ids.Length == 0)
            {
                Message("Sem IDs informados.");
                return null;
            }
            if (typeof(T).GetProperty(campoUnico) == null)
            {
                Message($"Não tem o campo {campoUnico}");
                return null;
            }

            CheckTableName();
            StringBuilder s = new StringBuilder();
            var max = ids.Length;
            if (ids.Length > 100)
            {
                if (_bd.TipoDB == TipoDB.SQLite)
                    await ExecuteSQL("DELETE FROM Temp_In_Use");
                else
                    await ExecuteSQL("TRUNCATE TABLE Temp_In_Use;");
                string insert = "INSERT INTO Temp_In_Use VALUES ";
                int values = 0;
                s.Append(insert);
                int i = 0;
                for (; i < max; i++)
                {
                    if (values > 100)
                    {
                        s.Append(';');
                        await ExecuteSQL(s.ToString());
                        s.Clear();
                        s.Append(insert);
                        values = 0;
                    }
                    if (values > 0)
                        s.Append(',');
                    s.Append('(');
                    s.Append(ids[i]);
                    s.Append(')');
                    values++;
                }
                s.Append(';');
                await ExecuteSQL(s.ToString());
                s.Clear();
                s.Append($"SELECT T.* FROM {_bd.DBConfig.OpenName}{_tableName}{_bd.DBConfig.CloseName} AS T ");
                s.Append($" INNER JOIN Temp_In_Use U WHERE T.{campoUnico} = U.ID");
            }
            else
            {
                s.Append($"SELECT T.* FROM {_bd.DBConfig.OpenName}{_tableName}{_bd.DBConfig.CloseName} AS T ");
                s.Append($" WHERE T.{campoUnico} IN ({string.Join(",", ids)})");
            }
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    if (conexaoSql == null || !IsConnected().Result)
                    {
                        Message("Sem conexão com banco ou arquivo de dados");
                        return default;
                    }
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = s.ToString();
                        return FromDataReader(await cmm.ExecuteReaderAsync());
                    }
                }
            }
            catch (Exception e)
            {
                e.Data.Add("sql", s.ToString());
                Error(e);
            }
            return null;
        }

        /// <summary>
        /// Retorna uma lista de objetos de acordo com a instrução SQL passada
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="parametros">Se quiser usar DbParameters ou quiser que a cláusula where seja preenchida por essa lista</param>
        /// <param name="ou"></param>
        /// <returns></returns>
        protected virtual async Task<IEnumerable<T>?> ListaByTSQL(string sql, IEnumerable<Chave>? parametros = null, bool? ou = null)
        {
            string where = string.Empty;
            if (parametros != null && parametros.Any() && !sql.ToUpper().Contains("WHERE"))
                where = _bdTools.WhereWithParameters(parametros, ou ?? false);

            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        if (parametros != null && parametros.Any())
                        {
                            foreach (var c in parametros)
                            {
                                cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                            }
                        }
                        cmm.Connection = conexaoSql;
                        if (string.IsNullOrEmpty(where))
                            cmm.CommandText = sql;
                        else
                            cmm.CommandText = string.Concat(sql, " ", where);
                        using (DbDataReader dataReader = await cmm.ExecuteReaderAsync())
                            return FromDataReader(dataReader);
                    }
                }
            }
            catch (Exception e)
            {
                e.Data.Add("sql", sql);
                Error(e);
            }
            return null;

        }

        /// <summary>
        /// Construtor de instrução SQL SELECT. A cláusula WHERE é preenchida de acordo com o objeto IEnumerable<Chave>
        /// </summary>
        /// <param name="campos">Objeto IEnumerable<Chave>. Quando for STRING, a cláusula WHERE terá o termo LIKE</param>
        /// <returns></returns>
        protected virtual async Task<IEnumerable<T>?> ProcuraPor(IEnumerable<Chave> campos)
        {
            string s = SelectWithWhereParameters(campos);
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        foreach (var c in campos)
                        {
                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                        }
                        cmm.Connection = conexaoSql;
                        cmm.CommandText = s.ToString();

                        var lista = FromDataReader(await cmm.ExecuteReaderAsync());
                        return lista;
                    }
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
            return null;
        }


        protected List<T>? FromDataReader(DbDataReader? reader)
        {
            if (reader == null || !reader.HasRows)
            {
                _msg = "Nenhum resultado encontrado";
                if (reader != null && !reader.IsClosed) reader.Close();
                return null;
            }

            List<T> lista = new List<T>();
            DataTable? dt = new DataTable();
            try
            {
                dt.Load(reader);
                if (dt == null)
                {
                    _msg = "DbDataReader to DataTable resultou em null";
                    return null;
                }
                if (dt.Rows.Count == 0)
                {
                    _msg = "DbDataReader to DataTable resultou em nenhum registro";
                    return null;
                }

                _msg = String.Empty;
                Rows(dt.Rows.Count);
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    var o = Objeto(dt.Rows[i]);
                    lista.Add(o);
                    Progresso(i);
                }
            }
            catch (ConstraintException ce)
            {
                ConstraintExceptionError(dt, ce);
            }
            catch (Exception e)
            {
                e.Data.Add("Objeto", typeof(T).Name);
                Error(e);
            }
            finally
            {
                if (reader != null && !reader.IsClosed)
                    reader.Close();
            }
            return lista;
        }


        private DataTable? MyLoad(DbDataReader? reader)
        {
            if (reader == null || !reader.HasRows)
            {
                _msg = "Nenhum resultado encontrado";
                if (reader != null && !reader.IsClosed) reader.Close();
                return null;
            }
            DataTable dt = new DataTable();
            try
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var type = reader.GetFieldType(i);
                    dt.Columns.Add(name, type);
                }

                while (reader.Read())
                {
                    DataRow row = dt.NewRow();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.GetValue(i);
                    dt.Rows.Add(row);
                }
            }
            catch (ArgumentNullException ex)
            {
                ex.Data.Add("Objeto", typeof(T).Name);
                Error(ex);
            }
            catch (ArgumentException ex)
            {
                ex.Data.Add("Objeto", typeof(T).Name);
                Error(ex);
            }
            catch (InvalidOperationException ex)
            {
                ex.Data.Add("Objeto", typeof(T).Name);
                Error(ex);
            }
            catch (ConstraintException ex)
            {
                ConstraintExceptionError(dt, ex);
            }
            catch (DataException ex)
            {
                ex.Data.Add("Objeto", typeof(T).Name);
                Error(ex);
            }
            catch (Exception e)
            {
                e.Data.Add("Objeto", typeof(T).Name);
                Error(e);
            }
            return dt;
        }

        private void ConstraintExceptionError(DataTable? dt, ConstraintException ce)
        {
            if (dt == null)
            {
                ce.Data.Add("Objeto", typeof(T).Name);
                ce.Data.Add("DataTable", "Não foi possível rastrear erros. DataTable é nulo aqui.");
                Error(ce);
                return;
            }
            DataRow[] rowErrors = dt.GetErrors();
            if (rowErrors != null && rowErrors.Length > 0)
            {
                ce.Data.Add("Erros", rowErrors.Length);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < rowErrors.Length; i++)
                {
                    sb.Clear();
                    sb.Append(rowErrors[i].RowError);
                    sb.Append('|');
                    foreach (DataColumn col in rowErrors[i].GetColumnsInError())
                    {
                        sb.Append(col.ColumnName);
                        sb.Append(": ");
                        sb.Append(rowErrors[i].GetColumnError(col));
                    }
                    ce.Data.Add($"Linha {i}", sb.ToString());
                }
            }
            Error(ce);
        }

        protected List<T>? FromDataTable(DataTable? table)
        {
            if (table == null || table.Rows.Count == 0)
            {
                _msg = "Nenhum resultado encontrado";
                if (table != null) table.Dispose();
                return null;
            }

            List<T> lista = new List<T>();
            try
            {
                _msg = String.Empty;
                Rows(table.Rows.Count);
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var o = Objeto(table.Rows[i]);
                    lista.Add(o);
                    Progresso(i);
                }
            }
            catch (ConstraintException ce)
            {
                ConstraintExceptionError(table, ce);
            }
            catch (Exception e)
            {
                e.Data.Add("Objeto", typeof(T).Name);
                Error(e);
            }
            table.Rows.Clear();
            table.Dispose();
            return lista;
        }

        /// <summary>
        /// Procura o objeto de acordo com o informado no objeto 'where'
        /// </summary>
        /// <param name="where">Lista de Objeto Chave, que será usado na cláusula WHERE com parâmetros</param>
        /// <returns></returns>
        protected virtual async Task<T?> Item(IEnumerable<Chave> where)
        {
            string sql = SelectWithWhereParameters(where);
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = sql;
                        foreach (var c in where)
                        {
                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                        }
                        using (DbDataReader reader = await cmm.ExecuteReaderAsync())
                            return FromDataReader(reader)?.FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                RegistraErro(ex, sql, where);
                return null;
            }
        }

        /// <summary>
        /// Procura o objeto de acordo com o atributo KEY do objeto
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public virtual async Task<T?> Item(T obj)
        {
            var r = await Item(BDTools.WhereKeyOrID(obj));
            return r;
        }

        /// <summary>
        /// Procura o objeto pelo código de autoincremento
        /// </summary>
        /// <param name="autoIncrementValue"></param>
        /// <returns></returns>
        public virtual async Task<T?> Item(int autoIncrementValue)
        {
            T obj = Activator.CreateInstance<T>();
            Chave? c = null;
            if (obj is IAuto)
            {
                c = new Chave
                {
                    Campo = "Auto",
                    Tipo = Tipo.INT,
                    Valor = autoIncrementValue
                };
            }
            else
            {
                Type type = obj.GetType();
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                foreach (PropertyInfo p in properties)
                {
                    Attribute[] atts = Attribute.GetCustomAttributes(p);
                    foreach (var a in atts)
                    {
                        Type t = a.GetType();
                        if (t == typeof(AutoIncrementAttribute))
                        {
                            c = new Chave
                            {
                                Campo = p.Name,
                                Tipo = Tipo.INT,
                                Valor = autoIncrementValue
                            };
                            break;
                        }
                    }
                    if (c != null) break;
                }
            }
            if (c == null)
                return null;
            return await Item(new List<Chave>() { c });
        }

        /// <summary>
        /// Insere o objeto.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns>Objeto com o novo código de autoincremento</returns>
        private async Task<T?> Insert(T obj)
        {
            bool ok = false;
            List<ColumnTable> colunas = BDTools.Campos(obj);
            using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
            {
                var insert = await InsertTransaction(conexaoSql, colunas);
                ok = insert.Item1;
                colunas = insert.Item2;
                if (ok)
                {
                    var dt = await SelectTransaction(conexaoSql, colunas);
                    if (dt?.Rows != null && dt.Rows.Count == 1)
                        return AtualizaObjetoOriginal(obj, dt.Rows[0]);
                }
            }
            return null;
        }

        /// <summary>
        /// Atualiza objeto original com dados de salvamento, como datas de inserção e atualização,
        /// usuários de inserção e atualização, origem do comando, e valor de autoincremento.<br/>
        /// Tento retornar com o objeto original atualizado, pois pode ter propriedades que não são salvas em banco
        /// e voltariam nulas se usasse apenas o resultado do banco
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="dr"></param>
        /// <returns></returns>
        protected T AtualizaObjetoOriginal(T obj, DataRow dr)
        {
            var o = Objeto(dr);
            if (o != null)
            {
                if (o is ICommonColumns oComm && obj is ICommonColumns objComm)
                {
                    objComm.DataInclusao = oComm.DataInclusao;
                    objComm.DataAlteracao = oComm.DataAlteracao;
                    objComm.UsuarioInclusao = oComm.UsuarioInclusao;
                    objComm.UsuarioAlteracao = oComm.UsuarioAlteracao;
                    objComm.Origem = oComm.Origem;
                }
                if (o is IAuto oAuto && obj is IAuto objAuto)
                    objAuto.Auto = oAuto.Auto;
            }
            return obj;
        }



        /// <summary>
        /// Incluir lista de objetos com a mesma conexão de banco
        /// </summary>
        /// <param name="lista">Lista de objetos</param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>> Incluir(IEnumerable<T> lista)
        {
            Rows(lista.Count());
            string origem = ControleAlteracao.Origem ?? Environment.MachineName;
            var l = new List<T>();
            if (lista.ElementAt(0) is ICommonColumns)
            {
                if (!AllowCurrentTimeStamp)
                {
                    DateTime dt = DateTime.UtcNow;
                    l = lista
                        .Select(m =>
                        {
                            ICommonColumns a = (ICommonColumns)m;
                            a.DataInclusao = dt;
                            a.DataAlteracao = dt;
                            a.Origem = origem;
                            a.UsuarioAlteracao = ControleAlteracao.Usuario;
                            a.UsuarioInclusao = ControleAlteracao.Usuario;
                            return m;
                        })
                        .ToList();
                }
                else
                {
                    l = lista
                        .Select(m =>
                        {
                            ICommonColumns a = (ICommonColumns)m;
                            a.Origem = origem;
                            a.UsuarioAlteracao = ControleAlteracao.Usuario;
                            a.UsuarioInclusao = ControleAlteracao.Usuario;
                            return m;
                        })
                        .ToList();
                }
            }
            else
                l = lista.ToList();

            if (l.Count > 1000)
            {
                return await Incluir(l, 200);
            }
            IEnumerable<T> r = await Inclusao(l, true);

            return r;
        }

        private async Task<IEnumerable<T>> Inclusao(IEnumerable<T> lista, bool dispararEventoProgresso = false)
        {
            CheckTableName();
            T obj = lista.ElementAt(0);
            List<ColumnTable> colunas = BDTools.Campos(obj);
            string sql = InsertWithParameters(colunas, true);
            int resultado = 0; int rowAffected = 0;
            List<T> incluidos = new List<T>();
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbTransaction transaction = conexaoSql.BeginTransaction())
                    {
                        using (DbCommand cmm = conexaoSql.CreateCommand())
                        {
                            cmm.CommandText = sql;
                            ColumnTable? d = null;

                            foreach (var item in lista)
                            {
                                string? nomeAuto = String.Empty;
                                try
                                {
                                    cmm.Parameters.Clear();
                                    colunas = BDTools.Campos(item);
                                    d = null;
                                    foreach (var c in colunas)
                                    {
                                        if (!BDTools.CampoEditavel(c, _bd.AllowCurrentTimeStamp)) continue;
                                        if (c.IsAutoIncrement) continue;
                                        d = BDTools.AtualizaValor(c);
                                        cmm.Parameters.Add(BDTools.CriaParameter(cmm, d));
                                    }
                                    try
                                    {
                                        nomeAuto = colunas.FirstOrDefault(m => m.IsAutoIncrement)?.Campo;

                                        // Usa retry helper para SQLite
                                        if (_bd.TipoDB == TipoDB.SQLite)
                                        {
                                            resultado = await ExecuteWithSQLiteRetryAsync(async () =>
                                            {
                                                if (!string.IsNullOrEmpty(nomeAuto))
                                                {
                                                    var undefined = await cmm.ExecuteScalarAsync();
                                                    if (int.TryParse(undefined?.ToString(), out int res) && res > 0)
                                                    {
                                                        PropertyInfo? propertyInfo = item.GetType().GetProperty(nomeAuto);
                                                        if (propertyInfo != null)
                                                            propertyInfo.SetValue(item, res);
                                                        return res;
                                                    }
                                                    return 0;
                                                }
                                                else
                                                    return await cmm.ExecuteNonQueryAsync();
                                            }, sql, colunas);
                                        }
                                        else
                                        {
                                            if (!string.IsNullOrEmpty(nomeAuto))
                                            {
                                                var undefined = await cmm.ExecuteScalarAsync();
                                                if (int.TryParse(undefined?.ToString(), out resultado) && resultado > 0)
                                                {
                                                    PropertyInfo? propertyInfo = item.GetType().GetProperty(nomeAuto);
                                                    if (propertyInfo != null)
                                                        propertyInfo.SetValue(item, resultado);
                                                }
                                            }
                                            else
                                                resultado = await cmm.ExecuteNonQueryAsync();
                                        }

                                        if (resultado > 0)
                                        {
                                            incluidos.Add(item);
                                            rowAffected++;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        RegistraErro(e, sql, colunas);
                                    }

                                    if (dispararEventoProgresso)
                                        Progresso(rowAffected);
                                }
                                catch (Exception e)
                                {
                                    RegistraErro(e, sql, colunas);
                                }
                            }
                        }

                        if (rowAffected == lista.Count())
                            await transaction.CommitAsync();
                        else
                        {
                            await transaction.RollbackAsync();
                            incluidos.Clear();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                RegistraErro(e, sql, colunas);
            }
            return incluidos.ToList();
        }

        protected void RegistraErro(Exception e, string sql, IEnumerable<ColumnTable>? colunas = null
            , [CallerFilePath] string path = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        {
            e.Data.Add("sql", sql);
            if (colunas != null && colunas.Any())
            {
                StringBuilder sb = new StringBuilder();
                foreach (var c in colunas)
                {
                    sb.Append(c.Parametro ?? c.Campo);
                    sb.Append('=');
                    sb.Append(c.Valor?.ToString() ?? "NULL");
                    sb.Append('|');
                }
                e.Data.Add("Param", sb.ToString());
            }
            Error(e, member, line, path);
        }

        protected void RegistraErro(Exception e, string sql, IEnumerable<Chave>? chaves = null
            , [CallerFilePath] string path = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        {
            e.Data.Add("sql", sql);
            if (chaves != null && chaves.Any())
            {
                StringBuilder sb = new StringBuilder();
                foreach (var c in chaves)
                {
                    sb.Append(c.Parametro ?? c.Campo);
                    sb.Append('=');
                    sb.Append(c.Valor?.ToString() ?? "NULL");
                    sb.Append('|');
                }
                e.Data.Add("Param", sb.ToString());
            }
            Error(e, member, line, path);
        }

        private async Task<IEnumerable<T>> Incluir(IEnumerable<T> lista, int limite)
        {
            int i = 0;
            List<T> ts = new List<T>();
            try
            {
                for (; i < lista.Count();)
                {
                    List<T> l = new List<T>();
                    l.AddRange(lista.Skip(i).Take(limite));
                    IEnumerable<T> r = await Inclusao(l);
                    if (r != null)
                        ts.AddRange(r.ToList());
                    i += limite;
                    Progresso(ts.Count);
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
            Message($"Incluídos: {ts.Count}");
            return ts;
        }

        private async Task<T?> Update(T obj)
        {
            bool ok = false;
            List<ColumnTable> colunas = BDTools.Campos(obj);
            using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
            {
                ok = await UpdateTransaction(conexaoSql, colunas);
                if (ok)
                {
                    var dt = await SelectTransaction(conexaoSql, colunas);
                    if (dt?.Rows != null && dt.Rows.Count == 1)
                        return AtualizaObjetoOriginal(obj, dt.Rows[0]);
                }
            }
            return null;
        }

        /// <summary>
        /// Atualiza uma lista de objetos com base em atributos de chave primária ou autoincremento.
        /// </summary>
        /// <param name="lista"></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Atualizar(IEnumerable<T> lista)
        {
            int j = lista.Count();
            Rows(j);
            if (j > 1000)
                return await Alteracao(lista, 200);
            IEnumerable<T>? r = await Alteracao(lista, true);
            return r;
        }

        private async Task<IEnumerable<T>?> Alteracao(IEnumerable<T> lista, int limite)
        {
            int ret = 0;
            int i = 0;
            IEnumerable<T>? ts = null;
            try
            {
                int j = lista.Count();
                for (; i < j;)
                {
                    List<T> l = new List<T>();
                    l.AddRange(lista.Skip(i).Take(limite));
                    ts = await Alteracao(l);
                    if (ts != null)
                        ret += ts.Count();
                    i += limite;
                    Progresso(ret);
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
            Message($"Alterados: {ret}");
            return ts;
        }

        private async Task<IEnumerable<T>?> Alteracao(IEnumerable<T> lista, bool dispararEventoProgresso = false)
        {
            string sql;
            var obj = lista.ElementAt(0);
            string origem = ControleAlteracao.Origem ?? Environment.MachineName;
            DateTime now = DateTime.UtcNow;
            var l = lista.ToList();
            if (obj is ICommonColumns)
            {
                if (!AllowCurrentTimeStamp)
                    l.ForEach(m =>
                    {
                        ICommonColumns a = (ICommonColumns)m;
                        a.Origem = origem;
                        a.DataAlteracao = now;
                        a.UsuarioAlteracao = ControleAlteracao.Usuario;
                    });
                else
                    l.ForEach(m =>
                    {
                        ICommonColumns a = (ICommonColumns)m;
                        a.Origem = origem;
                        a.UsuarioAlteracao = ControleAlteracao.Usuario;
                    });
            }


            List<ColumnTable> colunas = BDTools.Campos(obj);
            if (colunas.Any(m => m.IsAutoIncrement && m.Tipo != Tipo.GUID))
                sql = UpdateForAutoParameters(obj);
            else if (colunas.Any(m => m.IsKey))
                sql = UpdateWithKeyParameters(obj);
            else
            {
                Error("Impossível atualizar, não há colunas de referência para a cláusula WHERE");
                return null;
            }

            int resultado = 0; int rowAffected = 0;
            List<T> atualizados = new List<T>();
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbTransaction transaction = conexaoSql.BeginTransaction())
                    {
                        using (DbCommand cmm = conexaoSql.CreateCommand())
                        {
                            cmm.CommandText = sql;
                            ColumnTable? d = null;

                            foreach (var item in l)
                            {
                                try
                                {
                                    cmm.Parameters.Clear();
                                    colunas = BDTools.Campos(item);
                                    d = null;
                                    foreach (var c in colunas)
                                    {
                                        if (c.OnlyInsert) continue;
                                        if (!BDTools.CampoEditavel(c, _bd.AllowCurrentTimeStamp)) continue;
                                        d = BDTools.AtualizaValor(c);
                                        cmm.Parameters.Add(BDTools.CriaParameter(cmm, d));
                                    }
                                    try
                                    {
                                        // Usa retry helper para SQLite
                                        if (_bd.TipoDB == TipoDB.SQLite)
                                        {
                                            resultado = await ExecuteWithSQLiteRetryAsync(
                                                async () => await cmm.ExecuteNonQueryAsync(),
                                                sql, colunas);
                                        }
                                        else
                                        {
                                            resultado = await cmm.ExecuteNonQueryAsync();
                                        }

                                        if (resultado > 0)
                                        {
                                            atualizados.Add(item);
                                            rowAffected++;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        RegistraErro(e, sql, colunas);
                                    }

                                    if (dispararEventoProgresso)
                                        Progresso(rowAffected);
                                }
                                catch (Exception e)
                                {
                                    RegistraErro(e, sql, colunas);
                                }
                            }
                        }
                        if (rowAffected == lista.Count())
                            await transaction.CommitAsync();
                        else
                        {
                            await transaction.RollbackAsync();
                            atualizados.Clear();
                        }
                    }
                }
                if (Verbose)
                    Message($"Atualizados: {atualizados.Count} de {lista.Count()} em {_tableName}");
            }
            catch (Exception e)
            {
                Error(e);
            }
            return atualizados.ToList();
        }


        /// <summary>
        /// Somente em MySQL e SQLite. Usa a cláusula ON DUPLICATE KEY UPDATE
        /// </summary>
        /// <param name="lista"></param>
        /// <param name="dispararEventoProgresso"></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> AtualizarOuIncluir(IEnumerable<T> lista, bool dispararEventoProgresso = false)
        {
            if (_bd.TipoDB == TipoDB.NONE)
            {
                Message("MySQL, SQLite, Postgrees aceita a ação Update On Duplicate Key");
                return null;
            }
            else if (lista == null)
            {
                Message($"Lista {typeof(T).Name} veio nula");
                return null;
            }
            var l = new List<T>();
            var origem = Environment.MachineName;
            if (lista.ElementAt(0) is ICommonColumns)
            {
                if (!AllowCurrentTimeStamp)
                {
                    DateTime dt = DateTime.UtcNow;
                    l = lista
                        .Select(m =>
                        {
                            ICommonColumns a = (ICommonColumns)m;
                            a.DataInclusao = dt;
                            a.DataAlteracao = dt;
                            a.Origem = origem;
                            a.UsuarioAlteracao = ControleAlteracao.Usuario;
                            a.UsuarioInclusao = ControleAlteracao.Usuario;
                            return m;
                        })
                        .ToList();
                }
                else
                {
                    l = lista
                        .Select(m =>
                        {
                            ICommonColumns a = (ICommonColumns)m;
                            a.Origem = origem;
                            a.UsuarioAlteracao = ControleAlteracao.Usuario;
                            a.UsuarioInclusao = ControleAlteracao.Usuario;
                            return m;
                        })
                        .ToList();
                }
            }
            else
                l = lista.ToList();

            var item1 = l[0];
            List<ColumnTable> colunas = BDTools.Campos(item1);
            StringBuilder sql = new StringBuilder();
            var where = colunas.Where(m => m.IsAutoIncrement || m.IsKey);
            var auto = where?.FirstOrDefault(m => m.IsAutoIncrement);
            var keys = where?.Where(m => m.IsKey);
            List<T> atualizado = new List<T>();
            int rowAffected = 0; Guid guid = Guid.Empty;
            try
            {
                if (auto?.Valor != null && (int)auto.Valor > 0)
                {
                    sql.Append(UpdateForAutoParameters(item1));
                }
                else if (keys != null && keys.Any())
                {
                    sql.Append(InsertWithParameters(item1, false));
                    sql.Append(' ');
                    sql.Append(UpdateOnDuplicateKey(item1));
                }
                else
                    throw new ArgumentException("Não é possível fazer Update On Duplicate Key sem Key", typeof(T).Name);
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = sql.ToString();
                        ColumnTable? d = null;

                        foreach (var item in l)
                        {
                            string? nomeAuto = String.Empty;
                            try
                            {
                                colunas = BDTools.Campos(item);
                                d = null;
                                cmm.Parameters.Clear();
                                foreach (var c in colunas)
                                {
                                    if (!BDTools.CampoEditavel(c, _bd.AllowCurrentTimeStamp)) continue;

                                    d = BDTools.AtualizaValor(c);
                                    if (c.IsAutoIncrement && c.Tipo == Tipo.GUID)
                                    {
                                        if (Guid.TryParse(d.Valor?.ToString(), out guid))
                                            nomeAuto = c.Campo;
                                    }
                                    cmm.Parameters.Add(BDTools.CriaParameter(cmm, d));
                                }

                                // Usa retry helper para SQLite
                                int resultado;
                                if (_bd.TipoDB == TipoDB.SQLite)
                                {
                                    resultado = await ExecuteWithSQLiteRetryAsync(
                                        async () => await cmm.ExecuteNonQueryAsync(),
                                        sql.ToString(), colunas);
                                }
                                else
                                {
                                    resultado = await cmm.ExecuteNonQueryAsync();
                                }

                                if (resultado > 0)
                                {
                                    if (guid != Guid.Empty && !string.IsNullOrEmpty(nomeAuto))
                                    {
                                        PropertyInfo? propertyInfo = item.GetType().GetProperty(nomeAuto);
                                        propertyInfo?.SetValue(item, guid);
                                        guid = Guid.Empty;
                                    }
                                    else
                                    {
                                        nomeAuto = colunas.FirstOrDefault(m => m.IsAutoIncrement && m.Tipo != Tipo.GUID)?.Campo;
                                        if (!string.IsNullOrEmpty(nomeAuto))
                                        {
                                            if (_bd.DBConfig.TipoDB == TipoDB.MySQL)
                                                cmm.CommandText = "SELECT LAST_INSERT_ID();";
                                            else if (_bd.DBConfig.TipoDB == TipoDB.SQLite)
                                                cmm.CommandText = "SELECT last_insert_rowid()";
                                            var undefined = await cmm.ExecuteScalarAsync();
                                            cmm.CommandText = sql.ToString();
                                            int.TryParse(undefined?.ToString(), out resultado);
                                            if (resultado > 0)
                                            {
                                                PropertyInfo? propertyInfo = item.GetType().GetProperty(nomeAuto);
                                                propertyInfo?.SetValue(item, resultado);
                                            }
                                        }
                                    }
                                    atualizado.Add(item);
                                    rowAffected++;
                                }

                                if (dispararEventoProgresso)
                                    Progresso(rowAffected);
                            }
                            catch (Exception e)
                            {
                                RegistraErro(e, sql.ToString(), colunas);
                            }
                        }
                    }
                }

                if (Verbose)
                    Message($"Atualizados: {atualizado.Count} de {l.Count()} em {_tableName}");
            }
            catch (Exception e)
            {
                RegistraErro(e, sql.ToString(), colunas);
            }
            return atualizado;
        }

        protected async Task<int> UpdateSpecial(IEnumerable<Chave> camposParaAtualizar,
                                               IEnumerable<Chave> camposParaWhere)
        {
            string sql = UpdateWithWhereParameters(camposParaAtualizar, camposParaWhere);
            if (string.IsNullOrEmpty(sql)) return 0;
            int resultado = 0;
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = sql;

                        foreach (var c in camposParaAtualizar)
                        {
                            IChave chave = c as Chave;
                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, chave));
                        }
                        foreach (var c in camposParaWhere)
                        {
                            IChave chave = c as Chave;
                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, chave));
                        }

                        // Usa retry helper para SQLite
                        if (_bd.TipoDB == TipoDB.SQLite)
                        {
                            resultado = await ExecuteWithSQLiteRetryAsync(
                                async () => await cmm.ExecuteNonQueryAsync(),
                                sql, camposParaWhere);
                        }
                        else
                        {
                            resultado = await cmm.ExecuteNonQueryAsync();
                        }
                    }
                }
                if (Verbose)
                    Message($"Atualizado: {resultado} linha(s) afetada(s) em {_tableName}");
            }
            catch (Exception e)
            {
                var lista = new List<Chave>();
                if (camposParaAtualizar != null && camposParaWhere.Any())
                    lista.AddRange(camposParaAtualizar);
                if (camposParaWhere != null && camposParaWhere.Any())
                    lista.AddRange(camposParaWhere);
                RegistraErro(e, sql, lista);
            }
            Rows(resultado);
            return resultado;
        }


        /// <summary>
        /// Insere ou atualiza o objeto. Testado em MySQL e SQLite. Usa a instrução ON DUPLICATE KEY UPDATE.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public virtual async Task<T?> Upsert(T obj)
        {
            if (_bd.TipoDB == TipoDB.NONE)
            {
                Message("MySQL, SQLite, Postgrees aceita a ação Update On Duplicate Key");
                return null;
            }
            if (obj is ICommonColumns objD)
            {
                objD.Origem = ControleAlteracao.Origem ?? Environment.MachineName;
                if (!AllowCurrentTimeStamp)
                {
                    objD.DataAlteracao = DateTime.UtcNow;
                }
                if (string.IsNullOrEmpty(objD.UsuarioAlteracao) && !string.IsNullOrEmpty(ControleAlteracao.Usuario))
                {
                    objD.UsuarioAlteracao = ControleAlteracao.Usuario;
                }
            }
            StringBuilder sql = new StringBuilder(InsertWithParameters(obj, false));
            sql.Append(' ');
            sql.Append(UpdateOnDuplicateKey(obj));
            int resultado = 0;
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = sql.ToString();
                        ColumnTable? d = null;

                        string? nomeAuto = String.Empty;
                        Guid guid = Guid.Empty;
                        try
                        {
                            List<ColumnTable> colunas = BDTools.Campos(obj);
                            d = null;
                            cmm.Parameters.Clear();
                            foreach (var c in colunas)
                            {
                                if (!BDTools.CampoEditavel(c, _bd.AllowCurrentTimeStamp)) continue;

                                d = BDTools.AtualizaValor(c);
                                if (c.IsAutoIncrement && c.Tipo == Tipo.GUID)
                                {
                                    Guid.TryParse(d.Valor?.ToString(), out guid);
                                    nomeAuto = c.Campo;
                                }
                                cmm.Parameters.Add(BDTools.CriaParameter(cmm, d));
                            }

                            // Usa retry helper para SQLite
                            if (_bd.TipoDB == TipoDB.SQLite)
                            {
                                resultado = await ExecuteWithSQLiteRetryAsync(
                                    async () => await cmm.ExecuteNonQueryAsync(),
                                    sql.ToString(), colunas);
                            }
                            else
                            {
                                resultado = await cmm.ExecuteNonQueryAsync();
                            }

                            if (resultado > 0)
                            {
                                if (guid != Guid.Empty && !string.IsNullOrEmpty(nomeAuto))
                                {
                                    PropertyInfo? propertyInfo = obj.GetType().GetProperty(nomeAuto);
                                    propertyInfo?.SetValue(obj, guid);
                                    guid = Guid.Empty;
                                }
                                else
                                {
                                    nomeAuto = colunas.FirstOrDefault(m => m.IsAutoIncrement && m.Tipo != Tipo.GUID)?.Campo;
                                    if (!string.IsNullOrEmpty(nomeAuto))
                                    {
                                        cmm.CommandText = "SELECT LAST_INSERT_ID();";
                                        var undefined = await cmm.ExecuteScalarAsync();
                                        cmm.CommandText = sql.ToString();
                                        int.TryParse(undefined?.ToString(), out resultado);
                                        if (resultado > 0)
                                        {
                                            PropertyInfo? propertyInfo = obj.GetType().GetProperty(nomeAuto);
                                            propertyInfo?.SetValue(obj, resultado);
                                        }
                                    }
                                }
                            }
                            return obj;
                        }
                        catch (Exception e)
                        {
                            Error(e);
                            return null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Error(e);
                return null;
            }
            finally
            {
                if (Verbose)
                    Message($"Incluído/Alterado [Retorno: {resultado}]: {obj}");
            }
        }

        private async Task<int> Delete(T obj)
        {
            string sql = DeleteWithKeyParameters(obj);
            int resultado = 0;
            List<ColumnTable> colunas = BDTools.Campos(obj);
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = sql;

                        foreach (var c in colunas)
                        {
                            if (c.IsAutoIncrement || c.IsKey)
                                cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                        }

                        // Usa retry helper para SQLite
                        if (_bd.TipoDB == TipoDB.SQLite)
                        {
                            resultado = await ExecuteWithSQLiteRetryAsync(
                                async () => await cmm.ExecuteNonQueryAsync(),
                                sql, colunas);
                        }
                        else
                        {
                            resultado = await cmm.ExecuteNonQueryAsync();
                        }
                    }
                }
                if (Verbose)
                    Message($"Excluído: {obj} - {resultado} linha(s) afetada(s)");
            }
            catch (Exception e)
            {
                Error(e.Message);
            }
            Rows(resultado);
            return resultado;
        }


        private async Task<int> Delete(IEnumerable<T> lista, bool dispararEventoProgresso = false)
        {
            string sql = DeleteWithKeyParameters(lista.ElementAt(0));
            int rowAffected = 0;
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = sql;
                        foreach (var item in lista)
                        {
                            try
                            {
                                cmm.Parameters.Clear();
                                List<ColumnTable> colunas = BDTools.Campos(item);

                                foreach (var c in colunas)
                                {
                                    if (c.IsAutoIncrement || c.IsKey)
                                        cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                                }

                                int resultado;
                                // Usa retry helper para SQLite
                                if (_bd.TipoDB == TipoDB.SQLite)
                                {
                                    resultado = await ExecuteWithSQLiteRetryAsync(
                                        async () => await cmm.ExecuteNonQueryAsync(),
                                        sql, colunas);
                                }
                                else
                                {
                                    resultado = await cmm.ExecuteNonQueryAsync();
                                }
                                rowAffected += resultado;

                                if (dispararEventoProgresso)
                                    Progresso(rowAffected);
                            }
                            catch (Exception e)
                            {
                                Error(e.Message);
                            }
                        }
                    }
                }
                if (Verbose)
                    Message($"Excluídos: {lista.Count()} - {rowAffected} linha(s) afetada(s) em {_tableName}");
            }
            catch (Exception e)
            {
                Error(e.Message);
            }
            return rowAffected;
        }

        private async Task<int> Delete(IEnumerable<T> lista, int limite)
        {
            int ret = 0;
            int i = 0;
            try
            {
                for (; i < lista.Count();)
                {
                    List<T> l = new List<T>();
                    l.AddRange(lista.Skip(i).Take(limite));
                    ret += await Delete(l);
                    i = i + limite;
                    Progresso(ret);
                }
            }
            catch (Exception e)
            {
                Error(e.Message);
            }
            Message($"Excluídos: {ret}");
            return ret;
        }



        /// <summary>
        /// Para instruções UPDATE, INSERT e DELETE, o valor de retorno é o número de linhas afetadas pelo comando. 
        /// Para todos os outros tipos de declarações, o valor retornado é -1.
        /// </summary>
        /// <param name="camposParaWhere"></param>
        /// <returns></returns>
        private async Task<int> Delete(IEnumerable<Chave> camposParaWhere)
        {
            if (camposParaWhere == null || camposParaWhere.Count() == 0)
            {
                Message("Impossível excluir, faltam dados.");
                return -1;
            }

            string sql = DeleteWithWhereParameters(camposParaWhere);
            if (String.IsNullOrEmpty(sql))
            {
                Message("Imppossível excluir, faltam dados.");
                return -1;
            }

            int resultado = -1;
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        cmm.CommandText = sql;
                        foreach (var c in camposParaWhere)
                        {
                            cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                        }

                        // Usa retry helper para SQLite
                        if (_bd.TipoDB == TipoDB.SQLite)
                        {
                            resultado = await ExecuteWithSQLiteRetryAsync(
                                async () => await cmm.ExecuteNonQueryAsync(),
                                sql, camposParaWhere);
                        }
                        else
                        {
                            resultado = await cmm.ExecuteNonQueryAsync();
                        }
                    }
                }
                if (Verbose)
                    Message($"Excluídos: {camposParaWhere.Count()} - {resultado} linha(s) afetada(s) em {_tableName}");
            }
            catch (Exception e)
            {
                Error(e.Message);
            }
            Rows(resultado);
            Message($"Excluídos: {resultado}");
            return resultado;

        }

        protected virtual async Task<int> ExecuteSQL(string sql, IEnumerable<Chave>? parametros = null)
        {
            int rows = 0;
            try
            {
                using (DbConnection conexaoSql = await _bd.ObterConexaoAsync())
                {
                    using (DbCommand cmm = conexaoSql.CreateCommand())
                    {
                        if (parametros != null && parametros.Count() > 0)
                        {
                            foreach (var c in parametros)
                            {
                                cmm.Parameters.Add(BDTools.CriaParameter(cmm, c));
                            }
                        }
                        cmm.Connection = conexaoSql;
                        cmm.CommandText = sql;

                        // Usa retry helper para SQLite
                        if (_bd.TipoDB == TipoDB.SQLite)
                        {
                            rows = await ExecuteWithSQLiteRetryAsync(
                                async () => await cmm.ExecuteNonQueryAsync(),
                                sql, parametros);
                        }
                        else
                        {
                            rows = await cmm.ExecuteNonQueryAsync();
                        }
                    }
                    _msg = String.Empty;
                }
                if (Verbose)
                    Message($"Comando SQL executado: {sql} - {rows} linha(s) afetada(s)");
            }
            catch (Exception e)
            {
                RegistraErro(e, sql, parametros);
            }
            Rows(rows);
            Message($"{ExecuteSQLTable(sql)} afetados: {rows}");
            return rows;

        }

        private string ExecuteSQLTable(string sql)
        {
            IEnumerable<Type>? types = _bd.Tabelas;

            if (types == null || !types.Any())
                return _tableName;

            foreach (var tabela in types)
            {
                if (sql.Contains(tabela.Name))
                {
                    return tabela.Name;
                }
            }
            return _tableName;
        }

        public virtual async Task<bool> IsConnected()
        {
            return await _bd.IsServerConnectedAsync();
        }

        #region SQLite Retry Helper Methods

        /// <summary>
        /// Contador para evitar loops infinitos de retry
        /// </summary>
        private int _retryCount = 0;
        private const int MaxRetryAttempts = 3;

        /// <summary>
        /// Executa operação com retry automático para SQLite em caso de database locked
        /// </summary>
        private async Task<int> ExecuteWithSQLiteRetryAsync(
            Func<Task<int>> operation,
            string sql,
            IEnumerable<ColumnTable>? colunas)
        {
            return await SQLiteRetryHelper.ExecuteWithRetryAsync(
                operation,
                maxRetries: SQLiteRetryHelper.DefaultMaxRetries,
                retryDelayMs: SQLiteRetryHelper.DefaultRetryDelayMs,
                onRetry: (attempt, ex) =>
                {
                    if (Verbose)
                        Message($"SQLite bloqueado (tentativa {attempt}): {ex.Message}. Tentando liberar locks...");

                    // Tenta liberar locks
                    SQLiteRetryHelper.LimparPoolsConexao();
                });
        }

        /// <summary>
        /// Executa operação com retry automático para SQLite em caso de database locked
        /// </summary>
        private async Task<int> ExecuteWithSQLiteRetryAsync(
            Func<Task<int>> operation,
            string sql,
            IEnumerable<Chave>? chaves)
        {
            return await SQLiteRetryHelper.ExecuteWithRetryAsync(
                operation,
                maxRetries: SQLiteRetryHelper.DefaultMaxRetries,
                retryDelayMs: SQLiteRetryHelper.DefaultRetryDelayMs,
                onRetry: (attempt, ex) =>
                {
                    if (Verbose)
                        Message($"SQLite bloqueado (tentativa {attempt}): {ex.Message}. Tentando liberar locks...");

                    // Tenta liberar locks
                    SQLiteRetryHelper.LimparPoolsConexao();
                });
        }

        /// <summary>
        /// Tenta liberar locks e repetir operação (controle de recursão)
        /// </summary>
        private async Task<bool> TentarLiberarERepetirAsync()
        {
            if (_retryCount >= MaxRetryAttempts)
            {
                _retryCount = 0;
                return false;
            }

            _retryCount++;

            if (Verbose)
                Message($"Tentando liberar locks do SQLite (tentativa {_retryCount}/{MaxRetryAttempts})...");

            // Limpa pools e força GC
            SQLiteRetryHelper.LimparPoolsConexao();

            // Tenta liberar via conexão
            await _bd.LiberarLocksSQLiteAsync();

            // Aguarda um pouco antes de tentar novamente
            await Task.Delay(500 * _retryCount);

            return true;
        }

        #endregion
    }
}
