@echo off
echo 正在构建WallpaperSync发布版...

REM 保存项目根目录路径
set PROJECT_ROOT=%~dp0
set PROJECT_ROOT=%PROJECT_ROOT:~0,-1%

echo 0. 清理旧文件...
set FINAL_DIR=%PROJECT_ROOT%\final-release
set BUILD_DIR=%PROJECT_ROOT%\build

REM 清理final-release目录
if exist "%FINAL_DIR%" (
    echo 清理final-release目录...
    rmdir /s /q "%FINAL_DIR%"
)

REM 清理build目录
if exist "%BUILD_DIR%" (
    echo 清理build目录...
    rmdir /s /q "%BUILD_DIR%"
)

echo 1. 发布单文件版本...
cd /d "%PROJECT_ROOT%\src"
"C:\Program Files\dotnet\dotnet.exe" publish -c Release --self-contained --runtime win-x64 -p:PublishSingleFile=true

if errorlevel 1 (
    echo 发布失败！
    exit /b 1
)

echo 2. 复制文件到final-release...
set BUILD_DIR=%PROJECT_ROOT%\build\bin\Release\net8.0-windows10.0.19041\win-x64\publish

if not exist "%FINAL_DIR%" mkdir "%FINAL_DIR%"
if not exist "%FINAL_DIR%\icons" mkdir "%FINAL_DIR%\icons"

copy "%BUILD_DIR%\WallpaperSync.exe" "%FINAL_DIR%\"
copy "%PROJECT_ROOT%\icons\icon.ico" "%FINAL_DIR%\icons\"

echo.
echo ========================================
echo 打包完成！
echo 输出位置: %FINAL_DIR%\WallpaperSync.exe
echo ========================================