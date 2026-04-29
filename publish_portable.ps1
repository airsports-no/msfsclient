# Create a portable, single-file release of the MSFS Client
dotnet publish msfsclient/msfsclient.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Unpackaged=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
