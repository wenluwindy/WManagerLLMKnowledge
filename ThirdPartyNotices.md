# Third-Party Notices

This package distributes unmodified NuGet managed binaries and Windows x64 CPU native binaries from the following projects. Models are downloaded separately and are not included in this SDK.

| Project / package | Version | License | Source |
| --- | --- | --- | --- |
| LLamaSharp / LLamaSharp.Backend.Cpu | 0.24.0 | MIT | https://github.com/SciSharp/LLamaSharp |
| llama.cpp | ceda28ef8e310a8dee60bf275077a3eedae8e36c | MIT | https://github.com/ggml-org/llama.cpp |
| CommunityToolkit.HighPerformance | 8.4.0 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Bcl.AsyncInterfaces | 9.0.3 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Bcl.Numerics | 9.0.3 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.AI.Abstractions | 9.5.0-preview.1.25262.9 | MIT | https://github.com/dotnet/extensions |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.3 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging.Abstractions | 9.0.3 | MIT | https://github.com/dotnet/runtime |
| System.Diagnostics.DiagnosticSource | 9.0.3 | MIT | https://github.com/dotnet/runtime |
| System.IO.Pipelines | 9.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Linq.Async | 6.0.1 | MIT | https://github.com/dotnet/reactive |
| System.Numerics.Tensors | 9.0.3 | MIT | https://github.com/dotnet/runtime |
| System.Runtime.CompilerServices.Unsafe | 6.1.0 | MIT | https://github.com/dotnet/runtime |
| System.Text.Encodings.Web | 9.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Text.Json | 9.0.0 | MIT | https://github.com/dotnet/runtime |

Resolved dependency versions are recorded in Documentation~/packages.lock.json. Original project license texts are distributed under ThirdParty~/. The Windows SQLite implementation uses the operating system's winsqlite3 library and does not redistribute SQLite binaries.

CPU native binaries are provided by SciSharp/LLamaSharpBinaries and include GGML and OpenMP support. Model redistribution must retain the license and notices of the selected model and its conversion source.
