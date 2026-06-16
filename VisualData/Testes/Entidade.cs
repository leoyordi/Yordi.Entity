using Yordi.Tools;

namespace VisualData
{
    public class Movimento : Basico
    {
        public DateOnly DataSogo { get; set; }
        public TimeOnly HoraSogo { get; set; }

        public DateTime SogoDate { get => new DateTime(DataSogo, HoraSogo); }

        public decimal G1Traco { get; set; }
        public decimal G2Traco { get; set; }
        public decimal G3Traco { get; set; }
        public decimal G4Traco { get; set; }

        public decimal C1Traco { get; set; }

        public decimal D1Traco { get; set; }

        public decimal A1Traco { get; set; }


        public decimal G1Real { get; set; }
        public decimal G2Real { get; set; }
        public decimal G3Real { get; set; }
        public decimal G4Real { get; set; }

        public decimal C1Real { get; set; }

        public decimal D1Real { get; set; }

        public decimal A1Real { get; set; }

        public int GCiclos { get; set; }
        public int CCiclos { get; set; }
        public int DCiclos { get; set; }
        public int ACiclos { get; set; }
        public int CMix { get; set; }
    }
}
