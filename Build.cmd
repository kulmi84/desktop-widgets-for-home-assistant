@echo off
setlocal
cd /d "%~dp0"
set "SOLAR_COMPILER=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%SOLAR_COMPILER%" exit /b 1
set "APP_DEFINE="
set "APP_OUT=DesktopWidgetsForHomeAssistant.exe"
if /i "%~1"=="--store" set "APP_DEFINE=/define:STORE"
if /i "%~1"=="--store" set "APP_OUT=DesktopWidgetsForHomeAssistant.Store.exe"
"%SOLAR_COMPILER%" /nologo /codepage:65001 /target:winexe /win32icon:HomeAssistantWidget.ico /optimize+ /resource:Solaranlage.ico,Solaranlage.ico /resource:HomeAssistantWidget.ico,HomeAssistantWidget.ico %APP_DEFINE% /out:%APP_OUT% /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Security.dll /r:System.Web.Extensions.dll SolarKachel.cs /r:"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF\PresentationCore.dll" /r:"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF\WindowsBase.dll" DoorWidget.cs HeatingWidget.cs HomeAssistantEvents.cs ProfileBackup.cs ProgramOptions.cs WidgetPlacement.cs LightControls.cs SettingsTheme.cs DoorIcons.cs ReleaseVersion.cs UiText.cs
if errorlevel 1 exit /b 1
if /i "%~1"=="--store" exit /b 0
"%SOLAR_COMPILER%" /nologo /codepage:65001 /target:winexe /win32icon:HomeAssistantWidget.ico /optimize+ /out:DesktopWidgetsForHomeAssistant-Setup.exe /resource:DesktopWidgetsForHomeAssistant.exe,DesktopWidgetsForHomeAssistant.exe /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll Setup.cs ReleaseVersion.cs UiText.cs
exit /b %errorlevel%
