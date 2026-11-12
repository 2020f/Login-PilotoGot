# Runtime privado para QA Windows; no cambia instalaciones ni variables del sistema.
$taskNet10Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\net10-20261112\runtime'))
if (!(Test-Path -LiteralPath (Join-Path $taskNet10Root 'dotnet.exe')) -or
    !(Test-Path -LiteralPath (Join-Path $taskNet10Root 'shared\Microsoft.AspNetCore.App\10.0.15')) -or
    !(Test-Path -LiteralPath (Join-Path $taskNet10Root 'shared\Microsoft.NETCore.App\10.0.15'))) {
    throw 'Prepara primero el runtime QA privado .NET y ASP.NET Core 10.0.15, verificado con el manifiesto de Microsoft.'
}
$env:DOTNET_ROOT = $taskNet10Root
$env:DOTNET_ROOT_X64 = $taskNet10Root
$env:DOTNET_HOST_PATH = Join-Path $taskNet10Root 'dotnet.exe'
$env:VSTEST_DOTNET_PATH = $env:DOTNET_HOST_PATH
if (($env:PATH -split ';') -notcontains $taskNet10Root) { $env:PATH = $taskNet10Root + ';' + $env:PATH }
