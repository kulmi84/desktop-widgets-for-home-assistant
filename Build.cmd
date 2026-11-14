@echo off
setlocal
cd /d "%~dp0"
set "SOLAR_COMPILER=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%SOLAR_COMPILER%" exit /b 1
"%SOLAR_COMPILER%" /nologo /codepage:65001 /target:winexe /win32icon:HomeAssistantWidget.ico /optimize+ /resource:Solaranlage.ico,Solaranlage.ico /resource:HomeAssistantWidget.ico,HomeAssistantWidget.ico /out:SolarKachel.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Security.dll /r:System.Web.Extensions.dll SolarKachel.cs /r:"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF\PresentationCore.dll" /r:"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF\WindowsBase.dll" DoorWidget.cs DoorIcons.cs ReleaseVersion.cs
if errorlevel 1 exit /b 1
"%SOLAR_COMPILER%" /nologo /codepage:65001 /target:winexe /win32icon:HomeAssistantWidget.ico /optimize+ /out:SolarKachel-Setup.exe /resource:SolarKachel.exe,SolarKachel.exe /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll Setup.cs ReleaseVersion.cs
exit /b %errorlevel%
