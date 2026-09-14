//
// One application per process, and a state server on a fixed port: the classes share state and must not
// run concurrently.
//

[assembly: Xunit.CollectionBehavior (DisableTestParallelization = true)]
