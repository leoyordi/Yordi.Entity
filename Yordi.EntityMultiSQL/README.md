# Yordi.EntityMultiSQL

## Descrição

Yordi.EntityMultiSQL é um framework para criar instruções SQL para SQLite, MySQL e MSSQL. Ele permite realizar operações CRUD, criar tabelas e campos com base nos objetos POCO, além de gerenciar automaticamente índices (incluindo índices parciais com cláusula WHERE).

## Características

- Suporte para SQLite, MySQL e MSSQL
- Operações CRUD (Create, Read, Update, Delete)
- Criação automática de tabelas e campos com base em objetos POCO
- **Gerenciamento automático de índices** (simples, compostos e parciais)
- **Suporte para índices parciais com cláusula WHERE** (SQLite 3.8+ e MySQL 8.0+)
- Suporte para atributos personalizados para controle de mapeamento de colunas
- Detecção e atualização automática de mudanças em estruturas de tabelas
- Suporte para triggers em MySQL
- **🆕 Tratamento automático de "database is locked" no SQLite** (v1.2.0+)
- **🆕 Suporte a WAL mode e BusyTimeout para melhor concorrência** (v1.2.0+)

## Requisitos

- .NET 8.0
- Yordi.Tools 1.0.14+ (classe Chave movida para este pacote)
- SQLite 3.8.0+ (para índices parciais)
- MySQL 8.0.13+ (para índices parciais)

## Instalação

Para instalar o pacote, adicione a seguinte referência ao seu projeto:
```bash
dotnet add package Yordi.EntityMultiSQL
```

## Evolução

- **1.2.0** - 🆕 **Tratamento de "database is locked" no SQLite**
  - Adicionado `SQLiteRetryHelper` para retry automático com backoff exponencial
  - Configuração automática de `BusyTimeout=30000` (30 segundos) na connection string
  - Habilitação automática de `PRAGMA journal_mode=WAL` para melhor concorrência
  - Métodos `ResetarConexao()` e `LiberarLocksSQLiteAsync()` para liberação manual de locks
  - Classe `SQLiteLockStatus` para diagnóstico do estado de bloqueio do banco
- **1.1.4** - Atualização de dependências. Classe `Chave` movida para Yordi.Tools v1.0.14
- **1.1.3** - ⚠️ **DEPRECATED/NÃO UTILIZAR** - Versão com problemas de dependência. Use 1.1.4 ou superior
- **1.1.2** - Correção de bug na criação de tabelas SQLite com campos do tipo Guid. Agora trata como BLOB
- **1.1.1** - Correção de mensagem de log para inclusão de registros com o atributo Verbose
- **1.1.0** - Acréscimo de atributo Verbose, definido em configuração (DBConfig), para descrever em log a maioria dos CRUD (exceto R)
- **1.0.3** - Mudança de biblioteca de comunicação com SQLite. Voltamos para System.Data.SQLite
- **1.0.2** - Correção de bugs
- **1.0.1** - Correção de bugs
- **1.0.0** - Versão inicial

## ⚠️ Nota Importante sobre Versão 1.1.3

A versão **1.1.3 está DEPRECATED e não deve ser utilizada**. Esta versão contém objetos com dependências incorretas que impedem sua aplicabilidade prática. 

**Mudanças na versão 1.1.4:**
- A classe `Chave` foi movida para o pacote **Yordi.Tools v1.0.14**
- Todas as dependências foram corrigidas
- **Utilize sempre a versão 1.1.4 ou superior**

---

## 🆕 Tratamento de "Database is Locked" (v1.2.0+)

A partir da versão 1.2.0, o framework oferece tratamento automático para o erro `database is locked` no SQLite, que ocorre quando múltiplas operações tentam acessar o banco simultaneamente.

### O Problema

O SQLite tem limitações com escrita simultânea. Quando uma conexão mantém um lock por muito tempo, outras operações falham com a mensagem:

```
SQLiteException: database is locked
```

### A Solução

O framework agora implementa três camadas de proteção:

#### 1. BusyTimeout Automático

A connection string do SQLite é automaticamente configurada com `BusyTimeout=30000` (30 segundos), fazendo o SQLite aguardar até 30 segundos antes de falhar com "database is locked".

| Valor | Tempo |
|-------|-------|
| 1000 | 1 segundo |
| 30000 | 30 segundos |
| 60000 | 1 minuto |

#### 2. WAL Mode (Write-Ahead Logging)

O modo WAL é habilitado automaticamente ao abrir a conexão, permitindo melhor concorrência entre leituras e escritas:

