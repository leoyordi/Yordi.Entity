using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yordi.EntityMultiSQL
{
    public interface IPOCOIndexes
    {

        IEnumerable<Chave> GetIndexes();

        internal class IndexInfo
        {
            public string IndexName { get; set; } = string.Empty;
            public List<string> Columns { get; set; } = new List<string>();
            public bool IsUnique { get; set; } = false;
            public IEnumerable<Chave> Chaves { get; set; } = Enumerable.Empty<Chave>();

        }
    }
}
