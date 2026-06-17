using System.Data.Common;
using Yordi.EntityMultiSQL;
using Yordi.Tools;
using Xunit;

namespace Yordi.EntityMultiSQL.Tests
{
    /// <summary>
    /// Repositório de teste que exercita o motor de classificação (<c>Executar</c>) sem tocar no
    /// banco: registra opcionalmente um erro (como a base faz em timeout de lock / "database is
    /// locked") e devolve null — o desfecho resultante é o que o <see cref="RepositorioResult{T}"/>
    /// produziria para qualquer operação.
    /// </summary>
    public class RepoSimulado : RepositorioResult<ItemConflito>
    {
        public RepoSimulado(IBDConexao bd) : base(bd) { }

        public Task<Result<ItemConflito>> SimularAtualizar(Exception? erro)
        {
            Func<Task<ItemConflito?>> op = () =>
            {
                if (erro != null) Error(erro);   // dispara ExceptionEvent, como a base
                return Task.FromResult<ItemConflito?>(null);
            };
            return Executar(op, DeObjeto);
        }
    }

    public class ResultadoTests
    {
        // ── Bloqueado ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Atualizar_QuandoLockNaoObtido_RetornaBloqueado()
        {
            using var conexao = NovaConexaoTemp();
            // Parte 2: timeout do lock é sinalizado por BloqueioException
            var repo = new RepoSimulado(conexao);

            var r = await repo.SimularAtualizar(new BloqueioException("Timeout ao aguardar lock de escrita"));

            Assert.Equal(StatusOperacao.Bloqueado, r.Status);
            Assert.True(r.Bloqueou);
            Assert.False(r.Falhou);     // bloqueio transitório ≠ erro permanente
            Assert.False(r.Sucesso);
        }

        [Fact]
        public async Task Atualizar_QuandoDatabaseIsLocked_RetornaBloqueado()
        {
            using var conexao = NovaConexaoTemp();
            // Parte 1: "database is locked" reconhecido por SQLiteRetryHelper.IsDatabaseLocked
            var repo = new RepoSimulado(conexao);

            var r = await repo.SimularAtualizar(new Exception("SQL logic error - database is locked"));

            Assert.Equal(StatusOperacao.Bloqueado, r.Status);
            Assert.True(r.Bloqueou);
        }

        [Fact]
        public async Task Atualizar_QuandoErroComum_RetornaErroEnaoBloqueado()
        {
            using var conexao = NovaConexaoTemp();
            var repo = new RepoSimulado(conexao);

            var r = await repo.SimularAtualizar(new Exception("UNIQUE constraint failed"));

            Assert.Equal(StatusOperacao.Erro, r.Status);
            Assert.True(r.Falhou);
            Assert.False(r.Bloqueou);
            Assert.NotNull(r.Erro);
        }

        // ── Sucesso / NaoEncontrado (integração com banco real) ───────────────

        [Fact]
        public async Task Item_QuandoExiste_RetornaSucessoComValor()
        {
            using var conexao = NovaConexaoTemp();
            await PrepararTabelaComUmRegistro(conexao);

            var repo = new RepositorioResult<ItemConflito>(conexao);
            var r = await repo.Item(new ItemConflito { Codigo = 10 });

            Assert.Equal(StatusOperacao.Sucesso, r.Status);
            Assert.True(r.Sucesso);
            Assert.True(r.TemValor);
            Assert.Equal("X", r.Valor!.Nome);
        }

        [Fact]
        public async Task Item_QuandoNaoExiste_RetornaNaoEncontrado()
        {
            using var conexao = NovaConexaoTemp();
            await PrepararTabelaComUmRegistro(conexao);

            var repo = new RepositorioResult<ItemConflito>(conexao);
            var r = await repo.Item(new ItemConflito { Codigo = 999 });

            Assert.Equal(StatusOperacao.NaoEncontrado, r.Status);
            Assert.False(r.Sucesso);
            Assert.False(r.TemValor);
            Assert.False(r.Falhou);     // ausência de resultado ≠ erro
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

        private static async Task PrepararTabelaComUmRegistro(BDConexao conexao)
        {
            var conn = await conexao.ObterConexaoAsync();
            await ExecutarAsync(conn, "CREATE TABLE IF NOT EXISTS ItemConflito (Codigo INTEGER PRIMARY KEY, Nome TEXT);");
            await ExecutarAsync(conn, "DELETE FROM ItemConflito;");
            await ExecutarAsync(conn, "INSERT INTO ItemConflito (Codigo, Nome) VALUES (10, 'X');");
        }

        private static async Task ExecutarAsync(DbConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
