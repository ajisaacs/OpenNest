param(
    [Parameter(Mandatory, Position = 0)]
    [string]$DxfDir
)

$DbPath = Join-Path $PSScriptRoot 'test-training.db'
$SaveDir = 'X:\'
$Template = 'X:\Template.nstdot'

dotnet run --project (Join-Path $PSScriptRoot 'OpenNest.Console') -- --collect $DxfDir --db $DbPath --save-nests $SaveDir --template $Template
