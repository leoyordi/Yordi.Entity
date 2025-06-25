using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Reflection;
using System.Text;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public abstract class RepositorioBaseAbstract<T> : CommonBaseAbstract<T> where T : class
    {

        protected string _tableName;
        private readonly IBDConexao _bd;
        BDTools _bdTools;
        protected RepositorioBaseAbstract(IBDConexao bd)
        {
            _bd = bd;
            _bdTools = new BDTools(bd);
            _tableName = typeof(T).Name;
        }

        public bool AllowCurrentTimeStamp { get => _bd.AllowCurrentTimeStamp; }
        protected string TableName { get => _tableName; set => _tableName = value; }
        public bool Verbose => _bd.DBConfig.Verbose ?? false;

        internal void CheckTableName(T obj)
        {
            if (String.IsNullOrEmpty(_tableName))
                _tableName = obj.GetType().Name;
        }
        internal void CheckTableName()
        {
            if (String.IsNullOrEmpty(_tableName))
            {
                T obj = Activator.CreateInstance<T>();
                _tableName = obj.GetType().Name;
            }
        }


        internal T Objeto(DataRow row)
        {
            T obj = Activator.CreateInstance<T>();
            object? c; CommonColumns? common = null;
            bool utcDatas = false;
            if (obj is CommonColumns commonC)
            {
                //if (!AllowCurrentTimeStamp)
                //    utcDatas = true;
                common = commonC;
            }
            var properties = obj.GetType().GetProperties();
            foreach (var prop in properties)
            {
                try
                {
                    //PropertyInfo p = obj.GetType().GetProperty(prop.Name);
                    var bdIgnorar = Attribute.GetCustomAttribute(prop, typeof(BDIgnorarAttribute)) as BDIgnorarAttribute;
                    if (bdIgnorar != null)
                        continue;
                    if (prop != null && prop.CanRead && prop.CanWrite)
                    {
                        c = row[prop.Name];
                        bool isNull = c == null || DBNull.Value.Equals(c);
                        if (prop.PropertyType.IsEnum && c != null)
                            prop.SetValue(obj, Enum.ToObject(prop.PropertyType, c), null);
                        else
                        {
                            Type t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                            if (t.IsEnum && !isNull && c != null)
                                prop.SetValue(obj, Enum.ToObject(t, c), null);
                            else
                            {
                                object? safeValue = isNull ? null : Convert.ChangeType(c, t);
                                //object safeValue = null;
                                //if (c != null && !DBNull.Value.Equals(c))
                                //    safeValue = Convert.ChangeType(c, t);

                                prop.SetValue(obj, safeValue, null);
                                if (utcDatas && safeValue != null && common != null)
                                {
                                    if (string.Equals(prop.Name, nameof(CommonColumns.DataInclusao), StringComparison.OrdinalIgnoreCase))
                                        common.DataInclusao = ((DateTime)safeValue).ToLocalTime();
                                    else if (string.Equals(prop.Name, nameof(CommonColumns.DataAlteracao), StringComparison.OrdinalIgnoreCase))
                                        common.DataAlteracao = ((DateTime)safeValue).ToLocalTime();
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    e.Data.Add(prop.Name, e.Message);
                    Error(e);
                }
            }
            return obj;
        }

        internal string SelectWithKeyParameters(T obj)
        {
            CheckTableName(obj);
            StringBuilder s = new StringBuilder("SELECT * FROM ");
            s.Append(_bd.DBConfig.OpenName);
            s.Append(_tableName);
            s.Append(_bd.DBConfig.CloseName);
            s.Append(' ');
            s.Append(_bdTools.WhereWithParameters(obj));
            return s.ToString();
        }

        protected string SelectWithWhereParameters(IEnumerable<Chave> campos, bool ou = false)
        {
            CheckTableName();
            StringBuilder s = new StringBuilder("SELECT * FROM ");
            s.Append(_bd.DBConfig.OpenName);
            s.Append(_tableName);
            s.Append(_bd.DBConfig.CloseName);
            s.Append(' ');
            s.Append(_bdTools.WhereWithParameters(campos, ou));
            return s.ToString();
        }




        internal string InsertWithParameters(T obj, bool returnScopeIdentity = true)
        {
            CheckTableName(obj);
            List<ColumnTable> colunas = BDTools.Campos(obj);
            return InsertWithParameters(colunas, returnScopeIdentity);
        }

        internal string InsertWithParameters(List<ColumnTable> colunas, bool returnScopeIdentity = true)
        {
            CheckTableName();
            StringBuilder insert = new StringBuilder("INSERT INTO ");
            insert.Append(_bd.DBConfig.OpenName);
            insert.Append(_tableName);
            insert.Append(_bd.DBConfig.CloseName);
            insert.Append(' ');

            StringBuilder campos = new StringBuilder();
            StringBuilder values = new StringBuilder();

            int cont = 0;
            foreach (ColumnTable col in colunas)
            {
                if (!BDTools.CampoEditavel(col, _bd.AllowCurrentTimeStamp)) continue;
                if (col.IsAutoIncrement && col.Tipo != Tipo.GUID) continue;

                if (cont > 0)
                {
                    campos.Append(", ");
                    values.Append(", ");
                }
                campos.Append(_bd.DBConfig.OpenName);
                campos.Append(col.Campo);
                campos.Append(_bd.DBConfig.CloseName);
                values.Append($"@{col.Campo}");
                cont++;
            }
            insert.Append($"({campos}) VALUES ({values})");
            if (returnScopeIdentity)
            {
                if (colunas.Any(m => m.IsAutoIncrement && m.Tipo != Tipo.GUID))
                {
                    if (_bd.DBConfig.TipoDB == TipoDB.MySQL)
                        insert.Append("; SELECT LAST_INSERT_ID();");
                    else if (_bd.DBConfig.TipoDB == TipoDB.SQLite)
                        insert.Append(";SELECT last_insert_rowid();");
                }
            }

            return insert.ToString();
        }


        internal string InsertMultiRowsWithParameters(IEnumerable<T> lista, out IDictionary<T, IEnumerable<ColumnTable>>? keyValues)
        {
            if (lista == null || !lista.Any())
            {
                keyValues = null;
                return String.Empty;
            }
            var obj = lista.First();
            CheckTableName(obj);

            keyValues = new Dictionary<T, IEnumerable<ColumnTable>>(lista.Count());
            StringBuilder insert = new StringBuilder("INSERT INTO ");
            insert.Append(_bd.DBConfig.OpenName);
            insert.Append(_tableName);
            insert.Append(_bd.DBConfig.CloseName);
            insert.Append(' ');

            StringBuilder campos = new StringBuilder();
            StringBuilder values = new StringBuilder();

            List<ColumnTable> colunas = BDTools.Campos(lista.ElementAt(0));
            int cont = 0;
            //INSERT INTO TABLENAME (...)
            foreach (ColumnTable col in colunas)
            {
                if (BDTools.CampoEditavel(col, _bd.AllowCurrentTimeStamp)) continue;
                if (col.IsAutoIncrement && col.Tipo != Tipo.GUID) continue;
                if (cont > 0)
                    campos.Append(", ");
                campos.Append(_bd.DBConfig.OpenName);
                campos.Append(col.Campo);
                campos.Append(_bd.DBConfig.CloseName);
                cont++;
            }
            var j = lista.Count();
            for (int i = 0; i < j; i++)
            {
                T element = lista.ElementAt(i);
                colunas = BDTools.Campos(element);
                cont = 0;
                if (i > 0)
                    values.Append("),(");

                foreach (ColumnTable col in colunas)
                {
                    if (!BDTools.CampoEditavel(col, _bd.AllowCurrentTimeStamp)) continue;
                    if (col.IsAutoIncrement && col.Tipo != Tipo.GUID) continue;

                    if (cont > 0)
                        values.Append(", ");
                    col.Parametro = $"@{col.Campo}_{i}";
                    values.Append(col.Parametro);
                    cont++;
                }
                keyValues.Add(element, colunas);
            }

            insert.Append($"({campos}) VALUES ({values})");
            return insert.ToString();
        }



        internal string UpdateWithKeyParameters(T obj)
        {
            CheckTableName(obj);
            List<ColumnTable> colunas = BDTools.Campos(obj);
            return UpdateWithKeyParameters(colunas);
        }

        internal string UpdateWithKeyParameters(List<ColumnTable> colunas)
        {
            CheckTableName();

            StringBuilder update = new StringBuilder("UPDATE ");
            update.Append(_bd.DBConfig.OpenName);
            update.Append(_tableName);
            update.Append(_bd.DBConfig.CloseName);
            update.Append(" SET ");

            int cont = 0;
            foreach (ColumnTable col in colunas)
            {
                if (!col.IsAutoIncrement && !col.IsKey && !col.BDIgnorar)
                {
                    if (col.AutoInsertDate && !col.AutoUpdateDate) // Campo de data de inserção, não coloca esse campo para ser alterado em update
                        continue;
                    else if (col.AutoUpdateDate && AllowCurrentTimeStamp)
                        continue;
                    else if (col.OnlyInsert)
                        continue;

                    if (cont > 0)
                        update.Append(", ");
                    update.Append(_bd.DBConfig.OpenName);
                    update.Append(col.Campo);
                    update.Append(_bd.DBConfig.CloseName);
                    update.Append($" = @{col.Campo}");
                    cont++;
                }
            }
            update.Append(_bdTools.WhereWithParameters(colunas));
            return update.ToString();
        }

        internal string UpdateForAutoParameters(T obj)
        {
            CheckTableName(obj);

            List<ColumnTable> colunas = BDTools.Campos(obj);
            return UpdateForAutoParameters(colunas);
        }

        internal string UpdateForAutoParameters(List<ColumnTable> colunas)
        {
            CheckTableName();
            ColumnTable? auto = colunas.FirstOrDefault(m => m.IsAutoIncrement);
            if (auto == null)
                throw new ArgumentException("Objeto não tem autoincremento", _tableName);
            if (auto.Valor is int valor && valor == 0)
                throw new ArgumentException("Propriedade de autoincremento está sem valor", auto.Campo);
            StringBuilder update = new StringBuilder("UPDATE ");
            update.Append(_bd.DBConfig.OpenName);
            update.Append(_tableName);
            update.Append(_bd.DBConfig.CloseName);
            update.Append(" SET ");


            int cont = 0;
            foreach (ColumnTable col in colunas)
            {
                if (!col.IsAutoIncrement && !col.BDIgnorar)
                {
                    if (col.AutoInsertDate && !col.AutoUpdateDate) // Campo de data de inserção, não coloca esse campo para ser alterado em update
                        continue;
                    else if (col.AutoUpdateDate && AllowCurrentTimeStamp)
                        continue;
                    else if (col.OnlyInsert)
                        continue;

                    if (cont > 0)
                        update.Append(", ");
                    update.Append(_bd.DBConfig.OpenName);
                    update.Append(col.Campo);
                    update.Append(_bd.DBConfig.CloseName);
                    update.Append($" = @{col.Campo}");
                    cont++;
                }
            }
            List<Chave> l = new() { auto };
            update.Append(_bdTools.WhereWithParameters(l));
            return update.ToString();
        }


        internal string UpdateOnDuplicateKey(T obj)
        {
            CheckTableName();

            if (_bd.DBConfig.TipoDB != TipoDB.MySQL && _bd.DBConfig.TipoDB != TipoDB.SQLite)
                return String.Empty;

            List<ColumnTable> colunas = BDTools.Campos(obj);
            int cont = 0;

            StringBuilder update = new StringBuilder();
            if (_bd.DBConfig.TipoDB == TipoDB.SQLite)
            {
                update.Append(" ON CONFLICT (");
                var keys = colunas.Where(m => m.IsKey);
                foreach (var k in keys)
                {
                    if (cont > 0)
                        update.Append(", ");
                    update.Append(_bd.DBConfig.OpenName);
                    update.Append(k.Campo);
                    update.Append(_bd.DBConfig.CloseName);
                    cont++;
                }
                update.Append(") DO UPDATE SET ");
            }
            else if (_bd.DBConfig.TipoDB == TipoDB.MySQL)
                update.Append(" ON DUPLICATE KEY UPDATE ");

            cont = 0;
            foreach (ColumnTable col in colunas)
            {
                if (!col.IsAutoIncrement && !col.IsKey && !col.BDIgnorar)
                {
                    if (col.AutoInsertDate && !col.AutoUpdateDate) // Campo de data de inserção, não coloca esse campo para ser alterado em update
                        continue;
                    else if (col.AutoUpdateDate && _bd.DBConfig.TipoDB == TipoDB.MySQL)
                        continue;
                    else if (col.OnlyInsert) continue;

                    if (cont > 0)
                        update.Append(", ");
                    update.Append(_bd.DBConfig.OpenName);
                    update.Append(col.Campo);
                    update.Append(_bd.DBConfig.CloseName);
                    if (_bd.DBConfig.TipoDB == TipoDB.MySQL)
                        update.Append($" = VALUES({col.Campo})");
                    else
                        update.Append($" = excluded.{col.Campo}");
                    cont++;
                }
            }
            return update.ToString();
        }
        internal string UpdateWithWhereParameters(IEnumerable<Chave> camposParaAtualizar,
                                                IEnumerable<Chave>? camposParaWhere = null)
        {
            if (String.IsNullOrEmpty(_tableName) || camposParaWhere == null)
            {
                T obj = Activator.CreateInstance<T>();
                if (String.IsNullOrEmpty(_tableName))
                    _tableName = obj.GetType().Name;
                if (camposParaWhere == null)
                    camposParaWhere = BDTools.WhereKeyOrID(obj);
            }
            StringBuilder update = new StringBuilder("UPDATE ");
            update.Append(_bd.DBConfig.OpenName);
            update.Append(_tableName);
            update.Append(_bd.DBConfig.CloseName);
            update.Append(" SET ");

            int cont = 0;
            foreach (Chave col in camposParaAtualizar)
            {
                if (cont > 0)
                    update.Append(", ");
                update.Append(_bd.DBConfig.OpenName);
                update.Append(col.Campo);
                update.Append(_bd.DBConfig.CloseName);
                update.Append($" = @{col.Campo}");
                cont++;
            }
            update.Append(_bdTools.WhereWithParameters(camposParaWhere));
            return update.ToString();
        }


        internal string DeleteWithKeyParameters(T obj)
        {
            CheckTableName(obj);
            StringBuilder delete = new StringBuilder("DELETE FROM ");
            delete.Append(_bd.DBConfig.OpenName);
            delete.Append(_tableName);
            delete.Append(_bd.DBConfig.CloseName);
            delete.Append(" ");
            delete.Append(_bdTools.WhereWithParameters(obj));
            return delete.ToString();
        }

        internal string DeleteWithWhereParameters(IEnumerable<Chave> camposParaWhere)
        {
            CheckTableName();
            if (String.IsNullOrEmpty(_tableName))
            {
                throw new Exception("Impossível excluir - tabela não informada");
            }
            StringBuilder delete = new StringBuilder("DELETE FROM ");
            delete.Append(_bd.DBConfig.OpenName);
            delete.Append(_tableName);
            delete.Append(_bd.DBConfig.CloseName);
            delete.Append(' ');

            delete.Append(_bdTools.WhereWithParameters(camposParaWhere));
            return delete.ToString();
        }




        internal T CompleteAutoColumn(T obj)
        {
            Type type = obj.GetType();
            PropertyInfo[] properties = type.GetProperties(BDTools.Flags);
            foreach (PropertyInfo p in properties)
            {
                var keyAttribute = Attribute.GetCustomAttribute(p, typeof(KeyAttribute)) as KeyAttribute;
                if (p.Name.Contains("auto", StringComparison.CurrentCultureIgnoreCase))
                {
                    Tipo tipo = Conversores.PropriedadeTipo(p);
                    if (tipo == Tipo.GUID)
                    {
                        if (_bd.DBConfig.TipoDB == TipoDB.SQLServer)
                            NewGuid.TipoGuid = TipoGuid.MSSQL;
                        else
                            NewGuid.TipoGuid = TipoGuid.MySQL;
                        p.SetValue(obj, NewGuid.NewSequentialGuid());
                    }
                    else if (tipo == Tipo.INT)
                    {
                        Random rand1 = new Random((int)DateTime.Now.Ticks);
                        p.SetValue(obj, rand1.Next());
                    }
                }
            }
            return obj;
        }

        protected internal List<Chave> Datas(DateTime inicial, DateTime final)
        {
            if (typeof(T).IsAssignableFrom(typeof(CommonColumns)))
            {
                throw new ArgumentException("Objeto não tem CommonColumns", nameof(T));
            }
            //var ini = _bd.DBConfig.TipoDB == TipoDB.SQLite ? inicial.ToUniversalTime() : inicial;
            //var fin = _bd.DBConfig.TipoDB == TipoDB.SQLite ? final.ToUniversalTime() : final;
            var ini = !AllowCurrentTimeStamp ? inicial.ToUniversalTime() : inicial;
            var fin = !AllowCurrentTimeStamp ? final.ToUniversalTime() : final;

            return new List<Chave>() {
                new Chave()
                {
                    Campo = nameof(CommonColumns.DataInclusao),
                    Operador = Operador.MAIORouIGUALque,
                    Parametro = "Inicial",
                    Tipo = Tipo.DATA,
                    Valor = ini
                },
                new Chave()
                {
                    Campo = nameof(CommonColumns.DataInclusao),
                    Operador = Operador.MENORque,
                    Parametro = "Final",
                    Tipo = Tipo.DATA,
                    Valor = fin
                }
            };
        }

    }
}
