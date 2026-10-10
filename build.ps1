$ErrorActionPreference = 'Stop'
$PSScriptRootPath = $PSScriptRoot.Replace('/', '\')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll')
try {
    & $compiler /nologo /target:exe /main:IconBuilder "/out:$PSScriptRootPath\IconBuilder.exe" @references "$PSScriptRootPath\MarkdownBold.cs" "$PSScriptRootPath\IconBuilder.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Icon build failed' }
    & "$PSScriptRootPath\IconBuilder.exe" $PSScriptRootPath
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
    & $compiler /nologo /target:winexe "/win32icon:$PSScriptRootPath\MarkdownBold.ico" "/out:$PSScriptRootPath\MarkdownBold.exe" @references "$PSScriptRootPath\MarkdownBold.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally {
    Remove-Item -LiteralPath "$PSScriptRootPath\IconBuilder.exe" -ErrorAction SilentlyContinue
}
