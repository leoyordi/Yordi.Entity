using Xunit;

// A biblioteca usa estado estático (SQLiteConnectionManager, _writeLock) por presumir
// um único banco por processo. Rodar testes com bancos distintos em paralelo embaralha
// esse estado, então serializamos a execução.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
