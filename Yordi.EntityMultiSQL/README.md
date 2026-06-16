# Yordi.EntityMultiSQL

Framework .NET para mapeamento POCO → SQL, CRUD assíncrono, criação/atualização de tabelas e gerenciamento automático de índices (incluindo índices parciais).

## ⚠️ Posicionamento atual da biblioteca

> **Importante:** a biblioteca **não é mais especializada para MySQL**.  
> Apesar do nome `MultiSQL`, o design atual está orientado a um núcleo multibanco, com foco prático em SQLite e MySQL no runtime de conexão.

---

## Principais recursos

- CRUD assíncrono com repositórios genéricos
- Criação e atualização de tabelas por reflexão de POCOs
- Gerenciamento automático de índices:
  - simples
  - compostos
  - parciais (`WHERE`)
- Suporte a atributos de mapeamento (`Key`, `Autoincrement`, etc.)
- Tratamento de concorrência no SQLite com:
  - `BusyTimeout`
  - WAL mode (`PRAGMA journal_mode=WAL`)
  - lock de escrita coordenado por semáforo
- Checkpoint manual para cenários de pausa/continuidade de serviço
- Encerramento gracioso de conexão SQLite com checkpoint WAL no shutdown

---

## Novidades da linha 1.2.x

A evolução recente consolidou o comportamento de concorrência e encerramento no SQLite.

### 1) `SQLiteConnectionManager`

Gerencia o ciclo de vida da conexão SQLite:

- criação de conexão com `BusyTimeout` automático
- habilitação idempotente de WAL mode
- conexão por operação: cada chamada a `CriarConexao()` retorna uma nova `SQLiteConnection`; o pool nativo do SQLite reaproveita a conexão nativa — instanciar o objeto .NET é barato
- serialização de escrita com `SemaphoreSlim` estático
- checkpoint manual sem alterar `journal_mode`:
  - **`CheckpointAsync`** (via `CheckpointSQLiteAsync`): `PRAGMA wal_checkpoint(TRUNCATE)` + `PRAGMA shrink_memory`
- dois caminhos de encerramento:
  - **`EncerrarAsync`** (via `Dispose`/`DisposeAsync`): aguarda lock de escrita, `PRAGMA wal_checkpoint(TRUNCATE)`, `PRAGMA shrink_memory`, fechamento e limpeza de pool
  - **`LiberarLocksAsync`** (via `LiberarLocksSQLiteAsync`): limpeza de pool, checkpoint, `PRAGMA journal_mode=DELETE` (remove arquivos `-wal`/`-shm`), segundo checkpoint, `PRAGMA shrink_memory`, fechamento e limpeza de pool

### 2) `IBDConexao` com controle operacional

Além de abrir conexão, agora expõe métodos explícitos para lock/shutdown:

- `AguardarLockEscritaAsync(...)`
- `LiberarLockEscrita()`
- `ResetarConexao()`
- `CheckpointSQLiteAsync()`
- `LiberarLocksSQLiteAsync()`

### 3) `BDConexao` mais resiliente

- delega o comportamento SQLite ao `SQLiteConnectionManager`
- **conexão por operação para SQLite:** cada chamada a `ObterConexaoAsync` cria uma nova `SQLiteConnection` — elimina `ObjectDisposedException` causado por compartilhamento de instância entre threads
- mantém reset de conexão para recuperação em cenários de lock/estado inválido
- suporta descarte síncrono e assíncrono (`Dispose` / `DisposeAsync`)

---

## Requisitos

- .NET 8.0
- `Yordi.Tools` (compatível com a versão definida no projeto)
- `System.Data.SQLite`
- `MySql.Data`

---

## Instalação

```bash
dotnet add package Yordi.EntityMultiSQL
```

---

## Uso rápido

### 1) Configuração (`DBConfig`)

Exemplo típico para SQLite:

```csharp
var config = new DBConfig
{
    TipoDB = TipoDB.SQLite,
    Local = @".\\Database",
    Database = "Topcon.Service.db",
    TryReconnect = 3,
    SecondsWaitToTry = 1,
    UsarSQLiteWALMode = true
};

IBDConexao conexao = new BDConexao(config);
```

### 2) Repositório

```csharp
public class MeuRepositorio : RepositorioGenerico<MinhaEntidade>
{
    public MeuRepositorio(IBDConexao bd) : base(bd) { }
}
```

### 3) Entidade POCO

```csharp
[POCOtoDB(Tipo = POCOType.CADASTRO)]
public class MinhaEntidade
{
    [Autoincrement]
    public int Auto { get; set; }

    [Key]
    public string Codigo { get; set; } = string.Empty;
}
```

---

## Ciclo de vida em Windows Service (OnPause / OnContinue / OnStop)

Exemplo recomendado para SQLite:

```csharp
// OnPause: checkpoint leve, mantém WAL e operação para continuar depois
await conexao.CheckpointSQLiteAsync();

// OnContinue: retoma processamento normal

// OnStop/OnShutdown: encerramento forte + dispose
await conexao.LiberarLocksSQLiteAsync();
await conexao.DisposeAsync();
```

