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
