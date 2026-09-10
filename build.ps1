$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:winexe /optimize+ /win32manifest:"$PSScriptRoot\app.manifest" /win32icon:"$PSScriptRoot\assets\goose.ico" /out:"$PSScriptRoot\GooseStickyNotes.exe" "/resource:$PSScriptRoot\Modern.xaml,Modern.xaml" "/resource:$PSScriptRoot\assets\goose.png,goose.png" "/resource:$PSScriptRoot\assets\goose.ico,goose.ico" /reference:"$wpf\PresentationFramework.dll" /reference:"$wpf\PresentationCore.dll" /reference:"$wpf\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\Modern.cs" "$PSScriptRoot\Verify.cs" "$PSScriptRoot\DayInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }



