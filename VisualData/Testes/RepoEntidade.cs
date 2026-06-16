using Yordi.EntityMultiSQL;

namespace VisualData
{
    public class RepoEntidade : RepositorioAsyncAbstract<Movimento>
    {
        public RepoEntidade(IBDConexao bd) : base(bd)
        {
        }
    }
}
