@echo off
echo Starting cleanup of unnecessary Blazor files for IIS Production...
echo ------------------------------------------------------------------

:: 1. Delete Development appsettings
if exist "appsettings.Development.json" (
    del /f /q "appsettings.Development.json"
    echo [DELETED] appsettings.Development.json
)

:: 2. Delete PDB files (Debug symbols)
if exist "*.pdb" (
    del /f /q "*.pdb"
    echo [DELETED] All .pdb files (Debug Symbols)
)

:: 3. Delete BlazorDebugProxy folder
if exist "BlazorDebugProxy" (
    rmdir /s /q "BlazorDebugProxy"
    echo [DELETED] BlazorDebugProxy directory
)

:: 4. Delete Localization folders (Selected in the image)
for %%D in (cs de es fr it ja ko pl pt-BR ru tr zh-Hans zh-Hant) do (
    if exist "%%D" (
        rmdir /s /q "%%D"
        echo [DELETED] %%D language folder
    )
)

echo ------------------------------------------------------------------
echo Cleanup Finished! Ready for IIS deployment.
pause