# Servidor local simples para visualizar o site: http://localhost:5180
param([int]$Port = 5180)
$root = (Resolve-Path "$PSScriptRoot\..").Path
$types = @{ '.html'='text/html; charset=utf-8'; '.css'='text/css'; '.js'='text/javascript'; '.svg'='image/svg+xml';
            '.woff'='font/woff'; '.ttf'='font/ttf'; '.png'='image/png'; '.jpg'='image/jpeg'; '.json'='application/json' }
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Servindo $root em http://localhost:$Port"
while ($listener.IsListening) {
  $ctx = $listener.GetContext()
  $rel = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
  if ($rel -eq '') { $rel = 'index.html' }
  $file = [IO.Path]::GetFullPath((Join-Path $root $rel))
  if ($file.StartsWith($root) -and (Test-Path $file -PathType Container)) {
    if (-not $ctx.Request.Url.AbsolutePath.EndsWith('/')) {
      $ctx.Response.Redirect($ctx.Request.Url.AbsolutePath + '/'); $ctx.Response.Close(); continue
    }
    $file = Join-Path $file 'index.html'
  }
  if ($file.StartsWith($root) -and (Test-Path $file -PathType Leaf)) {
    $bytes = [IO.File]::ReadAllBytes($file)
    $ext = [IO.Path]::GetExtension($file).ToLower()
    $ctx.Response.ContentType = if ($types[$ext]) { $types[$ext] } else { 'application/octet-stream' }
    $ctx.Response.Headers.Add('Cache-Control', 'no-store')
    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
  } else { $ctx.Response.StatusCode = 404 }
  $ctx.Response.Close()
}
