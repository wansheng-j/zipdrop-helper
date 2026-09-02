@echo off
setlocal
rem One-click sync: check token health, then push to GitHub and Gitee.
rem Put this file in the repo root.
cd /d "%~dp0"

rem --- Token health check (token itself is never printed) ---
for /f "tokens=1,* delims==" %%a in ('^(echo protocol=https^&echo host=github.com^&echo.^)^|git credential fill ^| findstr /b "password="') do set GH_TOKEN=%%b
for /f "tokens=1,* delims==" %%a in ('^(echo protocol=https^&echo host=gitee.com^&echo.^)^|git credential fill ^| findstr /b "password="') do set GE_TOKEN=%%b

if "%GH_TOKEN%"=="" (set GH_CODE=000) else (
    curl.exe -s -o NUL -w "%%{http_code}" -H "Authorization: Bearer %GH_TOKEN%" https://api.github.com/user > "%TEMP%\gh_code.txt"
)
if "%GE_TOKEN%"=="" (set GE_CODE=000) else (
    curl.exe -s -o NUL -w "%%{http_code}" "https://gitee.com/api/v5/user?access_token=%GE_TOKEN%" > "%TEMP%\ge_code.txt"
)
set /p GH_CODE=<"%TEMP%\gh_code.txt"
set /p GE_CODE=<"%TEMP%\ge_code.txt"

if "%GH_CODE%"=="200" (echo [OK]   GitHub token: valid) else (echo [FAIL] GitHub token: invalid or missing)
if "%GE_CODE%"=="200" (echo [OK]   Gitee token : valid) else (echo [FAIL] Gitee token : invalid or missing)
echo.

git add -A
set "MSG=auto update"
set /p "MSG=Commit message (Enter=auto update): "
if "%MSG%"=="" set "MSG=auto update"
git commit -m "%MSG%"

echo.
echo === Push to GitHub ===
git push github main
echo.
echo === Push to Gitee ===
git push gitee main

echo.
echo Done. Press any key to close.
pause >nul
endlocal
