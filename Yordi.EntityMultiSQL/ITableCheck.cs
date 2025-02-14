using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public interface ITableCheck : IEventBaseClass
    {
        Task<bool> CriaTabela<T>(bool excluirSeExisitir = false) where T : class;
        Task<bool> CriaTabela(Type type, bool excluirSeExisitir = false);
        bool DBConectado { get; }
    }
}
