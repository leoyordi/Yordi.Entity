using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public class BDTools
    {
        private readonly IBDConexao _bd;
        private static IBDConexao staticBd;
        public BDTools(IBDConexao bd) { _bd = bd; staticBd = bd; }
        public static BindingFlags Flags => BindingFlags.Public | BindingFlags.Instance;

        public static List<ColumnTable> Campos(object obj)
        {        
            List<ColumnTable> campos = new List<ColumnTable>();
            ColumnTable? c = null;
            PropertyInfo[]? properties = null;
            Type t = obj.GetType();
            Type? type = null;
            if (obj is Type _type)
            {
                properties = _type.GetProperties(Flags);
                type = _type;
            }
            else
                properties = obj.GetType().GetProperties(Flags);
            try
            {
                foreach (PropertyInfo p in properties)
                {
                    if (!p.CanRead || !p.CanWrite)
                        continue;

                    c = new ColumnTable();

                    foreach (Attribute a in Attribute.GetCustomAttributes(p))
                    {
                        var att = a.GetType();
                        if (att == typeof(KeyAttribute))
                            c.IsKey = true;
                        if (att == typeof(DescriptionAttribute))
                            c.IsDescription = true;
                        else if (att == typeof(AutoIncrementAttribute))
                            c.IsAutoIncrement = true;
                        else if (att == typeof(BDIgnorarAttribute))
                            c.BDIgnorar = true;
                        else if (att == typeof(DefaultValueAttribute))
                            c.ValorPadrao = ((DefaultValueAttribute)a).Value;
                        else if (att == typeof(TamanhoAttribute))
                            c.Tamanho = ((TamanhoAttribute)a).Tamanho;
                        else if (att == typeof(StringLengthAttribute))
                            c.Tamanho = $"({((StringLengthAttribute)a).MaximumLength})";
                        else if (att == typeof(ColumnAttribute))
                            c.Campo = ((ColumnAttribute)a).Name;
                        else if (att == typeof(AutoUpdateDate))
                            c.AutoUpdateDate = true;
                        else if (att == typeof(AutoInsertDate))
                            c.AutoInsertDate = true;
                        else if (att == typeof(OnlyInsert))
                            c.OnlyInsert = true;
                        else if (att == typeof(OnlyUpdate))
                            c.OnlyUpdate = true;
                    }

                    if (String.IsNullOrEmpty(c.Campo))
                        c.Campo = p.Name;
                    if ((p.PropertyType.IsClass || true.Equals(p.PropertyType.Namespace?.Contains("ollection"))) && c.BDIgnorar)
                        c.PermiteNulo = true;
                    else if (p.PropertyType.IsGenericType)
                    {
                        var generic = p.PropertyType.GetGenericTypeDefinition();
                        if (generic == typeof(Nullable<>) || generic.IsArray)
                            c.PermiteNulo = true;
                        else if (Nullable.GetUnderlyingType(p.PropertyType) != null)
                            c.PermiteNulo = true;
                        else
                            c.PermiteNulo = false;
                    }

                    else if ((p.PropertyType.IsArray || p.PropertyType == typeof(string)) && !c.IsKey && !c.BDIgnorar)
                        c.PermiteNulo = true;
                    else if (p.PropertyType.IsEnum && !c.IsKey)
                        c.PermiteNulo = true;
                    else
                        c.PermiteNulo = false;

                    if (!c.BDIgnorar)
                    {
                        object? o;
                        if (type != null)
                            o = p.GetDefaultValue();
                        else
                            o = p.GetValue(obj, null);
                        Type? oType = o?.GetType();
                        if (p.PropertyType.IsEnum && oType != null)
                            c.Valor = Convert.ChangeType(o, Enum.GetUnderlyingType(oType));
                        else
                            c.Valor = o;
                    }

                    c.Tipo = Conversores.PropriedadeTipo(p);

                    campos.Add(c);

                }
            }
            catch (Exception e)
            {
                e.Data.Add("Tipo", nameof(obj));
                if (c != null)
                    e.Data.Add("Campo", c.Campo);
                Logger.LogSync(e);
            }
            return campos;
        }

        public static bool CampoEditavel(ColumnTable col, bool bdAllowCurrentTimeStamp)
        {
            if (col.BDIgnorar) return false;
            if (bdAllowCurrentTimeStamp && (col.AutoInsertDate || col.AutoUpdateDate))
                return false;
            return true;
        }

        public static ColumnTable AtualizaValor(ColumnTable c)
        {
            if (c.Tipo == Tipo.DATA)
            {
                if (c.Valor != null)
                {
                    if ((DateTime)c.Valor == DateTime.MinValue)
                        c.Valor = DataPadrao.MinValue;
                }
            }
            else if (c.Tipo == Tipo.STRING)
            {
                if (c.Valor == null)
                    c.Valor = String.Empty;
            }
            else if (c.Tipo == Tipo.GUID && c.IsAutoIncrement)
            {
                if (c.Valor == null || !Guid.TryParse(c.Valor.ToString(), out _))
                    c.Valor = Guid.NewGuid();
            }
            return c;
        }

        public static DbParameter CriaParameter(DbCommand cmd, IChave info)
        {
            // Cria o Parâmetro e add seu valores
            DbParameter param = cmd.CreateParameter();

            param.ParameterName = String.IsNullOrEmpty(info.Parametro) ? info.Campo : info.Parametro;

            switch (info.Tipo)
            {
                case Tipo.BOOL:
                    param.DbType = DbType.Boolean;
                    break;
                case Tipo.DATA:
                    param.DbType = DbType.DateTime;
                    break;
                case Tipo.DOUBLE:
                case Tipo.MONEY:
                    param.DbType = DbType.Decimal;
                    break;
                case Tipo.ENUM:
                case Tipo.INT:
                    param.DbType = DbType.Int32;
                    break;
                case Tipo.GUID:
                    // Para SQLite armazenar em BLOB (16 bytes). Para outros manter GUID nativo.
                    if (staticBd != null && staticBd.TipoDB == TipoDB.SQLite)
                        param.DbType = DbType.Binary;
                    else
                        param.DbType = DbType.Guid;
                    break;
                case Tipo.HORA:
                    param.DbType = DbType.Time;
                    break;
                case Tipo.BLOB:
                    param.DbType = DbType.Binary;
                    break;
                default:
                    param.DbType = DbType.String;
                    break;
            }

            object valor = info.Valor ?? DBNull.Value;

            // Normalização específica para GUID
            if (info.Tipo == Tipo.GUID && valor != DBNull.Value)
            {
                if (staticBd != null && staticBd.TipoDB == TipoDB.SQLite)
                {
                    // Garantir byte[16]
                    if (valor is Guid g)
                        valor = g.ToByteArray();
                    else if (valor is string s && Guid.TryParse(s, out var gParsed))
                        valor = gParsed.ToByteArray();
                    else if (valor is byte[] b)
                    {
                        if (b.Length != 16)
                            throw new InvalidCastException($"GUID em formato inválido (tamanho {b.Length})");
                        // mantém
                    }
                }
                else
                {
                    // Outros bancos: garantir Guid
                    if (valor is string s && Guid.TryParse(s, out var gParsed))
                        valor = gParsed;
                    else if (valor is byte[] b && b.Length == 16)
                        valor = new Guid(b);
                }
            }

            switch (info.Operador)
            {
                case Operador.COMECAcom:
                    param.Value = valor == DBNull.Value ? valor : $"{valor}%";
                    break;
                case Operador.CONTÉM:
                    param.Value = valor == DBNull.Value ? valor : $"%{valor}%";
                    break;
                case Operador.TERMINAcom:
                    param.Value = valor == DBNull.Value ? valor : $"%{valor}";
                    break;
                default:
                    param.Value = valor;
                    break;
            }

            // Retorna o Parâmetro criado
            return param;
        }


        #region Where
        public static IEnumerable<Chave> WhereKeyOrID(object objeto)
        {
            List<Chave> chaves = new List<Chave>();
            List<Chave> todosOsCampos = new List<Chave>();

            // --------------- Extrair de Colunas de autoincremento ---------------
            List<ColumnTable> campos = BDTools.Campos(objeto);
            var c1 = campos.FirstOrDefault(m => m?.Valor != null && m.IsAutoIncrement == true && 0 < Conversores.ToInt(m.Valor));
            if (c1 != null)
                chaves.Add(new Chave
                {
                    Campo = c1.Campo,
                    Valor = c1.Valor,
                    Tipo = c1.Tipo
                });
            // --------------- Fim de "auto" -----------------


            // ----------------- Extrair KeyAttribute ------------------
            if (chaves.Count == 0)
            {
                foreach (var c in campos)
                {
                    if (!c.BDIgnorar)
                        todosOsCampos.Add(c);
                    if (c.IsKey)
                        chaves.Add(c);
                }
            }
            // ---------------- Fim de KeyAttribute --------------------


            if (chaves.Count == 0)
                return todosOsCampos;
            return chaves;
        }

        public static IEnumerable<Chave> WhereKeyOrID(IEnumerable<ColumnTable> colunas)
        {
            List<Chave> chaves = new List<Chave>();
            List<Chave> todosOsCampos = new List<Chave>();

            // --------------- Extrair de Colunas de autoincremento ---------------
            var c1 = colunas.FirstOrDefault(m => m?.Valor != null && m.IsAutoIncrement == true && 0 < Conversores.ToInt(m.Valor));
            if (c1 != null)
                chaves.Add(new Chave
                {
                    Campo = c1.Campo,
                    Valor = c1.Valor,
                    Tipo = c1.Tipo
                });
            // --------------- Fim de "auto" -----------------


            // ----------------- Extrair KeyAttribute ------------------
            if (chaves.Count == 0)
            {
                foreach (var c in colunas)
                {
                    if (!c.BDIgnorar)
                        todosOsCampos.Add(c);
                    if (c.IsKey)
                        chaves.Add(c);
                }
            }
            // ---------------- Fim de KeyAttribute --------------------


            if (chaves.Count == 0)
                return todosOsCampos;
            return chaves;
        }


        public string WhereExpression(Chave campo)
        {
            string? parametroNome = string.IsNullOrEmpty(campo.Parametro) ? campo.Campo : campo.Parametro;

            StringBuilder s = new StringBuilder();
            if (!string.IsNullOrEmpty(campo.Tabela))
            {
                s.Append(_bd.DBConfig.OpenName);
                s.Append(campo.Tabela);
                s.Append(_bd.DBConfig.CloseName);
                s.Append(".");
            }
            s.Append(_bd.DBConfig.OpenName);
            s.Append(campo.Campo);
            s.Append(_bd.DBConfig.CloseName);

            if (campo.Valor == null)
            {
                s.Append(" IS NULL");
                return s.ToString();
            }


            switch (campo.Operador)
            {
                case Operador.COMECAcom:
                case Operador.CONTÉM:
                case Operador.TERMINAcom:
                    // SQL -> LIKE '%' + @Descricao + '%'
                    // MySQL -> LIKE CONCAT('%',@Descricao,'%')
                    // SQLite -> LIKE '%' || @Descricao || '%'
                    s.Append($" LIKE @{parametroNome}  COLLATE NOCASE");

                    //s.Append(" LIKE ");
                    ////if (ContextoConfiguracoes.TipoDB == TipoDB.MySQL)
                    //s.Append("CONCAT(");
                    //if (campo.Operador == Operador.COMECAcom || campo.Operador == Operador.CONTÉM)
                    //    s.Append("'%'");
                    ////if (ContextoConfiguracoes.TipoDB == TipoDB.MySQL)
                    //s.Append(",");
                    //s.Append($"@{parametroNome}");
                    ////if (ContextoConfiguracoes.TipoDB == TipoDB.MySQL)
                    //s.Append(",");
                    //if (campo.Operador == Operador.TERMINAcom || campo.Operador == Operador.CONTÉM)
                    //    s.Append("'%'");
                    ////if (ContextoConfiguracoes.TipoDB == TipoDB.MySQL)
                    //s.Append(")");
                    break;
                case Operador.MAIORouIGUALque:
                    s.Append($" >= @{parametroNome}");
                    break;
                case Operador.MAIORque:
                    s.Append($" > @{parametroNome}");
                    break;
                case Operador.MENORouIGUALque:
                    s.Append($" <= @{parametroNome}");
                    break;
                case Operador.MENORque:
                    s.Append($" < @{parametroNome}");
                    break;
                case Operador.IGUAL:
                default:
                    s.Append($" = @{parametroNome}");
                    break;
            }
            return s.ToString();
        }
        public string WhereWithParameters(object obj, bool ou = false)
        {
            return WhereWithParameters(WhereKeyOrID(obj), ou);
        }
        public string WhereWithParameters(IEnumerable<ColumnTable> colunas, bool ou = false)
        {
            return WhereWithParameters(WhereKeyOrID(colunas), ou);
        }

        public string WhereWithParameters(IEnumerable<Chave> campos, bool ou = false)
        {
            IEnumerable<Chave> chaves = campos;
            if (chaves == null || !chaves.Any())
                return String.Empty;

            StringBuilder s = new(" WHERE ");
            int cont = 0;
            foreach (var c in chaves)
            {
                if (cont > 0)
                    s.Append(ou ? " OR " : " AND ");
                s.Append(WhereExpression(c));
                cont++;
            }
            return s.ToString();
        }
        #endregion
    }
}
