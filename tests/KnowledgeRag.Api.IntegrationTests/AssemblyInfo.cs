using Xunit;

// ApiFactory must publish configuration before Program builds the host, so it uses
// process-wide environment variables. Running multiple WebApplicationFactory test
// classes in parallel can make one factory observe another factory's SQLite path.
// Keep this integration-test assembly deterministic until the host bootstrap can
// accept fully isolated per-factory configuration without ambient process state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
