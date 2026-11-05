using BenchmarkDotNet.Running;

// The benchmarks of the library. Read and write scenarios are added with the features they measure:
// dotnet run -c Release --project benchmarks/Eleris.TiffNet.Benchmarks -- --filter "*"
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
