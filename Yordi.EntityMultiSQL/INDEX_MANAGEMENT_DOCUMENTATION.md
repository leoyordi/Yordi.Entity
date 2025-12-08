# Gerenciamento de Índices - Documentação

## Visão Geral

Foi implementado um sistema completo de gerenciamento de índices para tabelas que implementam a interface `IPOCOIndexes`. O sistema automaticamente verifica, cria, atualiza e remove índices no banco de dados MySQL e SQLite, incluindo suporte para **índices parciais com cláusula WHERE**.

## Como Funciona

### 1. Interface IPOCOIndexes

Para habilitar o gerenciamento de índices em uma classe POCO, ela deve implementar a interface `IPOCOIndexes`:

```csharp
public class MinhaClasse : IPOCOIndexes
{
    public int Id { get; set; }
    public string Nome { get; set; }
    public string Email { get; set; }
    public DateTime DataCriacao { get; set; }
    public bool Ativo { get; set; }

    public IEnumerable<Chave> GetIndexes()
    {
        return new List<Chave>
        {
            // Índice simples no campo Email
            new Chave 
            { 
                Campo = "Email", 
                Parametro = "IX_MinhaClasse_Email" 
            },
            
            // Índice composto em Nome e DataCriacao
            new Chave 
            { 
                Campo = "Nome", 
                Parametro = "IX_MinhaClasse_Nome_Data" 
            },
            new Chave 
            { 
                Campo = "DataCriacao", 
                Parametro = "IX_MinhaClasse_Nome_Data" 
            },
            
            // Índice parcial (apenas registros ativos) - SQLite e MySQL 8.0+
            new Chave 
            { 
                Campo = "Email", 
                Parametro = "IX_MinhaClasse_Email_Ativos" 
            },
            new Chave 
            { 
                Parametro = "Ativo",  // Campo da condição WHERE
                Valor = true,         // Valor da condição
                Operador = Operador.IGUAL,
                Tipo = Tipo.BOOL,
                Tabela = null         // Opcional: nome da tabela para junções
            }
        };
    }
}
```

### 2. Propriedades da Classe Chave

Para definir índices, utilize as seguintes propriedades da classe `Chave`:

#### Para Colunas do Índice:
- **Campo**: Nome da coluna que fará parte do índice (obrigatório para colunas)
- **Parametro**: Nome do índice. Se não fornecido, será gerado automaticamente como `IX_{NomeTabela}_{NomeCampo}`

#### Para Cláusulas WHERE (Índices Parciais):
- **Parametro**: Nome do campo da condição WHERE
- **Valor**: Valor da condição
- **Operador**: Operador de comparação (IGUAL, DIFERENTE, MAIOR, MENOR, etc.)
- **Tipo**: Tipo de dados do valor
- **Tabela**: Nome da tabela (opcional, usado em condições com múltiplas tabelas)
- **Campo**: Deve ser **null ou vazio** para identificar como cláusula WHERE

Para criar **índices compostos**, use o mesmo valor de `Parametro` para múltiplas instâncias de `Chave`, cada uma com um `Campo` diferente.

### 3. Estrutura IndexInfo

A classe interna `IndexInfo` armazena informações completas sobre cada índice:

- **IndexName**: Nome exclusivo do índice
- **Columns**: Lista de colunas que compõem o índice
- **IsUnique**: Indica se é um índice único (preparado para implementação futura)
- **Chaves**: Coleção de objetos `Chave` que representam as condições WHERE do índice parcial

### 4. Métodos Implementados

#### GerenciarIndices
Método principal que coordena todo o processo de gerenciamento de índices:
- Verifica se o tipo implementa `IPOCOIndexes`
- Obtém os índices desejados através de `GetIndexes()`
- Constrói a estrutura `IndexInfo` separando colunas de condições WHERE
- Lista os índices existentes no banco de dados
- Identifica índices a criar, atualizar ou remover
- Executa as operações necessárias

#### ConstruirIndicesInfo
Processa a lista de `Chave` retornada por `GetIndexes()` e constrói um dicionário de `IndexInfo`:
- Agrupa chaves pelo nome do índice (`Parametro`)
- Separa colunas do índice (com `Campo` preenchido) das condições WHERE (com `Campo` vazio/null)
- Armazena as condições WHERE na propriedade `Chaves` de cada `IndexInfo`
- Retorna um dicionário com informações completas de cada índice

#### ListarIndicesExistentes
Consulta o banco de dados para obter todos os índices existentes na tabela:
- **MySQL**: Usa `INFORMATION_SCHEMA.STATISTICS`
- **SQLite**: Usa `pragma_index_list` e `pragma_index_info`

**Nota**: A detecção de cláusulas WHERE em índices existentes não está implementada (limitação dos metadados disponíveis).

#### ObterIndicesParaCriar
Compara os índices desejados com os existentes e identifica quais devem ser criados:
- Novos índices que não existem no banco
- Índices existentes com colunas diferentes (serão recriados)

