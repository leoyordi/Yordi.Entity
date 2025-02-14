using System.Text;
using System.Text.Json;
using Yordi.Tools;

namespace Yordi.EntityMultiSQL
{
    public class RepositorioFile<T> : EventBaseClass where T : class
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

        public RepositorioFile(string path, Encoding? encoding = null)
        {
            try
            {
                _encoding = encoding;
                _msg = this.ToString();
                Local = path;
            }
            catch (Exception ex)
            {
                Error(ex);
                throw;
            }
        }
        public RepositorioFile(Encoding? encoding = null)
        {
            _encoding = encoding;
            _msg = this.ToString();
        }



        public bool AtualizarOuAdicionarJson(T objeto)
        {
            MontaNomeArquivoCompleto();
            if (String.IsNullOrEmpty(_local))
            {
                Message("Arquivo não informado");
                return false;
            }
            try
            {
                string json = Conversores.ToJson(objeto, true);
                File.WriteAllText(_local, json);
                Message($"Arquivo {_local} escrito");
                return true;
            }
            catch (IOException io)
            {
                Error(io.Message);
                return false;
            }
            catch (Exception e)
            {
                Error(e);
                return false;
            }
        }

        public virtual T? LerJson()
        {
            if (String.IsNullOrEmpty(_local))
            {
                Message("Arquivo não informado");
                return null;
            }

            T? c;
            try
            {
                string r = FileTools.ReadAllText(_local, _encoding);
                if (String.IsNullOrEmpty(r))
                    return null;
                c = Conversores.FromJson<T>(r);// JsonSerializer.Deserialize<T>(r);
            }
            catch (Exception e)
            {
                Error(e);
                c = null;
            }
            return c;
        }
        public virtual async Task<T?> LerJsonAsync()
        {
            if (String.IsNullOrEmpty(_local))
            {
                Message("Arquivo não informado");
                return null;
            }
            if (!File.Exists(_local))
                return null;
            T? retorno = null;
            using (Stream s = File.OpenRead(_local))
            {
                retorno = await JsonSerializer.DeserializeAsync<T>(s, jsonOptions);
            }
            return retorno;
        }
        public virtual string? Ler()
        {
            if (string.IsNullOrEmpty(_local)) return null;
            try
            {
                MontaNomeArquivoCompleto();
                return FileTools.ReadAllText(_local, _encoding);
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
        public string? Ler(string arquivo)
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
                Message("Arquivo não informado");
                return false;
            }
            try
            {
                await FileTools.WriteTextAsync(_local, texto, _encoding);
                Message($"Arquivo {_local} escrito");
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
                Message($"Arquivo {arquivo} escrito");
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
