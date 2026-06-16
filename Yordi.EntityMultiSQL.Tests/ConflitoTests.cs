using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Yordi.EntityMultiSQL;
using Yordi.Tools;
using Xunit;

namespace Yordi.EntityMultiSQL.Tests
{
    /// <summary>
    /// Entidade mínima cuja cláusula WHERE é a chave [Key] (não-autoincremento),
    /// permitindo que mais de um registro case o mesmo critério.
    /// </summary>
    public class ItemConflito
    {
        [Key]
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
    }

    public class ConflitoTests
    {
        [Fact]
        public async Task AtualizarOuIncluir_QuandoWhereCasaMaisDeUmRegistro_RetornaConflito()
        {
            // Arrange: banco SQLite temporário e isolado por execução
            var pasta = Path.Combine(Path.GetTempPath(), "yordi_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);

            var config = new DBConfig
            {
                TipoDB = TipoDB.SQLite,
                Local = pasta,
                Database = "conflito.db",
                TryReconnect = 3,
                SecondsWaitToTry = 1,
                UsarSQLiteWALMode = true
            };

            await using var conexao = new BDConexao(config);

            // Tabela SEM unique/PK → admite dois registros com a mesma chave
            var conn = await conexao.ObterConexaoAsync();
            await ExecutarAsync(conn, "CREATE TABLE IF NOT EXISTS ItemConflito (Codigo INTEGER, Nome TEXT);");
            await ExecutarAsync(conn, "DELETE FROM ItemConflito;");
            await ExecutarAsync(conn, "INSERT INTO ItemConflito (Codigo, Nome) VALUES (1, 'A'), (1, 'B');");

            var repo = new RepositorioResult<ItemConflito>(new RepositorioGenerico<ItemConflito>(conexao));

            // Act: o WHERE (Codigo = 1) casa dois registros → conflito
            var resultado = await repo.AtualizarOuIncluir(new ItemConflito { Codigo = 1, Nome = "C" });

            // Assert
            Assert.Equal(StatusOperacao.Conflito, resultado.Status);
            Assert.True(resultado.Conflitou);
            Assert.False(resultado.Sucesso);
            Assert.False(resultado.Falhou);                 // conflito não é "Erro" genérico
            Assert.Equal(2, resultado.LinhasAfetadas);      // quantidade de registros conflitantes

            // Cleanup
            try { Directory.Delete(pasta, recursive: true); } catch { /* arquivo pode estar travado pelo SQLite */ }
        }

        private static async Task ExecutarAsync(DbConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
