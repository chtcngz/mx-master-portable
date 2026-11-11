@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo HATA: .NET 8 SDK bulunamadi. Bu bilgisayarda SDK kurulumu gerekmiyor;
  echo bu kaynak klasorunu .NET 8 SDK olan baska bir Windows bilgisayarda derleyebilirsiniz.
  pause
  exit /b 1
)
echo Windows x64 icin tek dosya, self-contained portable EXE olusturuluyor...
dotnet publish "MXMasterPortable.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%~dp0publish"
if errorlevel 1 (
  echo Derleme hatasi. Yukaridaki ciktiyi paylasin.
  pause
  exit /b 1
)
echo.
echo Bitti: "%~dp0publish\MXMasterPortable.exe"
pause
