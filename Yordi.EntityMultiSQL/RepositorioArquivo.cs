using System.Text;
using System.Text.Json;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    /// <summary>
    /// Lê arquivo e transforma no tipo informado ou salva objeto qualquer em arquivo, ambos em formato json
    /// </summary>
    public class RepositorioArquivo : EventBaseClass
    {
        private string? _local;
        private string? _internalLocal;
        private Encoding? _encoding = null;
        private JsonSerializerOptions jsonOptions = new()
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            WriteIndented = true
        };

        public Encoding? Encoding { set { _encoding = value; } }

        public string? Local { get => _local; set { _internalLocal = value; MontaNomeArquivoCompleto(); } }

        public RepositorioArquivo(string path, Encoding? encoding = null)
        {
            try
            {
                _encoding = encoding;
                _msg = this.ToString();
                Local = path;
            }
            catch (Exception ex)
            {
                _msg = ex.Message;
                Error(ex);
                throw;
            }
        }
        public RepositorioArquivo(Encoding? encoding = null)
        {
            _encoding = encoding;
            _msg = this.ToString();
        }



        public bool AtualizarOuAdicionarJson(object? objeto)
        {
            MontaNomeArquivoCompleto();
            if (String.IsNullOrEmpty(_local))
            {
                _msg = "Arquivo não informado";
                Message(_msg);
                return false;
            }
            try
            {
                string? json = objeto == null ? null : Conversores.ToJson(objeto, true);
                File.WriteAllText(_local, json);
                _msg = "Arquivo escrito";
                Message(_msg);
                return true;
            }
            catch (Exception e)
            {
                _msg = e.Message;
                Error(e);
                return false;
            }
        }

        public virtual object? LerJson(Type tipo)
        {
            if (String.IsNullOrEmpty(_local))
            {
                _msg = "Arquivo não informado";
                Message(_msg);
                return null;
            }

            object? c;
            try
            {
                string r = FileTools.ReadAllText(_local, _encoding);
                if (String.IsNullOrEmpty(r))
                    return null;
                c = Conversores.FromJson(r, tipo);// JsonSerializer.Deserialize<T>(r);
            }
            catch (Exception e)
            {
                Error(e);
                c = null;
            }

            return c;
        }
        public virtual async Task<object?> LerJsonAsync(Type tipo)
        {
            if (string.IsNullOrEmpty(_local))
            {
                _msg = "Arquivo não informado";
                Message(_msg);
                return null;
            }
            if (!File.Exists(_local))
                return null;
            object? retorno = null;
            using (Stream s = File.OpenRead(_local))
            {
                retorno = await JsonSerializer.DeserializeAsync(s, tipo, jsonOptions);
            }
            return retorno;
        }
        public virtual string? Ler()
        {
            if (string.IsNullOrEmpty(_local))
            {
                _msg = "Arquivo não informado";
                Message(_msg);
                return null;
            }
            try
            {
                MontaNomeArquivoCompleto();
                return FileTools.ReadAllText(_local, _encoding);
                //return FileTools.ReadAllText(_local);
            }
            catch (Exception e)
            {
                Error(e);
                return e.Message;
            }
        }

        /// <summary>
        /// Devolve o texto completo do arquivo informado
        /// </summary>
        /// <param name="arquivo"></param>
        /// <returns></returns>
        public string Ler(string arquivo)
        {
            try
            {
                string s = FileTools.ReadAllText(arquivo, _encoding);
                _msg = FileTools.Mensagem;
                if (!string.IsNullOrEmpty(_msg))
                    Message(_msg);
                return s;
            }
            catch (Exception e)
            {
                Error(e);
                return e.Message;
            }
        }

        /// <summary>
        /// Devolve em linhas, caso o arquivo as tenha, denro de uma array de string.
        /// </summary>
        /// <param name="arquivo"></param>
        /// <returns></returns>
        public string[]? LerLinhas(string arquivo)
        {
            try
            {
                string[]? r = FileTools.ReadAllLines(arquivo, _encoding);
                _msg = FileTools.Mensagem;
                if (!string.IsNullOrEmpty(_msg))
                    Message(_msg);
                return r;
            }
            catch (Exception e)
            {
                Error(e);
                return new string[1] { e.Message };
            }
        }

        public async Task<bool> Escrever(string texto)
        {
            MontaNomeArquivoCompleto();
            if (String.IsNullOrEmpty(_local))
            {
                _msg = "Arquivo não informado";
                return false;
            }
            try
            {
                await FileTools.WriteTextAsync(_local, texto, _encoding);
                _msg = "Arquivo escrito";
                Message(_msg);
                return true;
            }
            catch (Exception e)
            {
                Error(e);
                return false;
            }
        }
        public async Task<bool> Escrever(string texto, string arquivo)
        {
            try
            {
                await FileTools.WriteTextAsync(arquivo, texto, _encoding);
                _msg = "Arquivo escrito";
                Message(_msg);
                return true;
            }
            catch (Exception e)
            {
                Error(e);
                return false;
            }
        }

        private void MontaNomeArquivoCompleto()
        {
            if (string.IsNullOrEmpty(_internalLocal)) return;
            _local = _internalLocal;
            _local = _local.Replace("%TEMP%", FileTools.PastaTemporaria());            
            _local = _local.Replace("DATA", DataPadrao.Brasilia.ToString("_yyyyMMdd"));
        }

    }
}
