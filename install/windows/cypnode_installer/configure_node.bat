@if "%WORKSPACE%" == "" echo off

SET LOCAL=%~dp0
SET ZSG_TOOLS_WIN=%LOCAL%trunk\_externals\Shared
SET ZSG_TOOLS=%ZSG_TOOLS_WIN:\=/%
SET PATH=%ZSG_TOOLS_WIN%\Ruby\2.3.3\bin;C:\cygwin\bin;%PATH%

echo ZSG_TOOLS environment set to %ZSG_TOOLS%.
echo.
