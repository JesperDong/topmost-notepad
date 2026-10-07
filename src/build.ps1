param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compilerPath)) { throw '需要 Windows 自带的 .NET Framework 4.8 编译器。' }
$frameworkDirectory = Split-Path -Parent $compilerPath
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# Draw the application icon locally; no external assets or network required.
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $PSScriptRoot 'app.ico'
if (!(Test-Path -LiteralPath $iconPath)) {
    $bitmap = New-Object System.Drawing.Bitmap 64,64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $blueBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(45,95,160))
    $paperBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,253,246))
    $linePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(153,177,205)),3
    $pinPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(45,95,160)),3
    $graphics.FillRectangle($blueBrush,4,2,56,60)
    $graphics.FillRectangle($paperBrush,10,7,44,50)
    $graphics.DrawLine($linePen,17,22,36,22)
    $graphics.DrawLine($linePen,17,32,34,32)
    $graphics.DrawLine($linePen,17,42,43,42)
    $graphics.DrawLine($pinPen,38,13,49,13)
    $graphics.DrawLine($pinPen,40,13,40,21)
    $graphics.DrawLine($pinPen,47,13,47,21)
    $graphics.DrawLine($pinPen,40,21,37,26)
    $graphics.DrawLine($pinPen,47,21,50,26)
    $graphics.DrawLine($pinPen,37,26,50,26)
    $graphics.DrawLine($pinPen,44,26,44,35)
    $imageStream = New-Object System.IO.MemoryStream
    $bitmap.Save($imageStream,[System.Drawing.Imaging.ImageFormat]::Png)
    $imageBytes = $imageStream.ToArray()
    $iconStream = [System.IO.File]::Create($iconPath)
    $writer = New-Object System.IO.BinaryWriter $iconStream
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
    $writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$imageBytes.Length); $writer.Write([UInt32]22); $writer.Write($imageBytes)
    $writer.Dispose(); $imageStream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    $blueBrush.Dispose(); $paperBrush.Dispose(); $linePen.Dispose(); $pinPen.Dispose()
}

$targetPath = Join-Path $OutputDirectory '置顶记事本.exe'
$compilerArguments = @(
    '/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
    "/out:$targetPath", "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')", "/win32icon:$iconPath",
    "/reference:$(Join-Path $frameworkDirectory 'WPF\PresentationFramework.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'WPF\PresentationCore.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'WPF\WindowsBase.dll')", '/reference:System.Xaml.dll',
    "/reference:$(Join-Path $PSScriptRoot 'lib\ICSharpCode.AvalonEdit.dll')",
    "/resource:$(Join-Path $PSScriptRoot 'MainWindow.xaml'),TopNote.MainWindow.xaml",
    "/resource:$iconPath,TopNote.app.ico",
    (Join-Path $PSScriptRoot 'Document.cs'), (Join-Path $PSScriptRoot 'Dialogs.cs'),
    (Join-Path $PSScriptRoot 'KnowledgeRegions.cs'),
    (Join-Path $PSScriptRoot 'MainWindow.cs'), (Join-Path $PSScriptRoot 'Program.cs')
)
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination "$targetPath.config" -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'lib\ICSharpCode.AvalonEdit.dll') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '第三方许可.txt') -Destination $OutputDirectory -Force
Write-Output "已生成：$targetPath"
