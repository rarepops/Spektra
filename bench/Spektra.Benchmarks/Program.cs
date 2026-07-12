using System.Reflection;
using BenchmarkDotNet.Running;

// Discovers every [Benchmark] class in this assembly; --filter / --list / --job
// come from the args. Never invoke with no args in automation (it can prompt).
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
