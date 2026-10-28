# API (http://localhost:5095) ve arayüz geliştirme sunucusunu (http://localhost:5191) ayrı pencerelerde başlatır.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$root\src\OzcanDoviz.Api'; dotnet run"

$web = Join-Path $root 'frontend\ozcan-doviz-web'
if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    Push-Location $web
    npm install
    Pop-Location
}
Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$web'; npm run dev"
