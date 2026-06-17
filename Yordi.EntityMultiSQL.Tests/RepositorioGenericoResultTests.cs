using System.Data.Common;
using Yordi.EntityMultiSQL;
using Yordi.Tools;
using Xunit;

namespace Yordi.EntityMultiSQL.Tests
{
    /// <summary>Entidade que implementa IAuto + IDescricao, habilitando a busca textual.</summary>
    public class ItemBusca : IAuto, IDescricao
    {
        public int Auto { get; set; }
        public string? Descricao { get; set; }
    }

    public class RepositorioGenericoResultTests
    {
        [Fact]
        public async Task Lista_QuandoTipoNaoImplementaInterfaces_RetornaNaoEncontrado()
        {
            using var conexao = NovaConexaoTemp();
            // ItemConflito não implementa IDescricao+IAuto → guard
            var repo = new RepositorioGenericoResult<ItemConflito>(conexao);

            var r = await repo.Lista("qualquer");

            Assert.Equal(StatusOperacao.NaoEncontrado, r.Status);
            Assert.False(r.Sucesso);
        }

        [Fact]
        public async Task Lista_PorTexto_RetornaSucessoComResultadoEncapsulado()
        {
            using var conexao = NovaConexaoTemp();
            await PrepararTabelaBusca(conexao);
            var repo = new RepositorioGenericoResult<ItemBusca>(conexao);

            var r = await repo.Lista("alp");          // CONTÉM em Descricao (case-insensitive)

            Assert.Equal(StatusOperacao.Sucesso, r.Status);
            Assert.Single(r.Valor!);
            Assert.Equal("Alpha", r.Valor!.First().Descricao);
        }

        [Fact]
        public async Task Lista_Vazia_RetornaListaCompleta()
        {
            using var conexao = NovaConexaoTemp();
            await PrepararTabelaBusca(conexao);
            var repo = new RepositorioGenericoResult<ItemBusca>(conexao);

            var r = await repo.Lista("");

            Assert.Equal(StatusOperacao.Sucesso, r.Status);
            Assert.Equal(2, r.LinhasAfetadas);
        }

        [Fact]
        public async Task PorAutoMinMax_RetornaFaixa()
        {
            using var conexao = NovaConexaoTemp();
            await PrepararTabelaBusca(conexao);
            var repo = new RepositorioGenericoResult<ItemBusca>(conexao);

            var r = await repo.PorAutoMinMax(1, 1);

            Assert.Equal(StatusOperacao.Sucesso, r.Status);
            Assert.Single(r.Valor!);
            Assert.Equal(1, r.Valor!.First().Auto);
        }

        // ── Infra ─────────────────────────────────────────────────────────────

        private static BDConexao NovaConexaoTemp()
        {
            var pasta = Path.Combine(Path.GetTempPath(), "yordi_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);
            var config = new DBConfig
            {
                TipoDB = TipoDB.SQLite,
                Local = pasta,
                Database = "teste.db",
                TryReconnect = 3,
                SecondsWaitToTry = 1,
                UsarSQLiteWALMode = true
            };
            return new BDConexao(config);
        }

        private static async Task PrepararTabelaBusca(BDConexao conexao)
        {
            var conn = await conexao.ObterConexaoAsync();
            await ExecutarAsync(conn, "CREATE TABLE IF NOT EXISTS ItemBusca (Auto INTEGER PRIMARY KEY, Descricao TEXT);");
            await ExecutarAsync(conn, "DELETE FROM ItemBusca;");
            await ExecutarAsync(conn, "INSERT INTO ItemBusca (Auto, Descricao) VALUES (1, 'Alpha'), (2, 'Beta');");
        }

        private static async Task ExecutarAsync(DbConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
