using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Classe usada para montar instruções SQL, especialmente as relacionadas à clausula WHERE
    /// </summary>
    public class Chave : IChave
    {
        /// <summary>
        /// Nome do campo no banco de dados
        /// </summary>
        public string? Campo { get; set; }
        /// <summary>
        /// Valor do campo
        /// </summary>
        public object? Valor { get; set; }
        /// <summary>
        /// Tipo do campo
        /// </summary>
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
