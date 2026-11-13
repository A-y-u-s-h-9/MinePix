# Add D:\dotnet to the User PATH (no duplicates, nothing else touched)
$p = [Environment]::GetEnvironmentVariable('Path','User')
if (($p -split ';') -notcontains 'D:\dotnet') {
    [Environment]::SetEnvironmentVariable('Path', ($p.TrimEnd(';') + ';D:\dotnet'), 'User')
    Write-Host 'Added D:\dotnet to User PATH.' -ForegroundColor Green
} else {
    Write-Host 'D:\dotnet already in User PATH.' -ForegroundColor Yellow
}
Write-Host 'Open a NEW terminal, then run: dotnet --version'