`CheckpointSQLiteAsync` não altera `journal_mode`, portanto é apropriado para pausa temporária.
`LiberarLocksSQLiteAsync` aplica `PRAGMA journal_mode=DELETE`, indicado para encerramento definitivo.

---

## Índices automáticos e parciais

A biblioteca mantém suporte a índices definidos nas entidades via `IPOCOIndexes` e classe `Chave` (`Yordi.Tools`), inclusive com cláusula `WHERE`.

Para detalhes completos:

- `INDEX_MANAGEMENT_DOCUMENTATION.md`

---

## Shutdown gracioso (SQLite / WAL)

Em `Host`, `Worker Service` ou `Windows Service`, finalize explicitamente a conexão no encerramento:

```csharp
await conexao.LiberarLocksSQLiteAsync();
await conexao.DisposeAsync();
```

`LiberarLocksSQLiteAsync` aplica `PRAGMA journal_mode=DELETE`, o que faz o SQLite remover os arquivos auxiliares (`-wal`, `-shm`). `DisposeAsync` executa o checkpoint WAL e limpa os pools de conexão.

---

## Evolução (resumo)

- **1.2.5**
  - **Fix:** `AtualizaValor` lançava `InvalidCastException` ao processar propriedades `DateOnly` mapeadas como `Tipo.DATA` — o cast direto `(DateTime)c.Valor` foi substituído por um guard `is DateOnly`, deixando o valor passar sem modificação para normalização em `CriaParameter`
  - **Fix:** `Objeto(DataRow)` — `TimeOnly` e `TimeSpan` agora reconhecem o formato `"0001-01-01 HH:mm:ss.fff"` gravado pelo `CriaParameter`, usando `DateTime.TryParse` como fallback; formatos tradicionais (`"HH:mm"`, `"HH:mm:ss"`, `"d.HH:mm:ss.fff"`) continuam suportados
- **1.2.4**
  - **Fix:** `CriaParameter` passa a usar `DbType.String` para `Tipo.HORA` — elimina truncamento de segundos e milissegundos causado por `DbType.Time` no `System.Data.SQLite`
  - **Novo:** `TimeSpan` e `TimeOnly` são serializados como `"0001-01-01 HH:mm:ss.fff"`, formato DATETIME-like compatível com as funções nativas do SQLite (`time()`, `strftime()`, comparações e ordenação)
  - **Novo:** `DateOnly` é serializado como `"yyyy-MM-dd"` com `DbType.String` explícito, evitando conflito com `DbType.DateTime` que seria atribuído pelo mapeamento `Tipo.DATA` após atualização do `Yordi.Tools`
  - **Compatibilidade:** o formato ISO 8601 TEXT é lexicograficamente ordenável — `ORDER BY` em colunas de data/hora funciona corretamente; sem quebra para tabelas já existentes gravadas pelo driver
- **1.2.3**
  - **Fix:** verificação de existência da coluna no `DataRow` antes de acessar — evita `ArgumentException` silenciado para propriedades sem coluna correspondente
  - **Fix:** tipo valor não-anulável (`int`, `bool`, etc.) com valor nulo no banco mantém o `default` do tipo em vez de lançar `ArgumentException`
  - **Fix:** conversão UTC→Local para campos de auditoria (`DataInclusao`/`DataAlteracao`) reativada e corrigida — respeita `DateTime.Kind` para evitar dupla conversão quando o driver já retorna `Local` (comportamento padrão do `System.Data.SQLite`)
  - **Fix:** `long→int` com `checked` cast para detectar overflow; `bool` tratado via comparação com `0`/`1` (padrão SQLite)
  - **Fix:** condição invertida em `Datas()` que impedia lançar `ArgumentException` quando `T` não herdava de `CommonColumns`
  - **Novo:** suporte a `DateOnly`, `TimeOnly`, `TimeSpan` e `DateTimeOffset` no mapeamento — converte a partir de `string`, `DateTime`, `TimeSpan` e valores numéricos Unix timestamp (`long`/`int`) para total compatibilidade com SQLite
  - **Novo:** cache estático de `PropertyInfo[]` por tipo (`ConcurrentDictionary`) — evita reflexão repetida em grandes volumes de dados
- **1.2.2**
  - atualização e verificação de dependências: MySql.Data 9.7.0, SQLitePCLRaw 3.0.3, System.Data.SQLite 2.0.3, Yordi.Tools 1.0.19
- **1.2.1**
  - adicionado `CheckpointSQLiteAsync` para checkpoint manual sem alterar `journal_mode`
  - documentação de uso para ciclos `OnPause`/`OnContinue`/`OnStop` em serviços
- **1.2.0**
  - consolidação dos recursos de concorrência SQLite
  - melhorias para cenários de `database is locked`
  - gerenciamento de WAL e locks no ciclo de vida da conexão
- **1.1.4**
  - ajustes de dependência (`Chave` em `Yordi.Tools`)
- **1.1.3**
  - **DEPRECATED** (não utilizar)

---

## Contribuição

Contribuições são bem-vindas via issues e pull requests.

## Licença

MIT.

## Autor

Leopoldo Yordi (`leoyordi`).
