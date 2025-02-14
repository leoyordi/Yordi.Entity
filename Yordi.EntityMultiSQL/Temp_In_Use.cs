using System.ComponentModel.DataAnnotations;

namespace Yordi.EntityMultiSQL
{
    [POCOtoDB(POCOType.MOVIMENTO)]
    public class Temp_In_Use
    {
        [Key]
        public int ID { get; set; }
    }
}
