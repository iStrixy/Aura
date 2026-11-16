# Compila o Aura com o compilador que já vem no Windows (.NET Framework 4.8, C# 5). Não precisa instalar nada.
# Uso:  powershell -ExecutionPolicy Bypass -File build.ps1     (feche o Aura antes: o exe fica travado enquanto roda)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Ícone do app desenhado aqui (círculo azul->roxo com um arco branco), gravado como .ico com 4 tamanhos em PNG
if (-not (Test-Path Aura.ico)) {
    Add-Type -AssemblyName System.Drawing
    $imgs = foreach ($s in 16, 32, 48, 256) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'
        $rect = New-Object System.Drawing.RectangleF ($s * 0.06), ($s * 0.06), ($s * 0.88), ($s * 0.88)
        $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(100, 210, 255)), ([System.Drawing.Color]::FromArgb(191, 90, 242)), 45
        $g.FillEllipse($br, $rect)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ($s * 0.1); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawArc($pen, ($s * 0.29), ($s * 0.29), ($s * 0.42), ($s * 0.42), 150, 240)
        $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        , $ms.ToArray()
    }
    $out = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $out
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$imgs.Count)
    $offset = 6 + 16 * $imgs.Count; $i = 0
    foreach ($s in 16, 32, 48, 256) {
        $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$imgs[$i].Length); $w.Write([uint32]$offset)
        $offset += $imgs[$i].Length; $i++
    }
    foreach ($img in $imgs) { $w.Write($img) }
    [IO.File]::WriteAllBytes("$PSScriptRoot\Aura.ico", $out.ToArray())
}

$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = "$fw\WPF"
& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:Aura.exe /win32icon:Aura.ico `
    /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" `
    /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Web.Extensions.dll `
    /resource:ui\Widget.xaml,Widget.xaml /resource:ui\Net.xaml,Net.xaml `
    src\*.cs
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao" }
"Compilado: $PSScriptRoot\Aura.exe"
