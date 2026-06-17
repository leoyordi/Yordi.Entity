# Yordi.EntityMultiSQL

Framework .NET para mapeamento POCO → SQL, CRUD assíncrono, criação/atualização de tabelas e gerenciamento automático de índices (incluindo índices parciais).

## ⚠️ Posicionamento atual da biblioteca

> **Importante:** a biblioteca **não é mais especializada para MySQL**.  
> Apesar do nome `MultiSQL`, o design atual está orientado a um núcleo multibanco, com foco prático em SQLite e MySQL no runtime de conexão.

---

## Principais recursos

- CRUD assíncrono com repositórios genéricos
- Resultado encapsulado por operação (`Result<T>` / `RepositorioResult<T>`) — distingue sucesso, não-encontrado, conflito, bloqueio e erro
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

## Resultado encapsulado (`Result<T>`)

Os métodos do repositório retornam `Task<T?>`, `bool` ou `int`. Esse retorno "cru" não distingue, por exemplo, *"não encontrei"* de *"deu erro"*, nem *"0 linhas afetadas"* de *"falha"* — a informação ficava apenas no campo `Mensagem` (compartilhado e sujeito a corrida em uso concorrente).

`RepositorioResult<T>` é uma **classe-base** que herda de `RepositorioAsyncAbstract<T>` e **oculta** (via `new`) os métodos públicos de dados, trocando o retorno cru (`Task<T?>` / `bool` / `int`) por um `Result<T>` explícito. Toda a lógica de SQL permanece na base; aqui só há a tradução do desfecho, capturando erros pelos eventos da instância (sem parsing de string). Use instanciando-a diretamente ou herdando dela.

### Status possíveis (`StatusOperacao`)

| Status | Significado |
|---|---|
| `Sucesso` | operou e teve efeito (achou / inseriu / atualizou) |
| `NaoEncontrado` | executou sem erro, mas sem resultado (SELECT vazio, 0 linhas afetadas) |
| `Conflito` | esperava um único registro e o `WHERE` casou com mais de um |
| `Bloqueado` | bloqueio transitório (timeout de lock ou `database is locked`) — candidato a retry |
| `Erro` | exceção do banco ou de mapeamento |

### Uso

```csharp
var repo = new RepositorioResult<MinhaEntidade>(conexao);

var r = await repo.Item(new MinhaEntidade { Codigo = "ABC" });
switch (r.Status)
{
    case StatusOperacao.Sucesso:       Usar(r.Valor!);       break;
    case StatusOperacao.NaoEncontrado: Avisar("não existe"); break;
    case StatusOperacao.Bloqueado:     AgendarRetry();       break; // transitório
    case StatusOperacao.Erro:          Logar(r.Erro);        break; // r.Erro traz SQL e parâmetros em .Data
}

// atalhos:  r.Sucesso · r.Falhou · r.Conflitou · r.Bloqueou · r.TemValor
// dados:    r.Valor · r.LinhasAfetadas · r.Mensagem · r.Erro
```

Em `Conflito`, `r.LinhasAfetadas` traz a quantidade de registros que casaram o critério. Em falhas, `r.Erro` preserva o contexto de diagnóstico (SQL e parâmetros em `Exception.Data`).

> **Concorrência:** a captura de erro assina os eventos da instância **durante** a chamada. Use uma instância por operação lógica (o tempo de vida *transient*/*scoped* normal de um repositório); não compartilhe a mesma instância entre operações concorrentes.

A API "crua" (`RepositorioAsyncAbstract<T>` / `RepositorioGenerico<T>`) permanece inalterada — quem quiser o retorno encapsulado usa as classes `*Result`. A adoção é incremental.

### Herdeiros especializados

`RepositorioGenericoResult<T>` é o equivalente encapsulado de `RepositorioGenerico<T>`: mesmos atalhos (`Lista(string)`, `PorAutoMinMax`), agora retornando `Result<T>`.

```csharp
public class ClienteRepo : RepositorioGenericoResult<Cliente>
{
    public ClienteRepo(IBDConexao bd) : base(bd) { }
}

Result<IEnumerable<Cliente>> r = await new ClienteRepo(conexao).Lista("silva");
```

> **Caveat do `new` hiding:** a ocultação não é polimórfica. Se você segurar a instância por uma referência do tipo `RepositorioAsyncAbstract<T>`, chama as versões cruas; pelo tipo concreto (ou `RepositorioResult<T>`), vem `Result<T>`. Repositórios devem *adicionar* métodos, não sobrescrever o CRUD.

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

- **1.3.0**
  - **Novo:** resultado encapsulado `Result<T>` + `StatusOperacao` (`Sucesso`, `NaoEncontrado`, `Conflito`, `Bloqueado`, `Erro`) — elimina a ambiguidade de `null`/`false`/`0` e a corrida do campo `Mensagem` compartilhado
  - **Novo:** `RepositorioResult<T>` — classe-base que herda de `RepositorioAsyncAbstract` e oculta (via `new`) os métodos públicos, retornando sempre `Result<T>`; o SQL permanece único na base
  - **Novo:** `RepositorioGenericoResult<T>` — equivalente encapsulado de `RepositorioGenerico<T>` (atalhos `Lista(string)`/`PorAutoMinMax` retornando `Result<T>`)
  - **Novo:** `ConflitoException` (`AtualizarOuIncluir` com `WHERE` ambíguo) e `BloqueioException` (timeout de lock), classificadas como `Conflito`/`Bloqueado`; `database is locked` do SQLite também é reconhecido como `Bloqueado`, permitindo retry
  - **Novo:** projeto de testes xUnit `Yordi.EntityMultiSQL.Tests` (cobre `Conflito`, `Bloqueado`, `Erro`, `Sucesso` e `NaoEncontrado`)
  - **Mudança de comportamento:** timeout de lock agora dispara `ExceptionEvent` (antes `MessageEvent`); a API existente permanece compatível
  - dependência: `Yordi.Tools` 1.0.22
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
