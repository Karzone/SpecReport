// Method-level parallelism is the POINT of this sample: attribution that works sequentially
// but breaks under concurrency is the exact failure the M1 spike was built to catch.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
