namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Desfecho de uma operação de banco. Distingue os três casos que o retorno
    /// "cru" (null/false/0) não consegue separar.
    /// </summary>
    public enum StatusOperacao
    {
        /// <summary>Operou e teve efeito: achou o(s) objeto(s) ou afetou linha(s).</summary>
        Sucesso,
        /// <summary>Executou sem erro, mas não houve resultado: SELECT vazio, WHERE não casou, 0 linhas afetadas.</summary>
        NaoEncontrado,
        /// <summary>Esperava-se um único registro, mas o WHERE casou com mais de um.</summary>
        Conflito,
        /// <summary>Bloqueio transitório: timeout do lock de escrita ou "database is locked" do SQLite. Vale tentar de novo.</summary>
        Bloqueado,
        /// <summary>Houve exceção do banco/mapeamento ou um erro registrado durante a operação.</summary>
        Erro
    }

    /// <summary>
    /// Resultado imutável de uma operação. Diferente do <c>_msg</c> compartilhado,
    /// este objeto viaja junto com o retorno da chamada — sem corrida e sem ambiguidade.
    /// </summary>
    /// <typeparam name="T">Tipo do valor produzido (objeto, lista, bool, int...).</typeparam>
    public readonly struct Result<T>
    {
        public StatusOperacao Status { get; }
        public T? Valor { get; }
        public int LinhasAfetadas { get; }
        public string? Mensagem { get; }
        public Exception? Erro { get; }

        private Result(StatusOperacao status, T? valor, int linhas, string? mensagem, Exception? erro)
        {
            Status = status;
            Valor = valor;
            LinhasAfetadas = linhas;
            Mensagem = mensagem;
            Erro = erro;
        }

        /// <summary>Operou e teve efeito.</summary>
        public bool Sucesso => Status == StatusOperacao.Sucesso;
        /// <summary>Houve erro (exceção ou erro registrado).</summary>
        public bool Falhou => Status == StatusOperacao.Erro;
        /// <summary>Esperava-se um único registro e o WHERE casou com mais de um.</summary>
        public bool Conflitou => Status == StatusOperacao.Conflito;
        /// <summary>Bloqueio transitório (lock/timeout) — candidato a retry.</summary>
        public bool Bloqueou => Status == StatusOperacao.Bloqueado;
        /// <summary>Tem valor utilizável.</summary>
        public bool TemValor => Valor is not null;

        public static Result<T> Ok(T valor, int linhasAfetadas = 1, string? mensagem = null)
            => new(StatusOperacao.Sucesso, valor, linhasAfetadas, mensagem, null);

        public static Result<T> NaoEncontrado(string? mensagem = null)
            => new(StatusOperacao.NaoEncontrado, default, 0, mensagem, null);

        /// <summary>Esperava um único registro e achou <paramref name="quantidade"/> (fica em <c>LinhasAfetadas</c>).</summary>
        public static Result<T> Conflito(int quantidade, string? mensagem = null)
            => new(StatusOperacao.Conflito, default, quantidade, mensagem, null);

        /// <summary>Bloqueio transitório (lock/timeout). A exceção original, quando houver, fica em <c>Erro</c>.</summary>
        public static Result<T> Bloqueado(string? mensagem = null, Exception? erro = null)
            => new(StatusOperacao.Bloqueado, default, 0, mensagem ?? erro?.Message, erro);

        /// <summary>Falha sem exceção associada (erro registrado via <c>Error(string)</c>).</summary>
        public static Result<T> Falha(string mensagem)
            => new(StatusOperacao.Erro, default, 0, mensagem, null);

        /// <summary>Falha com a exceção original (que carrega SQL e parâmetros em <c>Erro.Data</c>).</summary>
        public static Result<T> DeErro(Exception erro, string? mensagem = null)
            => new(StatusOperacao.Erro, default, 0, mensagem ?? erro.Message, erro);

        public override string ToString()
            => $"{Status} | Linhas={LinhasAfetadas} | {Mensagem}";
    }
}
