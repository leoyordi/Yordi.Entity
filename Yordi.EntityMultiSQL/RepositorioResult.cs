using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Camada fina sobre um <see cref="RepositorioAsyncAbstract{T}"/> que converte
    /// o retorno "cru" (null/false/0 + _msg) em um <see cref="Result{T}"/> explícito.
    /// <para>
    /// Não reimplementa nada: delega para o repositório real e, durante cada chamada,
    /// assina <c>ExceptionEvent</c>/<c>ErroEvent</c> para saber com certeza se houve
    /// falha — sem depender do campo compartilhado <c>_msg</c>.
    /// </para>
    /// <para>
    /// <b>Importante:</b> como os eventos são da instância, use um repositório por
    /// operação lógica (o tempo de vida transient/scoped normal de um repositório).
    /// Não compartilhe a mesma instância entre operações concorrentes.
    /// </para>
    /// </summary>
    public class RepositorioResult<T> where T : class
    {
        private readonly RepositorioAsyncAbstract<T> _repo;

        public RepositorioResult(RepositorioAsyncAbstract<T> repo) => _repo = repo;

        /// <summary>Acesso ao repositório subjacente, caso precise de algo não exposto aqui.</summary>
        public RepositorioAsyncAbstract<T> Repositorio => _repo;

        #region Consultas
        public Task<Result<T>> Item(T obj) => Executar(() => _repo.Item(obj), DeObjeto);
        public Task<Result<T>> Item(int autoIncrement) => Executar(() => _repo.Item(autoIncrement), DeObjeto);

        public Task<Result<IEnumerable<T>>> Lista() => Executar(() => _repo.Lista(), DeLista);
        public Task<Result<IEnumerable<T>>> Lista(IEnumerable<Chave> keys, bool ou = false)
            => Executar(() => _repo.Lista(keys, ou), DeLista);
        public Task<Result<IEnumerable<T>>> Lista(DateTime inicial, DateTime final)
            => Executar(() => _repo.Lista(inicial, final), DeLista);
        #endregion

        #region Escrita unitária
        public Task<Result<T>> Incluir(T obj) => Executar(() => _repo.Incluir(obj), DeObjeto);
        public Task<Result<T>> Atualizar(T obj) => Executar(() => _repo.Atualizar(obj), DeObjeto);
        public Task<Result<T>> AtualizarOuIncluir(T obj) => Executar(() => _repo.AtualizarOuIncluir(obj), DeObjeto);
        public Task<Result<T>> Upsert(T obj) => Executar(() => _repo.Upsert(obj), DeObjeto);

        public Task<Result<bool>> Excluir(T obj)
            => Executar(() => _repo.Excluir(obj), ok => ok
                ? Result<bool>.Ok(true, 1, _repo.Mensagem)
                : Result<bool>.NaoEncontrado(_repo.Mensagem));
        #endregion

        #region Escrita em lote
        public Task<Result<IEnumerable<T>>> Incluir(IEnumerable<T> lista) => Executar(() => _repo.Incluir(lista), DeLista);
        public Task<Result<IEnumerable<T>>> Atualizar(IEnumerable<T> lista) => Executar(() => _repo.Atualizar(lista), DeLista);

        public Task<Result<int>> Excluir(IEnumerable<T> lista)
            => Executar(() => _repo.Excluir(lista), n => n > 0
                ? Result<int>.Ok(n, n, _repo.Mensagem)
                : Result<int>.NaoEncontrado(_repo.Mensagem));
        #endregion

        #region Conversores de desfecho
        private Result<T> DeObjeto(T? valor)
            => valor is null
                ? Result<T>.NaoEncontrado(_repo.Mensagem)
                : Result<T>.Ok(valor, 1, _repo.Mensagem);

        private Result<IEnumerable<T>> DeLista(IEnumerable<T>? valor)
        {
            if (valor is null)
                return Result<IEnumerable<T>>.NaoEncontrado(_repo.Mensagem);
            var materializada = valor as ICollection<T> ?? valor.ToList();
            return materializada.Count > 0
                ? Result<IEnumerable<T>>.Ok(materializada, materializada.Count, _repo.Mensagem)
                : Result<IEnumerable<T>>.NaoEncontrado(_repo.Mensagem);
        }
        #endregion

        /// <summary>
        /// Executa a operação capturando qualquer erro registrado durante ela, e
        /// delega a classificação do sucesso (achou? afetou?) para <paramref name="aoConcluir"/>.
        /// </summary>
        private async Task<Result<TOut>> Executar<TRaw, TOut>(
            Func<Task<TRaw>> operacao,
            Func<TRaw, Result<TOut>> aoConcluir)
        {
            Exception? excecao = null;
            string? erroMsg = null;
            void OnException(Exception e, string origem, int line, string path) => excecao ??= e;
            void OnErro(string msg, string origem, int line, string path) => erroMsg ??= msg;

            _repo.ExceptionEvent += OnException;
            _repo.ErroEvent += OnErro;
            try
            {
                TRaw bruto = await operacao();

                if (excecao is ConflitoException conflito)
                    return Result<TOut>.Conflito(conflito.Quantidade, _repo.Mensagem);
                if (excecao is BloqueioException || (excecao is not null && SQLiteRetryHelper.IsDatabaseLocked(excecao)))
                    return Result<TOut>.Bloqueado(_repo.Mensagem, excecao);
                if (excecao is not null)
                    return Result<TOut>.DeErro(excecao, _repo.Mensagem);
                if (erroMsg is not null)
                    return Result<TOut>.Falha(erroMsg);

                return aoConcluir(bruto);
            }
            catch (Exception e) // rede de segurança: caso algum método propague em vez de registrar
            {
                return Result<TOut>.DeErro(e, _repo.Mensagem);
            }
            finally
            {
                _repo.ExceptionEvent -= OnException;
                _repo.ErroEvent -= OnErro;
            }
        }
    }
}
