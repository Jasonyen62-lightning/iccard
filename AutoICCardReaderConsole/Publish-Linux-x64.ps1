$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'AutoICCardReaderConsole.csproj'
$output = Join-Path $PSScriptRoot 'release\linux-x64'

dotnet publish $project --configuration Release --runtime linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Linux x64 Release published to $output"
