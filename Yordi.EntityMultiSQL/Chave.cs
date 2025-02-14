using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public class Chave : IChave
    {
        public string? Campo { get; set; }
        public object? Valor { get; set; }
        public Tipo Tipo { get; set; }

        public Operador Operador { get; set; }

        public string? Parametro { get; set; }
        public string? Tabela { get; set; }
    }

    public interface IChave
    {
        string? Campo { get; set; }
        object? Valor { get; set; }
        Tipo Tipo { get; set; }
        string? Parametro { get; set; }
        Operador Operador { get; set; }
        string? Tabela { get; set; }
    }
}
