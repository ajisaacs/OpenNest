!include "MUI2.nsh"

Name "OpenNest"

OutFile "opennest-setup.exe"

InstallDir "$PROGRAMFILES\OpenNest"

RequestExecutionLevel admin

!define DOT_MAJOR "4"
!define DOT_MINOR "0"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "../license.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

UninstPage uninstConfirm
UninstPage instfiles
 
Section "OpenNest (required)"
  Call IsDotNETInstalled
	SectionIn RO
#!/usr/bin/env 
	; Set output path to the installation directory.
	SetOutPath $INSTDIR

	File "OpenNest.exe"
	File "OpenNest.Core.dll"
	File "OpenNest.Engine.dll"
	File "Ionic.Zip.dll"
	File "netDxf.dll"

	; Write the installation path into the registry
	WriteRegStr HKLM SOFTWARE\OpenNest "Install_Dir" "$INSTDIR"

	; Write the uninstall keys for Windows
	WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenNest" "DisplayName" "OpenNest"
	WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenNest" "UninstallString" '"$INSTDIR\uninstall.exe"'
	WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenNest" "NoModify" 1
	WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenNest" "NoRepair" 1
	WriteUninstaller "uninstall.exe"
SectionEnd

Section "Start Menu Shortcuts"
	CreateDirectory "$SMPROGRAMS\OpenNest"
	CreateShortCut "$SMPROGRAMS\OpenNest\Uninstall.lnk" "$INSTDIR\uninstall.exe" "" "$INSTDIR\uninstall.exe" 0
	CreateShortCut "$SMPROGRAMS\OpenNest\OpenNest.lnk" "$INSTDIR\OpenNest.exe" "" "$INSTDIR\OpenNest.exe" 0
SectionEnd

Section "Uninstall"
	Delete "$INSTDIR\uninstall.exe"
	Delete "$INSTDIR\OpenNest.exe"
	Delete "$INSTDIR\OpenNest.Core.dll"
	Delete "$INSTDIR\OpenNest.Engine.dll"
	Delete "$INSTDIR\Ionic.Zip.dll"
	Delete "$INSTDIR\netDxf.dll"

	Delete "$SMPROGRAMS\OpenNest\Uninstall.lnk"
	Delete "$SMPROGRAMS\OpenNest\OpenNest.lnk"
	Delete "$SMPROGRAMS\OpenNest"
SectionEnd

; Usage
; Define in your script two constants:
;   DOT_MAJOR "(Major framework version)"
;   DOT_MINOR "{Minor framework version)"
; 
; Call IsDotNetInstalled
; This function will abort the installation if the required version 
; or higher version of the .NET Framework is not installed.  Place it in
; either your .onInit function or your first install section before 
; other code.
Function IsDotNetInstalled
 
  StrCpy $0 "0"
  StrCpy $1 "SOFTWARE\Microsoft\.NETFramework" ;registry entry to look in.
  StrCpy $2 0
 
  StartEnum:
    ;Enumerate the versions installed.
    EnumRegKey $3 HKLM "$1\policy" $2
 
    ;If we don't find any versions installed, it's not here.
    StrCmp $3 "" noDotNet notEmpty
 
    ;We found something.
    notEmpty:
      ;Find out if the RegKey starts with 'v'.  
      ;If it doesn't, goto the next key.
      StrCpy $4 $3 1 0
      StrCmp $4 "v" +1 goNext
      StrCpy $4 $3 1 1
 
      ;It starts with 'v'.  Now check to see how the installed major version
      ;relates to our required major version.
      ;If it's equal check the minor version, if it's greater, 
      ;we found a good RegKey.
      IntCmp $4 ${DOT_MAJOR} +1 goNext yesDotNetReg
      ;Check the minor version.  If it's equal or greater to our requested 
      ;version then we're good.
      StrCpy $4 $3 1 3
      IntCmp $4 ${DOT_MINOR} yesDotNetReg goNext yesDotNetReg
 
    goNext:
      ;Go to the next RegKey.
      IntOp $2 $2 + 1
      goto StartEnum
 
  yesDotNetReg:
    ;Now that we've found a good RegKey, let's make sure it's actually
    ;installed by getting the install path and checking to see if the 
    ;mscorlib.dll exists.
    EnumRegValue $2 HKLM "$1\policy\$3" 0
    ;$2 should equal whatever comes after the major and minor versions 
    ;(ie, v1.1.4322)
    StrCmp $2 "" noDotNet
    ReadRegStr $4 HKLM $1 "InstallRoot"
    ;Hopefully the install root isn't empty.
    StrCmp $4 "" noDotNet
    ;build the actuall directory path to mscorlib.dll.
    StrCpy $4 "$4$3.$2\mscorlib.dll"
    IfFileExists $4 yesDotNet noDotNet
 
  noDotNet:
    ;Nope, something went wrong along the way.  Looks like the 
    ;proper .NET Framework isn't installed.  
    MessageBox MB_OK "You must have v${DOT_MAJOR}.${DOT_MINOR} or greater of the .NET Framework installed.  Aborting!"
    Abort
 
  yesDotNet:
    ;Everything checks out.  Go on with the rest of the installation.
 
FunctionEnd