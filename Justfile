set shell := ["bash", "-euo", "pipefail", "-c"]

configuration := "Release"
nupkgs_dir := justfile_directory() + "/nupkgs"

clean:
    find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
    rm -rf "{{nupkgs_dir}}" ~/.nuget/packages/pumpkin.dotnet*

build-core:
    dotnet build src/Pumpkin.DotNet.SourceGenerator/Pumpkin.DotNet.SourceGenerator.csproj -c {{configuration}}
    dotnet build tools/Pumpkin.DotNet.Tools.WitPostProcessor/Pumpkin.DotNet.Tools.WitPostProcessor.csproj -c {{configuration}}
    dotnet build src/Pumpkin.DotNet.Api/Pumpkin.DotNet.Api.csproj -c {{configuration}}
    dotnet build src/Pumpkin.DotNet.Runtime/Pumpkin.DotNet.Runtime.csproj -c {{configuration}} --no-dependencies

test: build-core
    dotnet test tests/Pumpkin.DotNet.Tests/Pumpkin.DotNet.Tests.csproj -c {{configuration}} --no-build

pack: build-core
    mkdir -p "{{nupkgs_dir}}"
    dotnet pack src/Pumpkin.DotNet.Api/Pumpkin.DotNet.Api.csproj -c {{configuration}} -o "{{nupkgs_dir}}" --no-build
    dotnet pack src/Pumpkin.DotNet.Runtime/Pumpkin.DotNet.Runtime.csproj -c {{configuration}} -o "{{nupkgs_dir}}" --no-build
    dotnet pack src/Pumpkin.DotNet.Sdk/Pumpkin.DotNet.Sdk.csproj -c {{configuration}} -o "{{nupkgs_dir}}"

build-sample: pack
    dotnet restore samples/Pumpkin.DotNet.Sample/Pumpkin.DotNet.Sample.csproj \
        --source "{{nupkgs_dir}}" \
        --source https://api.nuget.org/v3/index.json \
        --force
    dotnet build samples/Pumpkin.DotNet.Sample/Pumpkin.DotNet.Sample.csproj -c {{configuration}} --no-restore
    test -s "samples/Pumpkin.DotNet.Sample/bin/{{configuration}}/net11.0/wasi-wasm/native/Pumpkin.DotNet.Sample.wasm"

ci: clean test build-sample