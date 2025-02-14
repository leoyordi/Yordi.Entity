namespace Yordi.EntityMultiSQL
{
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
