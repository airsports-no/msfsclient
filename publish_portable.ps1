# Create a portable, folder-based release of the MSFS Client
# This is more reliable for WinUI 3 than SingleFile mode.
dotnet publish msfsclient/msfsclient.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Unpackaged=true `
    -p:EnableMsixTooling=false `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