```sql
PRAGMA journal_mode=WAL;
```

#### 3. Retry Automático com SQLiteRetryHelper

Quando ocorre um lock, o sistema tenta novamente com backoff exponencial:

```csharp
// Configuração padrão
SQLiteRetryHelper.DefaultMaxRetries = 3;      // Máximo de tentativas
SQLiteRetryHelper.DefaultRetryDelayMs = 500;  // Delay inicial em ms
```

### Uso Manual do SQLiteRetryHelper

Para operações customizadas, você pode usar o helper diretamente:

```csharp
using Yordi.EntityMultiSQL;

// Executar operação com retry automático
var resultado = await SQLiteRetryHelper.ExecuteWithRetryAsync(
    async () => await meuComando.ExecuteNonQueryAsync(),
    maxRetries: 3,
    retryDelayMs: 500,
    onRetry: (tentativa, ex) => Console.WriteLine($"Tentativa {tentativa}: {ex.Message}")
);
```

### Verificar Status de Lock

Para diagnóstico, você pode verificar o status do banco:

```csharp
var status = await SQLiteRetryHelper.VerificarStatusLockAsync(connectionString);

Console.WriteLine(status.Conectado);      // true/false
Console.WriteLine(status.PodeEscrever);   // true/false  
Console.WriteLine(status.JournalMode);    // "wal", "delete", etc.
Console.WriteLine(status.WalBlocked);     // true/false

// Ou simplesmente:
Console.WriteLine(status.ToString());
// Output: "Conectado | Escrita: OK | Journal: wal | WAL Blocked: False | WAL Pages: 0/0"
```

### Liberar Locks Manualmente

Em casos extremos, você pode forçar a liberação de locks:

```csharp
// Via IBDConexao
await conexao.LiberarLocksSQLiteAsync();

// Ou via helper estático
SQLiteRetryHelper.LimparPoolsConexao();
await SQLiteRetryHelper.TentarLiberarLocksAsync(connectionString);
```

### Resetar Conexão

Se a conexão estiver corrompida ou travada:

```csharp
conexao.ResetarConexao();
```

### Métodos que Suportam Retry Automático

Todos os métodos de escrita do `RepositorioAsyncAbstract` agora suportam retry automático para SQLite:

| Método | Retry Automático |
|--------|------------------|
| `Incluir(T obj)` | ✅ |
| `Incluir(IEnumerable<T>)` | ✅ |
| `Atualizar(T obj)` | ✅ |
| `Atualizar(IEnumerable<T>)` | ✅ |
| `AtualizarOuIncluir(T obj)` | ✅ |
| `AtualizarOuIncluir(IEnumerable<T>)` | ✅ |
| `Upsert(T obj)` | ✅ |
| `Excluir(T obj)` | ✅ |
| `Excluir(IEnumerable<T>)` | ✅ |
| `ExecuteSQL(string sql)` | ✅ |

### Compatibilidade com Transações

O retry é compatível com transações. Se você usar `BeginTransaction`, o retry acontece apenas na execução do comando, preservando a transação:

```csharp
using (var transaction = conexao.BeginTransaction())
{
    // O retry acontece aqui, dentro da mesma transação
    await repositorio.Incluir(objeto);
    
    // Se falhar após todas as tentativas, você ainda pode fazer rollback
    await transaction.CommitAsync();
}
```

---

## Uso

### Configuração

Primeiro, configure a conexão com o banco de dados implementando a interface `IBDConexao`:

```csharp
public class MinhaConexao : IBDConexao 
{ 
    // Implementação dos métodos e propriedades da interface IBDConexao 
}
```

### Repositório

Crie uma classe de repositório que herda de `RepositorioAsyncAbstract<T>` ou `RepositorioGenerico`:

```csharp
public class MeuRepositorio : RepositorioGenerico<POCOclass> 
{ 
    public MeuRepositorio(IBDConexao bd) : base(bd) { }
    // Métodos específicos do repositório
}
```

### Entidade

Defina suas entidades POCO com os atributos necessários:

```csharp
[POCOtoDB(Tipo = POCOType.CADASTRO)]
public class POCOclass 
{
    [Autoincrement] 
    public int Id { get; set; }
    
    [Key] 
    public string KeyProperty { get; set; }
    
    // Outros campos
}
```

### Gerenciamento de Índices

Para habilitar o gerenciamento automático de índices, implemente a interface `IPOCOIndexes`:

**Nota:** A classe `Chave` agora está disponível no pacote `Yordi.Tools` (v1.0.14+).

