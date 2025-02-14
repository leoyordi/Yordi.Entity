using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// TODO: desenvolver única classe criadora de expressões SQL.
    /// Atualmente temos em uso a baseada em T (classes genéricas) e uma experimental baseada em Type
    /// Em uso também temos classes que gerenciam tabelas: 
    /// 1. TableCheck, baseado em T
    /// 2. TableCheckByType, baseado em Type. Esse foi um bom começo
    /// Em teoria o que precisa ser feito?
    /// A classe RepositorioAsyncAbstract é a única que está sendo atualmente mantida. 
    /// Todas as correções e melhorias tem sido feitas nela. 
    /// A classe RepositorioBaseByType foi criada uma vez e não foi mantida, portanto, está desatualizada.
    /// Então, devo transformar a RepositorioAsyncAbstract<typeparamref name="T"/>, RepositorioBaseAbstract<T> e CommonBaseAbstract<T>
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
        /// Só funciona para classes que são IBasico
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
                    c.Operador = Operador.CONTÉM;
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
