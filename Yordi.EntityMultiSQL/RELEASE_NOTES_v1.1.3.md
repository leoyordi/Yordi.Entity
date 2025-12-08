# Release Notes - Versão 1.1.3

## ?? Novidades

### Gerenciamento Automático de Índices com Suporte a WHERE

A versão 1.1.3 traz um sistema completo de gerenciamento automático de índices para tabelas POCO, incluindo suporte para **índices parciais com cláusula WHERE**.

## ? Principais Recursos

### 1. Interface IPOCOIndexes
- Nova interface para definir índices em classes POCO
- Suporte para índices simples, compostos e parciais
- Gerenciamento automático durante criação/alteração de tabelas

### 2. Índices Parciais (Partial Indexes)
- Suporte para cláusulas WHERE em índices
- Compatível com SQLite 3.8.0+ e MySQL 8.0.13+
- Reduz tamanho de índices e melhora performance

### 3. Gerenciamento Inteligente
- ? Criação automática de novos índices
- ? Remoção de índices obsoletos
- ? Recriação quando colunas são modificadas
- ? Suporte para múltiplas condições WHERE (AND)

## ?? Exemplo de Uso

```csharp
public class Usuario : IPOCOIndexes
{
    public int Id { get; set; }
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
                Campo = "Email", 
                Parametro = "IX_Usuario_Email" 
            },
            
            // Índice parcial: apenas usuários ativos
            new Chave 
            { 
                Campo = "UltimoAcesso", 
                Parametro = "IX_Usuario_UltimoAcesso_Ativos" 
            },
            new Chave 
            { 
                Parametro = "Ativo",      // Campo WHERE
                Valor = true,             // Valor WHERE
                Operador = Operador.IGUAL,
                Tipo = Tipo.BOOL
            }
        };
    }
}
```

**SQL Gerado:**
```sql
CREATE INDEX IF NOT EXISTS IX_Usuario_Email ON Usuario (Email);
CREATE INDEX IF NOT EXISTS IX_Usuario_UltimoAcesso_Ativos 
    ON Usuario (UltimoAcesso) 
    WHERE Ativo = 1;
```

## ?? Implementação Técnica

### Arquitetura

1. **IPOCOIndexes.IndexInfo**
   - `IndexName`: Nome único do índice
   - `Columns`: Lista de colunas do índice
   - `Chaves`: Condições WHERE para índices parciais
   - `IsUnique`: Preparado para índices únicos

2. **Métodos Principais**
   - `GerenciarIndices`: Orquestra todo o processo
   - `ConstruirIndicesInfo`: Separa colunas de condições WHERE
   - `ListarIndicesExistentes`: Consulta índices do banco
   - `GerarScriptCriacaoIndice`: Gera SQL com WHERE
   - `ObterIndicesParaCriar/Remover`: Determina mudanças

3. **Integração**
   - Chamado automaticamente em `CriaTabela()`
   - Chamado automaticamente em `AlteraTabela()`
   - Usa `_bdTools.WhereExpression()` para formatação

## ?? Compatibilidade

| Banco de Dados | Versão Mínima | Índices Parciais |
|----------------|---------------|------------------|
| SQLite         | 3.8.0 (2013)  | ? Suportado     |
| MySQL          | 8.0.13 (2018) | ? Suportado     |
| MSSQL          | -             | ?? Em breve      |

## ?? Documentação

- [README.md](README.md) - Guia de início rápido
- [INDEX_MANAGEMENT_DOCUMENTATION.md](INDEX_MANAGEMENT_DOCUMENTATION.md) - Documentação completa

## ?? Correções

Nenhuma correção de bugs nesta versão (apenas novos recursos).

## ?? Limitações Conhecidas

1. Mudanças apenas em cláusulas WHERE não são detectadas automaticamente
2. Índices UNIQUE ainda não implementados (estrutura preparada)
3. Suporte DESC/ASC não implementado
4. MSSQL filtered indexes em desenvolvimento

## ?? Próximas Versões

- Suporte para índices UNIQUE
- Suporte para índices DESC/ASC
- Implementação completa para MSSQL
- Detecção de mudanças em WHERE clauses
- Suporte para índices com expressões

## ?? Migração

Para aproveitar os novos recursos:

1. Implemente `IPOCOIndexes` nas suas classes POCO
2. Defina os índices no método `GetIndexes()`
3. Execute `CriaTabela()` ou `AlteraTabela()`
4. Os índices serão criados automaticamente

**Nota**: Classes existentes continuam funcionando normalmente sem modificações.

## ?? Créditos

- Desenvolvido por: Leopoldo Yordi
- GitHub: https://github.com/leoyordi/Yordi.Entity
- Licença: MIT

---

Para dúvidas ou sugestões, abra uma issue no repositório GitHub.