```csharp
using Yordi.Tools; // Chave agora está neste namespace

public class Usuario : IPOCOIndexes
{
    [Key]
    public int Id { get; set; }
    public string Login { get; set; }
    public string Email { get; set; }
    public bool Ativo { get; set; }
    public DateTime UltimoAcesso { get; set; }

    public IEnumerable<Chave> GetIndexes()
    {
        return new List<Chave>
        {
            // Índice simples
            new Chave 
            { 
                Campo = "Login", 
                Parametro = "IX_Usuario_Login" 
            },
            
            // Índice composto
            new Chave 
            { 
                Campo = "Email", 
                Parametro = "IX_Usuario_Email_Ativo" 
            },
            new Chave 
            { 
                Campo = "Ativo", 
                Parametro = "IX_Usuario_Email_Ativo" 
            },
            
            // Índice parcial (apenas usuários ativos)
            new Chave 
            { 
                Campo = "UltimoAcesso", 
                Parametro = "IX_Usuario_UltimoAcesso_Ativos" 
            },
            new Chave 
            { 
                Parametro = "Ativo",      // Campo da condição WHERE
                Valor = true,             // Valor da condição
                Operador = Operador.IGUAL,
                Tipo = Tipo.BOOL
            }
        };
    }
}
```

**SQL Gerado (SQLite):**
```sql
CREATE INDEX IF NOT EXISTS IX_Usuario_Login ON Usuario (Login);
CREATE INDEX IF NOT EXISTS IX_Usuario_Email_Ativo ON Usuario (Email, Ativo);
CREATE INDEX IF NOT EXISTS IX_Usuario_UltimoAcesso_Ativos ON Usuario (UltimoAcesso) WHERE Ativo = 1;
```

### Exemplo Completo

```csharp
using Yordi.EntityMultiSQL;
using Yordi.Tools; // Para usar a classe Chave

class Teste : EventBaseClass
{
    async Task Test()
    {
        IBDConexao conexao = new MinhaConexao();
        IEnumerable<Type> types = conexao.Tabelas; // new List<Type>() { typeof(POCOclass) }
        
        TableCheckByType bllCheckTable = new TableCheckByType(conexao, debug: true);
        
        foreach (var type in types)
        {
            // Cria/atualiza tabela e gerencia índices automaticamente
            if (!await bllCheckTable.CriaTabela(type, false))
                Message($"Verificação da tabela {type.Name} resultou em erro");
        }
        
        var repositorio = new MeuRepositorio(conexao);
        var entidade = new POCOclass { KeyProperty = "Exemplo" }; 
        await repositorio.Insere(entidade);
    }
}
```

## Recursos Avançados

### Índices Parciais

Índices parciais (partial indexes) incluem apenas um subconjunto de linhas baseado em uma condição WHERE. São úteis para:
- Reduzir o tamanho do índice
- Melhorar performance de queries específicas
- Economizar espaço em disco

**Exemplo:**
```csharp
// Índice apenas para pedidos pendentes
new Chave { Campo = "DataPedido", Parametro = "IX_Pedidos_Pendentes" },
new Chave 
{ 
    Parametro = "Status",
    Valor = "Pendente",
    Operador = Operador.IGUAL,
    Tipo = Tipo.STRING
}
```

**SQL Gerado:**
```sql
CREATE INDEX IX_Pedidos_Pendentes ON Pedidos (DataPedido) WHERE Status = 'Pendente';
```

### Gerenciamento Automático

O sistema automaticamente:
- ✅ Cria índices novos quando a tabela é criada ou atualizada
- ✅ Remove índices obsoletos que não estão mais definidos
- ✅ Recria índices quando as colunas são modificadas
- ✅ Suporta múltiplas condições WHERE (AND)
- ✅ Usa formatação SQL correta para cada tipo de banco de dados

Para mais detalhes, consulte a [documentação completa de índices](INDEX_MANAGEMENT_DOCUMENTATION.md).

## Documentação Adicional

- [Gerenciamento de Índices - Documentação Completa](INDEX_MANAGEMENT_DOCUMENTATION.md)

## Contribuição

Contribuições são bem-vindas! Sinta-se à vontade para abrir issues e pull requests no [repositório GitHub](https://github.com/leoyordi/Yordi.Entity).

## Licença

Este projeto está licenciado sob a [MIT License](LICENSE).

## Autores

- Leopoldo Yordi (leoyordi)

## Agradecimentos

Agradecemos a todos os contribuidores e usuários do projeto!
