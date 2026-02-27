# Quick launcher script for DocMan
# Usage: .\run.ps1 [-Interval 5]

param(
    [int]$Interval = 3
)

Set-Location $PSScriptRoot
dotnet run -- -Interval $Interval
