using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public abstract class CommonBaseAbstract<T> : EventBaseClass where T : class
    {
        public virtual List<ColumnTable> Campos()
        {
            T obj = Activator.CreateInstance<T>();
            return BDTools.Campos(obj);
        }
    }
}