#### ObterIndicesParaRemover
Identifica índices que devem ser removidos:
- Índices que começam com `IX_{NomeTabela}_` mas não estão na lista de índices desejados
- Índices que mudaram sua composição de colunas

#### GerarScriptCriacaoIndice
Gera o script SQL para criar um índice, incluindo cláusula WHERE se especificada:
- **MySQL 8.0+**: `CREATE [UNIQUE] INDEX nome ON tabela (col1, col2) WHERE condição`
- **SQLite 3.8.0+**: `CREATE [UNIQUE] INDEX IF NOT EXISTS nome ON tabela (col1, col2) WHERE condição`

Usa o método `_bdTools.WhereExpression(chave)` para gerar as condições WHERE corretamente formatadas.

#### GerarScriptRemocaoIndice
Gera o script SQL para remover um índice:
- **MySQL**: `DROP INDEX nome ON tabela`
- **SQLite**: `DROP INDEX IF EXISTS nome`

## Índices Parciais (Partial Indexes)

### O que são?

Índices parciais são índices que incluem apenas um subconjunto de linhas de uma tabela, baseado em uma condição WHERE. Eles são úteis para:
- Reduzir o tamanho do índice
- Melhorar performance de queries específicas
- Economizar espaço em disco

### Suporte por Banco de Dados

- **SQLite**: Suportado desde versão 3.8.0 (2013)
- **MySQL**: Suportado desde versão 8.0.13 (2018)
- **PostgreSQL**: Suportado (mas não implementado neste projeto)

### Exemplo de Uso

```csharp
public class Pedido : IPOCOIndexes
{
    public int Id { get; set; }
    public string NumeroPedido { get; set; }
    public DateTime DataPedido { get; set; }
    public string Status { get; set; }
    public decimal Valor { get; set; }

    public IEnumerable<Chave> GetIndexes()
    {
        return new List<Chave>
        {
            // Índice normal em NumeroPedido
            new Chave 
            { 
                Campo = "NumeroPedido", 
                Parametro = "IX_Pedido_Numero" 
            },
            
            // Índice parcial: apenas pedidos pendentes
            // Útil para queries que frequentemente filtram por Status = 'Pendente'
            new Chave 
            { 
                Campo = "DataPedido", 
                Parametro = "IX_Pedido_Data_Pendentes" 
            },
            new Chave 
            { 
                Parametro = "Status",
                Valor = "Pendente",
                Operador = Operador.IGUAL,
                Tipo = Tipo.STRING
            },
            
            // Índice parcial: pedidos de alto valor (> 1000)
            new Chave 
            { 
                Campo = "NumeroPedido", 
                Parametro = "IX_Pedido_Numero_AltoValor" 
            },
            new Chave 
            { 
                Campo = "DataPedido", 
                Parametro = "IX_Pedido_Numero_AltoValor" 
            },
            new Chave 
            { 
                Parametro = "Valor",
                Valor = 1000,
                Operador = Operador.MAIOR,
                Tipo = Tipo.DECIMAL
            }
        };
    }
}
```

**SQL Gerado para SQLite:**
```sql
CREATE INDEX IF NOT EXISTS IX_Pedido_Data_Pendentes 
ON Pedido (DataPedido) 
WHERE Status = 'Pendente';

CREATE INDEX IF NOT EXISTS IX_Pedido_Numero_AltoValor 
ON Pedido (NumeroPedido, DataPedido) 
WHERE Valor > 1000;
```

## Quando os Índices são Gerenciados

O gerenciamento de índices é executado automaticamente nos seguintes cenários:

### 1. Criação de Tabela (CriaTabela)
Após criar uma nova tabela com sucesso, o método `GerenciarIndices` é chamado para criar os índices definidos.

### 2. Alteração de Tabela (AlteraTabela)
Sempre que uma tabela existente é verificada/alterada, os índices são atualizados:
- Índices faltantes são criados
- Índices obsoletos são removidos
- Índices com colunas diferentes são recriados

## Características

### Suporte Multi-Database
- **MySQL 8.0+**: Suporte completo incluindo índices parciais
- **SQLite 3.8.0+**: Suporte completo incluindo índices parciais
- **MSSQL**: Estrutura preparada para implementação futura (índices filtrados)

### Índices Compostos
Múltiplas colunas podem fazer parte do mesmo índice usando o mesmo nome no `Parametro`:

```csharp
new Chave { Campo = "Nome", Parametro = "IX_Composto" },
new Chave { Campo = "Sobrenome", Parametro = "IX_Composto" }
```

### Índices Parciais
Condições WHERE podem ser adicionadas a qualquer índice:
- Use `Campo = null` ou vazio na `Chave` para identificar condições WHERE
- Use `Parametro` para especificar o campo da condição
- Defina `Valor`, `Operador` e `Tipo` conforme necessário

### Detecção de Mudanças
O sistema detecta quando:
- Um índice não existe e precisa ser criado
- Um índice existe mas com colunas diferentes
- Um índice existe mas não é mais necessário

