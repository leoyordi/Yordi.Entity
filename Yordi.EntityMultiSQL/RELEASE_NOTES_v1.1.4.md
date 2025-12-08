# Release Notes - Versão 1.1.4

## 🔧 Atualização de Dependências

### Mudanças Principais

#### Classe `Chave` Movida para Yordi.Tools

A classe `Chave` foi transferida do pacote `Yordi.EntityMultiSQL` para o pacote **`Yordi.Tools` versão 1.0.14**. Esta mudança melhora a organização do código e permite o reuso da classe em outros pacotes do ecossistema Yordi.

### ⚠️ Versão 1.1.3 - DEPRECATED

**A versão 1.1.3 está OBSOLETA e NÃO deve ser utilizada.**

#### Motivo da Deprecação
- Objetos com dependências incorretas
- Classe `Chave` definida no pacote errado
- Falta de aplicabilidade prática devido a problemas de referência

#### Ação Requerida
Se você está usando a versão 1.1.3, **atualize imediatamente para a versão 1.1.4** ou superior.

## 📦 Dependências Atualizadas

| Pacote | Versão Anterior | Nova Versão | Mudança |
|--------|-----------------|-------------|---------|
| Yordi.Tools | 1.0.12 | **1.0.14** | Classe Chave movida para este pacote |

## 🔄 Migração da Versão 1.1.3 para 1.1.4

### Passo 1: Atualizar Pacote

```bash
dotnet remove package Yordi.EntityMultiSQL --version 1.1.3
dotnet add package Yordi.EntityMultiSQL --version 1.1.4
```

Ou atualize diretamente no `.csproj`:
```xml
<PackageReference Include="Yordi.EntityMultiSQL" Version="1.1.4" />
```

### Passo 2: Verificar Imports

**Antes (v1.1.3):**
```csharp
using Yordi.EntityMultiSQL; // Chave estava aqui
```

**Depois (v1.1.4):**
```csharp
using Yordi.EntityMultiSQL; // Para interfaces e repositórios
using Yordi.Tools;          // Para classe Chave
```

### Passo 3: Código de Exemplo Atualizado

```csharp
using Yordi.EntityMultiSQL;
using Yordi.Tools; // ← ADICIONAR ESTA LINHA

public class Usuario : IPOCOIndexes
{
    public int Id { get; set; }
    public string Email { get; set; }
    public bool Ativo { get; set; }

    public IEnumerable<Chave> GetIndexes() // Chave agora vem de Yordi.Tools
    {
        return new List<Chave>
        {
            new Chave 
            { 
                Campo = "Email", 
                Parametro = "IX_Usuario_Email" 
            },
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
            }
        };
    }
}
```

## ✅ O que NÃO mudou

- ✅ Todas as funcionalidades continuam as mesmas
- ✅ API pública não foi alterada
- ✅ Gerenciamento de índices funciona da mesma forma
- ✅ Índices parciais com WHERE continuam suportados
- ✅ Compatibilidade com .NET 8.0 mantida

## 📋 Checklist de Migração

- [ ] Atualizar pacote para versão 1.1.4
- [ ] Adicionar `using Yordi.Tools;` onde classe `Chave` é usada
- [ ] Compilar projeto e verificar erros
- [ ] Testar funcionalidades de índices
- [ ] Remover referências à versão 1.1.3

## 🐛 Problemas Conhecidos

Nenhum problema conhecido nesta versão.

## 💡 Recomendações

1. **Sempre use a versão mais recente** (1.1.4 ou superior)
2. **Evite versão 1.1.3** - está marcada como deprecated no NuGet
3. **Verifique dependências** - certifique-se que Yordi.Tools está em v1.0.14+

## 📚 Recursos Mantidos

Todos os recursos da versão 1.1.3 foram mantidos:
- ✅ Gerenciamento automático de índices
- ✅ Índices parciais com cláusula WHERE
- ✅ Suporte para SQLite 3.8+ e MySQL 8.0.13+
- ✅ Índices simples e compostos
- ✅ Criação/remoção automática de índices
- ✅ Detecção de mudanças em estruturas

## 🔗 Links Úteis

- [README.md](README.md) - Guia atualizado
- [INDEX_MANAGEMENT_DOCUMENTATION.md](INDEX_MANAGEMENT_DOCUMENTATION.md) - Documentação completa
- [Yordi.Tools no NuGet](https://www.nuget.org/packages/Yordi.Tools/)
- [Repositório GitHub](https://github.com/leoyordi/Yordi.Entity)

## 👥 Suporte

Para dúvidas ou problemas:
- Abra uma issue no [GitHub](https://github.com/leoyordi/Yordi.Entity/issues)
- Verifique a documentação completa

---

**Data de Lançamento:** 2025-12-08  
**Versão Anterior:** 1.1.3 (DEPRECATED)  
**Próxima Versão:** TBD

## 📝 Notas Finais

Esta é uma atualização de **manutenção e correção** que resolve problemas de dependência da versão anterior. **Todos os usuários da versão 1.1.3 devem atualizar imediatamente.**

A mudança da classe `Chave` para `Yordi.Tools` é uma melhoria arquitetural que beneficiará futuros desenvolvimentos e permitirá melhor reutilização de código entre os pacotes Yordi.

---

**Desenvolvido por:** Leopoldo Yordi  
**Licença:** MIT
