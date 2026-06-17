using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Base de repositório que retorna sempre <see cref="Result{T}"/>.
    /// <para>
    /// Herda de <see cref="RepositorioAsyncAbstract{T}"/> e <b>oculta</b> (via <c>new</c>) os
    /// métodos públicos de dados, trocando o retorno cru (<c>T?</c>/<c>bool</c>/<c>int</c>) por um
    /// <see cref="Result{T}"/> explícito. Toda a lógica de SQL permanece na base — aqui só há
    /// tradução do desfecho, capturando erros pelos eventos da instância (sem parsing de string).
    /// </para>
    /// <para>
    /// <b>Caveat do <c>new</c> hiding:</b> a ocultação não é polimórfica e <c>base.X</c> é resolvido
    /// estaticamente para <see cref="RepositorioAsyncAbstract{T}"/>. Se um herdeiro sobrescrever
    /// (override) um método cru, a versão <see cref="Result{T}"/> <b>não</b> enxergará o override —
    /// chamará sempre a implementação da base. Repositórios devem <i>adicionar</i> métodos, não
    /// sobrescrever o CRUD. As chamadas internas da base (ex.: <c>Excluir(T)</c> usando <c>Item</c>)
    /// continuam usando as versões cruas — não há embrulho duplo.
    /// </para>
    /// <para>
    /// <b>Concorrência:</b> a captura de erro assina os eventos da instância <b>durante</b> a chamada.
    /// Use uma instância por operação lógica; não compartilhe entre operações concorrentes.
    /// </para>
    /// </summary>
    public class RepositorioResult<T> : RepositorioAsyncAbstract<T> where T : class
    {
        public RepositorioResult(IBDConexao bd) : base(bd) { }

        #region Consultas
        public new Task<Result<T>> Item(T obj)
        { Func<T, Task<T?>> op = base.Item; return Executar(() => op(obj), DeObjeto); }

        public new Task<Result<T>> Item(int autoIncrement)
        { Func<int, Task<T?>> op = base.Item; return Executar(() => op(autoIncrement), DeObjeto); }

        public new Task<Result<IEnumerable<T>>> Lista()
        { Func<Task<IEnumerable<T>?>> op = base.Lista; return Executar(op, DeLista); }

        public new Task<Result<IEnumerable<T>>> Lista(Func<T, bool> where)
        { Func<Func<T, bool>, Task<IEnumerable<T>?>> op = base.Lista; return Executar(() => op(where), DeLista); }

        public new Task<Result<IEnumerable<T>>> Lista(IEnumerable<Chave> keys, bool ou = false)
        { Func<IEnumerable<Chave>, bool, Task<IEnumerable<T>?>> op = base.Lista; return Executar(() => op(keys, ou), DeLista); }

        public new Task<Result<IEnumerable<T>>> Lista(DateTime inicial, DateTime final)
        { Func<DateTime, DateTime, Task<IEnumerable<T>?>> op = base.Lista; return Executar(() => op(inicial, final), DeLista); }

        public new Task<Result<IEnumerable<T>>> Lista(int[] ids)
        { Func<int[], Task<IEnumerable<T>?>> op = base.Lista; return Executar(() => op(ids), DeLista); }

        public new Task<Result<IEnumerable<T>>> Lista(int[] ids, string campoUnico)
        { Func<int[], string, Task<IEnumerable<T>?>> op = base.Lista; return Executar(() => op(ids, campoUnico), DeLista); }
        #endregion

        #region Escrita unitária
        public new Task<Result<T>> Incluir(T obj)
        { Func<T, Task<T?>> op = base.Incluir; return Executar(() => op(obj), DeObjeto); }

        public new Task<Result<T>> Atualizar(T obj)
        { Func<T, Task<T?>> op = base.Atualizar; return Executar(() => op(obj), DeObjeto); }

        public new Task<Result<T>> AtualizarOuIncluir(T obj)
        { Func<T, Task<T?>> op = base.AtualizarOuIncluir; return Executar(() => op(obj), DeObjeto); }

        public new Task<Result<T>> Upsert(T obj)
        { Func<T, Task<T?>> op = base.Upsert; return Executar(() => op(obj), DeObjeto); }

        public new Task<Result<bool>> Excluir(T obj)
        {
            Func<T, Task<bool>> op = base.Excluir;
            return Executar(() => op(obj), ok => ok
                ? Result<bool>.Ok(true, 1, Mensagem)
                : Result<bool>.NaoEncontrado(Mensagem));
        }
        #endregion

        #region Escrita em lote
        public new Task<Result<IEnumerable<T>>> Incluir(IEnumerable<T> lista)
        { Func<IEnumerable<T>, Task<IEnumerable<T>>> op = base.Incluir; return Executar(() => op(lista), DeLista); }

        public new Task<Result<IEnumerable<T>>> Atualizar(IEnumerable<T> lista)
        { Func<IEnumerable<T>, Task<IEnumerable<T>?>> op = base.Atualizar; return Executar(() => op(lista), DeLista); }

        public new Task<Result<IEnumerable<T>>> AtualizarOuIncluir(IEnumerable<T> lista, bool dispararEventoProgresso = false)
        { Func<IEnumerable<T>, bool, Task<IEnumerable<T>?>> op = base.AtualizarOuIncluir; return Executar(() => op(lista, dispararEventoProgresso), DeLista); }

        public new Task<Result<int>> Excluir(IEnumerable<T> lista)
        {
            Func<IEnumerable<T>, Task<int>> op = base.Excluir;
            return Executar(() => op(lista), n => n > 0
                ? Result<int>.Ok(n, n, Mensagem)
                : Result<int>.NaoEncontrado(Mensagem));
        }
        #endregion

        #region Conversores de desfecho (protected — reutilizáveis por herdeiros especializados)
        protected Result<T> DeObjeto(T? valor)
            => valor is null
                ? Result<T>.NaoEncontrado(Mensagem)
                : Result<T>.Ok(valor, 1, Mensagem);

        protected Result<IEnumerable<T>> DeLista(IEnumerable<T>? valor)
        {
            if (valor is null)
                return Result<IEnumerable<T>>.NaoEncontrado(Mensagem);
            var materializada = valor as ICollection<T> ?? valor.ToList();
            return materializada.Count > 0
                ? Result<IEnumerable<T>>.Ok(materializada, materializada.Count, Mensagem)
                : Result<IEnumerable<T>>.NaoEncontrado(Mensagem);
        }
        #endregion

        /// <summary>
        /// Executa a operação capturando qualquer erro registrado durante ela (via eventos) e
        /// delega a classificação do sucesso (achou? afetou?) para <paramref name="aoConcluir"/>.
        /// <c>protected</c> para que herdeiros especializados embrulhem suas próprias operações.
        /// </summary>
        protected async Task<Result<TOut>> Executar<TRaw, TOut>(
            Func<Task<TRaw>> operacao,
            Func<TRaw, Result<TOut>> aoConcluir)
        {
            Exception? excecao = null;
            string? erroMsg = null;
            void OnException(Exception e, string origem, int line, string path) => excecao ??= e;
            void OnErro(string msg, string origem, int line, string path) => erroMsg ??= msg;

            ExceptionEvent += OnException;
            ErroEvent += OnErro;
            try
            {
                TRaw bruto = await operacao();

                if (excecao is ConflitoException conflito)
                    return Result<TOut>.Conflito(conflito.Quantidade, Mensagem);
                if (excecao is BloqueioException || (excecao is not null && SQLiteRetryHelper.IsDatabaseLocked(excecao)))
                    return Result<TOut>.Bloqueado(Mensagem, excecao);
                if (excecao is not null)
                    return Result<TOut>.DeErro(excecao, Mensagem);
                if (erroMsg is not null)
                    return Result<TOut>.Falha(erroMsg);

                return aoConcluir(bruto);
            }
            catch (Exception e) // rede de segurança: caso algum método propague em vez de registrar
            {
                return Result<TOut>.DeErro(e, Mensagem);
            }
            finally
            {
                ExceptionEvent -= OnException;
                ErroEvent -= OnErro;
            }
        }
    }
}