**Limitação**: Mudanças apenas na cláusula WHERE não são detectadas automaticamente (devido às limitações dos metadados). O índice será recriado apenas se houver mudança nas colunas.

### Modo Debug
Quando `Debug` está habilitado, mensagens informativas são exibidas:
- "Verificando índices para tabela {nome}"
- "Índice {nome} criado na tabela {nome}"
- "Índice {nome} removido da tabela {nome}"

### Tratamento de Erros
Todos os métodos possuem try-catch com logging de erros detalhado, incluindo:
- Nome da tabela
- Operação sendo executada
- Stack trace completo

## Exemplo Completo de Uso

```csharp
public class Usuario : IPOCOIndexes
{
    [Key]
    public int Id { get; set; }
    
    public string Login { get; set; }
    public string Email { get; set; }
    public string Nome { get; set; }
    public string Sobrenome { get; set; }
    public DateTime UltimoAcesso { get; set; }
    public bool Ativo { get; set; }
    public int TentativasLogin { get; set; }

    public IEnumerable<Chave> GetIndexes()
    {
        return new List<Chave>
        {
            // Índice único no Login
            new Chave 
            { 
                Campo = "Login", 
                Parametro = "IX_Usuario_Login_Unique" 
            },
            
            // Índice no Email
            new Chave 
            { 
                Campo = "Email", 
                Parametro = "IX_Usuario_Email" 
            },
            
            // Índice composto em Nome e Sobrenome
            new Chave 
            { 
                Campo = "Nome", 
                Parametro = "IX_Usuario_NomeCompleto" 
            },
            new Chave 
            { 
                Campo = "Sobrenome", 
                Parametro = "IX_Usuario_NomeCompleto" 
            },
            
            // Índice parcial: apenas usuários ativos
            new Chave 
            { 
                Campo = "UltimoAcesso", 
                Parametro = "IX_Usuario_UltimoAcesso_Ativos" 
            },
            new Chave 
            { 
                Parametro = "Ativo",
                Valor = true,
                Operador = Operador.IGUAL,
                Tipo = Tipo.BOOL
            },
            
            // Índice parcial: usuários com falhas de login
            new Chave 
            { 
                Campo = "Login", 
                Parametro = "IX_Usuario_Login_ComFalhas" 
            },
            new Chave 
            { 
                Parametro = "TentativasLogin",
                Valor = 0,
                Operador = Operador.MAIOR,
                Tipo = Tipo.INT
            }
        };
    }
}

// Uso
var tableCheck = new TableCheckByType(conexao, debug: true);
await tableCheck.CriaTabela<Usuario>();
// Os índices (incluindo parciais) são criados automaticamente
```

**SQL Gerado:**
```sql
-- SQLite
CREATE INDEX IF NOT EXISTS IX_Usuario_Login_Unique ON Usuario (Login);
CREATE INDEX IF NOT EXISTS IX_Usuario_Email ON Usuario (Email);
CREATE INDEX IF NOT EXISTS IX_Usuario_NomeCompleto ON Usuario (Nome, Sobrenome);
CREATE INDEX IF NOT EXISTS IX_Usuario_UltimoAcesso_Ativos ON Usuario (UltimoAcesso) WHERE Ativo = 1;
CREATE INDEX IF NOT EXISTS IX_Usuario_Login_ComFalhas ON Usuario (Login) WHERE TentativasLogin > 0;
```

## Notas Importantes

1. **Índices de Primary Key**: O sistema ignora automaticamente índices PRIMARY KEY durante a verificação
2. **Convenção de Nomenclatura**: Índices criados automaticamente seguem o padrão `IX_{NomeTabela}_{NomeCampo}`
3. **Índices Únicos**: A propriedade `IsUnique` em `IndexInfo` está preparada para suporte futuro de índices UNIQUE
4. **Performance**: A verificação de índices é executada apenas durante criação/alteração de tabelas, não afetando operações CRUD
5. **Transações**: As operações de criação/remoção de índices não estão em transação separada, são executadas na mesma conexão da operação de tabela
6. **Versões MySQL**: Índices parciais requerem MySQL 8.0.13+. Versões anteriores ignorarão a cláusula WHERE
7. **Versões SQLite**: Índices parciais requerem SQLite 3.8.0+
8. **Limitação de Detecção**: Mudanças apenas na cláusula WHERE não são detectadas. O índice será recriado apenas se houver mudança nas colunas

## Melhorias Futuras Sugeridas

1. ? **Suporte para índices parciais com WHERE** (Implementado)
2. Suporte para índices UNIQUE através de propriedade adicional em `Chave`
3. Suporte para índices DESC/ASC
4. Suporte para índices com expressões (ex: `CREATE INDEX ON table ((LOWER(email)))`)
5. Suporte para índices full-text no MySQL
6. Implementação completa para MSSQL (índices filtrados)
7. Detecção de mudanças em cláusulas WHERE de índices existentes
8. Suporte para índices com INCLUDE (colunas adicionais)
