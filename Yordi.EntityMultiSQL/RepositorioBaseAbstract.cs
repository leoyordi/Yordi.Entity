using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Reflection;
using System.Text;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public abstract class RepositorioBaseAbstract<T> : CommonBaseAbstract<T> where T : class
    {

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();
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
            // FIX #3: utcDatas ativado corretamente
            bool utcDatas = !AllowCurrentTimeStamp;
            if (obj is CommonColumns commonC)
                common = commonC;

            // FIX #6: cache de propriedades por tipo evita reflexão repetida
            var properties = _propertyCache.GetOrAdd(obj.GetType(), t => t.GetProperties());

            foreach (var prop in properties)
            {
                try
                {
                    var bdIgnorar = Attribute.GetCustomAttribute(prop, typeof(BDIgnorarAttribute)) as BDIgnorarAttribute;
                    if (bdIgnorar != null)
                        continue;

                    if (prop == null || !prop.CanRead || !prop.CanWrite)
                        continue;

                    // FIX #1: verificar se a coluna existe no DataRow antes de acessar
                    if (!row.Table.Columns.Contains(prop.Name))
                        continue;

                    c = row[prop.Name];
                    bool isNull = c == null || DBNull.Value.Equals(c);

                    // FIX #2: tipo valor não-anulável com valor nulo → manter default
                    if (isNull)
                    {
                        bool isNullable = !prop.PropertyType.IsValueType
                                          || Nullable.GetUnderlyingType(prop.PropertyType) != null;
                        if (!isNullable)
                            continue;
                        prop.SetValue(obj, null, null);
                        continue;
                    }

                    if (prop.PropertyType.IsEnum)
                    {
                        prop.SetValue(obj, Enum.ToObject(prop.PropertyType, c!), null);
                        continue;
                    }

                    Type t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                    if (t.IsEnum)
                    {
                        prop.SetValue(obj, Enum.ToObject(t, c!), null);
                    }
                    else if (t == typeof(Guid))
                    {
                        if (c is Guid g)
                            prop.SetValue(obj, g, null);
                        else if (c is string s && Guid.TryParse(s, out Guid g2))
                            prop.SetValue(obj, g2, null);
                        else if (c is byte[] b && b.Length == 16)
                            prop.SetValue(obj, new Guid(b), null);
                        else
                            throw new InvalidCastException($"Impossível converter {c!.GetType().Name} para Guid");
                    }
                    else if (t == typeof(DateOnly))
                    {
                        if (c is DateOnly dateOnly)
                            prop.SetValue(obj, dateOnly, null);
                        else if (c is DateTime dateTime)
                            prop.SetValue(obj, DateOnly.FromDateTime(dateTime), null);
                        else if (c is string dateStr && DateOnly.TryParse(dateStr, out DateOnly parsedDate))
                            prop.SetValue(obj, parsedDate, null);
                        else if (c is long longDate)
                            prop.SetValue(obj, DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(longDate).LocalDateTime), null);
                        else if (c is int intDate)
                            prop.SetValue(obj, DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(intDate).LocalDateTime), null);
                        else
                            throw new InvalidCastException($"Impossível converter {c!.GetType().Name} para DateOnly");
                    }
                    else if (t == typeof(TimeOnly))
                    {
                        if (c is TimeOnly timeOnly)
                            prop.SetValue(obj, timeOnly, null);
                        else if (c is TimeSpan timeSpan)
                            prop.SetValue(obj, TimeOnly.FromTimeSpan(timeSpan), null);
                        else if (c is DateTime timeDateTime)
                            prop.SetValue(obj, TimeOnly.FromDateTime(timeDateTime), null);
                        else if (c is string timeStr)
                        {
                            // Tenta parse direto ("HH:mm", "HH:mm:ss", "HH:mm:ss.fff")
                            if (TimeOnly.TryParse(timeStr, out TimeOnly parsedTime))
                                prop.SetValue(obj, parsedTime, null);
                            // Formato gravado por CriaParameter: "0001-01-01 HH:mm:ss.fff"
                            else if (DateTime.TryParse(timeStr, out DateTime dtFromStr))
                                prop.SetValue(obj, TimeOnly.FromDateTime(dtFromStr), null);
                            else
                                throw new InvalidCastException($"Impossível converter string \"{timeStr}\" para TimeOnly");
                        }
                        else if (c is long longTime)
                            prop.SetValue(obj, TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(longTime)), null);
                        else if (c is int intTime)
                            prop.SetValue(obj, TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(intTime)), null);
                        else
                            throw new InvalidCastException($"Impossível converter {c!.GetType().Name} para TimeOnly");
                    }
                    else if (t == typeof(TimeSpan))
                    {
                        if (c is TimeSpan timeSpan2)
                            prop.SetValue(obj, timeSpan2, null);
                        else if (c is string timeSpanStr)
                        {
                            // Tenta parse direto ("HH:mm:ss", "d.HH:mm:ss.fff")
                            if (TimeSpan.TryParse(timeSpanStr, out TimeSpan parsedSpan))
                                prop.SetValue(obj, parsedSpan, null);
                            // Formato gravado por CriaParameter: "0001-01-01 HH:mm:ss.fff"
                            else if (DateTime.TryParse(timeSpanStr, out DateTime dtFromSpanStr))
                                prop.SetValue(obj, dtFromSpanStr.TimeOfDay, null);
                            else
                                throw new InvalidCastException($"Impossível converter string \"{timeSpanStr}\" para TimeSpan");
                        }
                        else if (c is long longSpan)
                            prop.SetValue(obj, TimeSpan.FromSeconds(longSpan), null);
                        else if (c is int intSpan)
                            prop.SetValue(obj, TimeSpan.FromSeconds(intSpan), null);
                        else
                            throw new InvalidCastException($"Impossível converter {c!.GetType().Name} para TimeSpan");
                    }
                    else if (t == typeof(DateTimeOffset))
                    {
                        if (c is DateTimeOffset dto)
                            prop.SetValue(obj, dto, null);
                        else if (c is DateTime dtOffset)
                            prop.SetValue(obj, new DateTimeOffset(dtOffset), null);
                        else if (c is string dtoStr && DateTimeOffset.TryParse(dtoStr, out DateTimeOffset parsedDto))
                            prop.SetValue(obj, parsedDto, null);
                        else if (c is long longDto)
                            prop.SetValue(obj, DateTimeOffset.FromUnixTimeSeconds(longDto).ToLocalTime(), null);
                        else if (c is int intDto)
                            prop.SetValue(obj, DateTimeOffset.FromUnixTimeSeconds(intDto).ToLocalTime(), null);
                        else
                            throw new InvalidCastException($"Impossível converter {c!.GetType().Name} para DateTimeOffset");
                    }
                    else if (t == typeof(DateTime))
                    {
                        DateTime resolved;
                        if (c is DateTime dt)
                            resolved = dt;
                        else if (c is string dtStr && DateTime.TryParse(dtStr, out DateTime parsedDt))
                            resolved = parsedDt;
                        else if (c is long longDt)
                            resolved = DateTimeOffset.FromUnixTimeSeconds(longDt).LocalDateTime;
                        else if (c is int intDt)
                            resolved = DateTimeOffset.FromUnixTimeSeconds(intDt).LocalDateTime;
                        else
                            throw new InvalidCastException($"Impossível converter {c!.GetType().Name} para DateTime");

                        // FIX #3: conversão UTC → Local para campos de auditoria
                        // Só converte se Kind == Utc — evita dupla conversão quando o driver já converteu
                        if (utcDatas && common != null)
                        {
                            DateTime localTime = resolved.Kind == DateTimeKind.Utc
                                ? resolved.ToLocalTime()
                                : resolved;

                            if (string.Equals(prop.Name, nameof(CommonColumns.DataInclusao), StringComparison.OrdinalIgnoreCase))
                            {
                                common.DataInclusao = localTime;
                                continue;
                            }
                            else if (string.Equals(prop.Name, nameof(CommonColumns.DataAlteracao), StringComparison.OrdinalIgnoreCase))
                            {
                                common.DataAlteracao = localTime;
                                continue;
                            }
                        }
                        prop.SetValue(obj, resolved, null);
                    }
                    else
                    {
                        object? safeValue;
                        // FIX #4: SQLite retorna inteiros como long; bool como 0/1
                        if (t == typeof(int) && c is long longVal)
                            safeValue = checked((int)longVal);
                        else if (t == typeof(bool) && c is long boolLong)
                            safeValue = boolLong != 0;
                        else if (t == typeof(bool) && c is int boolInt)
                            safeValue = boolInt != 0;
                        else
                            safeValue = Convert.ChangeType(c, t);

                        prop.SetValue(obj, safeValue, null);
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
                // CORREÇÃO: incluir apenas campos editáveis
                if (!BDTools.CampoEditavel(col, _bd.AllowCurrentTimeStamp)) continue;
                //if (BDTools.CampoEditavel(col, _bd.AllowCurrentTimeStamp)) continue;
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
                        var newGuid = NewGuid.NewSequentialGuid();
                        if (p.PropertyType == typeof(string))
                            p.SetValue(obj, newGuid.ToString());
                        else
                            p.SetValue(obj, newGuid);
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
            if (!typeof(CommonColumns).IsAssignableFrom(typeof(T)))
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
