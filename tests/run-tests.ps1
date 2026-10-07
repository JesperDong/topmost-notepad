$ErrorActionPreference = 'Stop'
$sourceDirectory = Join-Path $PSScriptRoot '..\src'
$outputDirectory = Join-Path $PSScriptRoot 'bin'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compilerPath)) { throw '没有找到 .NET Framework C# 编译器。' }
$frameworkDirectory = Split-Path -Parent $compilerPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function Invoke-Test {
    param([string]$Name, [string[]]$Sources, [bool]$UsesWindow)
    $targetPath = Join-Path $outputDirectory ($Name + '.exe')
    $arguments = @('/nologo', '/target:exe', '/codepage:65001', "/out:$targetPath")
    if ($UsesWindow) {
        $arguments += @(
            "/reference:$(Join-Path $frameworkDirectory 'WPF\PresentationFramework.dll')",
            "/reference:$(Join-Path $frameworkDirectory 'WPF\PresentationCore.dll')",
            "/reference:$(Join-Path $frameworkDirectory 'WPF\WindowsBase.dll')",
            '/reference:System.Xaml.dll',
            "/reference:$(Join-Path $sourceDirectory 'lib\ICSharpCode.AvalonEdit.dll')",
            "/resource:$(Join-Path $sourceDirectory 'MainWindow.xaml'),TopNote.MainWindow.xaml",
            "/resource:$(Join-Path $sourceDirectory 'app.ico'),TopNote.app.ico"
        )
    }
    $arguments += $Sources
    & $compilerPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "编译 $Name 失败。" }
    if ($UsesWindow) {
        Copy-Item -LiteralPath (Join-Path $sourceDirectory 'app.config') -Destination "$targetPath.config" -Force
        Copy-Item -LiteralPath (Join-Path $sourceDirectory 'lib\ICSharpCode.AvalonEdit.dll') -Destination $outputDirectory -Force
    }
    $result = & $targetPath
    if ($LASTEXITCODE -ne 0) {
        $result | Select-Object -Last 12
        throw "$Name 失败。"
    }
    $result | Select-Object -Last 1
}

Invoke-Test 'TestDocument' @(
    (Join-Path $sourceDirectory 'Document.cs'),
    (Join-Path $PSScriptRoot 'TestDocument.cs')) $false
Invoke-Test 'TestRegions' @(
    (Join-Path $sourceDirectory 'KnowledgeRegions.cs'),
    (Join-Path $PSScriptRoot 'TestRegions.cs')) $false
$windowSources = @(
    (Join-Path $sourceDirectory 'Document.cs'),
    (Join-Path $sourceDirectory 'Dialogs.cs'),
    (Join-Path $sourceDirectory 'KnowledgeRegions.cs'),
    (Join-Path $sourceDirectory 'MainWindow.cs'))
Invoke-Test 'TestWindow' ($windowSources + (Join-Path $PSScriptRoot 'TestWindow.cs')) $true
Invoke-Test 'TestFoldingWindow' ($windowSources + (Join-Path $PSScriptRoot 'TestFoldingWindow.cs')) $true
