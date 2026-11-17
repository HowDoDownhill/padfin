@echo off
:: Builds the Windows release into build\windows. Runs build.ps1 with the execution policy
:: bypassed for this one process, so it works where PowerShell scripts are blocked by default.
:: Arguments are passed through, such as: build.bat -Output build\test
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
