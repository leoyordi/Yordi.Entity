using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// TODO: desenvolver �nica classe criadora de express�es SQL.
    /// Atualmente temos em uso a baseada em T (classes gen�ricas) e uma experimental baseada em Type
    /// Em uso tamb�m temos classes que gerenciam tabelas: 
    /// 1. TableCheck, baseado em T
    /// 2. TableCheckByType, baseado em Type. Esse foi um bom come�o
    /// Em teoria o que precisa ser feito?
    /// A classe RepositorioAsyncAbstract � a �nica que est� sendo atualmente mantida. 
    /// Todas as corre��es e melhorias tem sido feitas nela. 
    /// A classe RepositorioBaseByType foi criada uma vez e n�o foi mantida, portanto, est� desatualizada.
    /// Ent�o, devo transformar a RepositorioAsyncAbstract<typeparamref name="T"/>, RepositorioBaseAbstract<T> e CommonBaseAbstract<T>
    /// em classes com base em Type. Com as classes que herdam dessas 3, poderei fazer conforme o modelo abaixo.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    //public class RepositorioGenericoII<T> : RepositorioBaseByType where T : class
    //{
    //    public RepositorioGenericoII(IBDConexao bd) : base(bd, typeof(T))
    //    {

    //    }
    //}
    public class RepositorioGenerico<T> : RepositorioAsyncAbstract<T> where T : class
    {
        public RepositorioGenerico(IBDConexao bd) : base(bd)
        {
        }

        /// <summary>
        /// S� funciona para classes que s�o IBasico
        /// </summary>
        /// <param name="procuraPor"></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>?> Lista(string procuraPor)
        {
            if (typeof(T).IsOfGenericType(typeof(IDescricao)) && typeof(T).IsOfGenericType(typeof(IAuto)))
            {
                Chave c = new Chave();
                if (string.IsNullOrEmpty(procuraPor))
                    return await base.Lista();

                c.Tipo = Tipo.STRING;
                c.Valor = procuraPor;
                int i = 0;
                if (int.TryParse(procuraPor, out i))
                {
                    c.Operador = Operador.IGUAL;
                    c.Campo = "Auto";
                    c.Tipo = Tipo.INT;
                }
                else
                {
                    c.Operador = Operador.CONT�M;
                    c.Campo = "Descricao";
                }
                return await base.Lista(new List<Chave>() { c });
            }
            return null;
        }

        public virtual async Task<IEnumerable<T>?> PorAutoMinMax(int inicial, int final)
        {
            if (typeof(T).IsOfGenericType(typeof(IAuto)))
            {
                Chave ci = new Chave
                {
                    Campo = "Auto",
                    Operador = Operador.MAIORouIGUALque,
                    Parametro = "Inicial",
                    Tipo = Tipo.INT,
                    Valor = inicial // .Date //apenas para data para o banco acrescentar "00:00:00" no final
                };
                Chave cf = new Chave
                {
                    Campo = "Auto",
                    Operador = Operador.MENORouIGUALque,
                    Parametro = "Final",
                    Tipo = Tipo.INT,
                    Valor = final
                };

                return await base.Lista(new List<Chave>() { ci, cf });
            }
            return null;
        }


    }

}
