# Yordi.EntityMultiSQL

## Release Candidate (RC) — Atenção

> Esta versão está sendo publicada como **Release Candidate (RC)** para testes e validação. Todos os pacotes e dependências foram atualizados para versões mais recentes, mas esta build **ainda não foi totalmente testada**. Pode conter quebras de compatibilidade, alterações de API ou problemas de integração.

### Recomendações:
- Use esta versão apenas para testes e validação em ambiente de desenvolvimento.
- Para produção, prefira a versão estável `1.1.4`.
- Reporte problemas na seção de Issues do repositório.

### Instalação da versão RC (pré-lançamento):
<pre>dotnet add package Yordi.EntityMultiSQL --version 1.2.0-rc2</pre>

## Descrição

Yordi.EntityMultiSQL é um framework para criar instruções SQL para SQLite, MySQL e MSSQL. Ele permite realizar operações CRUD, criar tabelas e campos com base nos objetos POCO.

## Características

- Suporte para SQLite, MySQL e MSSQL
- Operações CRUD (Create, Read, Update, Delete)
- Criação automática de tabelas e campos com base em objetos POCO
- Suporte para atributos personalizados para controle de mapeamento de colunas

## Requisitos

- .NET 8.0

## Instalação

Para instalar o pacote, adicione a seguinte referência ao seu projeto:
<pre>dotnet add package Yordi.EntityMultiSQL</pre>


## Uso

### Configuração

Primeiro, configure a conexão com o banco de dados implementando a interface `IBDConexao`:
<pre>public class MinhaConexao : IBDConexao { // Implementação dos métodos e propriedades da interface IBDConexao }</pre>


### Repositório

Crie uma classe de repositório que herda de `RepositorioAsyncAbstract<T>` ou `RepositorioGenerico`:
<pre>
  public class MeuRepositorio : RepositorioGenerico<POCOclass> { public MeuRepositorio(IBDConexao bd) : base(bd) { }
// Métodos específicos do repositório
}
</pre>


### Entidade

Defina suas entidades POCO com os atributos necessários:
<pre>
  [POCOtoDB(Tipo = POCOType.CADASTRO)]
  public class POCOclass {
    [Autoincrement] public int Id { get; set; }
    [Key] public string KeyProperty { get; set; }
// Outros campos
}
</pre>

### Exemplo de uso
<pre>
using Yordi.EntityMultiSQL;  
class Teste : EventBaseClass
{
  async Task Test()
  {
    IBDConexao conexao = new MinhaConexao();
    IEnumerable<Type> types = conexao.Tabelas; // new List<Type>() { typeof(POCOclass)}
    TableCheckByType bllCheckTable = new TableCheckByType(TipoDB, conexao);
    foreach (var type in types)
    {
      if (!await bllCheckTable.CriaTabela(type, false))
        Message($"Verificação da tabela {type.Name} resultou em erro");
    }
    var repositorio = new MeuRepositorio(conexao);
    var entidade = new POCOclass { KeyProperty = "Exemplo" }; 
    repositorio.Insere(entidade);
  }
}
</pre>


## Contribuição

Contribuições são bem-vindas! Sinta-se à vontade para abrir issues e pull requests no [repositório GitHub](https://github.com/leoyordi/Yordi.Entity).

## Licença

Este projeto está licenciado sob a [MIT License](LICENSE).

## Autores

- Leopoldo Yordi (leoyordi)

## Agradecimentos

Agradecemos a todos os contribuidores e usuários do projeto!
