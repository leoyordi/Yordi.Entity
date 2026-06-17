using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Equivalente a <see cref="RepositorioGenerico{T}"/>, porém retornando sempre
    /// <see cref="Result{T}"/>. Oferece os mesmos atalhos de consulta (busca textual e faixa de
    /// <c>Auto</c>), encapsulados.
    /// <para>
    /// O <see cref="RepositorioGenerico{T}"/> "cru" permanece inalterado — quem quiser o retorno
    /// encapsulado usa esta classe. O SQL continua na base (<see cref="RepositorioAsyncAbstract{T}"/>);
    /// aqui só há montagem de critérios (<see cref="Chave"/>) e delegação para as consultas herdadas.
    /// </para>
    /// </summary>
    public class RepositorioGenericoResult<T> : RepositorioResult<T> where T : class
    {
        public RepositorioGenericoResult(IBDConexao bd) : base(bd) { }

        /// <summary>
        /// Busca textual (apenas para classes <c>IDescricao</c> + <c>IAuto</c>):
        /// vazio = lista completa; numérico = igualdade por <c>Auto</c>; texto = <c>CONTÉM</c> em <c>Descricao</c>.
        /// </summary>
        public Task<Result<IEnumerable<T>>> Lista(string procuraPor)
        {
            if (!(typeof(T).IsOfGenericType(typeof(IDescricao)) && typeof(T).IsOfGenericType(typeof(IAuto))))
                return Task.FromResult(Result<IEnumerable<T>>.NaoEncontrado($"{typeof(T).Name} não implementa IDescricao+IAuto"));

            if (string.IsNullOrEmpty(procuraPor))
                return Lista(); // Lista() encapsulada, herdada de RepositorioResult<T>

            var c = new Chave { Tipo = Tipo.STRING, Valor = procuraPor };
            if (int.TryParse(procuraPor, out _))
            {
                c.Operador = Operador.IGUAL;
                c.Campo = "Auto";
                c.Tipo = Tipo.INT;
            }
            else
            {
                c.Operador = Operador.CONTÉM;
                c.Campo = "Descricao";
            }
            return Lista(new List<Chave> { c }); // Lista(keys) encapsulada, herdada
        }

        /// <summary>Faixa de <c>Auto</c> em [<paramref name="inicial"/>, <paramref name="final"/>] (apenas para classes <c>IAuto</c>).</summary>
        public Task<Result<IEnumerable<T>>> PorAutoMinMax(int inicial, int final)
        {
            if (!typeof(T).IsOfGenericType(typeof(IAuto)))
                return Task.FromResult(Result<IEnumerable<T>>.NaoEncontrado($"{typeof(T).Name} não implementa IAuto"));

            var chaves = new List<Chave>
            {
                new Chave { Campo = "Auto", Operador = Operador.MAIORouIGUALque, Parametro = "Inicial", Tipo = Tipo.INT, Valor = inicial },
                new Chave { Campo = "Auto", Operador = Operador.MENORouIGUALque, Parametro = "Final",   Tipo = Tipo.INT, Valor = final },
            };
            return Lista(chaves); // Lista(keys) encapsulada, herdada
        }
    }
}
