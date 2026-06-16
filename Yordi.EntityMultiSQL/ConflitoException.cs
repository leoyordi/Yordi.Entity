namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Lançada/registrada quando uma operação que esperava um único registro
    /// encontra mais de um (ex.: AtualizarOuIncluir com WHERE ambíguo).
    /// </summary>
    public class ConflitoException : Exception
    {
        public int Quantidade { get; }
        public ConflitoException(string mensagem, int quantidade) : base(mensagem)
            => Quantidade = quantidade;
    }
}
