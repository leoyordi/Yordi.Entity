namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Registrada quando não foi possível obter o lock de escrita dentro do tempo limite
    /// (situação transitória — normalmente vale a pena tentar a operação novamente).
    /// </summary>
    public class BloqueioException : Exception
    {
        public BloqueioException(string mensagem) : base(mensagem) { }
    }
}
