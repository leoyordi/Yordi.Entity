using System.Data;
using System.Data.Common;
using System.Text;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{

    public class TableCheckBase : EventBaseClass
    {
        private readonly IBDConexao _bd;
        protected internal readonly BDTools _bdTools;
        private protected string? _tableName;
        public TableCheckBase(IBDConexao bd)
        {
            _bd = bd;
            _bdTools = new BDTools(bd);
        }

        public bool DBConectado
        {
            get
            {
                if (_bd == null) return false;
                if (!_bd.Conectado)
                    return _bd.IsServerConnectedAsync().Result;
                return _bd.Conectado;
            }
        }

        #region Tabelas

        internal string FieldsSQLDefinition(ColumnTable coluna, bool allowCurrentTimeStamp)
        {
            StringBuilder s = new StringBuilder();
            switch (coluna.Tipo)
            {
                case Tipo.BOOL:
                    if (_bd.TipoDB == TipoDB.MySQL)
                        s.Append("tinyint(1) ");
                    else if (_bd.TipoDB == TipoDB.SQLite)
                        s.Append("INTEGER ");
                    if (!coluna.PermiteNulo)
                    {
                        if (coluna.ValorPadrao != null)
                            s.Append($"DEFAULT {coluna.ValorPadrao} ");
                        else
                        {
                            s.Append("DEFAULT ");
                            if (_bd.TipoDB == TipoDB.MySQL)
                                s.Append("0 ");
                            else
                                s.Append("(0) ");
                        }
                    }
                    break;
                case Tipo.DATA:
                    s.Append("DATETIME ");
                    if (allowCurrentTimeStamp)
                    {
                        if (!coluna.PermiteNulo || coluna.AutoInsertDate)
                            s.Append("DEFAULT CURRENT_TIMESTAMP ");
                    }
                    if (_bd.TipoDB == TipoDB.MySQL && allowCurrentTimeStamp)
                    {
                        if (coluna.AutoUpdateDate)
                            s.Append(" ON UPDATE CURRENT_TIMESTAMP(0)");
                    }
                    break;
                case Tipo.DOUBLE:
                    if (_bd.TipoDB == TipoDB.MySQL)
                        s.Append("DECIMAL");
                    else if (_bd.TipoDB == TipoDB.SQLite)
                        s.Append("NUMERIC");//TANTO FAZ O TIPO
                    if (!string.IsNullOrEmpty(coluna.Tamanho))
                        s.Append(coluna.Tamanho);
                    else
                        s.Append("(18, 10)");

                    if (coluna.ValorPadrao != null)
                        s.Append($"DEFAULT {coluna.ValorPadrao} ");

                    s.Append(' ');
                    break;
                case Tipo.MONEY:
                    if (_bd.TipoDB == TipoDB.SQLite)
                        s.Append("NUMERIC (18,4) ");//TANTO FAZ O TIPO
                    else
                        s.Append("DECIMAL(18, 4) ");
                    if (coluna.ValorPadrao != null)
                        s.Append($"DEFAULT {coluna.ValorPadrao} ");
                    break;
                case Tipo.ENUM:
                    if (_bd.TipoDB == TipoDB.MySQL)
                        s.Append("TINYINT ");
                    else if (_bd.TipoDB == TipoDB.SQLite)
                        s.Append("INTEGER ");//TANTO FAZ O TIPO
                    if (coluna.ValorPadrao != null)
                        s.Append($"DEFAULT {coluna.ValorPadrao} ");
                    else
                        s.Append("DEFAULT 0 ");
                    break;
                case Tipo.GUID:
                    s.Append("VARCHAR(36) ");
                    if (coluna.ValorPadrao != null)
                        s.Append($"DEFAULT {coluna.ValorPadrao} ");
                    break;
                case Tipo.HORA:
                    if (_bd.TipoDB == TipoDB.MySQL)
                        s.Append("TIME(0) ");
                    else if (_bd.TipoDB == TipoDB.SQLite)
                        s.Append("DATETIME ");
                    if (coluna.ValorPadrao != null)
                        s.Append($"DEFAULT {coluna.ValorPadrao} ");
                    break;
                case Tipo.INT:
                    if (_bd.TipoDB == TipoDB.MySQL)
                    {
                        s.Append("BIGINT ");
                        if (coluna.IsAutoIncrement)
                            s.Append("PRIMARY KEY AUTO_INCREMENT ");
                    }
                    else if (_bd.TipoDB == TipoDB.SQLite)
                    {
                        s.Append("INTEGER ");
                        //SQLite tem recurso próprio para autoincrement.
                        // Se a coluna for PK, será usado o ROWID do SQLite

                        //if (coluna.IsAutoIncrement)
                        //    s.Append("IDENTITY(1,1) ");
                    }
                    if (coluna.ValorPadrao != null)
                        s.Append($"DEFAULT {coluna.ValorPadrao} ");
                    break;
                case Tipo.BLOB:
                    if (_bd.TipoDB == TipoDB.MySQL)
                        s.Append("MEDIUMBLOB ");
                    else if (_bd.TipoDB == TipoDB.SQLite)
                        s.Append("BLOB ");
                    break;
                case Tipo.STRING:
                default:
                    if (_bd.TipoDB == TipoDB.MySQL)
                    {
                        if (String.IsNullOrEmpty(coluna.Tamanho))
                            s.Append("VARCHAR(255)");
                        else if (coluna.Tamanho.ToUpper().Contains("MAX"))
                            s.Append("LONGTEXT");
                        else
                            s.Append($"VARCHAR{coluna.Tamanho}");
                        s.Append(' ');
                    }
                    else if (_bd.TipoDB == TipoDB.SQLite)
                        s.Append("TEXT COLLATE NOCASE");
                    break;
            }
            return s.ToString();
        }

        internal List<string>? ListaColunas(DbDataReader reader)
        {
            if (!reader.HasRows)
            {
                //Message("Nenhum registro encontrado");
                return null;
            }

            List<string> lista = new List<string>();
            DataTable dt = new DataTable();
            try
            {
                dt.Load(reader);
                if (dt == null)
                {
                    //Message("Nenhum registro encontrado");
                    return null;
                }
                if (dt.Rows.Count == 0)
                {
                    //Message("Nenhum registro encontrado");
                    return null;
                }

                _msg = String.Empty;
                Rows(dt.Rows.Count);
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    var valor = dt.Rows[i][0];
                    object? safeValue = (valor == null || DBNull.Value.Equals(valor)) ? null : Convert.ChangeType(valor, typeof(string));
                    if (safeValue != null && safeValue.ToString() is string s)
                        lista.Add(s);
                    Progresso(i);
                }
            }
            catch (Exception e)
            {
                Error(e);
            }
            return lista;
        }


        #endregion


    }
}
