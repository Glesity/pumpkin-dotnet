{ pkgs ? import <nixpkgs> { config.allowUnfree = true; } }:
let
  runtimeLibs = with pkgs; [
    stdenv.cc.cc.lib
    zlib
    openssl
    icu
  ];
  dotnetSdk = pkgs.dotnet-sdk_11;
in
pkgs.mkShell {
  packages = with pkgs; [
    just
    git
    curl
    pkg-config

    gcc
    gnumake
    git
    curl
    ripgrep
    fd
    fzf
    llvm

    wit-bindgen
    wasm-tools

    dotnetSdk
    roslyn-ls
    csharpier
    netcoredbg

    go
    gopls
    delve
    lua-language-server
    nil            
    nixpkgs-fmt
  ];

  LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath runtimeLibs;

  # Environment variables required for .NET and the language server on NixOS.
  DOTNET_ROOT = "${dotnetSdk}";
  DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  DOTNET_CLI_FORCE_UTF8_ENCODING = "1";
  MSBUILDTERMINALLOGGER = "on";

  shellHook = ''
    export MSBUILDTERMINALLOGGER="on"

    # Ensure global dotnet tools (~/.dotnet/tools) and the SDK are available on PATH.
    export PATH="$PATH:$HOME/.dotnet/tools"
            
    # Ensure the Roslyn Language Server can resolve the SDK.
    export DOTNET_ROOT="${dotnetSdk}"

    [ "$TERM" = "dumb" ] && export TERM="xterm-256color"
  '';
} 
