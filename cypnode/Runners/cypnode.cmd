@echo off
pushd %USERPROFILE%\.cypher & dotnet CYPNode.dll %* & popd