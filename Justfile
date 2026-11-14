clean:
    find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +

clean-pack:
    rm -rf ~/.nuget/packages/pumpkin.dotnet*
    rm -rf ./nupkgs/*

clean-sample:
    rm -rf samples/Pumpkin.DotNet.Sample/bin samples/Pumpkin.DotNet.Sample/obj

pack:
    dotnet pack Pumpkin.DotNet.slnx -c Release -o ./nupkgs

build-sample:
    dotnet build samples/Pumpkin.DotNet.Sample -c Release
