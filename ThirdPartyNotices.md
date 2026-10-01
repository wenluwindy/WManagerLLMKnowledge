# Third-Party Notices

This package distributes unmodified NuGet managed binaries and Windows x64 CPU native binaries from the following projects. Models are downloaded separately and are not included in this SDK.

| Project / package | Version | License | Source |
| --- | --- | --- | --- |
| LLamaSharp / LLamaSharp.Backend.Cpu | 0.27.0 | MIT | https://github.com/SciSharp/LLamaSharp |
| llama.cpp | 3f7c29d318e317b63f54c558bc69803963d7d88c | MIT | https://github.com/ggml-org/llama.cpp |
| CommunityToolkit.HighPerformance | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Bcl.AsyncInterfaces | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Bcl.Memory / Microsoft.Bcl.Numerics | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.AI.Abstractions | 10.4.1 | MIT | https://github.com/dotnet/extensions |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging.Abstractions | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| System.Diagnostics.DiagnosticSource | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| System.IO.Pipelines | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| System.Linq.Async / System.Interactive.Async | 7.0.0 | MIT | https://github.com/dotnet/reactive |
| System.Linq.AsyncEnumerable | 10.0.2 | MIT | https://github.com/dotnet/runtime |
| System.Numerics.Tensors | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT | https://github.com/dotnet/runtime |
| System.Text.Encodings.Web | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| System.Text.Json | 10.0.5 | MIT | https://github.com/dotnet/runtime |

Resolved dependency versions are recorded in Documentation~/packages.lock.json. Original project license texts are distributed under ThirdParty~/. The Windows SQLite implementation uses the operating system's winsqlite3 library and does not redistribute SQLite binaries.

CPU native binaries are provided by SciSharp/LLamaSharpBinaries and include GGML and OpenMP support. Model redistribution must retain the license and notices of the selected model and its conversion source.
