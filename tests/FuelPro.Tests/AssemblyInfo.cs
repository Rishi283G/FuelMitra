using Xunit;

// Disable parallel test execution because multiple integration tests read/write to the same SQLite database file.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
