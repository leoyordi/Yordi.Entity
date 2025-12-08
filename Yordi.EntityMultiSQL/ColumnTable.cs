using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Classe usada para montar instruções SQL, criar tabelas e alterar colunas.<br/>
    /// Os atributos nas propriedades das classes POCO são usados para transformar as propriedades em colunas de tabelas.<br/>
    /// </summary>
    public class ColumnTable : Chave
    {
        public bool IsAutoIncrement { get; set; }
        public bool IsKey { get; set; }
        public bool IsDescription { get; set; }
        public bool PermiteNulo { get; set; }
        public bool BDIgnorar { get; set; }

        public string? Tamanho { get; set; }
        public object? ValorPadrao { get; set; }

        public bool AutoInsertDate { get; set; }
        public bool AutoUpdateDate { get; set; }
        public bool OnlyInsert { get; set; }
        public bool OnlyUpdate { get; set; }

        public override string? ToString()
        {
            return base.Campo ?? base.ToString();
        }
    }
}
