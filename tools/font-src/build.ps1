# Recompila o gerador e gera a fonte, o CSS, a página de amostra e os SVGs do logo.
# Uso: powershell -ExecutionPolicy Bypass -File tools\font-src\build.ps1
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$exe = Join-Path $env:TEMP "MainStayFontGen.exe"
& "$fw\csc.exe" /nologo /optimize /out:$exe /r:"$fw\WPF\PresentationCore.dll" /r:"$fw\WPF\WindowsBase.dll" /r:"$fw\System.Xaml.dll" "$PSScriptRoot\MainStayFontGen.cs"
if ($LASTEXITCODE -ne 0) { exit 1 }
& $exe "$PSScriptRoot\glyphs.txt" (Resolve-Path "$PSScriptRoot\..\..\assets\fonts") (Resolve-Path "$PSScriptRoot\..\..\assets\logo")
exit $LASTEXITCODE
